using System.Collections.Frozen;
using System.Globalization;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Game;
using OmniToolbox.UI;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;
using IBattleChara = OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds.IBattleChara;
using IGameObject = OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds.IGameObject;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class MitigationSnapshotBuilder
{
    private static readonly TimeSpan SacredSoilDuration = TimeSpan.FromSeconds(15);
    private static readonly FrozenDictionary<uint, MitigationDefinition> MitigationByStatusID =
        new MitigationDefinition[]
        {
            new(1191, 20, 20, 20), new(1856, 15, 15, 15), new(1174, 10, 10, 10),
            new(74, 40, 40, 40), new(1176, 15, 15, 15), new(1175, 10, 10, 10),
            new(82, 100, 100, 100), new(2674, 15, 15, 15), new(2675, 15, 15, 15),
            new(77, 20, 20, 10), new(735, 10, 10, 10), new(1857, 10, 10, 10),
            new(1858, 10, 10, 10), new(89, 40, 40, 40), new(2678, 10, 10, 10),
            new(2679, 10, 10, 10), new(746, 10, 20, 0), new(747, 40, 40, 10),
            new(1894, 5, 10, 0), new(2682, 10, 10, 10), new(1840, 15, 15, 15),
            new(1832, 15, 10, 10), new(1834, 30, 30, 30), new(1839, 5, 10, 0),
            new(1836, 100, 100, 100), new(2683, 15, 15, 15), new(2684, 15, 15, 15),
            new(1219, 10, 10, 10), new(1873, 10, 10, 10), new(2708, 15, 15, 15),
            new(297, 0, 0, 180), new(1918, 0, 0, 180), new(1917, 0, 0, 250),
            new(299, 10, 10, 500), new(317, 0, 5, 0), new(1875, 0, 5, 0),
            new(2711, 10, 10, 0), new(849, 10, 10, 10), new(2717, 10, 10, 10),
            new(2618, 10, 10, 10), new(2619, 10, 10, 10), new(3003, 10, 10, 10),
            new(1232, 10, 10, 10), new(1179, 20, 20, 20), new(1934, 15, 15, 15),
            new(1951, 15, 15, 15), new(1826, 15, 15, 15), new(2707, 0, 10, 0),
            new(1193, 10, 10, 10), new(1195, 10, 5, 0), new(1203, 5, 10, 0),
            new(860, 10, 10, 10), new(9, 10, 10, 10), new(1715, 10, 10, 10),
            new(2115, 0, 10, 0), new(2500, 20, 20, 20), new(1722, 90, 90, 90),
            new(2496, 20, 20, 20), new(2119, 5, 5, 5), new(1719, 40, 40, 40),
            new(194, 20, 20, 20), new(195, 40, 40, 40), new(196, 80, 80, 80),
            new(863, 80, 80, 80), new(864, 80, 80, 80), new(1931, 80, 80, 80),
            new(3829, 40, 40, 40), new(3832, 40, 40, 40), new(3835, 40, 40, 40),
            new(3838, 40, 40, 40), new(3890, 10, 10, 10), new(3896, 10, 10, 10)
        }.ToFrozenDictionary(static definition => definition.StatusID);

    private static readonly FrozenSet<uint> SourceDebuffStatusIDs =
        new uint[] { 1193, 1195, 1203, 860, 9, 1715, 2115 }.ToFrozenSet();

    private static readonly FrozenSet<uint> AlwaysShowStatusIDs = new uint[]
    {
        3830, 1175, 82, 1362, 77, 1858, 87, 1457, 409, 2680, 1178, 810, 811,
        3255, 1898, 1836, 1218, 2710, 1889, 3892, 3903, 1921, 2607, 2608, 2609,
        3365, 2612, 2613, 2697, 488, 168, 2702, 2596, 2597, 2120, 2500, 1722,
        2496, 2119, 1719, 2114, 3686, 3687
    }.ToFrozenSet();

    private readonly Dictionary<uint, ActionDisplayInfo> actionCache = [];
    private readonly Dictionary<uint, StatusDisplayInfo> statusCache = [];
    private readonly HashSet<uint> activeSacredSoilTargets = [];
    private readonly Dictionary<uint, DateTime> sacredSoilUntilUTC = [];
    private readonly List<uint> expiredSacredSoilTargets = new(8);
    private readonly List<CachedStatus> visibleEnemyDebuffs = new(32);
    private readonly List<ActiveMitigation> mitigationBuilder = new(16);
    private readonly HashSet<ulong> seenStatusKeys = [];

    public void RefreshRuntime(IReadOnlyList<IBattleChara> battleCharas, DateTime now)
    {
        visibleEnemyDebuffs.Clear();
        foreach (var enemy in battleCharas)
        {
            if (enemy.ObjectKind != ObjectKind.BattleNpc)
            {
                continue;
            }

            foreach (var status in enemy.ToBCStruct()->StatusManager.Status)
            {
                if (status.StatusId != 0 && SourceDebuffStatusIDs.Contains(status.StatusId))
                {
                    visibleEnemyDebuffs.Add(new(
                        status.StatusId,
                        status.SourceObject.ObjectId,
                        status.Param,
                        status.RemainingTime));
                }
            }
        }

        expiredSacredSoilTargets.Clear();
        foreach (var entry in sacredSoilUntilUTC)
        {
            if (entry.Value <= now && !activeSacredSoilTargets.Contains(entry.Key))
            {
                expiredSacredSoilTargets.Add(entry.Key);
            }
        }

        foreach (var entityId in expiredSacredSoilTargets)
        {
            sacredSoilUntilUTC.Remove(entityId);
        }
    }

    public void RefreshTarget(IBattleChara? target, IGameObject targetObject, DateTime now)
    {
        if (target == null)
        {
            return;
        }

        var found = false;
        foreach (var status in target.ToBCStruct()->StatusManager.Status)
        {
            if (status.StatusId == SACRED_SOIL_STATUS_ID)
            {
                found = true;
                break;
            }
        }

        if (found)
        {
            StartSacredSoil(targetObject.EntityID, now);
        }
        else
        {
            activeSacredSoilTargets.Remove(targetObject.EntityID);
        }
    }

    public void StartSacredSoil(uint targetEntityID, DateTime now)
    {
        if (targetEntityID != 0 && activeSacredSoilTargets.Add(targetEntityID))
        {
            sacredSoilUntilUTC[targetEntityID] = now + SacredSoilDuration;
        }
    }

    public ActionDisplayInfo GetActionInfo(uint actionID)
    {
        if (actionCache.TryGetValue(actionID, out var cached))
        {
            return cached;
        }

        var info = LuminaGetter.TryGetRow<LuminaAction>(actionID, out var row)
            ? new ActionDisplayInfo(row.Name.ToString())
            : new ActionDisplayInfo(actionID.ToString(CultureInfo.InvariantCulture));
        actionCache[actionID] = info;
        return info;
    }

    public DamageActionDisplay ResolveDamageAction(
        uint actionID,
        ActionDisplayInfo action,
        IBattleChara? target,
        IGameObject source)
    {
        if (actionID != 0 && !string.IsNullOrWhiteSpace(action.Name) && action.Name != "0")
        {
            return new(action.Name, DamageSourceKind.Skill);
        }

        return TryGetDamageOverTimeStatus(target, source, out var dotStatus)
            ? new(dotStatus.Name, DamageSourceKind.Dot)
            : new(OmniLoc.Get("Feature.MitigationMonitor.Action.AutoAttack"), DamageSourceKind.AutoAttack);
    }

    public ActiveMitigation[] Build(
        IBattleChara? target,
        IBattleChara source,
        IGameObject targetObject,
        DamageKind damageKind,
        DateTime now)
    {
        mitigationBuilder.Clear();
        seenStatusKeys.Clear();
        if (target != null)
        {
            CollectStatuses(target, targetObject, damageKind, MitigationStatusSourceKind.Target, now);
        }

        CollectStatuses(source, targetObject, damageKind, MitigationStatusSourceKind.CurrentSource, now);
        foreach (var status in visibleEnemyDebuffs)
        {
            CollectStatus(
                status.StatusID,
                status.SourceID,
                status.Param,
                status.RemainingTime,
                targetObject,
                damageKind,
                MitigationStatusSourceKind.VisibleEnemyFallback,
                now);
        }

        mitigationBuilder.Sort(static (left, right) => left.Category.CompareTo(right.Category));
        return mitigationBuilder.Count == 0 ? [] : mitigationBuilder.ToArray();
    }

    public float CalculateMitigationPercent(ReadOnlySpan<ActiveMitigation> statuses)
    {
        var factor = 1f;
        foreach (var status in statuses)
        {
            if (status.AffectsPercent)
            {
                factor *= 1f - Math.Clamp(status.Value, 0, 100) / 100f;
            }
        }

        return (1f - factor) * 100f;
    }

    public void ClearTargetWindows()
    {
        activeSacredSoilTargets.Clear();
        sacredSoilUntilUTC.Clear();
    }

    public void ClearRuntime()
    {
        visibleEnemyDebuffs.Clear();
        expiredSacredSoilTargets.Clear();
        mitigationBuilder.Clear();
        seenStatusKeys.Clear();
        ClearTargetWindows();
    }

    private void CollectStatuses(
        IBattleChara source,
        IGameObject target,
        DamageKind damageKind,
        MitigationStatusSourceKind sourceKind,
        DateTime now)
    {
        foreach (var status in source.ToBCStruct()->StatusManager.Status)
        {
            if (status.StatusId != 0)
            {
                CollectStatus(
                    status.StatusId,
                    status.SourceObject.ObjectId,
                    status.Param,
                    status.RemainingTime,
                    target,
                    damageKind,
                    sourceKind,
                    now);
            }
        }
    }

    private void CollectStatus(
        uint statusID,
        uint sourceID,
        ushort param,
        float remainingTime,
        IGameObject target,
        DamageKind damageKind,
        MitigationStatusSourceKind sourceKind,
        DateTime now)
    {
        var sourceStatus = sourceKind is MitigationStatusSourceKind.CurrentSource or MitigationStatusSourceKind.VisibleEnemyFallback;
        if (sourceStatus && !SourceDebuffStatusIDs.Contains(statusID) ||
            !seenStatusKeys.Add(((ulong)statusID << 32) | sourceID))
        {
            return;
        }

        var display = GetStatusInfo(statusID);
        var stackCount = Math.Max(1, (int)param);
        var iconID = stackCount is > 1 and <= 16 ? display.IconID + (uint)(stackCount - 1) : display.IconID;
        var displayRemainingTime = MathF.Max(0f, remainingTime);
        if (statusID == SACRED_SOIL_STATUS_ID && sacredSoilUntilUTC.TryGetValue(target.EntityID, out var seenUntil))
        {
            displayRemainingTime = MathF.Max(displayRemainingTime, (float)Math.Max(0d, (seenUntil - now).TotalSeconds));
        }

        if (AlwaysShowStatusIDs.Contains(statusID))
        {
            mitigationBuilder.Add(new(
                statusID, display.Name, iconID, displayRemainingTime, 0, stackCount,
                MitigationStatusCategory.Shield, true, false));
            return;
        }

        if (!MitigationByStatusID.TryGetValue(statusID, out var definition))
        {
            if (sourceKind == MitigationStatusSourceKind.Target && TargetEffectStatusIDs.Contains(statusID))
            {
                mitigationBuilder.Add(new(
                    statusID, display.Name, iconID, displayRemainingTime, 0, stackCount,
                    MitigationStatusCategory.Vulnerability, true, false));
            }

            return;
        }

        var percentValue = ResolveStatusPercentValue(statusID, sourceID, definition, target, damageKind);
        var category = definition.HasPercentMitigation
            ? MitigationStatusCategory.Mitigation
            : MitigationStatusCategory.Shield;
        mitigationBuilder.Add(new(
            statusID,
            display.Name,
            iconID,
            displayRemainingTime,
            percentValue > 0 ? percentValue : definition.DisplayValueFor(damageKind),
            stackCount,
            category,
            category == MitigationStatusCategory.Shield || percentValue > 0,
            percentValue > 0));
    }

    private static int ResolveStatusPercentValue(
        uint statusID,
        uint sourceID,
        MitigationDefinition definition,
        IGameObject target,
        DamageKind damageKind)
    {
        if (statusID == 2675)
        {
            return sourceID == target.EntityID || sourceID == (uint)target.GameObjectID ? 15 : 10;
        }

        if (statusID == 1174)
        {
            return InterventionSourceHasBonus(sourceID) ? 20 : 10;
        }

        return definition.PercentValueFor(damageKind);
    }

    private static bool InterventionSourceHasBonus(uint sourceID)
    {
        if (FindObject(sourceID) is not IBattleChara source)
        {
            return false;
        }

        foreach (var status in source.ToBCStruct()->StatusManager.Status)
        {
            if (status.StatusId is 1191 or 3829)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetDamageOverTimeStatus(IBattleChara? target, IGameObject source, out StatusDisplayInfo display)
    {
        display = default;
        if (target == null)
        {
            return false;
        }

        foreach (var status in target.ToBCStruct()->StatusManager.Status)
        {
            if (status.StatusId == 0 ||
                status.SourceObject.ObjectId != source.EntityID &&
                status.SourceObject.ObjectId != (uint)source.GameObjectID)
            {
                continue;
            }

            if (DamageOverTimeStatusIDs.Contains(status.StatusId))
            {
                display = GetStatusInfo(status.StatusId);
                return true;
            }
        }

        return false;
    }

    private StatusDisplayInfo GetStatusInfo(uint statusID)
    {
        if (statusCache.TryGetValue(statusID, out var cached))
        {
            return cached;
        }

        var info = LuminaGetter.TryGetRow<LuminaStatus>(statusID, out var row)
            ? new StatusDisplayInfo(row.Name.ToString(), row.Icon)
            : new StatusDisplayInfo(statusID.ToString(CultureInfo.InvariantCulture), 0);
        statusCache[statusID] = info;
        return info;
    }

    private static IGameObject? FindObject(uint id)
    {
        var objectTable = DService.Instance().ObjectTable;
        return CombatCharacterSnapshot.Find(id) ??
               objectTable.SearchByID(id) ??
               objectTable.SearchByEntityID(id);
    }

    #region 预置数据

    private static readonly FrozenSet<uint> DamageOverTimeStatusIDs = new uint[]
    {
        18, 19, 124, 129, 143, 144, 161, 162, 163, 164, 189, 235,
        250, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 275,
        283, 284, 285, 286, 287, 288, 289, 314, 320, 339, 343, 352,
        375, 376, 377, 379, 407, 453, 484, 485, 487, 503, 515, 530,
        531, 532, 533, 534, 535, 559, 560, 569, 580, 581, 605, 619,
        624, 642, 643, 645, 659, 666, 677, 686, 798, 801, 809, 940,
        979, 1001, 1011, 1046, 1074, 1117, 1150, 1200, 1201, 1210, 1214, 1254,
        1309, 1312, 1313, 1314, 1319, 1321, 1322, 1324, 1326, 1327, 1328, 1377,
        1386, 1458, 1466, 1474, 1498, 1507, 1508, 1519, 1527, 1577, 1578, 1640,
        1714, 1723, 1736, 1758, 1787, 1788, 1813, 1899, 1940, 2029, 2073, 2075,
        2079, 2080, 2081, 2082, 2083, 2084, 2085, 2086, 2087, 2088, 2089, 2103,
        2104, 2156, 2192, 2194, 2199, 2200, 2285, 2322, 2327, 2389, 2399, 2401,
        2432, 2499, 2636, 2646, 2726, 2735, 2774, 2880, 2896, 2900, 2901, 2906,
        2913, 2914, 2915, 2916, 2917, 2918, 2919, 2920, 2921, 2922, 2935, 2942,
        2943, 2944, 2945, 2946, 2947, 2948, 2949, 2950, 2951, 2962, 2963, 2967,
        2968, 2993, 3000, 3008, 3059, 3060, 3061, 3062, 3063, 3064, 3065, 3066,
        3067, 3068, 3069, 3070, 3071, 3072, 3073, 3074, 3075, 3076, 3077, 3078,
        3081, 3082, 3118, 3133, 3261, 3266, 3267, 3284, 3287, 3288, 3294, 3312,
        3359, 3390, 3399, 3407, 3462, 3479, 3480, 3481, 3519, 3521, 3537, 3547,
        3548, 3559, 3587, 3636, 3643, 3644, 3692, 3705, 3706, 3712, 3761, 3767,
        3777, 3778, 3779, 3795, 3797, 3798, 3826, 3871, 3872, 3915, 3917, 3918,
        3919, 3923, 3924, 3925, 3926, 3932, 3933, 3966, 3981, 3982, 4068, 4122,
        4137, 4138, 4149, 4150, 4181, 4184, 4269, 4379, 4380, 4449, 4466, 4467,
        4499, 4500, 4503, 4504, 4511, 4512, 4559, 4563, 4566, 4574, 4585, 4622,
        4652, 4757, 4829, 4831, 4850, 4880, 4882, 4883, 4884, 4961, 5140, 5141,
        5183, 5341, 5384, 5387, 5425, 5442, 5469, 5495, 5496, 5497, 5498, 5499,
        5500, 5501, 5553, 5554
    }.ToFrozenSet();

    private static readonly FrozenSet<uint> TargetEffectStatusIDs = new uint[]
    {
        21, 22, 29, 30, 37, 38, 55, 56, 59, 60, 63, 64,
        126, 200, 202, 331, 350, 406, 431, 444, 456, 483, 493, 494,
        520, 521, 522, 523, 524, 525, 563, 572, 573, 576, 578, 582,
        583, 586, 588, 600, 601, 621, 638, 640, 657, 658, 670, 685,
        691, 695, 714, 720, 721, 722, 804, 806, 812, 819, 820, 821,
        893, 894, 898, 899, 912, 929, 931, 934, 965, 1025, 1026, 1052,
        1053, 1054, 1089, 1100, 1137, 1138, 1157, 1208, 1226, 1255, 1260, 1272,
        1383, 1402, 1412, 1435, 1517, 1545, 1597, 1693, 1694, 1702, 1759, 1761,
        1763, 1776, 1782, 1789, 1845, 1973, 2090, 2091, 2094, 2095, 2096, 2097,
        2098, 2120, 2121, 2122, 2123, 2144, 2145, 2198, 2213, 2248, 2278, 2347,
        2371, 2465, 2506, 2508, 2521, 2801, 2902, 2903, 2912, 2937, 2940, 2941,
        2971, 2998, 3130, 3131, 3132, 3135, 3136, 3323, 3360, 3361, 3366, 3372,
        3401, 3414, 3415, 3491, 3516, 3536, 3557, 3619, 3638, 3639, 3757, 3935,
        3998, 4162, 4164, 4375, 4383, 4386, 4436, 4455, 4456, 4540, 4710, 4955,
        4957, 4958, 4959, 4960, 4961, 4963, 4964, 4997, 4998, 4999, 5000, 5001,
        5002, 5021, 5022, 5023, 5024, 5025, 5026, 5045, 5046, 5180, 5181, 5198,
        5199, 5318, 5336, 5337, 5338, 5392, 5393, 5431, 5432, 5467, 5551, 5552
    }.ToFrozenSet();

    #endregion

    #region 常量

    private const uint SACRED_SOIL_STATUS_ID = 299;

    #endregion
}
