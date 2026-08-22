using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using OmniToolbox.Tooltips;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class CollectableValueTooltip
{
    private const ushort ShopColor = OmniTheme.ShopColorType;

    private const string CollectabilityKey = "ItemTooltip.CollectableReward.Collectability";
    private const string ExperienceKey = "ItemTooltip.CollectableReward.Experience";
    private const string ScripKey = "ItemTooltip.CollectableReward.Scrip";

    private static readonly uint[][] RowIDToJob =
    [
        [15, 23],
        [16, 24],
        [17, 25],
        [18, 26],
        [19, 27],
        [20, 28],
        [21, 29],
        [22, 30],
        [31],
        [32],
        [14],
    ];

    private readonly ItemTooltipInventoryContext inventoryContext;
    private readonly TooltipManager.ItemTooltipUpdateDelegate tooltipHandler;
    private Dictionary<uint, CollectableCachedDetails>? collectableCache;
    private long[] experiencePerLevel = [];
    private TooltipManager? tooltipManager;
    private IDisposable? inventoryLease;

    public CollectableValueTooltip(ItemTooltipInventoryContext inventoryContext)
    {
        this.inventoryContext = inventoryContext;
        tooltipHandler = OnItemTooltip;
    }

    public void SetEnabled(bool enabled)
    {
        if ((tooltipManager is not null) == enabled)
        {
            return;
        }

        if (enabled)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }

    private void Enable()
    {
        collectableCache ??= BuildCache();

        var manager = TooltipManager.Instance();
        var lease = inventoryContext.Acquire();
        try
        {
            manager.RegItem(tooltipHandler);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        tooltipManager = manager;
        inventoryLease = lease;
        manager.TriggerItemDetailUpdate();
    }

    private void Disable()
    {
        try
        {
            tooltipManager?.Unreg(tooltipHandler);
        }
        finally
        {
            inventoryLease?.Dispose();
            inventoryLease = null;
            tooltipManager?.TriggerItemDetailUpdate();
            tooltipManager = null;
        }
    }

    private Dictionary<uint, CollectableCachedDetails> BuildCache()
    {
        experiencePerLevel = BuildExperiencePerLevel();
        var jobInfos = BuildJobInfos();
        var cache = new Dictionary<uint, CollectableCachedDetails>();

        foreach (var collection in DService.Instance().Data.GetSubrowExcelSheet<CollectablesShopItem>())
        {
            foreach (var collectable in collection)
            {
                if (collectable.Item.RowId == 0 ||
                    collectable.CollectablesShopRefine.RowId == 0 ||
                    collectable.CollectablesShopRewardScrip.RowId == 0 ||
                    !TryResolveJobInfo(collectable.RowId, jobInfos, out var jobInfo))
                {
                    continue;
                }

                var rewardScrip = collectable.CollectablesShopRewardScrip.Value;
                var refine = collectable.CollectablesShopRefine.Value;
                var levelMax = collectable.LevelMax;
                cache.TryAdd(collectable.Item.RowId, new(
                            jobInfo.ExpArrayIndex,
                            jobInfo.Name,
                            collectable.LevelMin,
                            levelMax,
                            rewardScrip.Currency,
                            [
                                new(
                            refine.LowCollectability,
                            rewardScrip.LowReward,
                            CalculateExperience(rewardScrip.ExpRatioLow, levelMax)),
                        new(
                            refine.MidCollectability,
                            rewardScrip.MidReward,
                            CalculateExperience(rewardScrip.ExpRatioMid, levelMax)),
                        new(
                            refine.HighCollectability,
                            rewardScrip.HighReward,
                            CalculateExperience(rewardScrip.ExpRatioHigh, levelMax)),
                            ]));
            }
        }

        return cache;
    }

    private static JobInfo?[] BuildJobInfos()
    {
        var jobInfos = new JobInfo?[RowIDToJob.Length];
        foreach (var job in DService.Instance().Data.GetExcelSheet<ClassJob>())
        {
            var index = job.ClassJobCategory.RowId switch
            {
                33 when job.DohDolJobIndex is >= 0 and < 8 => job.DohDolJobIndex,
                32 when job.DohDolJobIndex is >= 0 and < 3 => job.DohDolJobIndex + 8,
                _ => -1,
            };
            if (index >= 0)
            {
                jobInfos[index] = new(job.ExpArrayIndex, job.Name.ExtractText());
            }
        }

        return jobInfos;
    }

    private static bool TryResolveJobInfo(uint rowID, JobInfo?[] jobInfos, out JobInfo jobInfo)
    {
        for (var index = 0; index < RowIDToJob.Length; index++)
        {
            for (var rowIndex = 0; rowIndex < RowIDToJob[index].Length; rowIndex++)
            {
                if (RowIDToJob[index][rowIndex] != rowID)
                {
                    continue;
                }

                if (jobInfos[index] is JobInfo resolved && !string.IsNullOrWhiteSpace(resolved.Name))
                {
                    jobInfo = resolved;
                    return true;
                }

                jobInfo = default;
                return false;
            }
        }

        jobInfo = default;
        return false;
    }

    private static long[] BuildExperiencePerLevel()
    {
        var experience = new List<long>();
        foreach (var paramGrow in DService.Instance().Data.GetExcelSheet<ParamGrow>())
        {
            if (paramGrow.ExpToNext == 0)
            {
                break;
            }

            experience.Add(paramGrow.ExpToNext);
        }

        return [.. experience];
    }

    private long CalculateExperience(int expRatio, int levelMax) =>
        expRatio <= 0 || levelMax < 0 || levelMax >= experiencePerLevel.Length
            ? 0
            : expRatio * experiencePerLevel[levelMax] / 1000;

    private void OnItemTooltip(
        ItemKind itemKind,
        uint itemID,
        ref List<TooltipItemModification> modifications)
    {
        if (itemKind == ItemKind.EventItem ||
            collectableCache is null ||
            !collectableCache.TryGetValue(itemID, out var itemDetails) ||
            tooltipManager is null ||
            tooltipManager.GetOriginalItemTooltipText(TooltipItemType.Description).IsEmpty)
        {
            return;
        }

        using var rented = new RentedSeStringBuilder();
        AppendRewardBlock(rented.Builder, itemDetails, itemID);
        modifications.Add(new()
        {
            Target = TooltipItemType.Description,
            Type = TooltipModificationType.Append,
            Text = rented.Builder.ToReadOnlySeString(),
        });
    }

    private void AppendRewardBlock(SeStringBuilder builder, CollectableCachedDetails itemDetails, uint itemID)
    {
        var useExperienceFloor = ShouldUseExperienceFloor(itemDetails);
        var scripColor = GetScripColor(itemDetails.ScripRewardType);

        builder
            .Append(OmniLoc.Get("ItemTooltip.CollectableReward.Header"))
            .AppendNewLine();

        if (!TryGetCurrentCollectability(itemID, out var collectability))
        {
            builder
                .Append("  ")
                .PushColorType(ShopColor)
                .Append(OmniLoc.Get("ItemTooltip.CollectableReward.LevelRange"))
                .PopColorType()
                .Append(itemDetails.LevelMin)
                .Append('-')
                .Append(itemDetails.LevelMax)
                .PushColorType(ShopColor)
                .Append(itemDetails.JobName)
                .PopColorType()
                .AppendNewLine();

            AppendGenericReward(
                builder,
                itemDetails.Rewards[0],
                itemDetails.Rewards[1].QualityRequired - 1,
                useExperienceFloor,
                scripColor);
            builder.AppendNewLine();
            AppendGenericReward(
                builder,
                itemDetails.Rewards[1],
                itemDetails.Rewards[2].QualityRequired - 1,
                useExperienceFloor,
                scripColor);
            builder.AppendNewLine();
            AppendGenericReward(builder, itemDetails.Rewards[2], null, useExperienceFloor, scripColor);
            return;
        }

        if (collectability < itemDetails.Rewards[0].QualityRequired)
        {
            builder
                .Append("  ")
                .PushColorType(16)
                .Append(OmniLoc.Get("ItemTooltip.CollectableReward.BelowMinimum"))
                .Append(' ')
                .Append(itemDetails.Rewards[0].QualityRequired)
                .PopColorType();
            return;
        }

        var reward = collectability < itemDetails.Rewards[1].QualityRequired
            ? itemDetails.Rewards[0]
            : collectability < itemDetails.Rewards[2].QualityRequired
                ? itemDetails.Rewards[1]
                : itemDetails.Rewards[2];
        AppendItemReward(builder, reward, useExperienceFloor, scripColor);
    }

    private static void AppendGenericReward(
        SeStringBuilder builder,
        CollectableReward reward,
        int? maxQuality,
        bool useExperienceFloor,
        ushort scripColor)
    {
        builder
            .Append("  ")
            .PushColorType(ShopColor)
            .Append(OmniLoc.Get(CollectabilityKey))
            .PopColorType()
            .Append(reward.QualityRequired)
            .Append(" - ");
        if (maxQuality.HasValue)
        {
            builder.Append(maxQuality.Value);
        }
        else
        {
            builder.Append(OmniLoc.Get("ItemTooltip.CollectableReward.Max"));
        }

        builder
            .Append("  ")
            .PushColorType(ShopColor)
            .Append(OmniLoc.Get(ExperienceKey))
            .PopColorType()
            .Append(OmniNumberFormatter.Format(useExperienceFloor ? 1000 : reward.Experience))
            .Append(' ')
            .PushColorType(ShopColor)
            .Append(OmniLoc.Get(ScripKey))
            .PopColorType();
        if (scripColor != 0)
        {
            builder.PushColorType(scripColor);
        }

        builder.Append(reward.ScriptRewardCount);
        if (scripColor != 0)
        {
            builder.PopColorType();
        }
    }

    private static void AppendItemReward(
        SeStringBuilder builder,
        CollectableReward reward,
        bool useExperienceFloor,
        ushort scripColor)
    {
        builder
            .Append("  ")
            .PushColorType(ShopColor)
            .Append(OmniLoc.Get(ExperienceKey))
            .PopColorType()
            .Append(OmniNumberFormatter.Format(useExperienceFloor ? 1000 : reward.Experience))
            .AppendNewLine()
            .Append("  ")
            .PushColorType(ShopColor)
            .Append(OmniLoc.Get(ScripKey))
            .PopColorType();
        if (scripColor != 0)
        {
            builder.PushColorType(scripColor);
        }

        builder.Append(reward.ScriptRewardCount);
        if (scripColor != 0)
        {
            builder.PopColorType();
        }
    }

    private bool TryGetCurrentCollectability(uint itemID, out int collectability)
    {
        collectability = 0;
        if (!inventoryContext.TryGet(itemID, out var item) ||
            (item.Flags & InventoryItem.ItemFlags.Collectable) == 0 ||
            item.SpiritbondOrCollectability <= 0)
        {
            return false;
        }

        collectability = item.SpiritbondOrCollectability;
        return true;
    }

    private bool ShouldUseExperienceFloor(CollectableCachedDetails itemDetails)
    {
        if (experiencePerLevel.Length == 0)
        {
            return false;
        }

        var playerState = PlayerState.Instance();
        if (playerState == null)
        {
            return false;
        }

        var currentJobLevel = playerState->ClassJobLevels[itemDetails.JobTableIndex];
        return itemDetails.LevelMax <= Math.Min(
            currentJobLevel - currentJobLevel % 10,
            experiencePerLevel.Length - 11);
    }

    private static ushort GetScripColor(int scripRewardType) => scripRewardType switch
    {
        2 or 4 => 522,
        6 or 7 => OmniTheme.OrangeColorType,
        _ => 0,
    };

    private readonly record struct JobInfo(int ExpArrayIndex, string Name);

    private sealed record CollectableCachedDetails(
        int JobTableIndex,
        string JobName,
        int LevelMin,
        int LevelMax,
        int ScripRewardType,
        CollectableReward[] Rewards);

    private readonly record struct CollectableReward(
        int QualityRequired,
        int ScriptRewardCount,
        long Experience);
}
