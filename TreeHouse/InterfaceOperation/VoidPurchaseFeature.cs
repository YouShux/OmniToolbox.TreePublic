namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed class VoidPurchaseFeature : TreeHouseFeatureBase
{
    private readonly MarketPurchaseFeature marketPurchase;
    private readonly NpcShopPurchaseFeature npcShopPurchase;

    public VoidPurchaseFeature(
        MarketPurchaseFeature marketPurchase,
        NpcShopPurchaseFeature npcShopPurchase)
        : base(new(
            "void-purchase",
            "Feature.VoidPurchase.Title",
            "Feature.VoidPurchase.Description",
            TreeHouseCategory.InterfaceOperation,
            true,
            false,
            true))
    {
        this.marketPurchase = marketPurchase;
        this.npcShopPurchase = npcShopPurchase;
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = marketPurchase.DrawSettings();
        ImGui.Spacing();
        return npcShopPurchase.DrawSettings() || changed;
    }

    protected override void OnEnable()
    {
        marketPurchase.SetEnabled(true);
        try
        {
            npcShopPurchase.SetEnabled(true);
        }
        catch
        {
            marketPurchase.SetEnabled(false);
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            npcShopPurchase.SetEnabled(false);
        }
        finally
        {
            marketPurchase.SetEnabled(false);
        }
    }

    protected override void OnDispose()
    {
        npcShopPurchase.Dispose();
        marketPurchase.Dispose();
    }
}
