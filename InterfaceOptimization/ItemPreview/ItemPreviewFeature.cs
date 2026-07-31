using OmniToolbox.Collections;
using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Tooltips;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemPreview;

public sealed class ItemPreviewFeature : TreeHouseFeatureBase, IItemPreviewService
{
    private readonly ItemConfig config;
    private readonly ItemPreviewService itemPreviewService;
    private readonly ItemSupplementTooltip itemSupplementTooltip;
    private readonly CollectableValueTooltip collectableValueTooltip;
    private readonly MateriaTotalTooltip materiaTotalTooltip;
    private readonly ItemPreviewPanel panel;
    private ItemDetailImagePreview? imagePreview;

    public ItemPreviewFeature(
        ItemConfig config,
        ItemPreviewService itemPreviewService,
        ItemSupplementTooltip itemSupplementTooltip,
        ItemTooltipInventoryContext inventoryContext)
        : base(new(
            "item-preview",
            "Feature.ItemPreview.Title",
            "Feature.ItemPreview.Description",
            TreeHouseCategory.InterfaceOptimization))
    {
        this.config = config;
        this.itemPreviewService = itemPreviewService;
        this.itemSupplementTooltip = itemSupplementTooltip;
        collectableValueTooltip = new(inventoryContext);
        materiaTotalTooltip = new(inventoryContext);
        panel = new(config);
    }

    public override bool HasSettings => true;

    public bool CanPreview(uint? itemId) => IsEnabled && itemPreviewService.CanPreview(itemId);

    public bool CanPreview(CollectionEntry item) => IsEnabled && itemPreviewService.CanPreview(item);

    public bool Preview(uint itemId) => IsEnabled && itemPreviewService.Preview(itemId);

    public bool Preview(CollectionEntry item) => IsEnabled && itemPreviewService.Preview(item);

    public bool TryOn(uint itemId) => IsEnabled && itemPreviewService.TryOn(itemId);

    public void ClearPreview() => itemPreviewService.ClearPreview();

    public override bool DrawSettings()
    {
        if (!panel.Draw(out var tooltipChanged, out var imageContentChanged))
        {
            return false;
        }

        if (imageContentChanged)
        {
            imagePreview?.Refresh();
        }

        if (tooltipChanged)
        {
            itemSupplementTooltip.SetPreviewEnabled(IsEnabled);
            collectableValueTooltip.SetEnabled(IsEnabled && config.ShowCollectableValueInTooltip);
            materiaTotalTooltip.SetEnabled(IsEnabled && config.ShowMateriaTotalInTooltip);
        }

        return true;
    }

    protected override void OnEnable()
    {
        imagePreview = new(config.ImagePreview, itemPreviewService);
        try
        {
            itemSupplementTooltip.SetPreviewEnabled(true);
            collectableValueTooltip.SetEnabled(config.ShowCollectableValueInTooltip);
            materiaTotalTooltip.SetEnabled(config.ShowMateriaTotalInTooltip);
        }
        catch
        {
            materiaTotalTooltip.SetEnabled(false);
            collectableValueTooltip.SetEnabled(false);
            itemSupplementTooltip.SetPreviewEnabled(false);
            imagePreview?.Dispose();
            imagePreview = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            itemSupplementTooltip.SetPreviewEnabled(false);
            collectableValueTooltip.SetEnabled(false);
            materiaTotalTooltip.SetEnabled(false);
            itemPreviewService.ClearPreview();
        }
        finally
        {
            imagePreview?.Dispose();
            imagePreview = null;
        }
    }
}
