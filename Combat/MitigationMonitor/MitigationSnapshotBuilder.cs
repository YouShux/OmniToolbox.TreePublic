using System.Collections.Frozen;
using System.Globalization;
using OmniToolbox.Game;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using IBattleChara = OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds.IBattleChara;
using IGameObject = OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds.IGameObject;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class MitigationSnapshotBuilder
{
    private const uint SacredSoilStatusID = 299;

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

    private static readonly FrozenSet<uint> SourceDebuffStatusIds =
        new uint[] { 1193, 1195, 1203, 860, 9, 1715, 2115 }.ToFrozenSet();

    private static readonly FrozenSet<uint> AlwaysShowStatusIds = new uint[]
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
                if (status.StatusId != 0 && SourceDebuffStatusIds.Contains(status.StatusId))
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
            if (status.StatusId == SacredSoilStatusID)
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
        if (sourceStatus && !SourceDebuffStatusIds.Contains(statusID) ||
            !seenStatusKeys.Add(((ulong)statusID << 32) | sourceID))
        {
            return;
        }

        var display = GetStatusInfo(statusID);
        var stackCount = Math.Max(1, (int)param);
        var iconID = stackCount is > 1 and <= 16 ? display.IconID + (uint)(stackCount - 1) : display.IconID;
        var displayRemainingTime = MathF.Max(0f, remainingTime);
        if (statusID == SacredSoilStatusID && sacredSoilUntilUTC.TryGetValue(target.EntityID, out var seenUntil))
        {
            displayRemainingTime = MathF.Max(displayRemainingTime, (float)Math.Max(0d, (seenUntil - now).TotalSeconds));
        }

        if (AlwaysShowStatusIds.Contains(statusID))
        {
            mitigationBuilder.Add(new(
                statusID, display.Name, iconID, displayRemainingTime, 0, stackCount,
                MitigationStatusCategory.Shield, true, false));
            return;
        }

        if (!MitigationByStatusID.TryGetValue(statusID, out var definition))
        {
            if (sourceKind == MitigationStatusSourceKind.Target && IsActTargetStatusEffect(display.Name))
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

            var candidate = GetStatusInfo(status.StatusId);
            if (IsDamageOverTimeText(candidate.Name) || IsDamageOverTimeText(candidate.Description))
            {
                display = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsDamageOverTimeText(string text) =>
        text.Contains("\u6301\u7eed\u4f24\u5bb3", StringComparison.Ordinal) ||
        text.Contains("\u6301\u7e8c\u50b7\u5bb3", StringComparison.Ordinal) ||
        text.Contains("\u9010\u6e10\u6d41\u5931", StringComparison.Ordinal) ||
        text.Contains("\u9010\u6f38\u6d41\u5931", StringComparison.Ordinal) ||
        text.Contains("\u6d41\u8840", StringComparison.Ordinal) ||
        text.Contains("\u51bb\u4f24", StringComparison.Ordinal) ||
        text.Contains("\u51cd\u50b7", StringComparison.Ordinal) ||
        text.Contains("\u4e2d\u6bd2", StringComparison.Ordinal) ||
        text.Contains("\u731b\u6bd2", StringComparison.Ordinal) ||
        text.Contains("\u5267\u6bd2", StringComparison.Ordinal) ||
        text.Contains("\u5287\u6bd2", StringComparison.Ordinal) ||
        text.Contains("\u70c8\u6bd2", StringComparison.Ordinal) ||
        text.Contains("\u707c\u4f24", StringComparison.Ordinal) ||
        text.Contains("\u707c\u50b7", StringComparison.Ordinal) ||
        text.Contains("\u88c2\u4f24", StringComparison.Ordinal) ||
        text.Contains("\u88c2\u50b7", StringComparison.Ordinal);

    private static bool IsActTargetStatusEffect(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var hasSubject = name.Contains("\u53d7\u4f24", StringComparison.Ordinal) ||
                         name.Contains("\u8010\u6027", StringComparison.Ordinal) ||
                         name.Contains("\u9632\u5fa1\u529b", StringComparison.Ordinal);
        var hasChange = name.Contains("\u63d0\u5347", StringComparison.Ordinal) ||
                        name.Contains("\u964d\u4f4e", StringComparison.Ordinal) ||
                        name.Contains("\u4f4e\u4e0b", StringComparison.Ordinal) ||
                        name.Contains("\u52a0\u91cd", StringComparison.Ordinal) ||
                        name.Contains("\u51cf\u8f7b", StringComparison.Ordinal);
        return hasSubject && hasChange ||
               name.Contains("\u4f53\u529b\u589e\u52a0", StringComparison.Ordinal) ||
               name.Contains("\u4f53\u529b\u51cf\u5c11", StringComparison.Ordinal) ||
               name.Contains("\u4f53\u529b\u8870\u51cf", StringComparison.Ordinal);
    }

    private StatusDisplayInfo GetStatusInfo(uint statusID)
    {
        if (statusCache.TryGetValue(statusID, out var cached))
        {
            return cached;
        }

        var info = LuminaGetter.TryGetRow<LuminaStatus>(statusID, out var row)
            ? new StatusDisplayInfo(row.Name.ToString(), row.Description.ToString(), row.Icon)
            : new StatusDisplayInfo(statusID.ToString(CultureInfo.InvariantCulture), string.Empty, 0);
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
}
