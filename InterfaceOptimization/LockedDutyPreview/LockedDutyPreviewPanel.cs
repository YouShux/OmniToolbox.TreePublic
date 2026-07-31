using OmniToolbox.Config;
using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class LockedDutyPreviewPanel
{
    public static bool Draw(LockedDutyPreviewConfig config)
    {
        var changed = false;
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.LockedDutyPreview.ClearExcluded"), false))
        {
            changed = config.ExcludedContentFinderConditionIds.Count > 0;
            config.ExcludedContentFinderConditionIds.Clear();
        }

        ImGui.SameLine();
        ImGui.TextDisabled(string.Format(
            OmniLoc.Get("Feature.LockedDutyPreview.ExcludedCount"),
            config.ExcludedContentFinderConditionIds.Count));

        return changed;
    }
}
