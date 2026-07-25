using OmniToolbox.Config;
using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class MateriaTotalPanel
{
    public static bool Draw(MateriaTotalConfig config)
    {
        var highlightStats = config.HighlightStats;
        var changed = OmniControls.Checkbox(
            $"{OmniLoc.Get("Feature.MateriaTotal.HighlightStats")}##materiaTotalHighlightStats",
            ref highlightStats);
        if (changed)
        {
            config.HighlightStats = highlightStats;
        }

        var showTotal = config.ShowTotal;
        if (OmniControls.Checkbox(
            $"{OmniLoc.Get("Feature.MateriaTotal.ShowTotal")}##materiaTotalShowTotal",
            ref showTotal))
        {
            config.ShowTotal = showTotal;
            changed = true;
        }

        return changed;
    }
}
