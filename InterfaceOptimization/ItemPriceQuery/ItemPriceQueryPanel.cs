using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemPriceQuery;

internal sealed class ItemPriceQueryPanel(MarketConfig config)
{
    public bool Draw()
    {
        var showRegionPrice = config.ShowRegionPriceInTooltip;
        var showOriginalWorldPrice = config.ShowOriginalWorldPriceInTooltip;
        var changed = DrawScopeRow(
            "Settings.Market.MinimumPriceScope",
            "minimumPrice",
            ref showRegionPrice,
            ref showOriginalWorldPrice,
            OmniLoc.Get("Settings.Market.Scope.Help"));
        if (changed)
        {
            config.ShowRegionPriceInTooltip = showRegionPrice;
            config.ShowOriginalWorldPriceInTooltip = showOriginalWorldPrice;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var showRegionHistory = config.ShowRegionRecentPurchaseInTooltip;
        var showOriginalWorldHistory = config.ShowOriginalWorldRecentPurchaseInTooltip;
        if (DrawScopeRow(
                "Settings.Market.RecentPurchaseScope",
                "recentPurchase",
                ref showRegionHistory,
                ref showOriginalWorldHistory))
        {
            config.ShowRegionRecentPurchaseInTooltip = showRegionHistory;
            config.ShowOriginalWorldRecentPurchaseInTooltip = showOriginalWorldHistory;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var statisticsScope = config.StatisticsScope;
        if (DrawStatisticsScopeRow(ref statisticsScope))
        {
            config.StatisticsScope = statisticsScope;
            changed = true;
        }

        return changed;
    }

    private static bool DrawScopeRow(
        string labelKey,
        string id,
        ref bool region,
        ref bool originalWorld,
        string? helpText = null)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        var changed = OmniControls.Checkbox(
            $"{OmniLoc.Get("Settings.Market.Scope.Region")}##{id}Region",
            ref region);
        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        changed |= OmniControls.Checkbox(
            $"{OmniLoc.Get("Settings.Market.Scope.OriginalWorld")}##{id}OriginalWorld",
            ref originalWorld);
        if (helpText is not null)
        {
            ImGui.SameLine(0f, OmniTheme.Scale(6f));
            OmniControls.HelpIcon(helpText);
        }

        return changed;
    }

    private static bool DrawStatisticsScopeRow(ref MarketMetricScope scope)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Settings.Market.StatisticsScope"));
        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        var changed = DrawStatisticsScopeOption(
            "Settings.Market.Scope.Region",
            "##statisticsRegion",
            MarketMetricScope.Region,
            ref scope);
        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        changed |= DrawStatisticsScopeOption(
            "Settings.Market.Scope.DataCenter",
            "##statisticsDataCenter",
            MarketMetricScope.DataCenter,
            ref scope);
        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        changed |= DrawStatisticsScopeOption(
            "Settings.Market.Scope.World",
            "##statisticsWorld",
            MarketMetricScope.World,
            ref scope);
        ImGui.SameLine(0f, OmniTheme.Scale(6f));
        OmniControls.HelpIcon(OmniLoc.Get("Settings.Market.StatisticsScope.Help"));
        return changed;
    }

    private static bool DrawStatisticsScopeOption(
        string labelKey,
        string id,
        MarketMetricScope option,
        ref MarketMetricScope scope)
    {
        var selected = scope == option;
        if (!OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}{id}", ref selected) || !selected)
        {
            return false;
        }

        scope = option;
        return true;
    }
}
