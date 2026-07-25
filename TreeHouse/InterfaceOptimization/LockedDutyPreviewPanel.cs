using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class LockedDutyPreviewPanel
{
    public static bool Draw(LockedDutyPreviewConfig config)
    {
        var changed = false;
        var autoHide = config.AutoHideWhenAllUnlocked;
        if (OmniControls.Checkbox(
                $"{OmniLoc.Get("Feature.LockedDutyPreview.AutoHide")}##lockedDutyPreviewAutoHide",
                ref autoHide))
        {
            config.AutoHideWhenAllUnlocked = autoHide;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.LockedDutyPreview.AutoHide.Help"));
        ImGui.SameLine(0f, OmniTheme.Scale(20f));
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.LockedDutyPreview.ClearExcluded"), false))
        {
            changed = config.ExcludedContentFinderConditionIds.Count > 0;
            config.ExcludedContentFinderConditionIds.Clear();
        }

        ImGui.SameLine(0f, OmniTheme.Scale(4f));
        ImGui.TextDisabled(string.Format(
            OmniLoc.Get("Feature.LockedDutyPreview.ExcludedCount"),
            config.ExcludedContentFinderConditionIds.Count));

        return changed;
    }
}
