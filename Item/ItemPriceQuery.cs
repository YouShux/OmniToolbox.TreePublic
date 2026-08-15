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
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Item/ItemPriceQuery-1.png"
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

        var showRegionAverage = config.IsAverageSalePriceVisible(MarketMetricScope.Region);
        var showDataCenterAverage = config.IsAverageSalePriceVisible(MarketMetricScope.DataCenter);
        var showWorldAverage = config.IsAverageSalePriceVisible(MarketMetricScope.World);
        if (DrawStatisticsScopeRow(
                "Settings.Market.AverageSalePriceScope",
                "averageSalePrice",
                ref showRegionAverage,
                ref showDataCenterAverage,
                ref showWorldAverage,
                true))
        {
            config.ShowRegionAverageSalePriceInTooltip = showRegionAverage;
            config.ShowDataCenterAverageSalePriceInTooltip = showDataCenterAverage;
            config.ShowWorldAverageSalePriceInTooltip = showWorldAverage;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var showRegionVelocity = config.IsDailySaleVelocityVisible(MarketMetricScope.Region);
        var showDataCenterVelocity = config.IsDailySaleVelocityVisible(MarketMetricScope.DataCenter);
        var showWorldVelocity = config.IsDailySaleVelocityVisible(MarketMetricScope.World);
        if (DrawStatisticsScopeRow(
                "Settings.Market.DailySaleVelocityScope",
                "dailySaleVelocity",
                ref showRegionVelocity,
                ref showDataCenterVelocity,
                ref showWorldVelocity,
                false))
        {
            config.ShowRegionDailySaleVelocityInTooltip = showRegionVelocity;
            config.ShowDataCenterDailySaleVelocityInTooltip = showDataCenterVelocity;
            config.ShowWorldDailySaleVelocityInTooltip = showWorldVelocity;
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
        config.ShowRegionAverageSalePriceInTooltip = defaults.ShowRegionAverageSalePriceInTooltip;
        config.ShowDataCenterAverageSalePriceInTooltip = defaults.ShowDataCenterAverageSalePriceInTooltip;
        config.ShowWorldAverageSalePriceInTooltip = defaults.ShowWorldAverageSalePriceInTooltip;
        config.ShowRegionDailySaleVelocityInTooltip = defaults.ShowRegionDailySaleVelocityInTooltip;
        config.ShowDataCenterDailySaleVelocityInTooltip = defaults.ShowDataCenterDailySaleVelocityInTooltip;
        config.ShowWorldDailySaleVelocityInTooltip = defaults.ShowWorldDailySaleVelocityInTooltip;
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

    private static bool DrawStatisticsScopeRow(
        string labelKey,
        string id,
        ref bool region,
        ref bool dataCenter,
        ref bool world,
        bool showHelp)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        var changed = OmniControls.Checkbox(
            $"{OmniLoc.Get("Settings.Market.Scope.Region")}##{id}Region",
            ref region);
        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        changed |= OmniControls.Checkbox(
            $"{OmniLoc.Get("Settings.Market.Scope.DataCenter")}##{id}DataCenter",
            ref dataCenter);
        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        changed |= OmniControls.Checkbox(
            $"{OmniLoc.Get("Settings.Market.Scope.World")}##{id}World",
            ref world);
        if (!showHelp)
        {
            return changed;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(6f));
        OmniControls.HelpIcon(OmniLoc.Get("Settings.Market.StatisticsScope.Help"));
        return changed;
    }
}
