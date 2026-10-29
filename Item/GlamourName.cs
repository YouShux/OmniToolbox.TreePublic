using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Enums;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed class GlamourName : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Item/GlamourName-1.png"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "GlamourNameTitle",
            ["Description"] = "GlamourNameDescription"
        });

    private readonly TooltipManager.ItemTooltipUpdateDelegate tooltipHandler;
    private TooltipManager?                                   tooltipManager;

    public GlamourName()
    {
        tooltipHandler = OnItemTooltip;
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => tooltipManager?.Unreg(tooltipHandler));

        tooltipManager = TooltipManager.Instance();
        tooltipManager.RegItem(tooltipHandler);
        tooltipManager.TriggerItemDetailUpdate();
    }

    protected override void OnStop()
    {
        tooltipManager?.TriggerItemDetailUpdate();
        tooltipManager = null;
    }

    private void OnItemTooltip
    (
        ItemKind                          itemKind,
        uint                              itemID,
        ref List<TooltipItemModification> modifications
    )
    {
        if (itemKind == ItemKind.EventItem || itemID is 0 or 46982)
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
        var combinedName = builder.Builder
            .Append(originalName)
            .Append('\n')
            .Append(glamourName)
            .ToReadOnlySeString();
        modifications.Add(new()
        {
            Target = TooltipItemType.Name,
            Type = TooltipModificationType.Contribute,
            Text = combinedName
        });
        modifications.Add(new()
        {
            Target = TooltipItemType.GlamourName,
            Type = TooltipModificationType.Contribute,
            Text = default
        });
    }
}
