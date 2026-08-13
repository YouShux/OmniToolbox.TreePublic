using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.Exd;
using Lumina.Excel.Sheets;
using Lumina.Text;
using OmniToolbox.Host;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BestInSlotGear
{
    private const string GearingURL = "https://asvel.github.io/ffxiv-gearing/";
    private const string ShareCharset = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int ShareVersion = 5;
    private const int ShareVersionRange = 77;
    private const int ShareJobLevelRange = 6;
    private const int ShareSyncLevelRange = 800;
    private const int ShareGearTypeRange = 8;
    private const int ShareGearIDRange = 60000;
    private const int ShareMateriaGradeRange = 12;
    private const int ShareCustomStatRange = 1001;

    private static readonly byte[] ShareJobLevels = [50, 60, 70, 80, 90, 100];
    private static readonly ShareJob[] ShareJobs =
    [
        new(19, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID, TenacityParamID]),
        new(21, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID, TenacityParamID]),
        new(32, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID, TenacityParamID]),
        new(37, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID, TenacityParamID]),
        new(24, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SpellSpeedParamID, PietyParamID]),
        new(28, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SpellSpeedParamID, PietyParamID]),
        new(33, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SpellSpeedParamID, PietyParamID]),
        new(40, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SpellSpeedParamID, PietyParamID]),
        new(20, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID]),
        new(22, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID]),
        new(30, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID]),
        new(34, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID]),
        new(39, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID]),
        new(41, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID]),
        new(23, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID]),
        new(31, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID]),
        new(38, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SkillSpeedParamID]),
        new(25, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SpellSpeedParamID]),
        new(27, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SpellSpeedParamID]),
        new(35, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SpellSpeedParamID]),
        new(42, [CriticalHitParamID, DeterminationParamID, DirectHitParamID, SpellSpeedParamID])
    ];

    private bool TryExportCurrentResult(out string error)
    {
        if (result is not { Error: null } current)
        {
            error = OmniLoc.Get("Feature.BestInSlotGear.Share.NoResult");
            return false;
        }

        if (!TryCreateSharePayload(current, out var payload, out error))
        {
            return false;
        }

        var url = GearingURL + "?" + payload;
        ImGui.SetClipboardText(url);
        Util.OpenLink(url);
        OmniNotifier.Popup(
            Info.Title,
            OmniLoc.Get("Feature.BestInSlotGear.Share.Exported"),
            NotificationType.Success);
        return true;
    }

    private bool TryImportShare(string text, out string error)
    {
        if (!TryParseSharePayload(text, out var shared, out error) ||
            !TryBuildImportedResult(shared, out var imported, out error))
        {
            return false;
        }

        CancelCalculation();
        selectedJobID = imported.JobID;
        targetItemLevel = imported.ItemLevelLimit;
        selectedDutyID = 0;
        gearStage = GearStage.Graduation;
        optimizationTarget = OptimizationTarget.MaxDamage;
        result = imported;
        loadedSavedResultID = null;
        return true;
    }

    private static bool TryCreateSharePayload(
        CalculationResult current,
        out string payload,
        out string error)
    {
        payload = string.Empty;
        if (current.JobResults.Count > 0 || !TryGetShareJob(current.JobID, out var job, out var jobIndex))
        {
            error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedJob");
            return false;
        }

        var jobLevelIndex = Array.IndexOf(ShareJobLevels, current.Level);
        if (jobLevelIndex < 0 || current.ItemLevelLimit is <= 0 or >= ShareSyncLevelRange)
        {
            error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedLevel");
            return false;
        }

        var gears = new List<ShareGear>(current.Items.Count + (current.Food is null ? 0 : 1));
        foreach (var piece in current.Items)
        {
            if (piece.ItemID is 0 or >= ShareGearIDRange)
            {
                error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedItem");
                return false;
            }

            var customStats = piece.CustomStats;
            foreach (var meld in piece.Melds)
            {
                if (meld is { } currentMeld &&
                    (currentMeld.Grade is 0 or > ShareMateriaGradeRange ||
                     Array.IndexOf(job.StatParameterIDs, currentMeld.ParameterID) < 0))
                {
                    error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedMateria");
                    return false;
                }
            }

            if (customStats is not null)
            {
                foreach (var stat in customStats)
                {
                    if (stat.Value is <= 0 or >= ShareCustomStatRange ||
                        Array.IndexOf(job.StatParameterIDs, stat.ParameterID) < 0)
                    {
                        error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedCustomStats");
                        return false;
                    }
                }
            }

            gears.Add(new(piece.ItemID, [.. piece.Melds], customStats is null ? null : [.. customStats]));
        }

        if (current.Food is { } food)
        {
            if (food.ItemID is 0 or >= ShareGearIDRange)
            {
                error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedItem");
                return false;
            }

            gears.Add(new(food.ItemID, [], null));
        }

        payload = EncodeShare(jobIndex, jobLevelIndex, current.ItemLevelLimit, job, gears);
        error = string.Empty;
        return true;
    }

    private bool TryBuildImportedResult(
        ShareGearset shared,
        out CalculationResult imported,
        out string error)
    {
        imported = CalculationResult.Failed(string.Empty);
        if (!TryGetShareJob(shared.JobID, out _, out _) ||
            !JobProfiles.TryGetValue(shared.JobID, out var job) ||
            !LevelProfiles.TryGetValue(shared.JobLevel, out var level) ||
            !LuminaGetter.TryGetRow<ClassJob>(shared.JobID, out var classJob))
        {
            error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedJob");
            return false;
        }

        if (shared.Gears.Count == 0)
        {
            error = OmniLoc.Get("Feature.BestInSlotGear.Share.MissingGear");
            return false;
        }

        var itemLevelLimit = shared.SyncLevel ?? (int)shared.Gears
            .Select(static gear => LuminaGetter.TryGetRow<Item>(gear.ItemID, out var item) && IsPotentialGear(item)
                ? item.LevelItem.RowId
                : 0)
            .Max();
        if (itemLevelLimit is < MinimumSyncItemLevel or >= ShareSyncLevelRange ||
            !LuminaGetter.TryGetRow<ItemLevel>((uint)itemLevelLimit, out var syncItemLevel))
        {
            error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedLevel");
            return false;
        }

        var pieces = new List<GearPiece>(shared.Gears.Count);
        var equipmentStats = default(StatBlock);
        FoodOption? importedFood = null;
        foreach (var sharedGear in shared.Gears)
        {
            if (!LuminaGetter.TryGetRow<Item>(sharedGear.ItemID, out var item))
            {
                error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedItem");
                return false;
            }

            if (IsPotentialFood(item))
            {
                if (importedFood is not null ||
                    sharedGear.Melds.Count > 0 ||
                    sharedGear.CustomStats is not null)
                {
                    error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedItem");
                    return false;
                }

                if (!itemDataService.TryGetDetail(item.RowId, out var detail))
                {
                    error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedItem");
                    return false;
                }

                var bonuses = item.CanBeHq && detail.Effects.HQBonuses.Count > 0
                    ? detail.Effects.HQBonuses
                    : detail.Effects.NQBonuses;
                if (bonuses.Count == 0)
                {
                    error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedItem");
                    return false;
                }

                importedFood = new(
                    item.RowId,
                    item.Name.ExtractText(),
                    item.Icon,
                    item.LevelItem.RowId,
                    item.CanBeHq,
                    bonuses);

                continue;
            }

            if (item.LevelEquip > shared.JobLevel ||
                !IsSupportedGear(item, shared.JobID) ||
                !TryGetSlot(item.EquipSlotCategory.Value, out var slot))
            {
                error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedItem");
                return false;
            }

            var itemStats = BuildItemStats(item, job, syncItemLevel);
            var materiaOptions = new MateriaGradeOption?[sharedGear.Melds.Count];
            if (item.LevelItem.RowId > syncItemLevel.RowId && sharedGear.Melds.Count > 0)
            {
                error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedMateria");
                return false;
            }

            if (item.LevelItem.RowId <= syncItemLevel.RowId)
            {
                if (!TryApplyImportedMateria(
                        item,
                        job,
                        itemStats,
                        sharedGear.Melds,
                        materiaOptions,
                        out itemStats))
                {
                    error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedMateria");
                    return false;
                }

            }

            if (sharedGear.CustomStats is not null &&
                !TryApplyImportedCustomStats(item, job, syncItemLevel, itemStats, sharedGear.CustomStats, out itemStats))
            {
                error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedCustomStats");
                return false;
            }

            pieces.Add(new(
                item.RowId,
                slot,
                item.Name.ExtractText(),
                item.Icon,
                item.LevelItem.RowId,
                item.CanBeHq,
                item.IsUnique,
                slot == GearSlot.MainHand && item.EquipSlotCategory.Value.OffHand != 0,
                BuildMateriaDisplay(materiaOptions),
                [.. sharedGear.Melds],
                sharedGear.CustomStats is null ? null : [.. sharedGear.CustomStats],
                BuildStatText(itemStats, job)));
            equipmentStats += itemStats;
        }

        if (!HasCompleteImportedGear(pieces))
        {
            error = OmniLoc.Get("Feature.BestInSlotGear.Share.MissingGear");
            return false;
        }

        SortGearPieces(pieces);
        var gearStats = BuildBaseStats(classJob, job, level, GetCurrentTribeID()) + equipmentStats;
        var stats = importedFood is null ? gearStats : ApplyFood(gearStats, importedFood, job);
        imported = new(
            null,
            shared.JobID,
            classJob.Name.ExtractText(),
            shared.JobLevel,
            itemLevelLimit,
            pieces,
            stats,
            CalculateDamage(job, level, stats),
            CalculateGcd(level, stats, job),
            importedFood is null ? null : BuildFoodResult(importedFood, gearStats, job),
            []);
        error = string.Empty;
        return true;
    }

    private bool TryApplyImportedMateria(
        Item item,
        JobProfile job,
        StatBlock baseStats,
        IReadOnlyList<MateriaSelection?> melds,
        MateriaGradeOption?[] materiaOptions,
        out StatBlock stats)
    {
        stats = baseStats;
        var slotCount = item.IsAdvancedMeldingPermitted && item.MateriaSlotCount > 0 ? 5 : item.MateriaSlotCount;
        if (melds.Count > slotCount || melds.Count > 5)
        {
            return false;
        }

        var itemRowData = ExdModule.GetItemRowById(item.RowId);
        if (itemRowData == null)
        {
            return melds.Count == 0;
        }

        for (var index = 0; index < melds.Count; index++)
        {
            if (melds[index] is not { } meld)
            {
                continue;
            }

            if (!warmedMateriaCatalog.TryGetValue(meld.ParameterID, out var catalog))
            {
                return false;
            }

            var optionIndex = catalog.FindIndex(option => option.Grade == meld.Grade);
            if (optionIndex < 0)
            {
                return false;
            }

            var option = catalog[optionIndex];
            var advancedSlotIndex = index - item.MateriaSlotCount;
            if (option.RequiredItemLevel > item.LevelItem.RowId ||
                advancedSlotIndex >= 0 &&
                (advancedSlotIndex >= 8 || (option.AdvancedSlotMask & 1 << advancedSlotIndex) == 0))
            {
                return false;
            }

            materiaOptions[index] = option;
            stats = stats.WithParameter(
                meld.ParameterID,
                Math.Min(
                    stats.GetParameter(meld.ParameterID, job) + option.Value,
                    Math.Max(
                        stats.GetParameter(meld.ParameterID, job),
                        (int)InventoryItem.GetParameterMaxValue(meld.ParameterID, itemRowData))),
                job);
        }

        return true;
    }

    private static bool TryApplyImportedCustomStats(
        Item item,
        JobProfile job,
        ItemLevel syncItemLevel,
        StatBlock baseStats,
        IReadOnlyList<CustomStatValue> customStats,
        out StatBlock stats)
    {
        stats = baseStats;
        if (customStats.Count == 0 || customStats.Select(static stat => stat.ParameterID).Distinct().Count() != customStats.Count)
        {
            return false;
        }

        if (LuminaGetter.TryGetRow<MandervilleWeaponEnhance>(item.RowId, out var enhancement))
        {
            var allowed = new Dictionary<uint, (int Big, int Small)>();
            foreach (var current in enhancement.Materias)
            {
                if (!current.Materia.IsValid)
                {
                    continue;
                }

                var materia = current.Materia.Value;
                if (current.MateriaBigValueIndex < materia.Value.Count &&
                    current.MateriaSmallValueIndex < materia.Value.Count &&
                    materia.BaseParam.RowId != 0)
                {
                    allowed[materia.BaseParam.RowId] = (
                        materia.Value[current.MateriaBigValueIndex],
                        materia.Value[current.MateriaSmallValueIndex]);
                }
            }

            var bigCount = 0;
            var smallCount = 0;
            foreach (var stat in customStats)
            {
                if (!allowed.TryGetValue(stat.ParameterID, out var values))
                {
                    return false;
                }

                if (stat.Value == values.Big)
                {
                    bigCount++;
                }
                else if (stat.Value == values.Small)
                {
                    smallCount++;
                }
                else
                {
                    return false;
                }

                stats = AddCustomStat(stats, item, job, syncItemLevel, stat.ParameterID, stat.Value);
            }

            return customStats.Count == 3 && bigCount == 2 && smallCount == 1;
        }

        if (!LuminaGetter.TryGetRow<ResistanceWeaponAdjust>(item.RowId, out var adjustment) ||
            adjustment.MaxTotalStats == 0 ||
            adjustment.MaxEachStat == 0)
        {
            return false;
        }

        var allowedParameters = new HashSet<uint>();
        foreach (var parameter in adjustment.BaseParam)
        {
            var parameterID = ResolveItemParameter(parameter.RowId, job);
            if (parameterID != 0 && default(StatBlock).WithParameter(parameterID, 1, job) != default)
            {
                allowedParameters.Add(parameterID);
            }
        }

        var total = 0;
        foreach (var stat in customStats)
        {
            if (!allowedParameters.Contains(stat.ParameterID) || stat.Value is <= 0 || stat.Value > adjustment.MaxEachStat)
            {
                return false;
            }

            total += stat.Value;
            stats = AddCustomStat(stats, item, job, syncItemLevel, stat.ParameterID, stat.Value);
        }

        return total <= adjustment.MaxTotalStats;
    }

    private static bool HasCompleteImportedGear(IReadOnlyList<GearPiece> pieces)
    {
        foreach (var slot in RequiredSlots)
        {
            if (pieces.Count(piece => piece.Slot == slot) != 1)
            {
                return false;
            }
        }

        if (pieces.Count(static piece => piece.Slot == GearSlot.MainHand) != 1 ||
            pieces.Count(static piece => piece.Slot == GearSlot.Ring) != 2)
        {
            return false;
        }

        var mainHand = pieces.First(static piece => piece.Slot == GearSlot.MainHand);
        if (pieces.Count(static piece => piece.Slot == GearSlot.OffHand) != (mainHand.OccupiesOffHand ? 0 : 1))
        {
            return false;
        }

        var rings = pieces.Where(static piece => piece.Slot == GearSlot.Ring).ToArray();
        return rings[0].ItemID != rings[1].ItemID || !rings[0].IsUnique;
    }

    private static string EncodeShare(
        int jobIndex,
        int jobLevelIndex,
        int syncLevel,
        ShareJob job,
        List<ShareGear> gears)
    {
        var gearTypeUsed = new bool[ShareGearTypeRange];
        var materiaUsed = new bool[ShareMateriaGradeRange + 1, job.StatParameterIDs.Length];
        materiaUsed[ShareMateriaGradeRange, 0] = false;
        var hasEmptyMateria = false;
        foreach (var gear in gears)
        {
            gearTypeUsed[gear.CustomStats is null ? gear.Melds.Count : 7] = true;
            foreach (var meld in gear.Melds)
            {
                if (meld is null)
                {
                    hasEmptyMateria = true;
                }
                else
                {
                    materiaUsed[meld.Value.Grade, Array.IndexOf(job.StatParameterIDs, meld.Value.ParameterID)] = true;
                }
            }
        }

        var gearTypeCodes = new int[ShareGearTypeRange];
        Array.Fill(gearTypeCodes, -1);
        var gearTypeCodeRange = 0;
        for (var index = 0; index < gearTypeUsed.Length; index++)
        {
            if (gearTypeUsed[index])
            {
                gearTypeCodes[index] = gearTypeCodeRange++;
            }
        }

        var materiaCodes = new int[ShareMateriaGradeRange + 1, job.StatParameterIDs.Length];
        for (var grade = 0; grade <= ShareMateriaGradeRange; grade++)
        {
            for (var statIndex = 0; statIndex < job.StatParameterIDs.Length; statIndex++)
            {
                materiaCodes[grade, statIndex] = -1;
            }
        }

        var materiaCodeRange = 0;
        for (var grade = ShareMateriaGradeRange; grade >= 1; grade--)
        {
            for (var statIndex = 0; statIndex < job.StatParameterIDs.Length; statIndex++)
            {
                if (materiaUsed[grade, statIndex])
                {
                    materiaCodes[grade, statIndex] = materiaCodeRange++;
                }
            }
        }

        if (hasEmptyMateria)
        {
            materiaCodeRange++;
        }

        var minimumMateriaGrade = 0;
        if (!hasEmptyMateria)
        {
            while (minimumMateriaGrade < ShareMateriaGradeRange &&
                   !Enumerable.Range(0, job.StatParameterIDs.Length).Any(index => materiaUsed[minimumMateriaGrade, index]))
            {
                minimumMateriaGrade++;
            }
        }

        var gearCodes = new List<EncodedShareGear>(gears.Count);
        foreach (var gear in gears)
        {
            var meldCodes = new int[gear.Melds.Count];
            for (var index = 0; index < gear.Melds.Count; index++)
            {
                meldCodes[index] = gear.Melds[index] is { } meld
                    ? materiaCodes[meld.Grade, Array.IndexOf(job.StatParameterIDs, meld.ParameterID)]
                    : materiaCodeRange - 1;
            }

            gearCodes.Add(new(gear.ItemID, meldCodes, gear.CustomStats));
        }

        var ringIndex = gearCodes.Count - 1;
        while (ringIndex >= 0 && gearCodes[ringIndex].MeldCodes.Length == 0)
        {
            ringIndex--;
        }

        var ringsInversed = ringIndex > 0 && gearCodes[ringIndex - 1].ItemID > gearCodes[ringIndex].ItemID;
        gearCodes.Sort(static (left, right) => left.ItemID.CompareTo(right.ItemID));
        var gearIDDeltaRange = 1;
        for (var index = 1; index < gearCodes.Count; index++)
        {
            gearIDDeltaRange = Math.Max(gearIDDeltaRange, (int)(gearCodes[index].ItemID - gearCodes[index - 1].ItemID) + 1);
        }

        var gearIDDeltaDirection = 1;
        if (gearCodes.Count > 1)
        {
            if (gearIDDeltaRange >= gearCodes[0].ItemID ||
                gearCodes[1].ItemID - gearCodes[0].ItemID < gearCodes[^1].ItemID - gearCodes[^2].ItemID)
            {
                gearIDDeltaDirection = -1;
                gearCodes.Reverse();
            }

            if ((gearCodes[^1].ItemID - gearCodes[^2].ItemID) * gearIDDeltaDirection == gearIDDeltaRange - 1)
            {
                gearIDDeltaRange++;
            }
        }

        var value = BigInteger.Zero;
        void Write(int current, int range) => value = value * range + Math.Min(current, range - 1);
        void WriteBoolean(bool current) => Write(current ? 1 : 0, 2);
        for (var index = gearCodes.Count - 1; index >= 0; index--)
        {
            var gear = gearCodes[index];
            if (index > 0)
            {
                Write(
                    (int)((gear.ItemID - gearCodes[index - 1].ItemID) * gearIDDeltaDirection) +
                    (index == gearCodes.Count - 1 ? 1 : 0),
                    gearIDDeltaRange);
            }
            else
            {
                WriteBoolean(ringsInversed);
                WriteBoolean(gearIDDeltaDirection == 1);
                Write(gearIDDeltaRange, (int)gear.ItemID);
                Write((int)gear.ItemID, ShareGearIDRange);
            }

            if (gear.CustomStats is not null)
            {
                for (var statIndex = job.StatParameterIDs.Length - 1; statIndex >= 0; statIndex--)
                {
                    Write(
                        gear.CustomStats.FirstOrDefault(stat => stat.ParameterID == job.StatParameterIDs[statIndex]).Value,
                        ShareCustomStatRange);
                }
            }
            else
            {
                for (var meldIndex = gear.MeldCodes.Length - 1; meldIndex >= 0; meldIndex--)
                {
                    Write(gear.MeldCodes[meldIndex], materiaCodeRange);
                }
            }

            Write(gearTypeCodes[gear.CustomStats is null ? gear.MeldCodes.Length : 7], gearTypeCodeRange);
        }

        for (var grade = minimumMateriaGrade; grade <= ShareMateriaGradeRange; grade++)
        {
            if (grade == 0)
            {
                continue;
            }

            for (var statIndex = job.StatParameterIDs.Length - 1; statIndex >= 0; statIndex--)
            {
                WriteBoolean(materiaUsed[grade, statIndex]);
            }
        }

        Write(minimumMateriaGrade, ShareMateriaGradeRange + 1);
        for (var index = ShareGearTypeRange - 1; index >= 0; index--)
        {
            WriteBoolean(gearTypeUsed[index]);
        }

        Write(syncLevel, ShareSyncLevelRange);
        Write(jobLevelIndex, ShareJobLevelRange);
        WriteBoolean(true);
        Write(jobIndex, ShareJobs.Length + 12);
        Write(ShareVersion, ShareVersionRange);
        return EncodeBase62(value);
    }

    private static bool TryParseSharePayload(string text, out ShareGearset gearset, out string error)
    {
        gearset = default!;
        var payload = ExtractSharePayload(text);
        if (payload.Length <= 3 || payload.Any(character => ShareCharset.IndexOf(character) < 0))
        {
            error = OmniLoc.Get("Feature.BestInSlotGear.Share.InvalidLink");
            return false;
        }

        try
        {
            var input = DecodeBase62(payload);
            int Read(int range)
            {
                if (range <= 0)
                {
                    throw new FormatException();
                }

                var result = (int)(input % range);
                input /= range;
                return result;
            }

            bool ReadBoolean() => Read(2) == 1;
            if (Read(ShareVersionRange) != ShareVersion)
            {
                error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedVersion");
                return false;
            }

            var jobIndex = Read(ShareJobs.Length + 12);
            if (jobIndex >= ShareJobs.Length)
            {
                error = OmniLoc.Get("Feature.BestInSlotGear.Share.UnsupportedJob");
                return false;
            }

            var job = ShareJobs[jobIndex];
            var synced = ReadBoolean();
            var jobLevel = ShareJobLevels[synced ? Read(ShareJobLevelRange) : ShareJobLevels.Length - 1];
            var syncLevel = synced ? Read(ShareSyncLevelRange) : 0;
            var gearTypeDecode = new List<int>();
            for (var index = 0; index < ShareGearTypeRange; index++)
            {
                if (ReadBoolean())
                {
                    gearTypeDecode.Add(index);
                }
            }

            if (gearTypeDecode.Count == 0)
            {
                throw new FormatException();
            }

            var minimumMateriaGrade = Read(ShareMateriaGradeRange + 1);
            var materiaDecode = new List<MateriaSelection?>();
            for (var grade = ShareMateriaGradeRange; grade >= minimumMateriaGrade; grade--)
            {
                if (grade == 0)
                {
                    continue;
                }

                for (var statIndex = 0; statIndex < job.StatParameterIDs.Length; statIndex++)
                {
                    if (ReadBoolean())
                    {
                        materiaDecode.Add(new(job.StatParameterIDs[statIndex], (byte)grade));
                    }
                }
            }

            if (minimumMateriaGrade == 0)
            {
                materiaDecode.Add(null);
            }

            var gears = new List<ShareGear>();
            var gearIDDeltaRange = 0;
            var gearIDDeltaDirection = 0;
            var ringsInversed = false;
            var itemID = -1;
            while (input != 0)
            {
                if (gears.Count >= 20)
                {
                    throw new FormatException();
                }

                var gearType = gearTypeDecode[Read(gearTypeDecode.Count)];
                if (gearType == 6 || gearType is < 0 or > 7)
                {
                    throw new FormatException();
                }

                var melds = new List<MateriaSelection?>();
                List<CustomStatValue>? customStats = null;
                if (gearType == 7)
                {
                    customStats = [];
                    foreach (var parameterID in job.StatParameterIDs)
                    {
                        var statValue = Read(ShareCustomStatRange);
                        if (statValue > 0)
                        {
                            customStats.Add(new(parameterID, statValue));
                        }
                    }
                }
                else
                {
                    if (gearType > 0 && materiaDecode.Count == 0)
                    {
                        throw new FormatException();
                    }

                    for (var index = 0; index < gearType; index++)
                    {
                        melds.Add(materiaDecode[Read(materiaDecode.Count)]);
                    }
                }

                if (itemID < 0)
                {
                    itemID = Read(ShareGearIDRange);
                    if (itemID <= 0)
                    {
                        throw new FormatException();
                    }

                    gearIDDeltaRange = Read(itemID);
                    if (gearIDDeltaRange <= 0)
                    {
                        throw new FormatException();
                    }

                    gearIDDeltaDirection = ReadBoolean() ? 1 : -1;
                    ringsInversed = ReadBoolean();
                }
                else
                {
                    itemID += (Read(gearIDDeltaRange) - (input == 0 ? 1 : 0)) * gearIDDeltaDirection;
                    if (itemID <= 0 || itemID >= ShareGearIDRange)
                    {
                        throw new FormatException();
                    }
                }

                gears.Add(new((uint)itemID, melds, customStats));
            }

            if (ringsInversed != (gearIDDeltaDirection == -1))
            {
                gears.Reverse();
            }

            gearset = new(job.JobID, jobLevel, syncLevel == 0 ? null : syncLevel, gears);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or
                                          IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            error = OmniLoc.Get("Feature.BestInSlotGear.Share.InvalidLink");
            return false;
        }
    }

    private static string ExtractSharePayload(string text)
    {
        var value = text.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Host, "asvel.github.io", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith("/ffxiv-gearing", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            value = uri.Query.TrimStart('?');
        }

        value = value.TrimStart('?');
        var fragmentIndex = value.IndexOf('#');
        return fragmentIndex >= 0 ? value[..fragmentIndex] : value;
    }

    private static string EncodeBase62(BigInteger value)
    {
        var result = string.Empty;
        do
        {
            result = ShareCharset[(int)(value % 62)] + result;
            value /= 62;
        }
        while (value > 0);

        return result;
    }

    private static BigInteger DecodeBase62(string value)
    {
        var result = BigInteger.Zero;
        foreach (var character in value)
        {
            result = result * 62 + ShareCharset.IndexOf(character);
        }

        return result;
    }

    private static bool TryGetShareJob(uint jobID, out ShareJob job, out int index)
    {
        for (index = 0; index < ShareJobs.Length; index++)
        {
            if (ShareJobs[index].JobID == jobID)
            {
                job = ShareJobs[index];
                return true;
            }
        }

        job = default;
        return false;
    }

    private readonly record struct ShareJob(uint JobID, uint[] StatParameterIDs);
    private sealed record ShareGear(uint ItemID, List<MateriaSelection?> Melds, List<CustomStatValue>? CustomStats);
    private sealed record ShareGearset(uint JobID, byte JobLevel, int? SyncLevel, List<ShareGear> Gears);
    private sealed record EncodedShareGear(uint ItemID, int[] MeldCodes, List<CustomStatValue>? CustomStats);
}
