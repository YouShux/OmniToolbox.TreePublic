using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Market;
using OmniToolbox.Tooltips;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed class ItemPriceQuery : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ItemPriceQueryTitle"),
        Description = OmniLoc.Get("ItemPriceQueryDescription"),
        Category = ModuleCategory.InterfaceOptimization
    };

    private readonly MarketConfig config;
    private readonly MarketPriceService marketPriceService;
    private readonly ItemSupplementTooltip itemSupplementTooltip;

    public ItemPriceQuery(
        MarketConfig config,
        MarketPriceService marketPriceService,
        ItemSupplementTooltip itemSupplementTooltip)
    {
        this.config = config;
        this.marketPriceService = marketPriceService;
        this.itemSupplementTooltip = itemSupplementTooltip;
    }

    public override bool HasSettings => true;

    public MarketPrice? Get(uint itemID) => IsEnabled ? marketPriceService.Get(itemID) : null;

    public void Request(uint itemID)
    {
        if (IsEnabled)
        {
            marketPriceService.Request(itemID);
        }
    }

    public override bool DrawSettings()
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

        if (changed)
        {
            itemSupplementTooltip.SetMarketEnabled(IsEnabled);
        }

        return changed;
    }

    public override bool ResetSettings()
    {
        var defaults = new MarketConfig();
        config.ShowRegionPriceInTooltip = defaults.ShowRegionPriceInTooltip;
        config.ShowOriginalWorldPriceInTooltip = defaults.ShowOriginalWorldPriceInTooltip;
        config.ShowRegionRecentPurchaseInTooltip = defaults.ShowRegionRecentPurchaseInTooltip;
        config.ShowOriginalWorldRecentPurchaseInTooltip = defaults.ShowOriginalWorldRecentPurchaseInTooltip;
        config.StatisticsScope = defaults.StatisticsScope;
        return true;
    }

    protected override void OnEnable() => itemSupplementTooltip.SetMarketEnabled(true);

    protected override void OnDisable() => itemSupplementTooltip.SetMarketEnabled(false);

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
