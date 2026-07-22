using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.General;

internal sealed class MitigationRecordTable
{
    private static readonly float[] ColumnMinimumWidths = [44f, 70f, 38f, 42f, 34f];

    private readonly MitigationMonitorConfig config;
    private readonly MitigationCombatLog combatLog;
    private readonly Action saveConfig;
    private readonly MitigationRecordRenderer renderer;
    private readonly List<MitigationRecord> sourceRecords = new(256);
    private readonly List<MitigationRecord> visibleRecords = new(256);
    private readonly List<TargetFilterOption> targetOptions = new(8);
    private readonly HashSet<string> targetNames = new(StringComparer.Ordinal);
    private string? historyKey;
    private string? selectedTargetName;
    private string? filteredTargetName;
    private string tableId = "##MitigationRecords_100";
    private long sourceVersion = -1;
    private float tableScale = 1f;
    private bool filteredDotDamage = true;
    private bool columnWidthsDirty;

    public MitigationRecordTable(
        MitigationMonitorConfig config,
        MitigationCombatLog combatLog,
        Action saveConfig)
    {
        this.config = config;
        this.combatLog = combatLog;
        this.saveConfig = saveConfig;
        renderer = new(config);
    }

    public void Draw(string? selectedHistoryKey)
    {
        RefreshRecords(selectedHistoryKey);
        RefreshTableId();
        var flags = ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.BordersInnerV |
                    ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingFixedFit |
                    ImGuiTableFlags.NoPadOuterX;
        if (!config.Locked)
        {
            flags |= ImGuiTableFlags.Resizable;
        }

        using var table = ImRaii.Table(tableId, 6, flags, ImGui.GetContentRegionAvail());
        if (!table)
        {
            return;
        }

        var layout = CalculateInitialLayout(ImGui.GetContentRegionAvail().X);
        ImGui.TableSetupColumn("##time", ImGuiTableColumnFlags.WidthFixed, layout.Time);
        ImGui.TableSetupColumn("##action", ImGuiTableColumnFlags.WidthFixed, layout.Action);
        ImGui.TableSetupColumn("##target", ImGuiTableColumnFlags.WidthFixed, layout.Target);
        ImGui.TableSetupColumn("##damage", ImGuiTableColumnFlags.WidthFixed, layout.Damage);
        ImGui.TableSetupColumn("##mitigation", ImGuiTableColumnFlags.WidthFixed, layout.Mitigation);
        ImGui.TableSetupColumn("##status", ImGuiTableColumnFlags.WidthStretch, layout.Status);
        ImGui.TableSetupScrollFreeze(0, 1);
        DrawHeaders();

        if (visibleRecords.Count == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextDisabled(OmniLoc.Get("Feature.MitigationMonitor.NoData"));
        }
        else
        {
            for (var index = 0; index < visibleRecords.Count; index++)
            {
                renderer.Draw(visibleRecords[index], index);
            }
        }

        DrawTargetPopup();
        if (columnWidthsDirty && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            columnWidthsDirty = false;
            saveConfig();
        }
    }

    public void ResetRuntime()
    {
        sourceRecords.Clear();
        visibleRecords.Clear();
        targetOptions.Clear();
        targetNames.Clear();
        renderer.ResetRuntime();
        historyKey = null;
        selectedTargetName = null;
        filteredTargetName = null;
        sourceVersion = -1;
    }

    private void RefreshRecords(string? selectedHistoryKey)
    {
        if (!string.Equals(historyKey, selectedHistoryKey, StringComparison.Ordinal))
        {
            historyKey = selectedHistoryKey;
            selectedTargetName = null;
            sourceVersion = -1;
        }

        var currentVersion = historyKey == null
            ? combatLog.RecordsVersion
            : combatLog.HistoryVersion;
        if (sourceVersion != currentVersion)
        {
            sourceVersion = combatLog.CopyRecords(historyKey, sourceVersion, sourceRecords);
            BuildTargetOptions();
            NormalizeSelectedTarget();
            RebuildVisibleRecords();
            return;
        }

        if (filteredDotDamage != config.ShowDotDamage ||
            !string.Equals(filteredTargetName, selectedTargetName, StringComparison.Ordinal))
        {
            RebuildVisibleRecords();
        }
    }

    private void BuildTargetOptions()
    {
        targetOptions.Clear();
        targetNames.Clear();
        foreach (var record in sourceRecords)
        {
            if (record.Kind == MitigationRecordKind.Wipe ||
                string.IsNullOrWhiteSpace(record.TargetName) ||
                !targetNames.Add(record.TargetName))
            {
                continue;
            }

            targetOptions.Add(new(
                record.TargetName,
                record.TargetShortName,
                record.TargetJobName));
        }
    }

    private void NormalizeSelectedTarget()
    {
        if (selectedTargetName == null)
        {
            return;
        }

        foreach (var option in targetOptions)
        {
            if (option.Name == selectedTargetName)
            {
                return;
            }
        }

        selectedTargetName = null;
    }

    private void RebuildVisibleRecords()
    {
        visibleRecords.Clear();
        foreach (var record in sourceRecords)
        {
            if (!config.ShowDotDamage && record.SourceKind == DamageSourceKind.Dot ||
                selectedTargetName != null && record.TargetName != selectedTargetName)
            {
                continue;
            }

            visibleRecords.Add(record);
        }

        filteredDotDamage = config.ShowDotDamage;
        filteredTargetName = selectedTargetName;
    }

    private void RefreshTableId()
    {
        if (MathF.Abs(tableScale - OmniTheme.ScaleValue) <= 0.001f)
        {
            return;
        }

        tableScale = OmniTheme.ScaleValue;
        tableId = $"##MitigationRecords_{MathF.Round(tableScale * 100f):0}";
    }

    private TableLayout CalculateInitialLayout(float width)
    {
        Span<float> desired =
        [
            OmniTheme.Scale(config.TimeColumnWidth),
            OmniTheme.Scale(config.ActionColumnWidth),
            OmniTheme.Scale(config.TargetColumnWidth),
            OmniTheme.Scale(config.DamageColumnWidth),
            OmniTheme.Scale(config.MitigationColumnWidth)
        ];
        var desiredSum = 0f;
        var minimumSum = 0f;
        for (var index = 0; index < desired.Length; index++)
        {
            var minimum = OmniTheme.Scale(ColumnMinimumWidths[index]);
            desired[index] = MathF.Max(minimum, desired[index]);
            desiredSum += desired[index];
            minimumSum += minimum;
        }

        var firstColumnsWidth = MathF.Min(
            desiredSum,
            MathF.Max(minimumSum, width - OmniTheme.Scale(80f)));
        var scale = desiredSum <= 0f ? 1f : firstColumnsWidth / desiredSum;
        var time = MathF.Max(OmniTheme.Scale(ColumnMinimumWidths[0]), MathF.Floor(desired[0] * scale));
        var action = MathF.Max(OmniTheme.Scale(ColumnMinimumWidths[1]), MathF.Floor(desired[1] * scale));
        var target = MathF.Max(OmniTheme.Scale(ColumnMinimumWidths[2]), MathF.Floor(desired[2] * scale));
        var damage = MathF.Max(OmniTheme.Scale(ColumnMinimumWidths[3]), MathF.Floor(desired[3] * scale));
        var mitigation = MathF.Max(OmniTheme.Scale(ColumnMinimumWidths[4]), MathF.Floor(desired[4] * scale));
        return new(time, action, target, damage, mitigation, MathF.Max(
            OmniTheme.Scale(20f),
            width - time - action - target - damage - mitigation));
    }

    private void DrawHeaders()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        DrawHeader(0, "Feature.MitigationMonitor.Column.Time");
        DrawHeader(1, "Feature.MitigationMonitor.Column.Action");
        DrawTargetHeader();
        DrawHeader(3, "Feature.MitigationMonitor.Column.Damage");
        DrawHeader(4, "Feature.MitigationMonitor.Column.Mitigation");
        ImGui.TableSetColumnIndex(5);
        renderer.StatusColumnWidth = ImGui.GetContentRegionAvail().X + ImGui.GetStyle().CellPadding.X * 2f;
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.Column.Status"));
    }

    private void DrawHeader(int column, string labelKey)
    {
        ImGui.TableSetColumnIndex(column);
        CaptureColumnWidth(column);
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
    }

    private void DrawTargetHeader()
    {
        ImGui.TableSetColumnIndex(2);
        CaptureColumnWidth(2);
        var label = OmniLoc.Get("Feature.MitigationMonitor.Column.Target");
        if (selectedTargetName != null)
        {
            foreach (var option in targetOptions)
            {
                if (option.Name == selectedTargetName)
                {
                    label = string.IsNullOrWhiteSpace(option.ShortName) ? option.Name : option.ShortName;
                    break;
                }
            }
        }

        if (ImGui.Selectable($"{label}##MitigationTargetFilter"))
        {
            ImGui.OpenPopup("##MitigationTargetFilterPopup");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(OmniLoc.Get(selectedTargetName == null
                ? "Feature.MitigationMonitor.TargetFilter.Help"
                : "Feature.MitigationMonitor.TargetFilter.Active"));
        }
    }

    private void CaptureColumnWidth(int column)
    {
        if (column > 4 || config.Locked)
        {
            return;
        }

        var width = (ImGui.GetContentRegionAvail().X + ImGui.GetStyle().CellPadding.X * 2f) /
                    OmniTheme.ScaleValue;
        if (MathF.Abs(GetConfiguredColumnWidth(column) - width) > 0.5f)
        {
            SetConfiguredColumnWidth(column, width);
            columnWidthsDirty = true;
        }
    }

    private float GetConfiguredColumnWidth(int column) => column switch
    {
        0 => config.TimeColumnWidth,
        1 => config.ActionColumnWidth,
        2 => config.TargetColumnWidth,
        3 => config.DamageColumnWidth,
        _ => config.MitigationColumnWidth
    };

    private void SetConfiguredColumnWidth(int column, float width)
    {
        switch (column)
        {
            case 0:
                config.TimeColumnWidth = width;
                break;
            case 1:
                config.ActionColumnWidth = width;
                break;
            case 2:
                config.TargetColumnWidth = width;
                break;
            case 3:
                config.DamageColumnWidth = width;
                break;
            default:
                config.MitigationColumnWidth = width;
                break;
        }
    }

    private void DrawTargetPopup()
    {
        if (!ImGui.BeginPopup("##MitigationTargetFilterPopup"))
        {
            return;
        }

        if (ImGui.Selectable(
                OmniLoc.Get("Feature.MitigationMonitor.TargetFilter.All"),
                selectedTargetName == null))
        {
            selectedTargetName = null;
            ImGui.CloseCurrentPopup();
        }

        foreach (var option in targetOptions)
        {
            var label = string.IsNullOrWhiteSpace(option.JobName)
                ? option.Name
                : $"{option.JobName} {option.Name}";
            if (ImGui.Selectable($"{label}##{option.Name}", option.Name == selectedTargetName))
            {
                selectedTargetName = option.Name;
                ImGui.CloseCurrentPopup();
            }
        }

        if (targetOptions.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MitigationMonitor.TargetFilter.Empty"));
        }

        ImGui.EndPopup();
    }

    private readonly record struct TargetFilterOption(string Name, string ShortName, string JobName);

    private readonly record struct TableLayout(
        float Time,
        float Action,
        float Target,
        float Damage,
        float Mitigation,
        float Status);
}
