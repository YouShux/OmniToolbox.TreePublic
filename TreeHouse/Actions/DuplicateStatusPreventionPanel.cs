using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Config;
using OmniToolbox.UI;
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

        var enabledCount = 0;
        foreach (var enabled in config.EnabledActions.Values)
        {
            if (enabled)
            {
                enabledCount++;
            }
        }

        using var node = ImRaii.TreeNode(string.Format(
            OmniLoc.Get("Feature.DuplicateStatusPrevention.ActionCount"),
            enabledCount,
            config.EnabledActions.Count));
        if (!node)
        {
            return changed;
        }

        var rowHeight = MathF.Max(ImGui.GetFrameHeightWithSpacing(), OmniTheme.Scale(30f));
        using var table = ImRaii.Table(
            "##duplicateStatusActions",
            2,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(ImGui.GetContentRegionAvail().X, rowHeight * 9f + ImGui.GetStyle().ItemSpacing.Y));
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
        var iconSize = new Vector2(OmniTheme.Scale(24f));
        if (ImageHelper.GetGameIcon(action.Icon) is { } icon)
        {
            ImGui.Image(icon.Handle, iconSize);
            ImGui.SameLine();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(action.Name.ToString());
    }

    private static void DrawStatus(DuplicateStatusRule status, ref bool first)
    {
        if (!LuminaGetter.TryGetRow<LuminaStatus>(status.StatusId, out var statusRow))
        {
            return;
        }

        var iconSize = new Vector2(ImGui.GetTextLineHeightWithSpacing());
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
