using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using OmniToolbox.Tooltips;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed unsafe class CollectableValuePreviewFeature : TreeHouseFeatureBase
{
    private const ushort Green = 504;
    private const ushort Orange = 500;
    private const ushort Purple = 522;
    private const ushort BelowMinimumColor = 16;

    private const string CollectabilityKey = "Feature.CollectableValuePreview.Collectability";
    private const string ExperienceKey = "Feature.CollectableValuePreview.Experience";
    private const string ScripKey = "Feature.CollectableValuePreview.Scrip";

    private static readonly uint[][] RowIdToJob =
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

    public CollectableValuePreviewFeature(ItemTooltipInventoryContext inventoryContext)
        : base(new(
            "collectable-value-preview",
            "Feature.CollectableValuePreview.Title",
            "Feature.CollectableValuePreview.Description",
            TreeHouseCategory.InterfaceOptimization))
    {
        this.inventoryContext = inventoryContext;
        tooltipHandler = OnItemTooltip;
    }

    protected override void OnEnable()
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

    protected override void OnDisable()
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
        var jobInfos = new JobInfo?[RowIdToJob.Length];
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

    private static bool TryResolveJobInfo(uint rowId, JobInfo?[] jobInfos, out JobInfo jobInfo)
    {
        for (var index = 0; index < RowIdToJob.Length; index++)
        {
            for (var rowIndex = 0; rowIndex < RowIdToJob[index].Length; rowIndex++)
            {
                if (RowIdToJob[index][rowIndex] != rowId)
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
        uint itemId,
        ref List<TooltipItemModification> modifications)
    {
        if (itemKind == ItemKind.EventItem ||
            collectableCache is null ||
            !collectableCache.TryGetValue(itemId, out var itemDetails) ||
            tooltipManager is null ||
            tooltipManager.GetOriginalItemTooltipText(TooltipItemType.Description).IsEmpty)
        {
            return;
        }

        using var rented = new RentedSeStringBuilder();
        AppendRewardBlock(rented.Builder, itemDetails, itemId);
        modifications.Add(new()
        {
            Target = TooltipItemType.Description,
            Type = TooltipModificationType.Append,
            Text = rented.Builder.ToReadOnlySeString(),
        });
    }

    private void AppendRewardBlock(SeStringBuilder builder, CollectableCachedDetails itemDetails, uint itemId)
    {
        var useExperienceFloor = ShouldUseExperienceFloor(itemDetails);
        var scripColor = GetScripColor(itemDetails.ScripRewardType);

        builder
            .AppendNewLine()
            .PushColorType(Orange)
            .Append(OmniLoc.Get("Feature.CollectableValuePreview.Header"))
            .PopColorType()
            .AppendNewLine();

        if (!TryGetCurrentCollectability(itemId, out var collectability))
        {
            builder
                .Append("      ")
                .PushColorType(Green)
                .Append(OmniLoc.Get("Feature.CollectableValuePreview.LevelRange"))
                .PopColorType()
                .Append(' ')
                .Append(itemDetails.LevelMin)
                .Append(" - ")
                .Append(itemDetails.LevelMax)
                .Append(' ')
                .PushColorType(Green)
                .Append(itemDetails.JobName)
                .PopColorType()
                .AppendNewLine();

            AppendGenericReward(
                builder,
                itemDetails.Rewards[0],
                itemDetails.Rewards[1].QualityRequired - 1,
                useExperienceFloor,
                scripColor);
            AppendGenericReward(
                builder,
                itemDetails.Rewards[1],
                itemDetails.Rewards[2].QualityRequired - 1,
                useExperienceFloor,
                scripColor);
            AppendGenericReward(builder, itemDetails.Rewards[2], null, useExperienceFloor, scripColor);
            return;
        }

        if (collectability < itemDetails.Rewards[0].QualityRequired)
        {
            builder
                .Append("      ")
                .PushColorType(BelowMinimumColor)
                .Append(OmniLoc.Get("Feature.CollectableValuePreview.BelowMinimum"))
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
            .Append("      ")
            .PushColorType(Green)
            .Append(OmniLoc.Get(CollectabilityKey))
            .PopColorType()
            .Append(' ')
            .Append(reward.QualityRequired)
            .Append(" - ");
        if (maxQuality.HasValue)
        {
            builder.Append(maxQuality.Value);
        }
        else
        {
            builder.Append(OmniLoc.Get("Feature.CollectableValuePreview.Max"));
        }

        builder
            .Append("  ")
            .PushColorType(Green)
            .Append(OmniLoc.Get(ExperienceKey))
            .PopColorType()
            .Append(' ')
            .Append((useExperienceFloor ? 1000 : reward.Experience).ToString("N0"))
            .Append("  ")
            .PushColorType(Green)
            .Append(OmniLoc.Get(ScripKey))
            .PopColorType()
            .Append(' ');
        if (scripColor != 0)
        {
            builder.PushColorType(scripColor);
        }

        builder.Append(reward.ScriptRewardCount);
        if (scripColor != 0)
        {
            builder.PopColorType();
        }

        builder.AppendNewLine();
    }

    private static void AppendItemReward(
        SeStringBuilder builder,
        CollectableReward reward,
        bool useExperienceFloor,
        ushort scripColor)
    {
        builder
            .Append("      ")
            .PushColorType(Green)
            .Append(OmniLoc.Get(ExperienceKey))
            .PopColorType()
            .Append(' ')
            .Append((useExperienceFloor ? 1000 : reward.Experience).ToString("N0"))
            .AppendNewLine()
            .Append("      ")
            .PushColorType(Green)
            .Append(OmniLoc.Get(ScripKey))
            .PopColorType()
            .Append(' ');
        if (scripColor != 0)
        {
            builder.PushColorType(scripColor);
        }

        builder.Append(reward.ScriptRewardCount);
        if (scripColor != 0)
        {
            builder.PopColorType();
        }

        builder.AppendNewLine();
    }

    private bool TryGetCurrentCollectability(uint itemId, out int collectability)
    {
        collectability = 0;
        if (!inventoryContext.TryGet(itemId, out var item) ||
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
        2 or 4 => Purple,
        6 or 7 => Orange,
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
