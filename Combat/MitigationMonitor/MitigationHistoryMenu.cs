using System.Globalization;
using System.IO;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

internal sealed class MitigationHistoryMenu(
    MitigationMonitorConfig config,
    MitigationCombatLog combatLog,
    MitigationReplayStore replayStore)
{
    private const string HISTORY_POPUP_ID = "##MitigationHistoryPopup";
    private const string IMPORT_POPUP_ID = "##MitigationImportPopup";

    private readonly List<MitigationCombatHistory> items = new(40);
    private string[] importFiles = [];
    private long historyVersion = -1;

    public string? ActiveHistoryKey { get; private set; }

    public void Open() => ImGui.OpenPopup(HISTORY_POPUP_ID);

    public void Draw()
    {
        OmniControls.SetNextAutoResizeWindowSizeConstraints(Vector2.Zero, ImGui.GetMainViewport().WorkSize);
        if (!ImGui.BeginPopup(HISTORY_POPUP_ID))
        {
            return;
        }

        ImGui.SetWindowFontScale(config.EffectiveScale);
        using var scale = new OmniTheme.ScaleScope(ImGui.GetFontSize() / OmniTheme.REFERENCE_FONT_SIZE);
        RefreshItems();
        if (ImGui.Selectable(
                OmniLoc.Get("Feature.MitigationMonitor.History.Realtime"),
                ActiveHistoryKey == null))
        {
            ActiveHistoryKey = null;
        }

        OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(OmniLoc.Get("Feature.MitigationMonitor.History.Import")).X);
        if (ImGui.SmallButton(OmniLoc.Get("Feature.MitigationMonitor.History.Import")))
        {
            importFiles = replayStore.GetImportableFiles();
            ImGui.OpenPopup(IMPORT_POPUP_ID);
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
                new(0f, MathF.Max(1f, MathF.Min(
                    ImGui.GetMainViewport().WorkSize.Y - ImGui.GetCursorPosY() -
                    ImGui.GetStyle().WindowPadding.Y - ImGui.GetStyle().WindowBorderSize * 2f,
                    ImGui.GetTextLineHeightWithSpacing() * 12 * 2f))),
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
        var exportWidth = OmniControls.CompactButtonSize(OmniLoc.Get("Feature.MitigationMonitor.History.Export")).X;
        using var table = OmniControls.DataTable(
            "##MitigationHistoryTable",
            [string.Empty, string.Empty],
            [ImGui.GetFontSize() * 12f, exportWidth],
            [ImGui.GetFontSize() * 20f, exportWidth], out _,
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX, stretchColumn: 0);
        if (!table)
        {
            return;
        }

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
            if (OmniControls.WrappedSelectable($"{label}##{item.Key}", item.Key == ActiveHistoryKey))
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
        OmniControls.SetNextAutoResizeWindowSizeConstraints(Vector2.Zero, ImGui.GetMainViewport().WorkSize);
        if (!ImGui.BeginPopup(IMPORT_POPUP_ID))
        {
            return;
        }

        ImGui.SetWindowFontScale(config.EffectiveScale);
        using var scale = new OmniTheme.ScaleScope(ImGui.GetFontSize() / OmniTheme.REFERENCE_FONT_SIZE);
        if (importFiles.Length == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MitigationMonitor.History.NoImportFiles"));
            using var wrap = ImRaii.TextWrapPos(0f);
            ImGui.TextDisabled(replayStore.ExportDirectory);
        }
        else
        {
            foreach (var file in importFiles)
            {
                if (OmniControls.WrappedSelectable($"{Path.GetFileName(file)}##{file}") && replayStore.Import(file) is { } imported)
                {
                    ActiveHistoryKey = combatLog.AddImported(imported);
                    historyVersion = -1;
                    ImGui.CloseCurrentPopup();
                }

                if (ImGui.IsItemHovered())
                {
                    OmniControls.HelpTooltip(file);
                }
            }
        }

        ImGui.EndPopup();
    }
}
