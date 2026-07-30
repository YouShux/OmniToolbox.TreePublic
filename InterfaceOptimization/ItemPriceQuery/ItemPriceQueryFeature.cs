using OmniToolbox.Config;
using OmniToolbox.Market;
using OmniToolbox.Tooltips;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemPriceQuery;

public sealed class ItemPriceQueryFeature : TreeHouseFeatureBase, IItemPriceQueryService
{
    private readonly MarketConfig config;
    private readonly MarketPriceService marketPriceService;
    private readonly ItemSupplementTooltip itemSupplementTooltip;
    private readonly ItemPriceQueryPanel panel;

    public ItemPriceQueryFeature(
        MarketConfig config,
        MarketPriceService marketPriceService,
        ItemSupplementTooltip itemSupplementTooltip)
        : base(new(
            "item-price-query",
            "Feature.ItemPriceQuery.Title",
            "Feature.ItemPriceQuery.Description",
            TreeHouseCategory.InterfaceOptimization))
    {
        this.config = config;
        this.marketPriceService = marketPriceService;
        this.itemSupplementTooltip = itemSupplementTooltip;
        panel = new(config);
    }

    public override bool HasSettings => true;

    public MarketPrice? Get(uint itemId) => IsEnabled ? marketPriceService.Get(itemId) : null;

    public void Request(uint itemId)
    {
        if (IsEnabled)
        {
            marketPriceService.Request(itemId);
        }
    }

    public override bool DrawSettings()
    {
        if (!panel.Draw())
        {
            return false;
        }

        itemSupplementTooltip.SetMarketEnabled(IsEnabled);
        return true;
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
}
