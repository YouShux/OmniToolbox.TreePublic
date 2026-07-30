using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Enums;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class GlamourNameFeature : TreeHouseFeatureBase
{
    private readonly TooltipManager.ItemTooltipUpdateDelegate tooltipHandler;
    private TooltipManager? tooltipManager;

    public GlamourNameFeature() : base(new(
        "glamour-name",
        "Feature.GlamourName.Title",
        "Feature.GlamourName.Description",
        TreeHouseCategory.InterfaceOptimization))
    {
        tooltipHandler = OnItemTooltip;
    }

    protected override void OnEnable()
    {
        tooltipManager = TooltipManager.Instance();
        tooltipManager.RegItem(tooltipHandler);
        tooltipManager.TriggerItemDetailUpdate();
    }

    protected override void OnDisable()
    {
        var manager = tooltipManager!;
        manager.Unreg(tooltipHandler);
        manager.TriggerItemDetailUpdate();
        tooltipManager = null;
    }

    private void OnItemTooltip(
        ItemKind itemKind,
        uint itemId,
        ref List<TooltipItemModification> modifications)
    {
        if (itemKind == ItemKind.EventItem || itemId is 0 or 46982)
        {
            return;
        }

        var manager = tooltipManager!;
        var originalName = manager.GetOriginalItemTooltipText(TooltipItemType.Name);
        var glamourName = manager.GetOriginalItemTooltipText(TooltipItemType.GlamourName);
        if (originalName.IsEmpty || glamourName.IsEmpty)
        {
            return;
        }

        var originalText = originalName.ExtractText();
        var glamourText = glamourName.ExtractText();
        if (string.IsNullOrWhiteSpace(originalText) ||
            string.IsNullOrWhiteSpace(glamourText) ||
            string.Equals(originalText, glamourText, StringComparison.Ordinal))
        {
            return;
        }

        using var builder = new RentedSeStringBuilder();
        builder.Builder
            .Append(originalName)
            .Append('\n')
            .Append(glamourName);
        modifications.Add(new()
        {
            Target = TooltipItemType.Name,
            Type = TooltipModificationType.Contribute,
            Text = builder.Builder.ToReadOnlySeString()
        });
    }
}
