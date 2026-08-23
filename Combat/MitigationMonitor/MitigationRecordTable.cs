using System.Drawing;
using Dalamud.Interface;
using OmniToolbox.Config;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;

namespace OmniToolbox.TreePublic;

internal sealed class MitigationRecordTable
{
    private const float HeaderHeight = 28f;
    private static readonly float[] ColumnMinimumWidths = [44f, 70f, 60f, 42f, 34f];

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
    private long sourceVersion = -1;
    private bool filteredDotDamage = true;

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

    public void Draw(
        string? selectedHistoryKey,
        Action openHistory,
        Action<Vector2> collapse,
        Action toggleLock,
        Action openSettings)
    {
        RefreshRecords(selectedHistoryKey);
        var available = ImGui.GetContentRegionAvail();
        var width = MathF.Max(config.Scale(240f), available.X);
        var height = MathF.Max(config.Scale(60f), available.Y);
        var layout = CalculateLayout(width);
        var headerMin = ImGui.GetCursorScreenPos();
        DrawHeader(headerMin, width, layout, openHistory, collapse, toggleLock, openSettings);
        ImGui.SetCursorScreenPos(headerMin);
        ImGui.Dummy(new Vector2(width, config.Scale(HeaderHeight)));

        var bodyHeight = MathF.Max(config.Scale(30f), height - config.Scale(HeaderHeight));
        if (ImGui.BeginChild(
                "##MitigationRecordsBody",
                new Vector2(width, bodyHeight),
                false,
                ImGuiWindowFlags.NoScrollbar))
        {
            var bodyMin = ImGui.GetCursorScreenPos();
            if (visibleRecords.Count == 0)
            {
                DrawNoData(bodyMin, new Vector2(width, bodyHeight));
            }
            else
            {
                for (var index = 0; index < visibleRecords.Count; index++)
                {
                    var record = visibleRecords[index];
                    var rowMin = ImGui.GetCursorScreenPos();
                    var rowHeight = renderer.CalculateRowHeight(record, layout.Status);
                    if (ImGui.InvisibleButton($"##MitigationRecord{index}", new Vector2(width, rowHeight)))
                    {
                        ImGui.SetClipboardText(MitigationRecordCopyText.Build(record));
                        OmniNotifier.Banner(OmniLoc.Get("Feature.MitigationMonitor.Copy.Success"));
                    }

                    renderer.Draw(
                        record,
                        index,
                        rowMin,
                        width,
                        rowHeight,
                        layout,
                        ImGui.IsItemHovered(),
                        ImGui.IsItemActive());
                }
            }
        }

        ImGui.EndChild();
        DrawTargetPopup();
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

            targetOptions.Add(new(record.TargetName, record.TargetShortName, record.TargetJobName));
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

    private MitigationTableLayout CalculateLayout(float width)
    {
        Span<float> desired =
        [
            config.Scale(config.TimeColumnWidth),
            config.Scale(config.ActionColumnWidth),
            config.Scale(config.TargetColumnWidth),
            config.Scale(config.DamageColumnWidth),
            config.Scale(config.MitigationColumnWidth)
        ];
        var desiredSum = 0f;
        var minimumSum = 0f;
        for (var index = 0; index < desired.Length; index++)
        {
            var minimum = config.Scale(ColumnMinimumWidths[index]);
            desired[index] = MathF.Max(minimum, desired[index]);
            desiredSum += desired[index];
            minimumSum += minimum;
        }

        var statusMinimumWidth = GetHeaderControlsWidth() + config.Scale(40f);
        var firstColumnsWidth = MathF.Min(
            desiredSum,
            MathF.Max(minimumSum, width - statusMinimumWidth));
        var excessScale = desiredSum <= minimumSum
            ? 0f
            : (firstColumnsWidth - minimumSum) / (desiredSum - minimumSum);
        var time = ScaleColumnWidth(0, desired[0], excessScale);
        var action = ScaleColumnWidth(1, desired[1], excessScale);
        var target = ScaleColumnWidth(2, desired[2], excessScale);
        var damage = ScaleColumnWidth(3, desired[3], excessScale);
        var mitigation = ScaleColumnWidth(4, desired[4], excessScale);
        return new(
            time,
            action,
            target,
            damage,
            mitigation,
            MathF.Max(config.Scale(20f), width - time - action - target - damage - mitigation));
    }

    private float ScaleColumnWidth(int index, float desiredWidth, float excessScale)
    {
        var minimumWidth = config.Scale(ColumnMinimumWidths[index]);
        return MathF.Max(
            minimumWidth,
            MathF.Floor(minimumWidth + (desiredWidth - minimumWidth) * excessScale));
    }

    private void DrawHeader(
        Vector2 min,
        float width,
        MitigationTableLayout layout,
        Action openHistory,
        Action<Vector2> collapse,
        Action toggleLock,
        Action openSettings)
    {
        var height = config.Scale(HeaderHeight);
        ImGui.GetWindowDrawList().AddRectFilled(
            min,
            min + new Vector2(width, height),
            OmniTheme.Color(KnownColor.White.ToVector4() with { W = 0.035f }));

        var x = min.X;
        DrawHeaderText("Feature.MitigationMonitor.Column.Time", x, layout.Time, min.Y);
        x += layout.Time;
        DrawHeaderText("Feature.MitigationMonitor.Column.Action", x, layout.Action, min.Y);
        x += layout.Action;
        DrawTargetHeader(new(x, min.Y), layout.Target);
        x += layout.Target;
        DrawHeaderText("Feature.MitigationMonitor.Column.Damage", x, layout.Damage, min.Y);
        x += layout.Damage;
        DrawHeaderText("Feature.MitigationMonitor.Column.Mitigation", x, layout.Mitigation, min.Y);
        x += layout.Mitigation;
        DrawHeaderText(
            "Feature.MitigationMonitor.Column.Status",
            x,
            MathF.Max(config.Scale(40f), layout.Status - GetHeaderControlsWidth()),
            min.Y);
        DrawHeaderControls(min, width, openHistory, collapse, toggleLock, openSettings);
        DrawResizeHandles(min, layout, width);

        if (!config.Locked &&
            ImGui.IsMouseHoveringRect(min, min + new Vector2(width, height)) &&
            ImGui.IsMouseDragging(ImGuiMouseButton.Left) &&
            !ImGui.IsAnyItemActive())
        {
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }
    }

    private void DrawHeaderText(string key, float x, float width, float y)
    {
        var text = MitigationRecordRenderer.FitText(
            OmniLoc.Get(key),
            MathF.Max(config.Scale(4f), width - config.Scale(8f)));
        var textSize = ImGui.CalcTextSize(text);
        ImGui.GetWindowDrawList().AddText(
            new Vector2(
                x + MathF.Max(0f, (width - textSize.X) * 0.5f),
                y + MathF.Max(config.Scale(1f), (config.Scale(HeaderHeight) - textSize.Y) * 0.5f)),
            OmniTheme.Color(KnownColor.White.ToVector4()),
            text);
    }

    private void DrawTargetHeader(Vector2 min, float width)
    {
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##MitigationTargetFilterHeader", new Vector2(width, config.Scale(HeaderHeight)));
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

        var caret = FontAwesomeIcon.CaretDown.ToIconString();
        var caretSize = ImGui.CalcTextSize(caret);

        var display = MitigationRecordRenderer.FitText(
            label,
            MathF.Max(config.Scale(4f), width - caretSize.X - config.Scale(12f)));
        var textSize = ImGui.CalcTextSize(display);
        var startX = min.X + MathF.Max(0f, (width - textSize.X - caretSize.X - config.Scale(4f)) * 0.5f);
        var active = selectedTargetName != null;
        if (ImGui.IsItemHovered() || active)
        {
            ImGui.GetWindowDrawList().AddRectFilled(
                min + config.Scale(new Vector2(2f, 3f)),
                min + new Vector2(width, config.Scale(HeaderHeight)) - config.Scale(new Vector2(2f, 3f)),
                OmniTheme.Color((active ? KnownColor.SteelBlue : KnownColor.White).ToVector4() with { W = active ? 0.18f : 0.08f }),
                config.Scale(3f));
        }

        var textY = min.Y + MathF.Max(config.Scale(1f), (config.Scale(HeaderHeight) - textSize.Y) * 0.5f);
        ImGui.GetWindowDrawList().AddText(
            new Vector2(startX, textY),
            OmniTheme.Color(active ? KnownColor.LightSkyBlue.ToVector4() : KnownColor.White.ToVector4()),
            display);
        ImGui.GetWindowDrawList().AddText(
            new Vector2(startX + textSize.X + config.Scale(4f), min.Y + MathF.Max(config.Scale(1f), (config.Scale(HeaderHeight) - caretSize.Y) * 0.5f)),
            OmniTheme.Color(KnownColor.White.ToVector4() with { W = ImGui.IsItemHovered() || active ? 1f : 0.72f }),
            caret);

        if (ImGui.IsItemClicked())
        {
            ImGui.OpenPopup("##MitigationTargetFilterPopup");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(OmniLoc.Get(active
                ? "Feature.MitigationMonitor.TargetFilter.Active"
                : "Feature.MitigationMonitor.TargetFilter.Help"));
        }
    }

    private void DrawHeaderControls(
        Vector2 headerMin,
        float width,
        Action openHistory,
        Action<Vector2> collapse,
        Action toggleLock,
        Action openSettings)
    {
        var buttonSize = config.Scale(new Vector2(22f, 20f));
        var spacing = config.Scale(3f);
        var x = headerMin.X + width - config.Scale(4f) - buttonSize.X * 4f - spacing * 3f;
        var y = headerMin.Y +
                MathF.Max(config.Scale(1f), (config.Scale(HeaderHeight) - buttonSize.Y) * 0.5f) +
                config.Scale(1f);
        DrawIconButton(FontAwesomeIcon.History, "##MitigationHistory", "Feature.MitigationMonitor.History.Title", new(x, y), buttonSize, openHistory);
        x += buttonSize.X + spacing;
        var collapsePosition = new Vector2(x, y);
        DrawIconButton(FontAwesomeIcon.ChevronCircleUp, "##MitigationCollapse", "Feature.MitigationMonitor.Collapse", collapsePosition, buttonSize, () => collapse(collapsePosition));
        x += buttonSize.X + spacing;
        DrawIconButton(
            config.Locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
            "##MitigationLock",
            config.Locked ? "Feature.MitigationMonitor.Unlock" : "Feature.MitigationMonitor.Lock",
            new(x, y),
            buttonSize,
            toggleLock);
        x += buttonSize.X + spacing;
        DrawIconButton(FontAwesomeIcon.Cog, "##MitigationSettings", "Feature.MitigationMonitor.Settings", new(x, y), buttonSize, openSettings);
    }

    private static void DrawIconButton(
        FontAwesomeIcon icon,
        string id,
        string tooltipKey,
        Vector2 position,
        Vector2 size,
        Action action)
    {
        ImGui.SetCursorScreenPos(position);
        if (ImGui.InvisibleButton(id, size))
        {
            action();
        }

        var text = icon.ToIconString();
        var textSize = ImGui.CalcTextSize(text) * 0.82f;
        ImGui.GetWindowDrawList().AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize() * 0.82f,
            position + new Vector2(
                MathF.Max(0f, (size.X - textSize.X) * 0.5f),
                MathF.Max(0f, (size.Y - textSize.Y) * 0.5f)),
            OmniTheme.Color((ImGui.IsItemActive() ? KnownColor.LightSkyBlue : KnownColor.White).ToVector4() with
            {
                W = ImGui.IsItemHovered() || ImGui.IsItemActive() ? 1f : 0.90f
            }),
            text);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(OmniLoc.Get(tooltipKey));
        }
    }

    private float GetHeaderControlsWidth() => config.Scale(22f * 4f + 3f * 3f + 8f);

    private void DrawResizeHandles(Vector2 headerMin, MitigationTableLayout layout, float totalWidth)
    {
        var x = headerMin.X;
        Span<float> widths = [layout.Time, layout.Action, layout.Target, layout.Damage, layout.Mitigation];
        for (var index = 0; index < widths.Length; index++)
        {
            x += widths[index];
            DrawResizeHandle(index, x, headerMin.Y, layout, totalWidth);
        }
    }

    private void DrawResizeHandle(int index, float x, float y, MitigationTableLayout layout, float totalWidth)
    {
        var handleWidth = config.Scale(7f);
        var min = new Vector2(x - handleWidth * 0.5f, y);
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton(
            $"##MitigationColumnResize{index}",
            new Vector2(handleWidth, config.Scale(HeaderHeight)));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (hovered || active)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }

        ImGui.GetWindowDrawList().AddLine(
            new Vector2(x, y + config.Scale(hovered || active ? 2f : 6f)),
            new Vector2(x, y + config.Scale(HeaderHeight - (hovered || active ? 2f : 6f))),
            OmniTheme.Color((hovered || active ? KnownColor.LightSkyBlue : KnownColor.LightSlateGray).ToVector4() with
            {
                W = active ? 0.95f : hovered ? 0.65f : 0.46f
            }),
            config.Scale(hovered || active ? 2f : 1f));

        if (active && MathF.Abs(ImGui.GetIO().MouseDelta.X) > 0.01f)
        {
            ResizeColumn(index, ImGui.GetIO().MouseDelta.X, layout, totalWidth);
        }

        if (ImGui.IsItemDeactivated())
        {
            saveConfig();
        }
    }

    private void ResizeColumn(int index, float delta, MitigationTableLayout layout, float totalWidth)
    {
        Span<float> widths = [layout.Time, layout.Action, layout.Target, layout.Damage, layout.Mitigation];
        if (index < widths.Length - 1)
        {
            var applied = Math.Clamp(
                delta,
                config.Scale(ColumnMinimumWidths[index]) - widths[index],
                widths[index + 1] - config.Scale(ColumnMinimumWidths[index + 1]));
            widths[index] += applied;
            widths[index + 1] -= applied;
        }
        else
        {
            var usedBeforeMitigation = widths[0] + widths[1] + widths[2] + widths[3];
            var maximum = MathF.Max(
                config.Scale(ColumnMinimumWidths[4]),
                totalWidth -
                usedBeforeMitigation -
                GetHeaderControlsWidth() -
                config.Scale(40f));
            widths[4] = Math.Clamp(
                widths[4] + delta,
                config.Scale(ColumnMinimumWidths[4]),
                maximum);
        }

        config.TimeColumnWidth = widths[0] / config.ScaleValue;
        config.ActionColumnWidth = widths[1] / config.ScaleValue;
        config.TargetColumnWidth = widths[2] / config.ScaleValue;
        config.DamageColumnWidth = widths[3] / config.ScaleValue;
        config.MitigationColumnWidth = widths[4] / config.ScaleValue;
    }

    private void DrawNoData(Vector2 bodyMin, Vector2 bodySize)
    {
        var center = bodyMin + bodySize * 0.5f;
        var color = OmniTheme.Color(KnownColor.Gainsboro.ToVector4() with { W = 0.72f });
        var shadow = OmniTheme.Color(KnownColor.Black.ToVector4() with { W = 0.42f });
        var iconSize = config.Scale(new Vector2(32f, 24f));
        var iconMin = new Vector2(
            MathF.Floor(center.X - iconSize.X * 0.5f),
            MathF.Floor(center.Y - config.Scale(34f)));
        var iconMax = iconMin + iconSize;
        ImGui.GetWindowDrawList().AddRect(
            iconMin + config.Scale(new Vector2(1f)),
            iconMax + config.Scale(new Vector2(1f)),
            shadow,
            config.Scale(4f),
            ImDrawFlags.None,
            config.Scale(2.5f));
        ImGui.GetWindowDrawList().AddRect(
            iconMin,
            iconMax,
            color,
            config.Scale(4f),
            ImDrawFlags.None,
            config.Scale(2.5f));
        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(iconMin.X + config.Scale(7f), iconMin.Y + config.Scale(13f)),
            new Vector2(iconMax.X - config.Scale(7f), iconMax.Y - config.Scale(5f)),
            color,
            config.Scale(2f));

        var text = OmniLoc.Get("Feature.MitigationMonitor.NoData");
        var textSize = ImGui.CalcTextSize(text);
        var textPosition = new Vector2(
            MathF.Floor(center.X - textSize.X * 0.5f),
            MathF.Floor(iconMax.Y + config.Scale(10f)));
        ImGui.GetWindowDrawList().AddText(textPosition + config.Scale(new Vector2(1f)), shadow, text);
        ImGui.GetWindowDrawList().AddText(textPosition, color, text);
    }

    private void DrawTargetPopup()
    {
        if (!ImGui.BeginPopup("##MitigationTargetFilterPopup"))
        {
            return;
        }

        ImGui.SetWindowFontScale(config.EffectiveScale);
        if (ImGui.Selectable(OmniLoc.Get("Feature.MitigationMonitor.TargetFilter.All"), selectedTargetName == null))
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
}

internal readonly record struct MitigationTableLayout(
    float Time,
    float Action,
    float Target,
    float Damage,
    float Mitigation,
    float Status);
