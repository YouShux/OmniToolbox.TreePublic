using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Tooltips;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemQuery;

public sealed class ItemQueryFeature : TreeHouseFeatureBase
{
    private readonly ItemOwnedCountTooltip itemOwnedCountTooltip;
    private readonly ItemQueryPanel panel;

    public ItemQueryFeature(
        ItemConfig itemConfig,
        PlayerInventoryService inventoryService,
        ItemOwnedCountTooltip itemOwnedCountTooltip)
        : base(new(
            "item-query",
            "Feature.ItemQuery.Title",
            "Feature.ItemQuery.Description",
            TreeHouseCategory.InterfaceOptimization))
    {
        this.itemOwnedCountTooltip = itemOwnedCountTooltip;
        panel = new(
            itemConfig,
            inventoryService,
            itemOwnedCountTooltip.RefreshSettings);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => panel.Draw();

    protected override void OnEnable() => itemOwnedCountTooltip.SetFeatureEnabled(true);

    protected override void OnDisable() => itemOwnedCountTooltip.SetFeatureEnabled(false);
}
