using System.Globalization;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.DutyState;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Game;
using OmenTools;
using OmenTools.OmenService;
using GameCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using IBattleChara = OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds.IBattleChara;
using ICharacter = OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds.ICharacter;
using IGameObject = OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds.IGameObject;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class MitigationRecorder
{
    private readonly MitigationCombatLog combatLog;
    private readonly MitigationSnapshotBuilder snapshotBuilder = new();
    private readonly object cacheSyncRoot = new();
    private readonly HashSet<uint> friendlyEntityIds = [];
    private Hook<ActionEffectHandler.Delegates.Receive>? receiveHook;
    private bool pvpStateCleared;

    public MitigationRecorder(MitigationCombatLog combatLog) => this.combatLog = combatLog;

    public void Register(FeatureLifetime lifetime)
    {
        receiveHook = DService.Instance().Hook.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
            ActionEffectHandler.MemberFunctionPointers.Receive,
            OnActionEffect);
        lifetime.Add(receiveHook.Dispose);
        receiveHook.Enable();

        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 100))
        {
            throw new InvalidOperationException("Mitigation monitor update registration failed.");
        }

        lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
        var services = DService.Instance();
        services.DutyState.DutyWiped += OnDutyWiped;
        lifetime.Add(() => services.DutyState.DutyWiped -= OnDutyWiped);
        services.ClientState.Logout += OnLogout;
        lifetime.Add(() => services.ClientState.Logout -= OnLogout);
        services.ClientState.TerritoryChanged += OnTerritoryChanged;
        lifetime.Add(() => services.ClientState.TerritoryChanged -= OnTerritoryChanged);

        if (services.ClientState.IsLoggedIn && !GameState.IsInPVPArea)
        {
            lock (cacheSyncRoot)
            {
                RefreshRuntimeCacheNoLock(DateTime.UtcNow, true);
            }
        }
    }

    public void Reset()
    {
        receiveHook = null;
        pvpStateCleared = false;
        lock (cacheSyncRoot)
        {
            friendlyEntityIds.Clear();
            snapshotBuilder.ClearRuntime();
            CombatCharacterSnapshot.Clear();
        }
    }

    private void OnActionEffect(
        uint casterEntityID,
        GameCharacter* caster,
        Vector3* targetPosition,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        try
        {
            ProcessActionEffect(casterEntityID, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Mitigation monitor ActionEffect processing failed.");
        }
        finally
        {
            receiveHook!.Original(casterEntityID, caster, targetPosition, header, effects, targetEntityIds);
        }
    }

    private void ProcessActionEffect(
        uint casterEntityID,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn ||
            header == null ||
            effects == null ||
            targetEntityIds == null ||
            header->NumTargets == 0 ||
            ClearForPvp())
        {
            return;
        }

        CombatCharacterSnapshot.Refresh();
        if (FindObject(casterEntityID) is not IBattleChara source ||
            source.ObjectKind != ObjectKind.BattleNpc ||
            combatLog.ShouldSuppressWrites() ||
            !services.Condition[ConditionFlag.InCombat])
        {
            return;
        }

        lock (cacheSyncRoot)
        {
            var now = DateTime.UtcNow;
            if (friendlyEntityIds.Count == 0)
            {
                RefreshRuntimeCacheNoLock(now, false);
            }

            var action = snapshotBuilder.GetActionInfo(header->ActionId);
            for (var index = 0; index < Math.Min((int)header->NumTargets, 32); index++)
            {
                var targetID = targetEntityIds[index];
                if (!friendlyEntityIds.Contains(targetID.ObjectId) ||
                    FindObject(targetID) is not { } target ||
                    !TryDecodeTargetDamage(&effects[index], out var damage))
                {
                    continue;
                }

                var targetBattleChara = target as IBattleChara;
                var targetCharacter = target as ICharacter;
                snapshotBuilder.RefreshTarget(targetBattleChara, target, now);
                if (header->ActionId is 188 or 23578)
                {
                    snapshotBuilder.StartSacredSoil(target.EntityID, now);
                }

                var actionDisplay = snapshotBuilder.ResolveDamageAction(
                    header->ActionId,
                    action,
                    targetBattleChara,
                    source);
                var statuses = snapshotBuilder.Build(targetBattleChara, source, target, damage.Kind, now);
                combatLog.AddDamage(new(
                    MitigationRecordKind.Damage,
                    now,
                    default,
                    actionDisplay.Name,
                    source.Name,
                    target.EntityID,
                    target.Name,
                    CreateShortName(target.Name),
                    GetJobName(targetCharacter),
        targetCharacter?.ClassJob.RowId ?? 0,
                    actionDisplay.SourceKind,
                    damage.Damage,
                    damage.Kind,
                    damage.Blocked,
                    damage.Parried,
                    damage.Missed,
                    damage.Invulnerable,
                    snapshotBuilder.CalculateMitigationPercent(statuses),
                    statuses,
                    GetShieldValue(targetCharacter),
                    targetCharacter?.CurrentHp ?? 0));
            }
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!DService.Instance().ClientState.IsLoggedIn || ClearForPvp() || combatLog.ShouldSuppressWrites())
        {
            return;
        }

        lock (cacheSyncRoot)
        {
            RefreshRuntimeCacheNoLock(DateTime.UtcNow, true);
        }
    }

    private bool ClearForPvp()
    {
        if (!GameState.IsInPVPArea)
        {
            pvpStateCleared = false;
            return false;
        }

        if (!pvpStateCleared)
        {
            combatLog.ClearRealtime();
            pvpStateCleared = true;
            lock (cacheSyncRoot)
            {
                friendlyEntityIds.Clear();
                snapshotBuilder.ClearRuntime();
            }
        }

        return true;
    }

    private void RefreshRuntimeCacheNoLock(DateTime now, bool detectDeaths)
    {
        var services = DService.Instance();
        CombatCharacterSnapshot.Refresh();
        friendlyEntityIds.Clear();
        TrackFriendlyNoLock(services.ObjectTable.LocalPlayer, now, detectDeaths);
        foreach (var partyMember in services.PartyList)
        {
            if (partyMember.EntityId == 0)
            {
                continue;
            }

            friendlyEntityIds.Add(partyMember.EntityId);
            TrackFriendlyNoLock(
                FindObject(partyMember.EntityId),
                now,
                detectDeaths);
        }

        snapshotBuilder.RefreshRuntime(CombatCharacterSnapshot.BattleCharas, now);
    }

    private void TrackFriendlyNoLock(IGameObject? target, DateTime now, bool detectDeaths)
    {
        if (target == null || target.EntityID == 0)
        {
            return;
        }

        friendlyEntityIds.Add(target.EntityID);
        snapshotBuilder.RefreshTarget(target as IBattleChara, target, now);
        if (!detectDeaths)
        {
            return;
        }

        var character = target as ICharacter;
        combatLog.UpdateFriendly(new(
            target.EntityID,
            !target.IsDead && (character == null || character.CurrentHp > 0),
            target.Name,
            CreateShortName(target.Name),
            GetJobName(character),
        character?.ClassJob.RowId ?? 0,
            GetShieldValue(character),
            character?.CurrentHp ?? 0), now);
    }

    private static bool TryDecodeTargetDamage(ActionEffectHandler.TargetEffects* targetEffects, out TargetDamageResult result)
    {
        result = new() { Kind = DamageKind.Special };
        foreach (var effect in targetEffects->Effects)
        {
            switch ((ActionEffectType)effect.Type)
            {
                case ActionEffectType.Miss:
                    result.Found = true;
                    result.Missed = true;
                    break;
                case ActionEffectType.Invulnerable:
                    result.Found = true;
                    result.Invulnerable = true;
                    break;
                case ActionEffectType.Damage:
                case ActionEffectType.BlockedDamage:
                case ActionEffectType.ParriedDamage:
                    result.Found = true;
                    result.Blocked |= (ActionEffectType)effect.Type == ActionEffectType.BlockedDamage;
                    result.Parried |= (ActionEffectType)effect.Type == ActionEffectType.ParriedDamage;
                    result.Kind = DecodeDamageKind(effect.Param1);
                    result.Damage += (effect.Param4 & 0x40) == 0
                        ? (uint)effect.Value
                        : (uint)effect.Value + ((uint)effect.Param3 << 16);
                    break;
            }
        }

        return result.Found;
    }

    private static DamageKind DecodeDamageKind(byte param1) => (param1 & 0x0F) switch
    {
        1 or 2 or 3 or 4 or 7 => DamageKind.Physical,
        5 => DamageKind.Magical,
        _ => DamageKind.Special
    };

    private static uint GetShieldValue(ICharacter? target) =>
        target == null || target.CurrentHp == 0
            ? 0
            : (uint)Math.Max(0d, Math.Round(
                target.CurrentHp * target.ShieldPercentage / 100d,
                MidpointRounding.AwayFromZero));

    private static string GetJobName(ICharacter? character) =>
        character?.ClassJob.ValueNullable?.Name.ToString() ?? string.Empty;

    private static IGameObject? FindObject(GameObjectId id)
    {
        var objectTable = DService.Instance().ObjectTable;
        return CombatCharacterSnapshot.Find(id.Id, id.ObjectId) ??
               objectTable.SearchByID(id.Id) ??
               objectTable.SearchByEntityID(id.ObjectId);
    }

    private static IGameObject? FindObject(uint entityID) =>
        CombatCharacterSnapshot.Find(entityID) ?? DService.Instance().ObjectTable.SearchByEntityID(entityID);

    private static string CreateShortName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0][0]}{parts[^1][0]}";
        }

        var text = new StringInfo(name.Trim());
        var length = Math.Min(2, text.LengthInTextElements);
        return length == 0 ? "?" : text.SubstringByTextElements(0, length);
    }

    private void OnDutyWiped(IDutyStateEventArgs _)
    {
        if (!DService.Instance().ClientState.IsLoggedIn || GameState.IsInPVPArea)
        {
            return;
        }

        combatLog.RecordWipe(DateTime.UtcNow);
        lock (cacheSyncRoot)
        {
            snapshotBuilder.ClearTargetWindows();
        }
    }

    private void OnLogout(int _, int unusedReason) => ClearRuntimeState();

    private void OnTerritoryChanged(uint _) => ClearRuntimeState();

    private void ClearRuntimeState()
    {
        combatLog.ClearRealtime();
        pvpStateCleared = false;
        lock (cacheSyncRoot)
        {
            friendlyEntityIds.Clear();
            snapshotBuilder.ClearRuntime();
        }
    }
}
