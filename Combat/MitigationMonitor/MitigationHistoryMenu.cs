using System.Globalization;
using System.IO;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

internal sealed class MitigationHistoryMenu(
    MitigationCombatLog combatLog,
    MitigationReplayStore replayStore)
{
    private const string HistoryPopupID = "##MitigationHistoryPopup";
    private const string ImportPopupID = "##MitigationImportPopup";

    private readonly List<MitigationCombatHistory> items = new(40);
    private string[] importFiles = [];
    private long historyVersion = -1;

    public string? ActiveHistoryKey { get; private set; }

    public void Open() => ImGui.OpenPopup(HistoryPopupID);

    public void Draw()
    {
        if (!ImGui.BeginPopup(HistoryPopupID))
        {
            return;
        }

        RefreshItems();
        if (ImGui.Selectable(
                OmniLoc.Get("Feature.MitigationMonitor.History.Realtime"),
                ActiveHistoryKey == null))
        {
            ActiveHistoryKey = null;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(OmniLoc.Get("Feature.MitigationMonitor.History.Import")))
        {
            importFiles = replayStore.GetImportableFiles();
            ImGui.OpenPopup(ImportPopupID);
        }

        DrawImportPopup();
        if (items.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MitigationMonitor.History.Empty"));
        }
        else if (items.Count > 12)
        {
            using var child = ImRaii.Child(
                "##MitigationHistoryItems",
                new(0f, ImGui.GetTextLineHeightWithSpacing() * 12 * 2f),
                false);
            if (child)
            {
                DrawItems();
            }
        }
        else
        {
            DrawItems();
        }

        ImGui.EndPopup();
    }

    public void ClearSelection()
    {
        ActiveHistoryKey = null;
        historyVersion = -1;
    }

    public void ResetRuntime()
    {
        ClearSelection();
        items.Clear();
        importFiles = [];
    }

    private void RefreshItems()
    {
        if (historyVersion == combatLog.HistoryVersion)
        {
            return;
        }

        historyVersion = combatLog.CopyHistory(historyVersion, items);
        if (ActiveHistoryKey == null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item.Key == ActiveHistoryKey)
            {
                return;
            }
        }

        ActiveHistoryKey = null;
    }

    private void DrawItems()
    {
        using var table = ImRaii.Table(
            "##MitigationHistoryTable",
            2,
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX);
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("##history", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##export", ImGuiTableColumnFlags.WidthFixed);
        foreach (var item in items)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var zone = string.IsNullOrWhiteSpace(item.ZoneName)
                ? OmniLoc.Get("Feature.MitigationMonitor.History.UnknownZone")
                : item.ZoneName;
            var label = string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.MitigationMonitor.History.Item"),
                item.ElapsedLabel,
                zone,
                item.StartUTC.ToLocalTime());
            if (ImGui.Selectable($"{label}##{item.Key}", item.Key == ActiveHistoryKey))
            {
                ActiveHistoryKey = item.Key;
            }

            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"{OmniLoc.Get("Feature.MitigationMonitor.History.Export")}##{item.Key}"))
            {
                replayStore.Export(item);
            }
        }
    }

    private void DrawImportPopup()
    {
        if (!ImGui.BeginPopup(ImportPopupID))
        {
            return;
        }

        if (importFiles.Length == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MitigationMonitor.History.NoImportFiles"));
            ImGui.TextDisabled(replayStore.ExportDirectory);
        }
        else
        {
            foreach (var file in importFiles)
            {
                if (ImGui.Selectable($"{Path.GetFileName(file)}##{file}") && replayStore.Import(file) is { } imported)
                {
                    ActiveHistoryKey = combatLog.AddImported(imported);
                    historyVersion = -1;
                    ImGui.CloseCurrentPopup();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(file);
                }
            }
        }

        ImGui.EndPopup();
    }
}
