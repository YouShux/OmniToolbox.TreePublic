using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.Exd;
using Lumina.Excel.Sheets;
using Lumina.Text;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.UI;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BestInSlotGear
{
    private static CalculationResult Calculate(
        CalculationInput input,
        CancellationToken cancellationToken,
        Action<float> reportProgress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.CommonJobs is not null)
        {
            return CalculateCommon(input, cancellationToken, reportProgress);
        }

        if (!TryBuildDecisionGroups(
                input.OptionsBySlot,
                input.Job,
                input.OptimizationTarget == OptimizationTarget.SpecificGcd,
                cancellationToken,
                out var groups,
                out var weapons,
                out var missingSlot))
        {
            return CalculationResult.Failed(string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.BestInSlotGear.MissingSlot"),
                GetSlotName(missingSlot, 0)));
        }

        reportProgress(0.62f);
        var solution = FindBestSolution(
            groups,
            weapons,
            input.Foods,
            input.BaseStats,
            input.Job,
            input.LevelProfile,
            input.OptimizationTarget,
            input.TargetGcd,
            cancellationToken,
            reportProgress);
        if (solution is null)
        {
            return CalculationResult.Failed(OmniLoc.Get("Feature.BestInSlotGear.NoTargetGcdSolution"));
        }

        var pieces = solution.Options.SelectMany(static option => option.Pieces).ToList();
        SortGearPieces(pieces);

        return new(
            null,
            input.JobID,
            input.JobName,
            input.Level,
            input.ItemLevelLimit,
            pieces,
            solution.Stats,
            solution.Damage,
            CalculateGcd(input.LevelProfile, solution.Stats, input.Job),
            solution.Food,
            []);
    }

    private static CalculationResult CalculateCommon(
        CalculationInput input,
        CancellationToken cancellationToken,
        Action<float> reportProgress)
    {
        var preserveSpeed = input.OptimizationTarget == OptimizationTarget.SpecificGcd;
        if (!TryBuildSharedDecisionGroups(
                input.OptionsBySlot,
                input.Job,
                preserveSpeed,
                cancellationToken,
                out var sharedGroups,
                out var missingSlot))
        {
            return CalculationResult.Failed(string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.BestInSlotGear.MissingSlot"),
                GetSlotName(missingSlot, 0)));
        }

        var states = new List<CommonJobSearchState>(input.CommonJobs!.Count);
        var preparedJobs = 0;
        foreach (var commonJob in input.CommonJobs.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryBuildDecisionGroups(
                    commonJob.OptionsBySlot,
                    commonJob.Job,
                    preserveSpeed,
                    cancellationToken,
                    out var personalGroups,
                    out var weapons,
                    out missingSlot))
            {
                return CalculationResult.Failed(string.Format(
                    CultureInfo.CurrentCulture,
                    OmniLoc.Get("Feature.BestInSlotGear.MissingSlot"),
                    GetSlotName(missingSlot, 0)));
            }

            var progressStart = 0.62f + 0.12f * preparedJobs / input.CommonJobs.Count;
            var personalBest = FindBestSolution(
                personalGroups,
                weapons,
                commonJob.Foods,
                commonJob.BaseStats,
                commonJob.Job,
                input.LevelProfile,
                OptimizationTarget.MaxDamage,
                input.TargetGcd,
                cancellationToken,
                progress => reportProgress(
                    progressStart +
                    (progress - 0.62f) * 0.12f / input.CommonJobs.Count / 0.37f));
            if (personalBest is null)
            {
                return CalculationResult.Failed(OmniLoc.Get("Feature.BestInSlotGear.CalculationFailed"));
            }

            states.Add(new(commonJob, weapons, personalBest.Damage));
            reportProgress(0.62f + 0.12f * ++preparedJobs / input.CommonJobs.Count);
        }

        var solution = FindBestCommonSolution(
            sharedGroups,
            states,
            input.LevelProfile,
            input.OptimizationTarget,
            input.TargetGcd,
            cancellationToken,
            reportProgress);
        if (solution is null)
        {
            return CalculationResult.Failed(OmniLoc.Get("Feature.BestInSlotGear.NoTargetGcdSolution"));
        }

        var sharedItems = solution.SharedOptions.SelectMany(static option => option.Pieces).ToList();
        SortGearPieces(sharedItems);
        var jobResults = new List<CalculationResult>(states.Count);
        foreach (var state in states)
        {
            var jobSolution = solution.JobSolutions[state.Preparation.JobID];
            var items = jobSolution.Options.SelectMany(static option => option.Pieces).ToList();
            SortGearPieces(items);
            jobResults.Add(new(
                null,
                state.Preparation.JobID,
                state.Preparation.JobName,
                input.Level,
                input.ItemLevelLimit,
                items,
                jobSolution.Stats,
                jobSolution.Damage,
                CalculateGcd(input.LevelProfile, jobSolution.Stats, state.Preparation.Job),
                jobSolution.Food,
                []));
        }

        return new(
            null,
            input.JobID,
            input.JobName,
            input.Level,
            input.ItemLevelLimit,
            sharedItems,
            solution.SharedStats,
            0d,
            0d,
            null,
            jobResults);
    }

    private static CommonSearchSolution? FindBestCommonSolution(
        List<List<GearOption>> groups,
        IReadOnlyList<CommonJobSearchState> jobs,
        LevelProfile level,
        OptimizationTarget target,
        double targetGcd,
        CancellationToken cancellationToken,
        Action<float> reportProgress)
    {
        if (jobs.Count == 0)
        {
            return null;
        }

        var speedCap = int.MaxValue;
        if (target == OptimizationTarget.SpecificGcd)
        {
            speedCap = 0;
            foreach (var state in jobs)
            {
                speedCap = Math.Max(speedCap, FindMaximumSpeedForGcd(level, state.Preparation.Job, targetGcd));
            }
        }

        var frontier = BuildGearFrontier(
            groups,
            default,
            jobs[0].Preparation.Job,
            target == OptimizationTarget.SpecificGcd,
            speedCap,
            cancellationToken,
            reportProgress,
            0.74f,
            0.9f);
        SearchState? bestState = null;
        Dictionary<uint, SearchSolution>? bestJobSolutions = null;
        var bestMaximumLoss = double.MaxValue;
        var bestAverageLoss = double.MaxValue;
        for (var index = 0; index < frontier.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = frontier[index];
            var jobSolutions = new Dictionary<uint, SearchSolution>(jobs.Count);
            var hasSolution = true;
            var maximumLoss = 0d;
            var totalLoss = 0d;
            foreach (var jobState in jobs)
            {
                var jobSolution = FindBestWeaponSolution(
                    jobState,
                    state.Stats,
                    level,
                    target,
                    targetGcd,
                    cancellationToken);
                if (jobSolution is null)
                {
                    hasSolution = false;
                    break;
                }

                var loss = Math.Max(0d, 1d - jobSolution.Damage / jobState.PersonalBestDamage);
                maximumLoss = Math.Max(maximumLoss, loss);
                totalLoss += loss;
                jobSolutions.Add(jobState.Preparation.JobID, jobSolution);
            }

            if (hasSolution)
            {
                var averageLoss = totalLoss / jobs.Count;
                if (maximumLoss < bestMaximumLoss - 0.0000001d ||
                    Math.Abs(maximumLoss - bestMaximumLoss) <= 0.0000001d &&
                    averageLoss < bestAverageLoss - 0.0000001d)
                {
                    bestMaximumLoss = maximumLoss;
                    bestAverageLoss = averageLoss;
                    bestJobSolutions = jobSolutions;
                    bestState = state;
                }
            }

            if ((index & 255) == 0 || index + 1 == frontier.Count)
            {
                reportProgress(0.9f + 0.09f * (index + 1) / frontier.Count);
            }
        }

        return bestJobSolutions is null || bestState is null
            ? null
            : new(GetSearchOptions(bestState, groups.Count), bestState.Stats, bestJobSolutions);
    }

    private static SearchSolution? FindBestWeaponSolution(
        CommonJobSearchState state,
        StatBlock sharedStats,
        LevelProfile level,
        OptimizationTarget target,
        double targetGcd,
        CancellationToken cancellationToken)
    {
        GearEvaluation? bestEvaluation = null;
        foreach (var weapon in state.Weapons)
        {
            var evaluation = FindGearEvaluation(
                weapon,
                state.Preparation.BaseStats + sharedStats,
                state.Preparation.Foods,
                state.Preparation.Job,
                level,
                target,
                targetGcd,
                cancellationToken);
            if (evaluation is null)
            {
                continue;
            }

            if (!IsBetterGearEvaluation(evaluation, bestEvaluation))
            {
                continue;
            }

            bestEvaluation = evaluation;
        }

        return bestEvaluation is { } selected
            ? BuildSearchSolution(
                [selected.Option],
                selected.Food,
                state.Preparation.BaseStats + sharedStats,
                state.Preparation.Job)
            : null;
    }

    private static void SortGearPieces(List<GearPiece> pieces) => pieces.Sort(static (left, right) =>
    {
        var slot = left.Slot.CompareTo(right.Slot);
        return slot != 0 ? slot : left.ItemID.CompareTo(right.ItemID);
    });

    private int GetDefaultItemLevel(uint jobID)
    {
        var itemLevel = (uint)MinimumSyncItemLevel;
        var jobIDs = jobID switch
        {
            TankPresetID => TankJobIDs,
            HealerPresetID => HealerJobIDs,
            _ => [jobID]
        };
        if (!warmupComplete)
        {
            foreach (var item in LuminaGetter.Get<Item>())
            {
                if (LevelProfiles.ContainsKey(item.LevelEquip) &&
                    jobIDs.Any(currentJobID => IsSupportedGear(item, currentJobID)))
                {
                    itemLevel = Math.Max(itemLevel, item.LevelItem.RowId);
                }
            }

            return (int)itemLevel;
        }

        foreach (var currentJobID in jobIDs)
        {
            if (!warmedGearItemsByJob.TryGetValue(currentJobID, out var items))
            {
                continue;
            }

            foreach (var item in items)
            {
                if (LevelProfiles.ContainsKey(item.LevelEquip))
                {
                    itemLevel = Math.Max(itemLevel, item.LevelItem.RowId);
                }
            }
        }

        return (int)itemLevel;
    }

    private bool IsGearCandidate(Item item, uint jobID, GearStage stage, int syncItemLevel) =>
        IsSupportedGear(item, jobID) &&
        item.LevelItem.RowId >= GetMinimumCandidateItemLevel(syncItemLevel) &&
        IsAvailableForStage(item, stage);

    private static uint GetMinimumCandidateItemLevel(int syncItemLevel) =>
        (uint)Math.Max(MinimumCandidateItemLevel, syncItemLevel - CandidateItemLevelWindow);

    private bool IsAvailableForStage(Item item, GearStage stage) =>
        stage == GearStage.Graduation ||
        item.LevelItem.RowId <= openingStageItemLevelLimit &&
        !collectionSourceService.IsOpeningStageRestricted(item.RowId);

    private static uint BuildOpeningStageItemLevelLimit(IReadOnlyList<Item> items)
    {
        byte currentLevel = 0;
        uint graduationItemLevel = 0;
        foreach (var item in items)
        {
            if (!LevelProfiles.ContainsKey(item.LevelEquip) || item.LevelEquip < currentLevel)
            {
                continue;
            }

            if (item.LevelEquip > currentLevel)
            {
                currentLevel = item.LevelEquip;
                graduationItemLevel = item.LevelItem.RowId;
                continue;
            }

            graduationItemLevel = Math.Max(graduationItemLevel, item.LevelItem.RowId);
        }

        if (graduationItemLevel <= 15)
        {
            return 999;
        }

        return graduationItemLevel - 15;
    }

    private void SortGearCandidates(
        List<Item> candidates,
        JobProfile job,
        uint[] parameterIDs,
        IReadOnlyDictionary<uint, List<MateriaGradeOption>> materiaCatalog,
        ItemLevel syncItemLevel,
        bool preserveSpeed,
        GearStage stage)
    {
        var keys = new Dictionary<uint, (GearSlot Slot, bool Custom, DamageKey Key, int Utility, uint ItemLevel)>(
            candidates.Count);
        foreach (var item in candidates)
        {
            if (!TryGetSlot(item.EquipSlotCategory.Value, out var slot))
            {
                continue;
            }

            var stats = BuildItemStats(item, job, syncItemLevel);
            var hasUpperBound = TryBuildItemUpperBound(
                item,
                stats,
                job,
                parameterIDs,
                materiaCatalog,
                syncItemLevel,
                out var upperBound);
            keys[item.RowId] = (
                slot,
                !hasUpperBound,
                DamageKey.From(hasUpperBound ? upperBound : stats, job, preserveSpeed),
                (hasUpperBound ? upperBound : stats).Utility,
                item.LevelItem.RowId);
        }

        var upgradeTargetIDs = new HashSet<uint>();
        if (stage == GearStage.Graduation)
        {
            foreach (var item in candidates)
            {
                if (!keys.TryGetValue(item.RowId, out var sourceKey) || sourceKey.Custom)
                {
                    continue;
                }

                foreach (var targetItemID in collectionSourceService.GetExchangeTargets(item.RowId))
                {
                    if (keys.TryGetValue(targetItemID, out var targetKey) &&
                        !targetKey.Custom &&
                        targetKey.Slot == sourceKey.Slot &&
                        targetKey.ItemLevel > sourceKey.ItemLevel)
                    {
                        upgradeTargetIDs.Add(targetItemID);
                    }
                }
            }
        }

        candidates.Sort((left, right) =>
        {
            var leftKey = keys[left.RowId];
            var rightKey = keys[right.RowId];
            var comparison = leftKey.Slot.CompareTo(rightKey.Slot);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = upgradeTargetIDs.Contains(right.RowId).CompareTo(upgradeTargetIDs.Contains(left.RowId));
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = leftKey.Custom.CompareTo(rightKey.Custom);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CompareDamageKeysDescending(leftKey.Key, rightKey.Key);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = rightKey.Utility.CompareTo(leftKey.Utility);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = right.LevelItem.RowId.CompareTo(left.LevelItem.RowId);
            return comparison != 0 ? comparison : right.RowId.CompareTo(left.RowId);
        });
    }

    private static int CompareDamageKeysDescending(DamageKey left, DamageKey right)
    {
        var comparison = right.WeaponDamage.CompareTo(left.WeaponDamage);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.MainStat.CompareTo(left.MainStat);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.CriticalHit.CompareTo(left.CriticalHit);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.Determination.CompareTo(left.Determination);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.DirectHit.CompareTo(left.DirectHit);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.Tenacity.CompareTo(left.Tenacity);
        return comparison != 0 ? comparison : right.Speed.CompareTo(left.Speed);
    }

    private static bool IsPotentialGear(Item item) =>
        item.RowId != 0 &&
        !item.Name.IsEmpty &&
        item.LevelItem.RowId is > 0 and <= 999 &&
        item.LevelEquip > 0 &&
        item.FilterGroup is >= 1 and <= 4 &&
        item.EquipSlotCategory.RowId is > 0 and <= 14 and not 6 &&
        item.ClassJobCategory.IsValid &&
        item.Rarity is >= 2 and <= 4 &&
        !item.IsPvP;

    private static bool IsSupportedGear(Item item, uint jobID) =>
        IsPotentialGear(item) && item.ClassJobCategory.Value.IsClassJobIn(jobID);

    private static bool IsPotentialFood(Item item) =>
        item.RowId != 0 && !item.Name.IsEmpty && item.ItemUICategory.RowId == 46;

    private static StatBlock BuildItemStats(Item item, JobProfile job, ItemLevel syncItemLevel)
    {
        var damageParameterID = job.UsesMagicDamage ? 13u : 12u;
        var stats = new StatBlock(
            GetItemParameter(item, (uint)job.MainAttribute, job),
            GetItemParameter(item, VitalityParamID, job),
            GetItemParameter(item, CriticalHitParamID, job),
            GetItemParameter(item, DeterminationParamID, job),
            GetItemParameter(item, DirectHitParamID, job),
            GetItemParameter(item, job.UsesSpellSpeed ? SpellSpeedParamID : SkillSpeedParamID, job),
            GetItemParameter(item, PietyParamID, job),
            GetItemParameter(item, TenacityParamID, job),
            (job.UsesMagicDamage ? item.DamageMag : item.DamagePhys) + GetItemParameter(item, damageParameterID, job));
        if (item.LevelItem.RowId <= syncItemLevel.RowId)
        {
            return stats;
        }

        var synced = new StatBlock(
            Math.Min(stats.MainStat, GetSyncedParameterCap(item, syncItemLevel, (uint)job.MainAttribute)),
            Math.Min(stats.Vitality, GetSyncedParameterCap(item, syncItemLevel, VitalityParamID)),
            Math.Min(stats.CriticalHit, GetSyncedParameterCap(item, syncItemLevel, CriticalHitParamID)),
            Math.Min(stats.Determination, GetSyncedParameterCap(item, syncItemLevel, DeterminationParamID)),
            Math.Min(stats.DirectHit, GetSyncedParameterCap(item, syncItemLevel, DirectHitParamID)),
            Math.Min(
                stats.Speed,
                GetSyncedParameterCap(
                    item,
                    syncItemLevel,
                    job.UsesSpellSpeed ? SpellSpeedParamID : SkillSpeedParamID)),
            Math.Min(stats.Piety, GetSyncedParameterCap(item, syncItemLevel, PietyParamID)),
            Math.Min(stats.Tenacity, GetSyncedParameterCap(item, syncItemLevel, TenacityParamID)),
            Math.Min(stats.WeaponDamage, GetSyncedParameterCap(item, syncItemLevel, damageParameterID)));
        if (syncItemLevel.RowId != OccultSyncItemLevel ||
            item.ItemSpecialBonus.RowId is not (OccultItemSpecialBonusID or OccultSetSpecialBonusID))
        {
            return synced;
        }

        for (var index = 0; index < Math.Min(item.BaseParamSpecial.Count, item.BaseParamValueSpecial.Count); index++)
        {
            var parameterID = ResolveItemParameter(item.BaseParamSpecial[index].RowId, job);
            if (parameterID != 0 && item.BaseParamValueSpecial[index] > 0)
            {
                synced = synced.WithParameter(
                    parameterID,
                    synced.GetParameter(parameterID, job) + item.BaseParamValueSpecial[index],
                    job);
            }
        }

        return synced;
    }

    private static int GetSyncedParameterCap(Item item, ItemLevel itemLevel, uint parameterID)
    {
        if (!LuminaGetter.TryGetRow<BaseParam>(parameterID, out var parameter) ||
            item.BaseParamModifier >= parameter.MeldParam.Count)
        {
            return 0;
        }

        return (int)Math.Round(
            GetItemLevelParameter(itemLevel, parameterID) *
            GetSlotParameterPercent(parameter, item.EquipSlotCategory.Value) *
            parameter.MeldParam[item.BaseParamModifier] /
            100000d,
            MidpointRounding.AwayFromZero);
    }

    private static int GetItemLevelParameter(ItemLevel itemLevel, uint parameterID) => parameterID switch
    {
        1 => itemLevel.Strength,
        2 => itemLevel.Dexterity,
        VitalityParamID => itemLevel.Vitality,
        4 => itemLevel.Intelligence,
        5 => itemLevel.Mind,
        PietyParamID => itemLevel.Piety,
        12 => itemLevel.PhysicalDamage,
        13 => itemLevel.MagicalDamage,
        TenacityParamID => itemLevel.Tenacity,
        DirectHitParamID => itemLevel.DirectHitRate,
        CriticalHitParamID => itemLevel.CriticalHit,
        DeterminationParamID => itemLevel.Determination,
        SkillSpeedParamID => itemLevel.SkillSpeed,
        SpellSpeedParamID => itemLevel.SpellSpeed,
        _ => 0
    };

    private static int GetSlotParameterPercent(BaseParam parameter, EquipSlotCategory slot)
    {
        if (slot.MainHand != 0)
        {
            return slot.OffHand != 0 ? parameter.TwoHandWeaponPercent : parameter.OneHandWeaponPercent;
        }

        return slot switch
        {
            { OffHand: not 0 } => parameter.OffHandPercent,
            { Head: not 0 } => parameter.HeadPercent,
            { Body: not 0 } => parameter.ChestPercent,
            { Gloves: not 0 } => parameter.HandsPercent,
            { Legs: not 0 } => parameter.LegsPercent,
            { Feet: not 0 } => parameter.FeetPercent,
            { Ears: not 0 } => parameter.EarringPercent,
            { Neck: not 0 } => parameter.NecklacePercent,
            { Wrists: not 0 } => parameter.BraceletPercent,
            { FingerL: not 0 } or { FingerR: not 0 } => parameter.RingPercent,
            _ => 0
        };
    }

    private static int GetItemParameter(Item item, uint parameterID, JobProfile job)
    {
        var value = 0;
        for (var index = 0; index < item.BaseParam.Count; index++)
        {
            if (ResolveItemParameter(item.BaseParam[index].RowId, job) == parameterID)
            {
                value += item.BaseParamValue[index];
            }

            if (item.CanBeHq &&
                index < item.BaseParamSpecial.Count &&
                index < item.BaseParamValueSpecial.Count &&
                ResolveItemParameter(item.BaseParamSpecial[index].RowId, job) == parameterID)
            {
                value += item.BaseParamValueSpecial[index];
            }
        }

        return value;
    }

    private static uint ResolveItemParameter(uint parameterID, JobProfile job) => parameterID switch
    {
        MainAttributeParamID => (uint)job.MainAttribute,
        SecondaryAttributeParamID => job.IsTank
            ? TenacityParamID
            : job.MainAttribute == PlayerAttribute.Mind
                ? PietyParamID
                : DirectHitParamID,
        _ => parameterID
    };

    private static bool IsCustomizableItem(Item item, StatBlock stats) =>
        item.Rarity == 4 &&
        item.LevelItem.RowId > 1 &&
        stats.Vitality > 0 &&
        stats.CriticalHit == 0 &&
        stats.Determination == 0 &&
        stats.DirectHit == 0 &&
        stats.Speed == 0 &&
        stats.Piety == 0 &&
        stats.Tenacity == 0;

    private static void BuildMateriaCatalog(Dictionary<uint, List<MateriaGradeOption>> catalog)
    {
        foreach (var materia in LuminaGetter.Get<Materia>())
        {
            var parameterID = materia.BaseParam.RowId;
            if (parameterID is not (CriticalHitParamID or DeterminationParamID or DirectHitParamID or
                TenacityParamID or SkillSpeedParamID or SpellSpeedParamID))
            {
                continue;
            }

            var count = Math.Min(materia.Item.Count, materia.Value.Count);
            for (var grade = 0; grade < count; grade++)
            {
                if (materia.Value[grade] <= 0 || !materia.Item[grade].IsValid)
                {
                    continue;
                }

                var materiaItem = materia.Item[grade].Value;
                if (materiaItem.LevelItem.RowId == 0 || materiaItem.Name.IsEmpty)
                {
                    continue;
                }

                if (!catalog.TryGetValue(parameterID, out var options))
                {
                    options = [];
                    catalog.Add(parameterID, options);
                }

                byte advancedSlotMask = 0;
                if (LuminaGetter.TryGetRow<MateriaGrade>((uint)grade, out var materiaGrade))
                {
                    var overmeldSlotCount = Math.Min(
                        materiaGrade.OvermeldNQPercent.Count,
                        materiaGrade.OvermeldHQPercent.Count);
                    for (var slot = 0; slot < overmeldSlotCount; slot++)
                    {
                        if (materiaGrade.OvermeldNQPercent[slot] > 0 || materiaGrade.OvermeldHQPercent[slot] > 0)
                        {
                            advancedSlotMask |= (byte)(1 << slot);
                        }
                    }
                }

                options.Add(new(
                    materia.Value[grade],
                    materiaItem.LevelItem.RowId,
                    materiaItem.Name.ExtractText(),
                    materiaItem.Icon,
                    advancedSlotMask));
            }
        }

        foreach (var options in catalog.Values)
        {
            options.Sort(static (left, right) =>
            {
                var value = right.Value.CompareTo(left.Value);
                return value != 0 ? value : right.RequiredItemLevel.CompareTo(left.RequiredItemLevel);
            });
        }
    }

    private List<GearOption> BuildUsefulItemOptions(
        IReadOnlyDictionary<GearSlot, List<GearOption>> optionsBySlot,
        Item item,
        GearSlot slot,
        StatBlock baseStats,
        JobProfile job,
        uint[] parameterIDs,
        IReadOnlyDictionary<uint, List<MateriaGradeOption>> materiaCatalog,
        ItemLevel syncItemLevel,
        bool preserveSpeed,
        out bool upperBoundPruned)
    {
        upperBoundPruned = false;
        if (TryBuildItemUpperBound(
                item,
                baseStats,
                job,
                parameterIDs,
                materiaCatalog,
                syncItemLevel,
                out var upperBound) &&
            optionsBySlot.TryGetValue(slot, out var existingOptions) &&
            IsDominatedInSlot(existingOptions, upperBound, item, slot, job, preserveSpeed))
        {
            upperBoundPruned = true;
            return [];
        }

        return BuildItemOptions(
            item,
            slot,
            baseStats,
            job,
            parameterIDs,
            materiaCatalog,
            syncItemLevel,
            preserveSpeed);
    }

    private static bool TryBuildItemUpperBound(
        Item item,
        StatBlock baseStats,
        JobProfile job,
        uint[] parameterIDs,
        IReadOnlyDictionary<uint, List<MateriaGradeOption>> materiaCatalog,
        ItemLevel syncItemLevel,
        out StatBlock upperBound)
    {
        upperBound = baseStats;
        if (IsCustomizableItem(item, baseStats))
        {
            return false;
        }

        var slotCount = item.IsAdvancedMeldingPermitted && item.MateriaSlotCount > 0 ? 5 : item.MateriaSlotCount;
        if (slotCount == 0 || item.LevelItem.RowId > syncItemLevel.RowId)
        {
            return true;
        }

        var itemRowData = ExdModule.GetItemRowById(item.RowId);
        if (itemRowData == null)
        {
            return false;
        }

        for (var parameterIndex = 0; parameterIndex < parameterIDs.Length; parameterIndex++)
        {
            var parameterID = parameterIDs[parameterIndex];
            var maximum = (int)InventoryItem.GetParameterMaxValue(parameterID, itemRowData);
            var addition = 0;
            for (var materiaSlot = 0; materiaSlot < slotCount; materiaSlot++)
            {
                if (TryGetMateria(
                        materiaCatalog,
                        parameterID,
                        item.LevelItem.RowId,
                        materiaSlot - item.MateriaSlotCount,
                        out var materia))
                {
                    addition += materia.Value;
                }
            }

            upperBound = upperBound.WithParameter(
                parameterID,
                Math.Min(
                    baseStats.GetParameter(parameterID, job) + addition,
                    Math.Max(baseStats.GetParameter(parameterID, job), maximum)),
                job);
        }

        return true;
    }

    private static bool IsDominatedInSlot(
        IReadOnlyList<GearOption> options,
        StatBlock upperBound,
        Item item,
        GearSlot slot,
        JobProfile job,
        bool preserveSpeed)
    {
        var upperKey = DamageKey.From(upperBound, job, preserveSpeed);
        var occupiesOffHand = slot == GearSlot.MainHand && item.EquipSlotCategory.Value.OffHand != 0;
        foreach (var option in options)
        {
            if (slot == GearSlot.MainHand && option.Pieces[0].OccupiesOffHand != occupiesOffHand ||
                slot == GearSlot.Ring && option.Pieces[0].IsUnique)
            {
                continue;
            }

            var existingKey = DamageKey.From(option.Stats, job, preserveSpeed);
            if (existingKey.Dominates(upperKey) ||
                existingKey == upperKey && option.Stats.Utility >= upperBound.Utility)
            {
                return true;
            }
        }

        return false;
    }

    private List<GearOption> BuildItemOptions(
        Item item,
        GearSlot slot,
        StatBlock baseStats,
        JobProfile job,
        uint[] parameterIDs,
        IReadOnlyDictionary<uint, List<MateriaGradeOption>> materiaCatalog,
        ItemLevel syncItemLevel,
        bool preserveSpeed)
    {
        var unique = new Dictionary<DamageKey, GearOption>();
        var manderville = new Dictionary<(DamageKey, CustomStatSelection), GearOption>();
        if (TryBuildMandervilleOptions(manderville, item, slot, baseStats, job, syncItemLevel, preserveSpeed))
        {
            return PruneDominated([.. manderville.Values], job, preserveSpeed);
        }

        if (TryBuildResistanceWeaponOption(unique, item, slot, baseStats, job, syncItemLevel, preserveSpeed))
        {
            return [.. unique.Values];
        }

        if (IsCustomizableItem(item, baseStats))
        {
            if (unsupportedCustomItemIDs.Add(item.RowId))
            {
                DalamudServices.PluginLog.Warning(
                    "[BIS] Unsupported customizable item excluded: item={ItemID} ({ItemName}).",
                    item.RowId,
                    item.Name.ExtractText());
            }

            return [];
        }

        AddItemOption(unique, item, slot, baseStats, job, [], preserveSpeed);
        var slotCount = item.IsAdvancedMeldingPermitted && item.MateriaSlotCount > 0 ? 5 : item.MateriaSlotCount;
        if (slotCount == 0 || item.LevelItem.RowId > syncItemLevel.RowId)
        {
            return [.. unique.Values];
        }

        var caps = new int[parameterIDs.Length];
        var itemRowData = ExdModule.GetItemRowById(item.RowId);
        for (var index = 0; index < parameterIDs.Length; index++)
        {
            caps[index] = itemRowData == null
                ? baseStats.GetParameter(parameterIDs[index], job)
                : (int)InventoryItem.GetParameterMaxValue(parameterIDs[index], itemRowData);
        }

        var additions = new int[parameterIDs.Length];
        var selected = new MateriaGradeOption?[slotCount];

        void Enumerate(int materiaSlot, int minimumParameterIndex)
        {
            if (materiaSlot == slotCount)
            {
                var meldedStats = baseStats;
                for (var index = 0; index < parameterIDs.Length; index++)
                {
                    meldedStats = meldedStats.WithParameter(
                        parameterIDs[index],
                        Math.Min(
                            baseStats.GetParameter(parameterIDs[index], job) + additions[index],
                            Math.Max(baseStats.GetParameter(parameterIDs[index], job), caps[index])),
                        job);
                }

                AddItemOption(unique, item, slot, meldedStats, job, selected, preserveSpeed);
                return;
            }

            var found = false;
            var advancedSlotIndex = materiaSlot - item.MateriaSlotCount;
            for (var index = minimumParameterIndex; index < parameterIDs.Length; index++)
            {
                if (!TryGetMateria(
                        materiaCatalog,
                        parameterIDs[index],
                        item.LevelItem.RowId,
                        advancedSlotIndex,
                        out var materia) ||
                    baseStats.GetParameter(parameterIDs[index], job) + additions[index] >= caps[index])
                {
                    continue;
                }

                found = true;
                additions[index] += materia.Value;
                selected[materiaSlot] = materia;
                Enumerate(
                    materiaSlot + 1,
                    materiaSlot + 1 < slotCount &&
                    (materiaSlot + 1 >= item.MateriaSlotCount) == (advancedSlotIndex >= 0)
                        ? index
                        : 0);
                selected[materiaSlot] = null;
                additions[index] -= materia.Value;
            }

            if (!found)
            {
                Enumerate(
                    advancedSlotIndex >= 0 ? slotCount : Math.Min(item.MateriaSlotCount, slotCount),
                    0);
            }
        }

        Enumerate(0, 0);
        return PruneDominated([.. unique.Values], job, preserveSpeed);
    }

    private static bool TryBuildMandervilleOptions(
        Dictionary<(DamageKey, CustomStatSelection), GearOption> unique,
        Item item,
        GearSlot slot,
        StatBlock baseStats,
        JobProfile job,
        ItemLevel syncItemLevel,
        bool preserveSpeed)
    {
        if (!IsCustomizableItem(item, baseStats) ||
            !LuminaGetter.TryGetRow<MandervilleWeaponEnhance>(item.RowId, out var enhancement))
        {
            return false;
        }

        var allocations = new List<CustomStatAllocation>(enhancement.Materias.Count);
        foreach (var current in enhancement.Materias)
        {
            if (!current.Materia.IsValid)
            {
                continue;
            }

            var materia = current.Materia.Value;
            if (current.MateriaBigValueIndex >= materia.Value.Count ||
                current.MateriaSmallValueIndex >= materia.Value.Count ||
                materia.BaseParam.RowId == 0)
            {
                continue;
            }

            var bigValue = materia.Value[current.MateriaBigValueIndex];
            var smallValue = materia.Value[current.MateriaSmallValueIndex];
            if (bigValue > 0 && smallValue > 0 &&
                baseStats.WithParameter(materia.BaseParam.RowId, 1, job) != baseStats)
            {
                allocations.Add(new(materia.BaseParam.RowId, bigValue, smallValue));
            }
        }

        if (allocations.Count < 3)
        {
            return false;
        }

        for (var first = 0; first < allocations.Count - 1; first++)
        {
            for (var second = first + 1; second < allocations.Count; second++)
            {
                for (var small = 0; small < allocations.Count; small++)
                {
                    if (small == first || small == second)
                    {
                        continue;
                    }

                    var stats = AddCustomStat(baseStats, item, job, syncItemLevel, allocations[first].ParameterID, allocations[first].BigValue);
                    stats = AddCustomStat(stats, item, job, syncItemLevel, allocations[second].ParameterID, allocations[second].BigValue);
                    stats = AddCustomStat(stats, item, job, syncItemLevel, allocations[small].ParameterID, allocations[small].SmallValue);
                    var selection = new CustomStatSelection(
                        Math.Min(allocations[first].ParameterID, allocations[second].ParameterID),
                        Math.Max(allocations[first].ParameterID, allocations[second].ParameterID),
                        allocations[small].ParameterID);
                    unique.TryAdd(
                        (DamageKey.From(stats, job, preserveSpeed), selection),
                        CreateItemOption(item, slot, stats, job, [], customStatSelection: selection));
                }
            }
        }

        return unique.Count > 0;
    }

    private static bool TryBuildResistanceWeaponOption(
        Dictionary<DamageKey, GearOption> unique,
        Item item,
        GearSlot slot,
        StatBlock baseStats,
        JobProfile job,
        ItemLevel syncItemLevel,
        bool preserveSpeed)
    {
        if (!IsCustomizableItem(item, baseStats) ||
            !LuminaGetter.TryGetRow<ResistanceWeaponAdjust>(item.RowId, out var adjustment) ||
            adjustment.MaxTotalStats == 0 ||
            adjustment.MaxEachStat == 0)
        {
            return false;
        }

        var parameters = new List<CustomStatParameter>(adjustment.BaseParam.Count);
        foreach (var parameter in adjustment.BaseParam)
        {
            var parameterID = ResolveItemParameter(parameter.RowId, job);
            if (parameterID == 0 || default(StatBlock).WithParameter(parameterID, 1, job) == default)
            {
                continue;
            }

            var duplicate = false;
            foreach (var existing in parameters)
            {
                if (existing.ParameterID == parameterID)
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
            {
                continue;
            }

            parameters.Add(new(
                parameterID,
                item.LevelItem.RowId > syncItemLevel.RowId
                    ? Math.Min(adjustment.MaxEachStat, GetSyncedParameterCap(item, syncItemLevel, parameterID))
                    : adjustment.MaxEachStat));
        }

        if (parameters.Count == 0)
        {
            return false;
        }

        AddItemOption(
            unique,
            item,
            slot,
            baseStats,
            job,
            [],
            preserveSpeed,
            new(
                Math.Min(adjustment.MaxTotalStats, adjustment.MaxEachStat * parameters.Count),
                adjustment.MaxEachStat,
                [.. parameters]));
        return true;
    }

    private static StatBlock AddCustomStat(
        StatBlock stats,
        Item item,
        JobProfile job,
        ItemLevel syncItemLevel,
        uint parameterID,
        int value)
    {
        var current = stats.GetParameter(parameterID, job);
        var target = current + value;
        if (item.LevelItem.RowId > syncItemLevel.RowId)
        {
            target = Math.Min(target, GetSyncedParameterCap(item, syncItemLevel, parameterID));
        }

        return stats.WithParameter(parameterID, target, job);
    }

    private static void AddItemOption(
        Dictionary<DamageKey, GearOption> unique,
        Item item,
        GearSlot slot,
        StatBlock stats,
        JobProfile job,
        IReadOnlyList<MateriaGradeOption?> materias,
        bool preserveSpeed,
        CustomStatBudget? customStats = null,
        CustomStatSelection? customStatSelection = null)
    {
        unique.TryAdd(
            DamageKey.From(stats, job, preserveSpeed),
            CreateItemOption(item, slot, stats, job, materias, customStats, customStatSelection));
    }

    private static GearOption CreateItemOption(
        Item item,
        GearSlot slot,
        StatBlock stats,
        JobProfile job,
        IReadOnlyList<MateriaGradeOption?> materias,
        CustomStatBudget? customStats = null,
        CustomStatSelection? customStatSelection = null) => new(
            [new(
                item.RowId,
                slot,
                item.Name.ExtractText(),
                item.Icon,
                item.LevelItem.RowId,
                item.CanBeHq,
                item.IsUnique,
                slot == GearSlot.MainHand && item.EquipSlotCategory.Value.OffHand != 0,
                BuildMateriaDisplay(materias),
                BuildStatText(stats, job))],
            stats,
            customStats,
            customStatSelection);

    private static bool TryGetMateria(
        IReadOnlyDictionary<uint, List<MateriaGradeOption>> catalog,
        uint parameterID,
        uint itemLevel,
        int advancedSlotIndex,
        out MateriaGradeOption materia)
    {
        if (catalog.TryGetValue(parameterID, out var options))
        {
            foreach (var option in options)
            {
                if (option.RequiredItemLevel <= itemLevel &&
                    (advancedSlotIndex < 0 ||
                     advancedSlotIndex < 8 &&
                     (option.AdvancedSlotMask & 1 << advancedSlotIndex) != 0))
                {
                    materia = option;
                    return true;
                }
            }
        }

        materia = default;
        return false;
    }

    private static IReadOnlyList<MateriaDisplay> BuildMateriaDisplay(IReadOnlyList<MateriaGradeOption?> materias)
    {
        if (materias.Count == 0)
        {
            return [];
        }

        var result = new List<MateriaDisplay>();
        foreach (var materia in materias)
        {
            if (materia is not { } current)
            {
                continue;
            }

            var existingIndex = -1;
            for (var index = 0; index < result.Count; index++)
            {
                if (string.Equals(result[index].Name, current.Name, StringComparison.Ordinal))
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                result[existingIndex] = result[existingIndex] with
                {
                    Count = result[existingIndex].Count + 1
                };
                continue;
            }

            result.Add(new(current.Name, current.IconID, 1));
        }

        return result;
    }

    private static string BuildStatText(StatBlock stats, JobProfile job)
    {
        var text = new List<string>(8);
        AddStat(text, (uint)job.MainAttribute, stats.MainStat);
        AddStat(text, VitalityParamID, stats.Vitality);
        AddStat(text, CriticalHitParamID, stats.CriticalHit);
        AddStat(text, DeterminationParamID, stats.Determination);
        AddStat(text, DirectHitParamID, stats.DirectHit);
        AddStat(text, job.UsesSpellSpeed ? SpellSpeedParamID : SkillSpeedParamID, stats.Speed);
        AddStat(text, PietyParamID, stats.Piety);
        AddStat(text, TenacityParamID, stats.Tenacity);
        return string.Join("  ", text);
    }

    private static void AddStat(List<string> text, uint parameterID, int value)
    {
        if (value > 0)
        {
            text.Add($"{GetParameterName(parameterID)} {value}");
        }
    }

    private static bool TryBuildDecisionGroups(
        IReadOnlyDictionary<GearSlot, List<GearOption>> optionsBySlot,
        JobProfile job,
        bool preserveSpeed,
        CancellationToken cancellationToken,
        out List<List<GearOption>> groups,
        out List<GearOption> weapons,
        out GearSlot missingSlot)
    {
        groups = [];
        if (!TryBuildWeaponGroup(
                optionsBySlot,
                job,
                preserveSpeed,
                cancellationToken,
                out weapons,
                out missingSlot))
        {
            return false;
        }

        foreach (var slot in RequiredSlots)
        {
            if (!optionsBySlot.TryGetValue(slot, out var options) || options.Count == 0)
            {
                missingSlot = slot;
                return false;
            }

            groups.Add(PruneDominated(options, job, preserveSpeed, cancellationToken));
        }

        if (!optionsBySlot.TryGetValue(GearSlot.Ring, out var rings) || rings.Count == 0)
        {
            missingSlot = GearSlot.Ring;
            return false;
        }

        var ringPairs = BuildRingPairs(rings, job, preserveSpeed, cancellationToken);
        if (ringPairs.Count == 0)
        {
            missingSlot = GearSlot.Ring;
            return false;
        }

        DalamudServices.PluginLog.Information(
            "[BIS] Ring options prepared: items={ItemCount}, options={OptionCount}, pairs={PairCount}.",
            rings.SelectMany(static option => option.Pieces).Select(static piece => piece.ItemID).Distinct().Count(),
            rings.Count,
            ringPairs.Count);
        groups.Add(ringPairs);
        groups.Sort(CompareDecisionGroups);
        missingSlot = default;
        return true;
    }

    private static bool TryBuildWeaponGroup(
        IReadOnlyDictionary<GearSlot, List<GearOption>> optionsBySlot,
        JobProfile job,
        bool preserveSpeed,
        CancellationToken cancellationToken,
        out List<GearOption> weapons,
        out GearSlot missingSlot)
    {
        weapons = [];
        if (!optionsBySlot.TryGetValue(GearSlot.MainHand, out var mainHands) || mainHands.Count == 0)
        {
            missingSlot = GearSlot.MainHand;
            return false;
        }

        optionsBySlot.TryGetValue(GearSlot.OffHand, out var offHands);
        var uniqueWeapons = new Dictionary<DamageKey, GearOption>();
        var discreteWeapons = new List<GearOption>();
        var customWeapons = new List<GearOption>();
        foreach (var mainHand in mainHands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mainHand.Pieces[0].OccupiesOffHand)
            {
                if (mainHand.CustomStatSelection is not null)
                {
                    discreteWeapons.Add(mainHand);
                }
                else if (mainHand.CustomStats is null)
                {
                    AddUniqueOption(uniqueWeapons, mainHand, job, preserveSpeed);
                }
                else
                {
                    customWeapons.Add(mainHand);
                }

                continue;
            }

            if (offHands is not { Count: > 0 })
            {
                continue;
            }

            foreach (var offHand in offHands)
            {
                if (mainHand.CustomStatSelection is { } mainSelection &&
                    offHand.CustomStatSelection is { } offHandSelection &&
                    mainSelection != offHandSelection)
                {
                    continue;
                }

                var combined = Combine(mainHand, offHand);
                if (combined.CustomStatSelection is not null)
                {
                    discreteWeapons.Add(combined);
                }
                else if (combined.CustomStats is null)
                {
                    AddUniqueOption(uniqueWeapons, combined, job, preserveSpeed);
                }
                else
                {
                    customWeapons.Add(combined);
                }
            }
        }

        weapons = PruneDominated(
            [.. uniqueWeapons.Values, .. customWeapons, .. discreteWeapons],
            job,
            preserveSpeed,
            cancellationToken);
        missingSlot = weapons.Count > 0 ? default : GearSlot.OffHand;
        return weapons.Count > 0;
    }

    private static bool TryBuildSharedDecisionGroups(
        IReadOnlyDictionary<GearSlot, List<GearOption>> optionsBySlot,
        JobProfile job,
        bool preserveSpeed,
        CancellationToken cancellationToken,
        out List<List<GearOption>> groups,
        out GearSlot missingSlot)
    {
        groups = [];
        foreach (var slot in RequiredSlots)
        {
            if (!optionsBySlot.TryGetValue(slot, out var options) || options.Count == 0)
            {
                missingSlot = slot;
                return false;
            }

            groups.Add(PruneDominated(options, job, preserveSpeed, cancellationToken));
        }

        if (!optionsBySlot.TryGetValue(GearSlot.Ring, out var rings) || rings.Count == 0)
        {
            missingSlot = GearSlot.Ring;
            return false;
        }

        var ringPairs = BuildRingPairs(rings, job, preserveSpeed, cancellationToken);
        if (ringPairs.Count == 0)
        {
            missingSlot = GearSlot.Ring;
            return false;
        }

        groups.Add(ringPairs);
        groups.Sort(CompareDecisionGroups);
        missingSlot = default;
        return true;
    }

    private static int CompareDecisionGroups(List<GearOption> left, List<GearOption> right)
    {
        var leftMaximumMainStat = 0;
        foreach (var option in left)
        {
            leftMaximumMainStat = Math.Max(leftMaximumMainStat, option.Stats.MainStat);
        }

        var rightMaximumMainStat = 0;
        foreach (var option in right)
        {
            rightMaximumMainStat = Math.Max(rightMaximumMainStat, option.Stats.MainStat);
        }

        var mainStat = rightMaximumMainStat.CompareTo(leftMaximumMainStat);
        return mainStat != 0 ? mainStat : left.Count.CompareTo(right.Count);
    }

    private static GearOption Combine(GearOption left, GearOption right) => new(
        [.. left.Pieces, .. right.Pieces],
        left.Stats + right.Stats,
        left.CustomStats ?? right.CustomStats,
        left.CustomStatSelection ?? right.CustomStatSelection);

    private static List<GearOption> BuildRingPairs(
        IReadOnlyList<GearOption> rings,
        JobProfile job,
        bool preserveSpeed,
        CancellationToken cancellationToken)
    {
        var frontier = new List<GearOption>();
        for (var left = 0; left < rings.Count; left++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var right = left; right < rings.Count; right++)
            {
                if (rings[left].Pieces[0].ItemID == rings[right].Pieces[0].ItemID && rings[left].Pieces[0].IsUnique)
                {
                    continue;
                }

                AddToParetoFrontier(frontier, Combine(rings[left], rings[right]), job, preserveSpeed);
            }
        }

        return frontier;
    }

    private static void AddToParetoFrontier(
        List<GearOption> frontier,
        GearOption candidate,
        JobProfile job,
        bool preserveSpeed)
    {
        var candidateKey = DamageKey.From(candidate.Stats, job, preserveSpeed);
        for (var index = frontier.Count - 1; index >= 0; index--)
        {
            var existingKey = DamageKey.From(frontier[index].Stats, job, preserveSpeed);
            if (existingKey.Dominates(candidateKey) ||
                existingKey == candidateKey && frontier[index].Stats.Utility >= candidate.Stats.Utility)
            {
                return;
            }

            if (candidateKey.Dominates(existingKey) ||
                candidateKey == existingKey && candidate.Stats.Utility > frontier[index].Stats.Utility)
            {
                frontier.RemoveAt(index);
            }
        }

        frontier.Add(candidate);
    }

    private static void AddUniqueOption(
        Dictionary<DamageKey, GearOption> unique,
        GearOption option,
        JobProfile job,
        bool preserveSpeed)
    {
        var key = DamageKey.From(option.Stats, job, preserveSpeed);
        if (!unique.TryGetValue(key, out var existing) || option.Stats.Utility > existing.Stats.Utility)
        {
            unique[key] = option;
        }
    }

    private static List<GearOption> PruneDominated(
        List<GearOption> options,
        JobProfile job,
        bool preserveSpeed,
        CancellationToken cancellationToken = default)
    {
        var unique = new Dictionary<DamageKey, GearOption>();
        var custom = new List<GearOption>();
        var discreteCustom = new List<GearOption>();
        foreach (var option in options)
        {
            if (option.CustomStats is not null)
            {
                custom.Add(option);
                continue;
            }

            if (option.CustomStatSelection is not null)
            {
                discreteCustom.Add(option);
                continue;
            }

            var key = DamageKey.From(option.Stats, job, preserveSpeed);
            if (!unique.TryGetValue(key, out var existing) || option.Stats.Utility > existing.Stats.Utility)
            {
                unique[key] = option;
            }
        }

        var values = new List<(DamageKey Key, GearOption Option)>(unique.Count);
        foreach (var entry in unique)
        {
            values.Add((entry.Key, entry.Value));
        }

        values.Sort(static (left, right) => right.Key.MainStat.CompareTo(left.Key.MainStat));
        var frontier = new List<(DamageKey Key, GearOption Option)>();
        foreach (var entry in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dominated = false;
            for (var index = frontier.Count - 1; index >= 0; index--)
            {
                if (frontier[index].Key.Dominates(entry.Key))
                {
                    dominated = true;
                    break;
                }

                if (entry.Key.Dominates(frontier[index].Key))
                {
                    frontier.RemoveAt(index);
                }
            }

            if (!dominated)
            {
                frontier.Add(entry);
            }
        }

        var result = new List<GearOption>(frontier.Count);
        foreach (var entry in frontier)
        {
            result.Add(entry.Option);
        }

        foreach (var option in custom)
        {
            var upperBound = option.Stats;
            foreach (var parameter in option.CustomStats!.Parameters)
            {
                upperBound = upperBound.WithParameter(
                    parameter.ParameterID,
                    upperBound.GetParameter(parameter.ParameterID, job) + parameter.EffectiveCap,
                    job);
            }

            var upperKey = DamageKey.From(upperBound, job, preserveSpeed);
            var dominated = false;
            foreach (var entry in frontier)
            {
                if (entry.Key.Dominates(upperKey) ||
                    entry.Key == upperKey && entry.Option.Stats.Utility >= upperBound.Utility)
                {
                    dominated = true;
                    break;
                }
            }

            if (!dominated)
            {
                result.Add(option);
            }
        }

        result.AddRange(discreteCustom);

        return result;
    }

    private static List<GearOption> PruneFactorDominatedSearchOptions(
        List<List<GearOption>> groups,
        IReadOnlyList<GearOption> weapons,
        IReadOnlyList<FoodOption> foods,
        StatBlock baseStats,
        JobProfile job,
        LevelProfile level,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        GetOptionGroupBounds(weapons, job, out var weaponMinimum, out var weaponMaximum);
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var continuationMinimum = baseStats + weaponMinimum;
            var continuationMaximum = baseStats + weaponMaximum;
            for (var otherIndex = 0; otherIndex < groups.Count; otherIndex++)
            {
                if (otherIndex == groupIndex)
                {
                    continue;
                }

                GetOptionGroupBounds(groups[otherIndex], job, out var minimum, out var maximum);
                continuationMinimum += minimum;
                continuationMaximum += maximum;
            }

            var before = groups[groupIndex].Count;
            groups[groupIndex] = PruneFactorDominated(
                groups[groupIndex],
                continuationMinimum,
                continuationMaximum,
                foods,
                job,
                level,
                cancellationToken);
            DalamudServices.PluginLog.Information(
                "[BIS] Exact factor dominance: slots={Slots}, before={Before}, after={After}, removed={Removed}.",
                GetDecisionGroupName(groups[groupIndex]),
                before,
                groups[groupIndex].Count,
                before - groups[groupIndex].Count);
        }

        var weaponContinuationMinimum = baseStats;
        var weaponContinuationMaximum = baseStats;
        foreach (var group in groups)
        {
            GetOptionGroupBounds(group, job, out var minimum, out var maximum);
            weaponContinuationMinimum += minimum;
            weaponContinuationMaximum += maximum;
        }

        var prunedWeapons = PruneFactorDominated(
            weapons,
            weaponContinuationMinimum,
            weaponContinuationMaximum,
            foods,
            job,
            level,
            cancellationToken);
        DalamudServices.PluginLog.Information(
            "[BIS] Exact factor dominance prepared: weaponsBefore={Before}, weaponsAfter={After}, elapsedMs={ElapsedMilliseconds}.",
            weapons.Count,
            prunedWeapons.Count,
            stopwatch.ElapsedMilliseconds);
        return prunedWeapons;
    }

    private static List<GearOption> PruneFactorDominated(
        IReadOnlyList<GearOption> options,
        StatBlock continuationMinimum,
        StatBlock continuationMaximum,
        IReadOnlyList<FoodOption> foods,
        JobProfile job,
        LevelProfile level,
        CancellationToken cancellationToken)
    {
        if (options.Count < 2)
        {
            return [.. options];
        }

        var removed = new bool[options.Count];
        var factorMinimums = new Dictionary<
            (DamageFactorKind Kind, int Delta, int Minimum, int Maximum, int Value, int Cap, bool Relative),
            double>();
        var comparisons = 0;
        for (var targetIndex = 0; targetIndex < options.Count; targetIndex++)
        {
            if (removed[targetIndex] ||
                options[targetIndex].CustomStats is not null ||
                options[targetIndex].CustomStatSelection is not null)
            {
                continue;
            }

            for (var candidateIndex = 0; candidateIndex < options.Count; candidateIndex++)
            {
                if ((++comparisons & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (candidateIndex == targetIndex ||
                    removed[candidateIndex] ||
                    options[candidateIndex].CustomStats is not null ||
                    options[candidateIndex].CustomStatSelection is not null ||
                    !DominatesForAllCompletions(
                        options[candidateIndex].Stats,
                        options[targetIndex].Stats,
                        continuationMinimum,
                        continuationMaximum,
                        foods,
                        job,
                        level,
                        factorMinimums))
                {
                    continue;
                }

                removed[targetIndex] = true;
                break;
            }
        }

        var result = new List<GearOption>(options.Count);
        for (var index = 0; index < options.Count; index++)
        {
            if (!removed[index])
            {
                result.Add(options[index]);
            }
        }

        return result;
    }

    private static bool DominatesForAllCompletions(
        StatBlock candidate,
        StatBlock target,
        StatBlock continuationMinimum,
        StatBlock continuationMaximum,
        IReadOnlyList<FoodOption> foods,
        JobProfile job,
        LevelProfile level,
        Dictionary<
            (DamageFactorKind Kind, int Delta, int Minimum, int Maximum, int Value, int Cap, bool Relative),
            double> factorMinimums)
    {
        if (CalculateDamage(job, level, continuationMinimum + candidate) <=
            CalculateDamage(job, level, continuationMinimum + target))
        {
            return false;
        }

        if (!DominatesForFood(null))
        {
            return false;
        }

        foreach (var food in foods)
        {
            if (!DominatesForFood(food))
            {
                return false;
            }
        }

        return true;

        bool DominatesForFood(FoodOption? food)
        {
            var mainBonus = food is null ? default : GetFoodBonus(food, (uint)job.MainAttribute);
            var criticalBonus = food is null ? default : GetFoodBonus(food, CriticalHitParamID);
            var determinationBonus = food is null ? default : GetFoodBonus(food, DeterminationParamID);
            var directHitBonus = food is null ? default : GetFoodBonus(food, DirectHitParamID);
            var minimumRatio = GetMinimumFactorRatio(
                                   DamageFactorKind.MainStat,
                                   candidate.MainStat,
                                   target.MainStat,
                                   continuationMinimum.MainStat,
                                   continuationMaximum.MainStat,
                                   mainBonus) *
                               GetMinimumFactorRatio(
                                   DamageFactorKind.CriticalHit,
                                   candidate.CriticalHit,
                                   target.CriticalHit,
                                   continuationMinimum.CriticalHit,
                                   continuationMaximum.CriticalHit,
                                   criticalBonus) *
                               GetMinimumFactorRatio(
                                   DamageFactorKind.Determination,
                                   candidate.Determination,
                                   target.Determination,
                                   continuationMinimum.Determination,
                                   continuationMaximum.Determination,
                                   determinationBonus) *
                               GetMinimumFactorRatio(
                                   DamageFactorKind.DirectHit,
                                   candidate.DirectHit,
                                   target.DirectHit,
                                   continuationMinimum.DirectHit,
                                   continuationMaximum.DirectHit,
                                   directHitBonus) *
                               GetMinimumFactorRatio(
                                   DamageFactorKind.WeaponDamage,
                                   candidate.WeaponDamage,
                                   target.WeaponDamage,
                                   continuationMinimum.WeaponDamage,
                                   continuationMaximum.WeaponDamage,
                                   default);
            if (job.IsTank)
            {
                minimumRatio *= GetMinimumFactorRatio(
                    DamageFactorKind.Tenacity,
                    candidate.Tenacity,
                    target.Tenacity,
                    continuationMinimum.Tenacity,
                    continuationMaximum.Tenacity,
                    food is null ? default : GetFoodBonus(food, TenacityParamID));
            }

            return minimumRatio > 1.0000000001d;
        }

        double GetMinimumFactorRatio(
            DamageFactorKind kind,
            int left,
            int right,
            int minimum,
            int maximum,
            ItemParameterBonus bonus)
        {
            var key = (
                Kind: kind,
                Delta: left - right,
                Minimum: minimum + right,
                Maximum: maximum + right,
                Value: bonus.Value,
                Cap: bonus.Max,
                Relative: bonus.IsRelative);
            if (factorMinimums.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var result = double.PositiveInfinity;
            for (var value = key.Minimum; value <= key.Maximum; value++)
            {
                var leftFactor = GetDamageFactor(key.Kind, ApplyFoodBonus(value + key.Delta, bonus), job, level);
                var rightFactor = GetDamageFactor(key.Kind, ApplyFoodBonus(value, bonus), job, level);
                result = Math.Min(result, leftFactor / rightFactor);
                if (result <= 1d && key.Delta <= 0)
                {
                    break;
                }
            }

            factorMinimums[key] = result;
            return result;
        }

    }

    private static double GetDamageFactor(
        DamageFactorKind kind,
        int value,
        JobProfile job,
        LevelProfile level) => kind switch
    {
        DamageFactorKind.MainStat => CalculateMainDamageFactor(job, level, value),
        DamageFactorKind.CriticalHit => CalculateCriticalFactor(level, value),
        DamageFactorKind.Determination => CalculateDeterminationFactor(level, value),
        DamageFactorKind.DirectHit => CalculateDirectHitFactor(level, value),
        DamageFactorKind.Tenacity => CalculateTenacityFactor(level, value),
        _ => CalculateWeaponDamageBase(job, level) + value
    };

    private static void GetOptionGroupBounds(
        IReadOnlyList<GearOption> options,
        JobProfile job,
        out StatBlock minimum,
        out StatBlock maximum)
    {
        minimum = options[0].Stats;
        maximum = GetOptionUpperBound(options[0], job);
        for (var index = 1; index < options.Count; index++)
        {
            minimum = Minimum(minimum, options[index].Stats);
            maximum = Maximum(maximum, GetOptionUpperBound(options[index], job));
        }
    }

    private List<FoodOption> BuildFoodOptions(JobProfile job, bool includeSpeed, GearStage stage)
    {
        var foods = new List<FoodOption>();
        foreach (var food in warmedFoodOptions)
        {
            if (stage != GearStage.Graduation &&
                food.ItemLevel > openingStageItemLevelLimit)
            {
                continue;
            }

            if (!food.Bonuses.Any(bonus => IsOptimizationParameter(bonus.BaseParamID, job, includeSpeed)))
            {
                continue;
            }

            foods.Add(food);
        }

        return PruneFoods(foods, job, includeSpeed);
    }

    private static List<FoodOption> PruneFoods(List<FoodOption> foods, JobProfile job, bool includeSpeed)
    {
        var removed = new bool[foods.Count];
        for (var index = 0; index < foods.Count; index++)
        {
            if (removed[index])
            {
                continue;
            }

            for (var other = 0; other < foods.Count; other++)
            {
                if (index != other && FoodDominates(foods[other], foods[index], job, includeSpeed))
                {
                    removed[index] = true;
                    break;
                }
            }
        }

        var result = new List<FoodOption>(foods.Count);
        for (var index = 0; index < foods.Count; index++)
        {
            if (!removed[index])
            {
                result.Add(foods[index]);
            }
        }

        return result;
    }

    private static bool FoodDominates(FoodOption left, FoodOption right, JobProfile job, bool includeSpeed)
    {
        var better = false;
        foreach (var parameterID in GetOptimizationParameterIDs(job, includeSpeed))
        {
            var leftBonus = GetFoodBonus(left, parameterID);
            var rightBonus = GetFoodBonus(right, parameterID);
            if (leftBonus.Value < rightBonus.Value || leftBonus.Max < rightBonus.Max || leftBonus.IsRelative != rightBonus.IsRelative)
            {
                return false;
            }

            better |= leftBonus.Value > rightBonus.Value || leftBonus.Max > rightBonus.Max;
        }

        return better;
    }

    private static ItemParameterBonus GetFoodBonus(FoodOption food, uint parameterID)
    {
        foreach (var bonus in food.Bonuses)
        {
            if (bonus.BaseParamID == parameterID)
            {
                return bonus;
            }
        }

        return new(parameterID, string.Empty, 0, 0, true);
    }

    private static SearchSolution? FindBestSolution(
        List<List<GearOption>> groups,
        IReadOnlyList<GearOption> weapons,
        IReadOnlyList<FoodOption> foods,
        StatBlock baseStats,
        JobProfile job,
        LevelProfile level,
        OptimizationTarget target,
        double targetGcd,
        CancellationToken cancellationToken,
        Action<float> reportProgress)
    {
        var speedCap = target == OptimizationTarget.SpecificGcd
            ? FindMaximumSpeedForGcd(level, job, targetGcd)
            : int.MaxValue;
        if (target == OptimizationTarget.MaxDamage)
        {
            weapons = PruneFactorDominatedSearchOptions(
                groups,
                weapons,
                foods,
                baseStats,
                job,
                level,
                cancellationToken);
            return FindBestMaxDamageSolution(
                groups,
                weapons,
                foods,
                baseStats,
                job,
                level,
                cancellationToken,
                reportProgress);
        }

        var frontier = BuildGearFrontier(
            groups,
            baseStats,
            job,
            target == OptimizationTarget.SpecificGcd,
            speedCap,
            cancellationToken,
            reportProgress,
            0.62f,
            0.9f);
        return FindBestGearSolution(
            frontier,
            groups.Count,
            weapons,
            foods,
            baseStats,
            job,
            level,
            target,
            targetGcd,
            cancellationToken,
            reportProgress,
            0.9f,
            0.99f);
    }

    private static SearchSolution? FindBestMaxDamageSolution(
        List<List<GearOption>> groups,
        IReadOnlyList<GearOption> weapons,
        IReadOnlyList<FoodOption> foods,
        StatBlock baseStats,
        JobProfile job,
        LevelProfile level,
        CancellationToken cancellationToken,
        Action<float> reportProgress)
    {
        var suffixUpperBounds = new StatBlock[groups.Count + 1];
        for (var index = groups.Count - 1; index >= 0; index--)
        {
            suffixUpperBounds[index] = suffixUpperBounds[index + 1] +
                                       GetOptionGroupUpperBound(groups[index], job);
        }

        var weaponUpperBound = GetOptionGroupUpperBound(weapons, job);
        var remainingUpperBounds = new StatBlock[suffixUpperBounds.Length];
        for (var index = 0; index < remainingUpperBounds.Length; index++)
        {
            remainingUpperBounds[index] = suffixUpperBounds[index] + weaponUpperBound;
        }

        var foodUpperBounds = BuildFoodUpperBounds(
            foods,
            job,
            baseStats + remainingUpperBounds[0]);
        var damageFactors = BuildDamageFactors(job, level, foodUpperBounds);
        var weaponCandidates = new List<WeaponSearchCandidate>(weapons.Count);
        foreach (var weapon in weapons)
        {
            var upperBound = GetOptionUpperBound(weapon, job);
            weaponCandidates.Add(new(
                weapon,
                upperBound,
                GetMaximumDamageUpperBound(
                    baseStats + suffixUpperBounds[0] + upperBound,
                    foodUpperBounds,
                    damageFactors)));
        }

        weaponCandidates.Sort(static (left, right) =>
        {
            var damage = right.SearchUpperBound.Damage.CompareTo(left.SearchUpperBound.Damage);
            return damage != 0
                ? damage
                : right.SearchUpperBound.Utility.CompareTo(left.SearchUpperBound.Utility);
        });
        var weaponSearchNodes = BuildWeaponSearchTree(weaponCandidates);
        var optionUpperBounds = new StatBlock[groups.Count][];
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var ranked = new List<(GearOption Option, DamageUpperBound UpperBound, int OriginalIndex)>(group.Count);
            for (var optionIndex = 0; optionIndex < group.Count; optionIndex++)
            {
                ranked.Add((
                    group[optionIndex],
                    GetMaximumDamageUpperBound(
                        baseStats + GetOptionUpperBound(group[optionIndex], job) + remainingUpperBounds[index + 1],
                        foodUpperBounds,
                        damageFactors),
                    optionIndex));
            }

            ranked.Sort(static (left, right) =>
            {
                var damage = right.UpperBound.Damage.CompareTo(left.UpperBound.Damage);
                if (damage != 0)
                {
                    return damage;
                }

                var utility = right.UpperBound.Utility.CompareTo(left.UpperBound.Utility);
                return utility != 0 ? utility : left.OriginalIndex.CompareTo(right.OriginalIndex);
            });
            var upperBounds = new StatBlock[ranked.Count];
            for (var optionIndex = 0; optionIndex < ranked.Count; optionIndex++)
            {
                group[optionIndex] = ranked[optionIndex].Option;
                upperBounds[optionIndex] = group[optionIndex].Stats + remainingUpperBounds[index + 1];
            }

            optionUpperBounds[index] = upperBounds;
        }

        var selected = new GearOption[groups.Count];
        GearOption[]? bestOptions = null;
        FoodEvaluation? bestFood = BuildInitialMaxDamageSolution(
            groups,
            weaponCandidates,
            foods,
            baseStats,
            job,
            level,
            selected,
            out bestOptions,
            cancellationToken);
        var seenByDepth = new Dictionary<DamageKey, int>[groups.Count + 1];
        for (var index = 0; index < seenByDepth.Length; index++)
        {
            seenByDepth[index] = [];
        }

        long visited = 0;
        long boundPruned = 0;
        long duplicatePruned = 0;
        long weaponBoundPruned = 0;
        long evaluated = 0;
        var stopwatch = Stopwatch.StartNew();
        var nextProgressLog = 3_000L;
        var currentRootOptionIndex = 0;

        DalamudServices.PluginLog.Information(
            "[BIS] Exact max-damage search started: armorGroups={GroupCount}, groupOptions={GroupOptions}, order={GroupOrder}, weapons={WeaponCount}, weaponNodes={WeaponNodeCount}, foods={FoodCount}, initialDamage={InitialDamage:F9}.",
            groups.Count,
            string.Join(",", groups.Select(static group => group.Count)),
            string.Join(",", groups.Select(GetDecisionGroupName)),
            weapons.Count,
            weaponSearchNodes.Count,
            foods.Count,
            bestFood?.Damage ?? 0d);
        Search(0, baseStats);

        void Search(int depth, StatBlock stats)
        {
            if ((++visited & 8191) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stopwatch.ElapsedMilliseconds >= nextProgressLog)
                {
                    DalamudServices.PluginLog.Information(
                        "[BIS] Exact max-damage progress: root={RootIndex}/{RootCount}, depth={Depth}/{GroupCount}, visited={Visited}, boundPruned={BoundPruned}, duplicatePruned={DuplicatePruned}, weaponBoundPruned={WeaponBoundPruned}, evaluated={Evaluated}, elapsedMs={ElapsedMilliseconds}.",
                        currentRootOptionIndex,
                        groups[0].Count,
                        depth,
                        groups.Count,
                        visited,
                        boundPruned,
                        duplicatePruned,
                        weaponBoundPruned,
                        evaluated,
                        stopwatch.ElapsedMilliseconds);
                    nextProgressLog = stopwatch.ElapsedMilliseconds + 3_000L;
                }
            }

            ref var seenUtility = ref CollectionsMarshal.GetValueRefOrAddDefault(
                seenByDepth[depth],
                DamageKey.From(stats, job, false),
                out var exists);
            if (exists && seenUtility >= stats.Utility)
            {
                duplicatePruned++;
                return;
            }

            seenUtility = stats.Utility;

            if (job.IsTank &&
                bestFood is { } tankBest &&
                IsUpperBoundPrunable(
                    stats + remainingUpperBounds[depth],
                    foodUpperBounds,
                    damageFactors,
                    tankBest))
            {
                boundPruned++;
                return;
            }

            if (depth == groups.Count)
            {
                SearchWeapons(0);
                return;

                void SearchWeapons(int nodeIndex)
                {
                    var node = weaponSearchNodes[nodeIndex];
                    if (bestFood is { } currentWeaponBest &&
                        IsUpperBoundPrunable(
                            stats + node.UpperBound,
                            foodUpperBounds,
                            damageFactors,
                            currentWeaponBest))
                    {
                        weaponBoundPruned += node.Count;
                        return;
                    }

                    if (node.LeftChild >= 0)
                    {
                        SearchWeapons(node.LeftChild);
                        SearchWeapons(node.RightChild);
                        return;
                    }

                    for (var weaponIndex = node.Start; weaponIndex < node.Start + node.Count; weaponIndex++)
                    {
                        var weaponCandidate = weaponCandidates[weaponIndex];
                        if (bestFood is { } weaponBest &&
                            IsUpperBoundPrunable(
                                stats + weaponCandidate.UpperBound,
                                foodUpperBounds,
                                damageFactors,
                                weaponBest))
                        {
                            weaponBoundPruned++;
                            continue;
                        }

                        evaluated++;
                        var weapon = weaponCandidate.Option;
                        GearEvaluation? evaluation;
                        if (weapon.CustomStats is null)
                        {
                            evaluation = new(weapon, FindBestFood(stats + weapon.Stats, foods, job, level));
                        }
                        else
                        {
                            evaluation = FindBestCustomGearEvaluation(
                                weapon,
                                stats,
                                foods,
                                job,
                                level,
                                OptimizationTarget.MaxDamage,
                                0d,
                                cancellationToken);
                        }

                        if (evaluation is null ||
                            bestFood is { } best &&
                            (evaluation.Food.Damage < best.Damage ||
                             evaluation.Food.Damage == best.Damage &&
                             evaluation.Food.Stats.Utility <= best.Stats.Utility))
                        {
                            continue;
                        }

                        bestFood = evaluation.Food;
                        bestOptions = new GearOption[selected.Length + 1];
                        Array.Copy(selected, bestOptions, selected.Length);
                        bestOptions[^1] = evaluation.Option;
                    }
                }
            }

            var group = groups[depth];
            for (var optionIndex = 0; optionIndex < group.Count; optionIndex++)
            {
                if (depth == 0)
                {
                    currentRootOptionIndex = optionIndex + 1;
                }

                if (!job.IsTank &&
                    bestFood is { } currentBest &&
                    IsUpperBoundPrunable(
                        stats + optionUpperBounds[depth][optionIndex],
                        foodUpperBounds,
                        damageFactors,
                        currentBest))
                {
                    boundPruned++;
                    if (depth == 0)
                    {
                        reportProgress(0.62f + 0.37f * (optionIndex + 1) / group.Count);
                    }

                    continue;
                }

                selected[depth] = group[optionIndex];
                Search(depth + 1, stats + group[optionIndex].Stats);

                if (depth == 0)
                {
                    reportProgress(0.62f + 0.37f * (optionIndex + 1) / group.Count);
                }
            }
        }

        DalamudServices.PluginLog.Information(
            "[BIS] Exact max-damage search ended: armorGroups={GroupCount}, weapons={WeaponCount}, visited={Visited}, boundPruned={BoundPruned}, duplicatePruned={DuplicatePruned}, weaponBoundPruned={WeaponBoundPruned}, evaluated={Evaluated}, seenByDepth={SeenByDepth}, elapsedMs={ElapsedMilliseconds}.",
            groups.Count,
            weapons.Count,
            visited,
            boundPruned,
            duplicatePruned,
            weaponBoundPruned,
            evaluated,
            string.Join(',', seenByDepth.Select(static seen => seen.Count)),
            stopwatch.ElapsedMilliseconds);
        return bestOptions is null || bestFood is null
            ? null
            : BuildSearchSolution(bestOptions, bestFood.Value, baseStats, job);
    }

    private static List<WeaponSearchNode> BuildWeaponSearchTree(
        IReadOnlyList<WeaponSearchCandidate> candidates)
    {
        var nodes = new List<WeaponSearchNode>((candidates.Count * 2) - 1);
        Build(0, candidates.Count);
        return nodes;

        int Build(int start, int count)
        {
            var upperBound = candidates[start].UpperBound;
            for (var index = start + 1; index < start + count; index++)
            {
                upperBound = Maximum(upperBound, candidates[index].UpperBound);
            }

            var nodeIndex = nodes.Count;
            nodes.Add(default);
            if (count <= 4)
            {
                nodes[nodeIndex] = new(start, count, upperBound, -1, -1);
                return nodeIndex;
            }

            var leftCount = count / 2;
            nodes[nodeIndex] = new(
                start,
                count,
                upperBound,
                Build(start, leftCount),
                Build(start + leftCount, count - leftCount));
            return nodeIndex;
        }
    }

    private static FoodEvaluation? BuildInitialMaxDamageSolution(
        IReadOnlyList<List<GearOption>> groups,
        IReadOnlyList<WeaponSearchCandidate> weapons,
        IReadOnlyList<FoodOption> foods,
        StatBlock baseStats,
        JobProfile job,
        LevelProfile level,
        GearOption[] selected,
        out GearOption[]? bestOptions,
        CancellationToken cancellationToken)
    {
        var stats = baseStats;
        for (var index = 0; index < groups.Count; index++)
        {
            selected[index] = groups[index][0];
            stats += selected[index].Stats;
        }

        GearEvaluation? best = null;
        foreach (var weapon in weapons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluation = FindGearEvaluation(
                weapon.Option,
                stats,
                foods,
                job,
                level,
                OptimizationTarget.MaxDamage,
                0d,
                cancellationToken);
            if (evaluation is not null && IsBetterGearEvaluation(evaluation, best))
            {
                best = evaluation;
            }
        }

        if (best is null)
        {
            bestOptions = null;
            return null;
        }

        bestOptions = new GearOption[selected.Length + 1];
        Array.Copy(selected, bestOptions, selected.Length);
        bestOptions[^1] = best.Option;
        return best.Food;
    }

    private static bool IsUpperBoundPrunable(DamageUpperBound upper, FoodEvaluation best) =>
        upper.Damage + 0.0000001d < best.Damage ||
        Math.Abs(upper.Damage - best.Damage) <= 0.0000001d &&
        upper.Utility <= best.Stats.Utility;

    private static bool IsUpperBoundPrunable(
        StatBlock stats,
        FoodUpperBoundTable foodUpperBounds,
        DamageFactorTable damageFactors,
        FoodEvaluation best)
    {
        var damage = GetMaximumDamage(stats, damageFactors);
        return damage + 0.0000001d < best.Damage ||
               Math.Abs(damage - best.Damage) <= 0.0000001d &&
               foodUpperBounds.Vitality[stats.Vitality] +
               foodUpperBounds.Speed[stats.Speed] +
               foodUpperBounds.Piety[stats.Piety] <= best.Stats.Utility;
    }

    private static string GetDecisionGroupName(IReadOnlyList<GearOption> group) =>
        string.Join('+', group[0].Pieces.Select(static piece => piece.Slot).Distinct());

    private static DamageUpperBound GetMaximumDamageUpperBound(
        StatBlock stats,
        FoodUpperBoundTable foodUpperBounds,
        DamageFactorTable damageFactors) => new(
        GetMaximumDamage(stats, damageFactors),
        foodUpperBounds.Vitality[stats.Vitality] +
        foodUpperBounds.Speed[stats.Speed] +
        foodUpperBounds.Piety[stats.Piety]);

    private static double GetMaximumDamage(
        StatBlock stats,
        DamageFactorTable damageFactors) =>
        0.01d * (damageFactors.WeaponDamageBase + stats.WeaponDamage) *
        damageFactors.MainStat[stats.MainStat] *
        damageFactors.Determination[stats.Determination] *
        damageFactors.Tenacity[stats.Tenacity] *
        damageFactors.TraitDamageMultiplier *
        damageFactors.CriticalHit[stats.CriticalHit] *
        damageFactors.DirectHit[stats.DirectHit];

    private static DamageFactorTable BuildDamageFactors(
        JobProfile job,
        LevelProfile level,
        FoodUpperBoundTable foodUpperBounds) => new(
        CalculateWeaponDamageBase(job, level),
        job.TraitDamageMultiplier,
        BuildDamageFactors(foodUpperBounds.MainStat, value => CalculateMainDamageFactor(job, level, value)),
        BuildDamageFactors(foodUpperBounds.CriticalHit, value => CalculateCriticalFactor(level, value)),
        BuildDamageFactors(foodUpperBounds.Determination, value => CalculateDeterminationFactor(level, value)),
        BuildDamageFactors(foodUpperBounds.DirectHit, value => CalculateDirectHitFactor(level, value)),
        BuildDamageFactors(
            foodUpperBounds.Tenacity,
            job.IsTank ? value => CalculateTenacityFactor(level, value) : static _ => 1d));

    private static double[] BuildDamageFactors(int[] upperBounds, Func<int, double> calculate)
    {
        var factors = new double[upperBounds.Length];
        for (var value = 0; value < upperBounds.Length; value++)
        {
            factors[value] = calculate(upperBounds[value]);
        }

        return factors;
    }

    private static FoodUpperBoundTable BuildFoodUpperBounds(
        IReadOnlyList<FoodOption> foods,
        JobProfile job,
        StatBlock maximumStats) => new(
        BuildFoodParameterUpperBounds(foods, (uint)job.MainAttribute, maximumStats.MainStat),
        BuildFoodParameterUpperBounds(foods, VitalityParamID, maximumStats.Vitality),
        BuildFoodParameterUpperBounds(foods, CriticalHitParamID, maximumStats.CriticalHit),
        BuildFoodParameterUpperBounds(foods, DeterminationParamID, maximumStats.Determination),
        BuildFoodParameterUpperBounds(foods, DirectHitParamID, maximumStats.DirectHit),
        BuildFoodParameterUpperBounds(
            foods,
            job.UsesSpellSpeed ? SpellSpeedParamID : SkillSpeedParamID,
            maximumStats.Speed),
        BuildFoodParameterUpperBounds(foods, PietyParamID, maximumStats.Piety),
        BuildFoodParameterUpperBounds(foods, TenacityParamID, maximumStats.Tenacity));

    private static int[] BuildFoodParameterUpperBounds(
        IReadOnlyList<FoodOption> foods,
        uint parameterID,
        int maximum)
    {
        var upperBounds = new int[maximum + 1];
        for (var current = 0; current <= maximum; current++)
        {
            var upperBound = current;
            foreach (var food in foods)
            {
                foreach (var bonus in food.Bonuses)
                {
                    if (bonus.BaseParamID == parameterID)
                    {
                        upperBound = Math.Max(upperBound, ApplyFoodBonus(current, bonus));
                    }
                }
            }

            upperBounds[current] = upperBound;
        }

        return upperBounds;
    }

    private static StatBlock GetOptionGroupUpperBound(IReadOnlyList<GearOption> options, JobProfile job)
    {
        var upperBound = default(StatBlock);
        foreach (var option in options)
        {
            upperBound = Maximum(upperBound, GetOptionUpperBound(option, job));
        }

        return upperBound;
    }

    private static StatBlock GetOptionUpperBound(GearOption option, JobProfile job)
    {
        var upperBound = option.Stats;
        if (option.CustomStats is null)
        {
            return upperBound;
        }

        foreach (var parameter in option.CustomStats.Parameters)
        {
            upperBound = upperBound.WithParameter(
                parameter.ParameterID,
                upperBound.GetParameter(parameter.ParameterID, job) + parameter.EffectiveCap,
                job);
        }

        return upperBound;
    }

    private static StatBlock Maximum(StatBlock left, StatBlock right) => new(
        Math.Max(left.MainStat, right.MainStat),
        Math.Max(left.Vitality, right.Vitality),
        Math.Max(left.CriticalHit, right.CriticalHit),
        Math.Max(left.Determination, right.Determination),
        Math.Max(left.DirectHit, right.DirectHit),
        Math.Max(left.Speed, right.Speed),
        Math.Max(left.Piety, right.Piety),
        Math.Max(left.Tenacity, right.Tenacity),
        Math.Max(left.WeaponDamage, right.WeaponDamage));

    private static StatBlock Minimum(StatBlock left, StatBlock right) => new(
        Math.Min(left.MainStat, right.MainStat),
        Math.Min(left.Vitality, right.Vitality),
        Math.Min(left.CriticalHit, right.CriticalHit),
        Math.Min(left.Determination, right.Determination),
        Math.Min(left.DirectHit, right.DirectHit),
        Math.Min(left.Speed, right.Speed),
        Math.Min(left.Piety, right.Piety),
        Math.Min(left.Tenacity, right.Tenacity),
        Math.Min(left.WeaponDamage, right.WeaponDamage));

    private static List<SearchState> BuildGearFrontier(
        IReadOnlyList<List<GearOption>> groups,
        StatBlock baseStats,
        JobProfile job,
        bool preserveSpeed,
        int speedCap,
        CancellationToken cancellationToken,
        Action<float> reportProgress,
        float progressStart,
        float progressEnd)
    {
        var rootKey = DamageKey.From(baseStats, job, preserveSpeed, speedCap);
        var frontier = new List<SearchState> { new(baseStats, rootKey, null, null) };
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var groupName = string.Join("+", group[0].Pieces.Select(static piece => piece.Slot.ToString()));
            var groupStopwatch = Stopwatch.StartNew();
            var inputFrontierCount = frontier.Count;
            var groupProgressRange = (progressEnd - progressStart) / groups.Count;
            var groupProgressStart = progressStart + groupProgressRange * groupIndex;
            DalamudServices.PluginLog.Information(
                "[BIS] Frontier group {GroupIndex}/{GroupCount} started: slots={Slots}, frontier={FrontierCount}, options={OptionCount}, combinations={CombinationCount}.",
                groupIndex + 1,
                groups.Count,
                groupName,
                inputFrontierCount,
                group.Count,
                (long)inputFrontierCount * group.Count);
            var unique = new Dictionary<DamageKey, SearchState>();
            var visited = 0;
            foreach (var state in frontier)
            {
                foreach (var option in group)
                {
                    if ((++visited & 2047) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    var stats = state.Stats + option.Stats;
                    if (preserveSpeed && stats.Speed > speedCap)
                    {
                        continue;
                    }

                    var key = DamageKey.From(stats, job, preserveSpeed, speedCap);
                    if (!unique.TryGetValue(key, out var existing) || stats.Utility > existing.Stats.Utility)
                    {
                        unique[key] = new(stats, key, state, option);
                    }
                }
            }

            DalamudServices.PluginLog.Information(
                "[BIS] Frontier group {GroupIndex}/{GroupCount} expanded: slots={Slots}, unique={UniqueCount}, elapsedMs={ElapsedMilliseconds}.",
                groupIndex + 1,
                groups.Count,
                groupName,
                unique.Count,
                groupStopwatch.ElapsedMilliseconds);
            reportProgress(groupProgressStart + groupProgressRange * 0.1f);
            var pruneStopwatch = Stopwatch.StartNew();
            frontier = PruneSearchStates(
                unique,
                cancellationToken,
                progress => reportProgress(groupProgressStart + groupProgressRange * (0.1f + 0.9f * progress)));
            DalamudServices.PluginLog.Information(
                "[BIS] Frontier group {GroupIndex}/{GroupCount} pruned: slots={Slots}, frontier={FrontierCount}, pruneMs={PruneMilliseconds}, totalMs={TotalMilliseconds}.",
                groupIndex + 1,
                groups.Count,
                groupName,
                frontier.Count,
                pruneStopwatch.ElapsedMilliseconds,
                groupStopwatch.ElapsedMilliseconds);
            reportProgress(groupProgressStart + groupProgressRange);
        }

        return frontier;
    }

    private static List<SearchState> PruneSearchStates(
        IReadOnlyDictionary<DamageKey, SearchState> unique,
        CancellationToken cancellationToken,
        Action<float> reportProgress)
    {
        var candidates = unique.Values.ToList();
        candidates.Sort(static (left, right) =>
        {
            var comparison = right.Key.WeaponDamage.CompareTo(left.Key.WeaponDamage);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = right.Key.MainStat.CompareTo(left.Key.MainStat);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = right.Key.CriticalHit.CompareTo(left.Key.CriticalHit);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = right.Key.Determination.CompareTo(left.Key.Determination);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = right.Key.DirectHit.CompareTo(left.Key.DirectHit);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = right.Key.Tenacity.CompareTo(left.Key.Tenacity);
            return comparison != 0 ? comparison : right.Key.Speed.CompareTo(left.Key.Speed);
        });

        if (candidates.Count <= SearchPruneBatchSize)
        {
            var frontier = PruneSortedSearchStates(candidates, 0, candidates.Count, cancellationToken);
            reportProgress(1f);
            return frontier;
        }

        var initialBatchCount = (candidates.Count + SearchPruneBatchSize - 1) / SearchPruneBatchSize;
        var roundCount = 1;
        for (var count = initialBatchCount; count > 1; count = (count + 1) / 2)
        {
            roundCount++;
        }

        var roundIndex = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = SearchPruneParallelism
        };

        void RunRound(int batchCount, Action<int> processBatch)
        {
            var completedBatches = 0;
            var progressLock = new object();
            Parallel.For(0, batchCount, parallelOptions, index =>
            {
                processBatch(index);
                lock (progressLock)
                {
                    completedBatches++;
                    reportProgress((roundIndex + (float)completedBatches / batchCount) / roundCount);
                }
            });
            roundIndex++;
        }

        var roundStopwatch = Stopwatch.StartNew();
        var frontiers = new List<SearchState>[initialBatchCount];
        RunRound(initialBatchCount, index =>
        {
            var start = index * SearchPruneBatchSize;
            frontiers[index] = PruneSortedSearchStates(
                candidates,
                start,
                Math.Min(SearchPruneBatchSize, candidates.Count - start),
                cancellationToken);
        });
        DalamudServices.PluginLog.Information(
            "[BIS] Pareto prune round {RoundIndex}/{RoundCount}: batches={BatchCount}, input={InputCount}, output={OutputCount}, parallelism={Parallelism}, elapsedMs={ElapsedMilliseconds}.",
            roundIndex,
            roundCount,
            frontiers.Length,
            candidates.Count,
            frontiers.Sum(static frontier => frontier.Count),
            SearchPruneParallelism,
            roundStopwatch.ElapsedMilliseconds);

        while (frontiers.Length > 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = new List<SearchState>[(frontiers.Length + 1) / 2];
            var inputCount = frontiers.Sum(static frontier => frontier.Count);
            roundStopwatch.Restart();
            RunRound(next.Length, index =>
            {
                var left = frontiers[index * 2];
                if (index * 2 + 1 >= frontiers.Length)
                {
                    next[index] = left;
                    return;
                }

                var right = frontiers[index * 2 + 1];
                next[index] = MergeSortedSearchFrontiers(left, right, cancellationToken);
            });
            DalamudServices.PluginLog.Information(
                "[BIS] Pareto prune round {RoundIndex}/{RoundCount}: batches={BatchCount}, input={InputCount}, output={OutputCount}, parallelism={Parallelism}, elapsedMs={ElapsedMilliseconds}.",
                roundIndex,
                roundCount,
                next.Length,
                inputCount,
                next.Sum(static frontier => frontier.Count),
                SearchPruneParallelism,
                roundStopwatch.ElapsedMilliseconds);
            frontiers = next;
        }

        return frontiers[0];
    }

    private static List<SearchState> MergeSortedSearchFrontiers(
        List<SearchState> left,
        List<SearchState> right,
        CancellationToken cancellationToken)
    {
        var result = new List<SearchState>(left.Count + right.Count);
        result.AddRange(left);
        var dominanceIndex = new DominanceIndex(left);
        for (var index = 0; index < right.Count; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // 属性已经按降序排列，只需检查右侧结果是否被左侧淘汰。
            if (!dominanceIndex.ContainsDominator(right[index].Key))
            {
                result.Add(right[index]);
            }
        }

        return result;
    }

    private static List<SearchState> PruneSortedSearchStates(
        IReadOnlyList<SearchState> candidates,
        int start,
        int count,
        CancellationToken cancellationToken)
    {
        var frontier = new List<SearchState>();
        var visited = 0;
        for (var candidateIndex = start; candidateIndex < start + count; candidateIndex++)
        {
            if ((++visited & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var candidate = candidates[candidateIndex];
            var dominated = false;
            for (var index = frontier.Count - 1; index >= 0; index--)
            {
                if (frontier[index].Key.Dominates(candidate.Key))
                {
                    dominated = true;
                    break;
                }
            }

            if (!dominated)
            {
                frontier.Add(candidate);
            }
        }

        return frontier;
    }

    private static SearchSolution? FindBestGearSolution(
        IReadOnlyList<SearchState> frontier,
        int groupCount,
        IReadOnlyList<GearOption> weapons,
        IReadOnlyList<FoodOption> foods,
        StatBlock baseStats,
        JobProfile job,
        LevelProfile level,
        OptimizationTarget target,
        double targetGcd,
        CancellationToken cancellationToken,
        Action<float> reportProgress,
        float progressStart,
        float progressEnd)
    {
        SearchState? bestState = null;
        GearEvaluation? bestEvaluation = null;
        for (var stateIndex = 0; stateIndex < frontier.Count; stateIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var weapon in weapons)
            {
                var evaluation = FindGearEvaluation(
                    weapon,
                    frontier[stateIndex].Stats,
                    foods,
                    job,
                    level,
                    target,
                    targetGcd,
                    cancellationToken);
                if (evaluation is null)
                {
                    continue;
                }

                if (!IsBetterGearEvaluation(evaluation, bestEvaluation))
                {
                    continue;
                }

                bestState = frontier[stateIndex];
                bestEvaluation = evaluation;
            }

            reportProgress(progressStart + (progressEnd - progressStart) * (stateIndex + 1) / frontier.Count);
        }

        if (bestState is null || bestEvaluation is null)
        {
            return null;
        }

        var options = GetSearchOptions(bestState, groupCount);
        Array.Resize(ref options, options.Length + 1);
        options[^1] = bestEvaluation.Option;
        return BuildSearchSolution(options, bestEvaluation.Food, baseStats, job);
    }

    private static GearEvaluation? FindGearEvaluation(
        GearOption option,
        StatBlock baseStats,
        IReadOnlyList<FoodOption> foods,
        JobProfile job,
        LevelProfile level,
        OptimizationTarget target,
        double targetGcd,
        CancellationToken cancellationToken)
    {
        if (option.CustomStats is not null)
        {
            return FindBestCustomGearEvaluation(
                option,
                baseStats,
                foods,
                job,
                level,
                target,
                targetGcd,
                cancellationToken);
        }

        var stats = baseStats + option.Stats;
        FoodEvaluation? food = target switch
        {
            OptimizationTarget.SpecificGcd => FindBestFoodForGcd(stats, foods, job, level, targetGcd),
            _ => FindBestFood(stats, foods, job, level)
        };
        return food is { } selected ? new(option, selected) : null;
    }

    private static bool IsBetterGearEvaluation(GearEvaluation candidate, GearEvaluation? current)
    {
        if (current is null)
        {
            return true;
        }

        return candidate.Food.Damage > current.Food.Damage ||
               candidate.Food.Damage == current.Food.Damage &&
               candidate.Food.Stats.Utility > current.Food.Stats.Utility;
    }

    private static GearOption[] GetSearchOptions(SearchState state, int groupCount)
    {
        var options = new GearOption[groupCount];
        for (var index = groupCount - 1; index >= 0; index--)
        {
            options[index] = state.Option!;
            state = state.Previous!;
        }

        return options;
    }

    private static GearEvaluation? FindBestCustomGearEvaluation(
        GearOption option,
        StatBlock baseStats,
        IReadOnlyList<FoodOption> foods,
        JobProfile job,
        LevelProfile level,
        OptimizationTarget target,
        double targetGcd,
        CancellationToken cancellationToken)
    {
        GearEvaluation? best = null;
        EvaluateFood(null);
        foreach (var food in foods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvaluateFood(food);
        }

        return best;

        void EvaluateFood(FoodOption? food)
        {
            var candidate = FindBestCustomGearEvaluationForFood(
                option,
                baseStats,
                food,
                job,
                level,
                target,
                targetGcd,
                cancellationToken);
            if (candidate is null)
            {
                return;
            }

            if (IsBetterGearEvaluation(candidate, best))
            {
                best = candidate;
            }
        }
    }

    private static GearEvaluation? FindBestCustomGearEvaluationForFood(
        GearOption option,
        StatBlock baseStats,
        FoodOption? food,
        JobProfile job,
        LevelProfile level,
        OptimizationTarget target,
        double targetGcd,
        CancellationToken cancellationToken)
    {
        var budget = option.CustomStats!;
        var fixedStats = baseStats + option.Stats;
        var speedParameterID = job.UsesSpellSpeed ? SpellSpeedParamID : SkillSpeedParamID;
        var speedIndex = -1;
        if (target == OptimizationTarget.SpecificGcd)
        {
            for (var index = 0; index < budget.Parameters.Length; index++)
            {
                if (budget.Parameters[index].ParameterID == speedParameterID)
                {
                    speedIndex = index;
                    break;
                }
            }
        }

        if (speedIndex < 0)
        {
            var table = BuildCustomAllocationTable(budget, fixedStats, food, job, level, -1, cancellationToken);
            if (!TryBuildCustomAllocation(budget, table, -1, 0, out var allocation))
            {
                return null;
            }

            var evaluation = BuildCustomGearEvaluation(option, baseStats, allocation, food, job, level);
            return IsCustomEvaluationValid(evaluation, job, level, target, targetGcd)
                ? evaluation
                : null;
        }

        GearEvaluation? best = null;
        var allocationTable = BuildCustomAllocationTable(
            budget,
            fixedStats,
            food,
            job,
            level,
            speedIndex,
            cancellationToken);
        for (var speed = 0; speed <= budget.MaxEachStat; speed++)
        {
            if ((speed & 15) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!TryBuildCustomAllocation(budget, allocationTable, speedIndex, speed, out var allocation))
            {
                continue;
            }

            var candidate = BuildCustomGearEvaluation(option, baseStats, allocation, food, job, level);
            if (!IsCustomEvaluationValid(candidate, job, level, target, targetGcd))
            {
                continue;
            }

            if (IsBetterGearEvaluation(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static CustomAllocationTable BuildCustomAllocationTable(
        CustomStatBudget budget,
        StatBlock fixedStats,
        FoodOption? food,
        JobProfile job,
        LevelProfile level,
        int fixedParameterIndex,
        CancellationToken cancellationToken)
    {
        var parameterIndices = new int[budget.Parameters.Length - (fixedParameterIndex >= 0 ? 1 : 0)];
        var targetIndex = 0;
        for (var sourceIndex = 0; sourceIndex < budget.Parameters.Length; sourceIndex++)
        {
            if (sourceIndex != fixedParameterIndex)
            {
                parameterIndices[targetIndex++] = sourceIndex;
            }
        }

        var scores = new double[parameterIndices.Length + 1, budget.Total + 1];
        var utilities = new int[parameterIndices.Length + 1, budget.Total + 1];
        var choices = new short[parameterIndices.Length, budget.Total + 1];
        for (var stage = 0; stage <= parameterIndices.Length; stage++)
        {
            for (var total = 0; total <= budget.Total; total++)
            {
                scores[stage, total] = double.NegativeInfinity;
            }
        }

        scores[0, 0] = 1d;
        for (var stage = 0; stage < parameterIndices.Length; stage++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameter = budget.Parameters[parameterIndices[stage]];
            var factors = new double[budget.MaxEachStat + 1];
            var utilityValues = new int[budget.MaxEachStat + 1];
            var current = fixedStats.GetParameter(parameter.ParameterID, job);
            var bonus = food is null ? default : GetFoodBonus(food, parameter.ParameterID);
            for (var value = 0; value <= budget.MaxEachStat; value++)
            {
                var foodValue = ApplyFoodBonus(current + Math.Min(value, parameter.EffectiveCap), bonus);
                factors[value] = GetCustomDamageFactor(parameter.ParameterID, foodValue, job, level);
                utilityValues[value] = GetCustomUtilityValue(parameter.ParameterID, foodValue, job);
            }

            for (var total = 0; total <= budget.Total; total++)
            {
                if ((total & 127) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (double.IsNegativeInfinity(scores[stage, total]))
                {
                    continue;
                }

                var maximum = Math.Min(budget.MaxEachStat, budget.Total - total);
                for (var value = 0; value <= maximum; value++)
                {
                    var nextTotal = total + value;
                    var score = scores[stage, total] * factors[value];
                    var utility = utilities[stage, total] + utilityValues[value];
                    if (score > scores[stage + 1, nextTotal] ||
                        score == scores[stage + 1, nextTotal] && utility > utilities[stage + 1, nextTotal])
                    {
                        scores[stage + 1, nextTotal] = score;
                        utilities[stage + 1, nextTotal] = utility;
                        choices[stage, nextTotal] = (short)value;
                    }
                }
            }
        }

        return new(parameterIndices, scores, choices);
    }

    private static bool TryBuildCustomAllocation(
        CustomStatBudget budget,
        CustomAllocationTable table,
        int fixedParameterIndex,
        int fixedParameterValue,
        out int[] allocation)
    {
        allocation = new int[budget.Parameters.Length];
        if (fixedParameterIndex >= 0)
        {
            if (fixedParameterValue > budget.MaxEachStat)
            {
                return false;
            }

            allocation[fixedParameterIndex] = fixedParameterValue;
        }

        var remaining = budget.Total - (fixedParameterIndex >= 0 ? fixedParameterValue : 0);
        if (remaining < 0 ||
            remaining > table.ParameterIndices.Length * budget.MaxEachStat ||
            double.IsNegativeInfinity(table.Scores[table.ParameterIndices.Length, remaining]))
        {
            return false;
        }

        for (var stage = table.ParameterIndices.Length - 1; stage >= 0; stage--)
        {
            var value = table.Choices[stage, remaining];
            allocation[table.ParameterIndices[stage]] = value;
            remaining -= value;
        }

        return true;
    }

    private static GearEvaluation BuildCustomGearEvaluation(
        GearOption option,
        StatBlock baseStats,
        IReadOnlyList<int> allocation,
        FoodOption? food,
        JobProfile job,
        LevelProfile level)
    {
        var itemStats = option.Stats;
        for (var index = 0; index < option.CustomStats!.Parameters.Length; index++)
        {
            var parameter = option.CustomStats.Parameters[index];
            itemStats = itemStats.WithParameter(
                parameter.ParameterID,
                itemStats.GetParameter(parameter.ParameterID, job) +
                Math.Min(allocation[index], parameter.EffectiveCap),
                job);
        }

        var pieces = (GearPiece[])option.Pieces.Clone();
        pieces[0] = pieces[0] with { StatText = BuildStatText(itemStats, job) };
        var allocatedOption = new GearOption(pieces, itemStats);
        var stats = baseStats + itemStats;
        if (food is not null)
        {
            stats = ApplyFood(stats, food, job);
        }

        return new(allocatedOption, new(food, stats, CalculateDamage(job, level, stats)));
    }

    private static bool IsCustomEvaluationValid(
        GearEvaluation evaluation,
        JobProfile job,
        LevelProfile level,
        OptimizationTarget target,
        double targetGcd) => target switch
        {
            OptimizationTarget.SpecificGcd => IsTargetGcd(level, evaluation.Food.Stats, job, targetGcd),
            _ => true
        };

    private static double GetCustomDamageFactor(
        uint parameterID,
        int value,
        JobProfile job,
        LevelProfile level) => parameterID switch
        {
            CriticalHitParamID => CalculateCriticalFactor(level, value),
            DeterminationParamID => CalculateDeterminationFactor(level, value),
            DirectHitParamID => CalculateDirectHitFactor(level, value),
            TenacityParamID when job.IsTank => CalculateTenacityFactor(level, value),
            _ => 1d
        };

    private static int GetCustomUtilityValue(uint parameterID, int value, JobProfile job) =>
        parameterID == VitalityParamID ||
        parameterID == PietyParamID ||
        parameterID == (job.UsesSpellSpeed ? SpellSpeedParamID : SkillSpeedParamID)
            ? value
            : 0;

    private static int FindMinimumSpeedForGcd(LevelProfile level, JobProfile job, double targetGcd)
    {
        var upper = Math.Max(level.Sub, 1);
        while (upper < 100_000 &&
               CalculateGcd(level, default(StatBlock) with { Speed = upper }, job) > targetGcd + 0.0001d)
        {
            upper *= 2;
        }

        var lower = 0;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (CalculateGcd(level, default(StatBlock) with { Speed = middle }, job) <= targetGcd + 0.0001d)
            {
                upper = middle;
            }
            else
            {
                lower = middle + 1;
            }
        }

        return lower;
    }

    private static int FindMaximumSpeedForGcd(LevelProfile level, JobProfile job, double targetGcd)
    {
        var minimum = FindMinimumSpeedForGcd(level, job, targetGcd);
        if (!IsTargetGcd(level, default(StatBlock) with { Speed = minimum }, job, targetGcd))
        {
            return minimum - 1;
        }

        var upper = Math.Max(minimum + 1, level.Sub);
        while (upper < 100_000 &&
               IsTargetGcd(level, default(StatBlock) with { Speed = upper }, job, targetGcd))
        {
            upper *= 2;
        }

        var lower = minimum;
        while (lower + 1 < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (IsTargetGcd(level, default(StatBlock) with { Speed = middle }, job, targetGcd))
            {
                lower = middle;
            }
            else
            {
                upper = middle;
            }
        }

        return lower;
    }

    private static bool IsTargetGcd(LevelProfile level, StatBlock stats, JobProfile job, double targetGcd) =>
        Math.Abs(CalculateGcd(level, stats, job) - targetGcd) < 0.0001d;

    private static SearchSolution BuildSearchSolution(
        GearOption[] options,
        FoodEvaluation food,
        StatBlock baseStats,
        JobProfile job) => new(
        options,
        food.Stats,
        food.Damage,
        food.Food is null
            ? null
            : BuildFoodResult(
                food.Food,
                options.Aggregate(baseStats, static (stats, option) => stats + option.Stats),
                job));

    private static FoodEvaluation FindBestFood(
        StatBlock stats,
        IReadOnlyList<FoodOption> foods,
        JobProfile job,
        LevelProfile level)
    {
        var best = new FoodEvaluation(null, stats, CalculateDamage(job, level, stats));
        foreach (var food in foods)
        {
            var foodStats = ApplyFood(stats, food, job);
            var damage = CalculateDamage(job, level, foodStats);
            if (damage > best.Damage || damage == best.Damage && foodStats.Utility > best.Stats.Utility)
            {
                best = new(food, foodStats, damage);
            }
        }

        return best;
    }

    private static FoodEvaluation? FindBestFoodForGcd(
        StatBlock stats,
        IReadOnlyList<FoodOption> foods,
        JobProfile job,
        LevelProfile level,
        double targetGcd)
    {
        FoodEvaluation? best = null;
        Evaluate(null, stats);
        foreach (var food in foods)
        {
            Evaluate(food, ApplyFood(stats, food, job));
        }

        return best;

        void Evaluate(FoodOption? food, StatBlock foodStats)
        {
            if (!IsTargetGcd(level, foodStats, job, targetGcd))
            {
                return;
            }

            var damage = CalculateDamage(job, level, foodStats);
            if (best is null || damage > best.Value.Damage)
            {
                best = new(food, foodStats, damage);
            }
        }
    }

    private static StatBlock ApplyFood(StatBlock stats, FoodOption food, JobProfile job)
    {
        var result = stats;
        foreach (var bonus in food.Bonuses)
        {
            var current = stats.GetParameter(bonus.BaseParamID, job);
            if (current <= 0)
            {
                continue;
            }

            result = result.WithParameter(
                bonus.BaseParamID,
                ApplyFoodBonus(current, bonus),
                job);
        }

        return result;
    }

    private static int ApplyFoodBonus(int current, ItemParameterBonus bonus) => current <= 0
        ? current
        : current + (bonus.IsRelative
            ? Math.Min((int)Math.Floor(current * bonus.Value / 100d), bonus.Max)
            : bonus.Value);

    private static FoodResult BuildFoodResult(FoodOption food, StatBlock gearStats, JobProfile job)
    {
        var effects = new List<string>();
        foreach (var bonus in food.Bonuses)
        {
            var current = gearStats.GetParameter(bonus.BaseParamID, job);
            if (current <= 0)
            {
                continue;
            }

            var value = ApplyFoodBonus(current, bonus) - current;
            if (value > 0)
            {
                effects.Add($"{bonus.Name} +{value}");
            }
        }

        return new(food.Name, food.IconID, food.ItemLevel, food.IsHighQuality, string.Join("  ", effects));
    }

    private static bool IsDamageParameter(uint parameterID, JobProfile job) =>
        parameterID == (uint)job.MainAttribute ||
        parameterID is CriticalHitParamID or DeterminationParamID or DirectHitParamID ||
        job.IsTank && parameterID == TenacityParamID;

    private static uint[] GetDamageParameterIDs(JobProfile job) =>
        job.IsTank
            ? [(uint)job.MainAttribute, CriticalHitParamID, DeterminationParamID, DirectHitParamID, TenacityParamID]
            : [(uint)job.MainAttribute, CriticalHitParamID, DeterminationParamID, DirectHitParamID];

    private static bool IsOptimizationParameter(uint parameterID, JobProfile job, bool includeSpeed) =>
        IsDamageParameter(parameterID, job) ||
        includeSpeed && parameterID == (job.UsesSpellSpeed ? SpellSpeedParamID : SkillSpeedParamID);

    private static uint[] GetOptimizationParameterIDs(JobProfile job, bool includeSpeed) => includeSpeed
        ? [.. GetDamageParameterIDs(job), job.UsesSpellSpeed ? SpellSpeedParamID : SkillSpeedParamID]
        : GetDamageParameterIDs(job);

    private static double CalculateGcd(LevelProfile level, StatBlock stats, JobProfile job) =>
        BetterCharacterPanelState.CalculateGcd(
            level.Level,
            stats.Speed,
            (int)Math.Round((1d - job.GcdModifier) * 100d));

    private static double CalculateCriticalFactor(LevelProfile level, int criticalHit)
    {
        var chance = Math.Floor(200d * (criticalHit - level.Sub) / level.Div + 50d) / 1000d;
        var damage = Math.Floor(200d * (criticalHit - level.Sub) / level.Div + 1400d) / 1000d;
        return (damage - 1d) * chance + 1d;
    }

    private static double CalculateDeterminationFactor(LevelProfile level, int determination) => Math.Floor(
        (140d * (determination - level.Main) / level.Det + 1000d) / level.DetTrunc) * level.DetTrunc / 1000d;

    private static double CalculateDirectHitFactor(LevelProfile level, int directHit) =>
        0.25d * Math.Floor(550d * (directHit - level.Sub) / level.Div) / 1000d + 1d;

    private static double CalculateTenacityFactor(LevelProfile level, int tenacity) =>
        Math.Floor(112d * (tenacity - level.Sub) / level.Div + 1000d) / 1000d;

    private static double CalculateWeaponDamageBase(JobProfile job, LevelProfile level) =>
        Math.Floor(level.Main * job.AttackStatModifier / 1000d);

    private static double CalculateMainDamageFactor(JobProfile job, LevelProfile level, int mainStat) => Math.Floor(
        (job.IsTank ? level.AttackPowerTank : level.AttackPower) *
        (Math.Floor(mainStat * job.PartyBonus) - level.Main) / level.Main + 100d) / 100d;

    private static double CalculateDamage(JobProfile job, LevelProfile level, StatBlock stats)
        => 0.01d * (CalculateWeaponDamageBase(job, level) + stats.WeaponDamage) *
            CalculateMainDamageFactor(job, level, stats.MainStat) * CalculateDeterminationFactor(level, stats.Determination) *
            (job.IsTank ? CalculateTenacityFactor(level, stats.Tenacity) : 1d) * job.TraitDamageMultiplier *
            CalculateCriticalFactor(level, stats.CriticalHit) * CalculateDirectHitFactor(level, stats.DirectHit);

    private static string GetParameterName(uint parameterID) =>
        LuminaGetter.TryGetRow<BaseParam>(parameterID, out var parameter)
            ? parameter.Name.ExtractText()
            : parameterID.ToString(CultureInfo.InvariantCulture);

    private static bool TryGetSlot(EquipSlotCategory category, out GearSlot slot)
    {
        if (category.MainHand > 0)
        {
            slot = GearSlot.MainHand;
            return true;
        }

        if (category.OffHand > 0)
        {
            slot = GearSlot.OffHand;
            return true;
        }

        if (category.Head > 0)
        {
            slot = GearSlot.Head;
            return true;
        }

        if (category.Body > 0)
        {
            slot = GearSlot.Body;
            return true;
        }

        if (category.Gloves > 0)
        {
            slot = GearSlot.Hands;
            return true;
        }

        if (category.Legs > 0)
        {
            slot = GearSlot.Legs;
            return true;
        }

        if (category.Feet > 0)
        {
            slot = GearSlot.Feet;
            return true;
        }

        if (category.Ears > 0)
        {
            slot = GearSlot.Ears;
            return true;
        }

        if (category.Neck > 0)
        {
            slot = GearSlot.Neck;
            return true;
        }

        if (category.Wrists > 0)
        {
            slot = GearSlot.Wrists;
            return true;
        }

        if (category.FingerL > 0 || category.FingerR > 0)
        {
            slot = GearSlot.Ring;
            return true;
        }

        slot = default;
        return false;
    }

    private static string GetSlotName(GearSlot slot, int ringIndex) => OmniLoc.Get(slot switch
    {
        GearSlot.MainHand => "Feature.BestInSlotGear.Slot.MainHand",
        GearSlot.OffHand => "Feature.BestInSlotGear.Slot.OffHand",
        GearSlot.Head => "Feature.BestInSlotGear.Slot.Head",
        GearSlot.Body => "Feature.BestInSlotGear.Slot.Body",
        GearSlot.Hands => "Feature.BestInSlotGear.Slot.Hands",
        GearSlot.Legs => "Feature.BestInSlotGear.Slot.Legs",
        GearSlot.Feet => "Feature.BestInSlotGear.Slot.Feet",
        GearSlot.Ears => "Feature.BestInSlotGear.Slot.Ears",
        GearSlot.Neck => "Feature.BestInSlotGear.Slot.Neck",
        GearSlot.Wrists => "Feature.BestInSlotGear.Slot.Wrists",
        GearSlot.Ring when ringIndex == 0 => "Feature.BestInSlotGear.Slot.RingLeft",
        GearSlot.Ring => "Feature.BestInSlotGear.Slot.RingRight",
        _ => "Feature.BestInSlotGear.Slot.Other"
    });

}
