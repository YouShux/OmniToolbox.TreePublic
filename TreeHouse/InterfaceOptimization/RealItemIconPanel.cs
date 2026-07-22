using OmniToolbox.Config;
using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class RealItemIconPanel
{
    public static bool Draw(RealItemIconConfig config)
    {
        var facewearGlasses = config.FacewearGlasses;
        if (!ImGui.Checkbox(
                $"{OmniLoc.Get("Feature.RealItemIcon.FacewearGlasses")}##realItemIconFacewearGlasses",
                ref facewearGlasses))
        {
            return false;
        }

        config.FacewearGlasses = facewearGlasses;
        return true;
    }
}
