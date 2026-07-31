using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace OmniToolbox.TreeHouse.Actions;

internal static class DuplicateStatusPreventionPanel
{
    public static bool Draw(DuplicateStatusPreventionConfig config)
    {
        var changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.DuplicateStatusPrevention.OverlapThreshold"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(100f));
        var overlapThreshold = config.OverlapThreshold;
        if (OmniControls.InputFloat("##duplicateStatusOverlapThreshold", ref overlapThreshold, 0f, 0f, "%.1f"))
        {
            config.OverlapThreshold = overlapThreshold;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        OmenTools.ImGuiOm.ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DuplicateStatusPrevention.OverlapThreshold.Help"));
        ImGui.Spacing();

        var enabledCount = 0;
        foreach (var enabled in config.EnabledActions.Values)
        {
            if (enabled)
            {
                enabledCount++;
            }
        }

        ImGui.TextUnformatted(string.Format(
            OmniLoc.Get("Feature.DuplicateStatusPrevention.ActionCount"),
            enabledCount,
            config.EnabledActions.Count));

        var rowHeight = MathF.Max(ImGui.GetFrameHeightWithSpacing(), OmniTheme.Scale(34f));
        var visibleRows = Math.Clamp(config.EnabledActions.Count, 1, 5);
        using var cellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(ImGui.GetStyle().CellPadding.X, OmniTheme.Scale(3f)));
        using var framePadding = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, OmniTheme.Scale(3f)));
        using var table = ImRaii.Table(
            "##duplicateStatusActions",
            2,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                rowHeight * (visibleRows + 1) + ImGui.GetStyle().ItemSpacing.Y));
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Action"), ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Status"), ImGuiTableColumnFlags.WidthStretch, 0.5f);
        OmniControls.ScrollableTableHeadersRow();

        foreach (var action in DuplicateStatusPreventionRules.OrderedActions)
        {
            if (!LuminaGetter.TryGetRow<LuminaAction>(action.Key, out var actionRow))
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = config.EnabledActions[action.Key];
            if (OmniControls.Checkbox($"##duplicateStatusAction{action.Key}", ref enabled))
            {
                config.EnabledActions[action.Key] = enabled;
                changed = true;
            }

            ImGui.SameLine();
            DrawAction(actionRow);

            ImGui.TableNextColumn();
            var first = true;
            foreach (var status in action.Value.Statuses)
            {
                DrawStatus(status, ref first);
            }

            if (action.Value.SecondStatuses is not null)
            {
                foreach (var status in action.Value.SecondStatuses)
                {
                    DrawStatus(status, ref first);
                }
            }
        }

        return changed;
    }

    private static void DrawAction(LuminaAction action)
    {
        var iconSize = new Vector2(MathF.Max(
            OmniTheme.Scale(24f),
            ImGui.GetTextLineHeightWithSpacing() + OmniTheme.Scale(6f)));
        FramedGameIcon.DrawAction(action, iconSize);
        ImGui.SameLine();

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(action.Name.ToString());
    }

    private static void DrawStatus(DuplicateStatusRule status, ref bool first)
    {
        if (!LuminaGetter.TryGetRow<LuminaStatus>(status.StatusId, out var statusRow))
        {
            return;
        }

        var iconSize = OmniTheme.StatusIconSize(MathF.Max(
            OmniTheme.Scale(24f),
            ImGui.GetTextLineHeightWithSpacing() + OmniTheme.Scale(10f)));
        if (!first && ImGui.GetContentRegionAvail().X >= iconSize.X + ImGui.GetStyle().ItemSpacing.X)
        {
            ImGui.SameLine();
        }

        if (ImageHelper.GetGameIcon(statusRow.Icon) is { } icon)
        {
            ImGui.Image(icon.Handle, iconSize);
        }
        else
        {
            ImGui.TextUnformatted(statusRow.Name.ToString());
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(string.Format(
                OmniLoc.Get("Feature.DuplicateStatusPrevention.StatusTooltip"),
                statusRow.Name,
                OmniLoc.Get(status.DetectionTarget switch
                {
                    StatusDetectionTarget.Self => "Feature.DuplicateStatusPrevention.Target.Self",
                    StatusDetectionTarget.Member => "Feature.DuplicateStatusPrevention.Target.Member",
                    _ => "Feature.DuplicateStatusPrevention.Target.Target"
                })));
        }

        first = false;
    }
}
