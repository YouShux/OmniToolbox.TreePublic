using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.OmenService;
using BattleNpcSubKind = Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind;
using ClientObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed unsafe class AutoHideModelFeature(AutoHideModelConfig config) : TreeHouseFeatureBase(new(
    "auto-hide-model",
    "Feature.AutoHideModel.Title",
    "Feature.AutoHideModel.Description",
    TreeHouseCategory.InterfaceOptimization,
    true))
{
    private const int ObjectScanStart = 1;
    private const int ObjectScanEnd = 200;
    private const int UnimportantNpcScanStart = 489;
    private const int UnimportantNpcScanEnd = 630;
    private const uint InvalidEntityId = 0xE0000000;
    private const uint EarthlyStarNameId = 6565;
    private const uint AsylumActionId = 3569;
    private const uint SacredSoilActionId = 188;
    private const VisibilityFlags InvisibleFlags = (VisibilityFlags)256;
    private static readonly string[] AsylumVfxPaths = ["vfx/common/eff/abi_cnj022g.avfx"];
    private static readonly string[] SacredSoilVfxPaths = ["vfx/common/eff/abi_swl053g.avfx"];
    private readonly HashSet<nint> hiddenObjects = [];
    private readonly HashSet<uint> friendPlayers = [];
    private readonly HashSet<uint> partyPlayers = [];
    private readonly HashSet<uint> freeCompanyPlayers = [];
    private FeatureLifetime? runtimeLifetime;
    private Hook<ActionEffectHandler.Delegates.Receive>? actionEffectHook;
    private long asylumBlockUntil;
    private long asylumAllowUntil;
    private long sacredSoilBlockUntil;
    private long sacredSoilAllowUntil;
    private bool asylumResourceBlocked;
    private bool sacredSoilResourceBlocked;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (!AutoHideModelPanel.Draw(config))
        {
            return false;
        }

        Refresh();
        return true;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            actionEffectHook = DService.Instance().Hook.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
                ActionEffectHandler.MemberFunctionPointers.Receive,
                OnActionEffect);
            lifetime.Add(() =>
            {
                actionEffectHook?.Dispose();
                actionEffectHook = null;
            });
            actionEffectHook.Enable();
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 100))
            {
                throw new InvalidOperationException("Auto hide model update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            var clientState = DService.Instance().ClientState;
            clientState.Logout += OnLogout;
            lifetime.Add(() => clientState.Logout -= OnLogout);
            clientState.TerritoryChanged += OnTerritoryChanged;
            lifetime.Add(() => clientState.TerritoryChanged -= OnTerritoryChanged);
            runtimeLifetime = lifetime;
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            Refresh();
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn || services.ObjectTable.LocalPlayer is null)
        {
            ShowAll();
            ClearGroundEffectDecisions();
            return;
        }

        RefreshGroundEffectResourceBlacklist();
        try
        {
            UpdateVisibility();
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Auto hide model visibility update failed.");
            ShowAll();
        }
    }

    private void UpdateVisibility()
    {
        if (ShouldSuspendByCondition())
        {
            ShowAll();
            return;
        }

        var objectManager = GameObjectManager.Instance();
        if (objectManager == null)
        {
            return;
        }

        var localGameObject = objectManager->Objects.IndexSorted[0].Value;
        if (localGameObject == null || localGameObject->EntityId == InvalidEntityId)
        {
            return;
        }

        if (ShouldSuspendByTerritory(localGameObject))
        {
            ShowAll();
            return;
        }

        var namePlate = RaptureAtkUnitManager.Instance()->GetAddonByName("NamePlate");
        if ((namePlate == null || !namePlate->IsVisible) &&
            !DService.Instance().Condition[ConditionFlag.Performing])
        {
            return;
        }

        var localPlayer = (Character*)localGameObject;
        var isBound = DService.Instance().Condition[ConditionFlag.BoundByDuty] &&
                      localGameObject->EventId.ContentId != EventHandlerContent.TreasureHuntDirector;
        RefreshPlayerContainers(objectManager, localPlayer);
        for (var index = ObjectScanStart; index < UnimportantNpcScanEnd; index++)
        {
            var gameObject = objectManager->Objects.IndexSorted[index].Value;
            if (gameObject == null || gameObject == localGameObject)
            {
                continue;
            }

            if (index >= UnimportantNpcScanStart)
            {
                SetVisibility(gameObject, !ShouldHideUnimportantNpc(gameObject));
                continue;
            }

            if (!gameObject->IsCharacter())
            {
                ProcessNonCharacterPet(gameObject, localPlayer, index);
                continue;
            }

            var character = (Character*)gameObject;
            if (gameObject->EntityId == InvalidEntityId && gameObject->ObjectKind != ClientObjectKind.Companion)
            {
                continue;
            }

            switch (gameObject->ObjectKind)
            {
                case ClientObjectKind.Pc:
                    SetVisibility(gameObject, isBound || ShouldShowPlayer(character));
                    break;
                case ClientObjectKind.BattleNpc
                    when (BattleNpcSubKind)gameObject->SubKind == BattleNpcSubKind.Pet &&
                         character->NameId == EarthlyStarNameId:
                    SetVisibility(gameObject, !ShouldHideGroundEffectOwner(gameObject->OwnerId, localPlayer));
                    break;
                case ClientObjectKind.BattleNpc
                    when (BattleNpcSubKind)gameObject->SubKind == BattleNpcSubKind.Pet:
                    ProcessOwnedObject(gameObject, gameObject->OwnerId, localPlayer, config.Pets, isBound);
                    break;
                case ClientObjectKind.BattleNpc
                    when (BattleNpcSubKind)gameObject->SubKind == BattleNpcSubKind.Buddy:
                    ProcessOwnedObject(gameObject, gameObject->OwnerId, localPlayer, config.Chocobos, false);
                    break;
                case ClientObjectKind.Companion:
                    ProcessOwnedObject(
                        gameObject,
                        character->CompanionOwnerId,
                        localPlayer,
                        config.Minions,
                        false);
                    break;
            }
        }
    }

    private void RefreshPlayerContainers(GameObjectManager* objectManager, Character* localPlayer)
    {
        friendPlayers.Clear();
        partyPlayers.Clear();
        freeCompanyPlayers.Clear();
        for (var index = ObjectScanStart; index < ObjectScanEnd; index++)
        {
            var gameObject = objectManager->Objects.IndexSorted[index].Value;
            if (gameObject == null ||
                !gameObject->IsCharacter() ||
                gameObject->ObjectKind != ClientObjectKind.Pc ||
                gameObject->EntityId == InvalidEntityId)
            {
                continue;
            }

            var character = (Character*)gameObject;
            if (character->IsFriend)
            {
                friendPlayers.Add(gameObject->EntityId);
            }

            if (IsObjectIdInParty(gameObject->EntityId))
            {
                partyPlayers.Add(gameObject->EntityId);
            }

            if (IsSameFreeCompany(character, localPlayer))
            {
                freeCompanyPlayers.Add(gameObject->EntityId);
            }
        }
    }

    private bool ShouldShowPlayer(Character* character)
    {
        if (!ShouldHide(config.Players))
        {
            return true;
        }

        var entityId = character->GameObject.EntityId;
        return config.Players.ShowDead && character->GameObject.IsDead() ||
               config.Players.ShowFriends && friendPlayers.Contains(entityId) ||
               config.Players.ShowFreeCompany && freeCompanyPlayers.Contains(entityId) ||
               config.Players.ShowParty && partyPlayers.Contains(entityId) ||
               IsTargetOfTarget(character);
    }

    private void ProcessOwnedObject(
        GameObject* gameObject,
        uint ownerId,
        Character* localPlayer,
        AutoHideUnitConfig unitConfig,
        bool forceShow)
    {
        if (ownerId == localPlayer->GameObject.EntityId)
        {
            SetVisibility(gameObject, !config.IncludeSelf || !unitConfig.HideAll);
            return;
        }

        SetVisibility(gameObject, forceShow || ShouldShowOwnedObject(ownerId, unitConfig));
    }

    private void ProcessNonCharacterPet(
        GameObject* gameObject,
        Character* localPlayer,
        int objectIndex)
    {
        if (!ShouldTreatAsNonCharacterPet(gameObject, objectIndex) || !ShouldHide(config.Minions))
        {
            Show(gameObject);
            return;
        }

        ProcessOwnedObject(gameObject, gameObject->OwnerId, localPlayer, config.Minions, false);
    }

    private bool ShouldShowOwnedObject(uint ownerId, AutoHideUnitConfig unitConfig)
    {
        if (!ShouldHide(unitConfig))
        {
            return true;
        }

        return unitConfig.ShowFriends && friendPlayers.Contains(ownerId) ||
               unitConfig.ShowFreeCompany && freeCompanyPlayers.Contains(ownerId) ||
               unitConfig.ShowParty && IsObjectIdInParty(ownerId);
    }

    private bool ShouldHideGroundEffectOwner(uint ownerId, Character* localPlayer) =>
        config.HideGroundHealingEffects &&
        ownerId != localPlayer->GameObject.EntityId &&
        !IsObjectIdInParty(ownerId);

    private bool IsTargetOfTarget(Character* character)
    {
        if (!config.ShowTargetOfTarget)
        {
            return false;
        }

        var target = (Character*)TargetSystem.Instance()->Target;
        return target != null &&
               target->GameObject.IsCharacter() &&
               CharacterManager.Instance()->LookupBattleCharaByEntityId(target->TargetId.ObjectId) == character;
    }

    private void SetVisibility(GameObject* gameObject, bool visible)
    {
        if (visible)
        {
            Show(gameObject);
        }
        else
        {
            Hide(gameObject);
        }
    }

    private void Hide(GameObject* gameObject)
    {
        var address = (nint)gameObject;
        if (!hiddenObjects.Contains(address))
        {
            if (gameObject->RenderFlags.HasFlag(InvisibleFlags))
            {
                return;
            }

            hiddenObjects.Add(address);
        }

        gameObject->RenderFlags |= InvisibleFlags;
    }

    private void Show(GameObject* gameObject)
    {
        var address = (nint)gameObject;
        if (!hiddenObjects.Remove(address))
        {
            return;
        }

        gameObject->RenderFlags &= ~InvisibleFlags;
    }

    private void ShowAll()
    {
        if (hiddenObjects.Count == 0)
        {
            return;
        }

        foreach (var address in hiddenObjects)
        {
            var gameObject = FindGameObject(address);
            if (gameObject != null)
            {
                gameObject->RenderFlags &= ~InvisibleFlags;
            }
        }

        hiddenObjects.Clear();
    }

    private void OnActionEffect(
        uint casterEntityId,
        Character* caster,
        Vector3* targetPosition,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        try
        {
            if (config.HideGroundHealingEffects && header != null &&
                TryGetGroundHealingVfxKind(header->ActionId, out var kind) &&
                DService.Instance().ObjectTable.LocalPlayer is { } localPlayer)
            {
                SetGroundEffectDecision(
                    kind,
                    casterEntityId != localPlayer.EntityID && !IsObjectIdInParty(casterEntityId));
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Auto hide model action effect handling failed.");
        }
        finally
        {
            actionEffectHook!.Original(
                casterEntityId,
                caster,
                targetPosition,
                header,
                effects,
                targetEntityIds);
        }
    }

    private void SetGroundEffectDecision(GroundHealingVfxKind kind, bool shouldBlock)
    {
        var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 4;
        switch (kind)
        {
            case GroundHealingVfxKind.Asylum:
                if (shouldBlock)
                {
                    asylumBlockUntil = until;
                }
                else
                {
                    asylumAllowUntil = until;
                    asylumBlockUntil = 0;
                }

                break;
            case GroundHealingVfxKind.SacredSoil:
                if (shouldBlock)
                {
                    sacredSoilBlockUntil = until;
                }
                else
                {
                    sacredSoilAllowUntil = until;
                    sacredSoilBlockUntil = 0;
                }

                break;
        }

        RefreshGroundEffectResourceBlacklist(kind);
    }

    private void RefreshGroundEffectResourceBlacklist()
    {
        RefreshGroundEffectResourceBlacklist(GroundHealingVfxKind.Asylum);
        RefreshGroundEffectResourceBlacklist(GroundHealingVfxKind.SacredSoil);
    }

    private void RefreshGroundEffectResourceBlacklist(GroundHealingVfxKind kind)
    {
        var shouldBlock = ShouldBlockGroundEffectResource(kind);
        ref var registered = ref (kind == GroundHealingVfxKind.Asylum
            ? ref asylumResourceBlocked
            : ref sacredSoilResourceBlocked);
        if (registered == shouldBlock)
        {
            return;
        }

        if (shouldBlock)
        {
            GameResourceManager.Instance().AddToBlacklist(
                typeof(AutoHideModelFeature),
                GetGroundHealingVfxPaths(kind));
        }
        else
        {
            GameResourceManager.Instance().RemoveFromBlacklist(
                typeof(AutoHideModelFeature),
                GetGroundHealingVfxPaths(kind));
        }

        registered = shouldBlock;
    }

    private bool ShouldBlockGroundEffectResource(GroundHealingVfxKind kind)
    {
        if (!config.HideGroundHealingEffects)
        {
            return false;
        }

        var currentTick = Stopwatch.GetTimestamp();
        return kind switch
        {
            GroundHealingVfxKind.Asylum =>
                currentTick <= asylumBlockUntil && currentTick > asylumAllowUntil,
            GroundHealingVfxKind.SacredSoil =>
                currentTick <= sacredSoilBlockUntil && currentTick > sacredSoilAllowUntil,
            _ => false
        };
    }

    private void ClearGroundEffectDecisions()
    {
        asylumBlockUntil = 0;
        asylumAllowUntil = 0;
        sacredSoilBlockUntil = 0;
        sacredSoilAllowUntil = 0;
        if (asylumResourceBlocked)
        {
            GameResourceManager.Instance().RemoveFromBlacklist(
                typeof(AutoHideModelFeature),
                AsylumVfxPaths);
            asylumResourceBlocked = false;
        }

        if (sacredSoilResourceBlocked)
        {
            GameResourceManager.Instance().RemoveFromBlacklist(
                typeof(AutoHideModelFeature),
                SacredSoilVfxPaths);
            sacredSoilResourceBlocked = false;
        }
    }

    private void Refresh()
    {
        ShowAll();
        friendPlayers.Clear();
        partyPlayers.Clear();
        freeCompanyPlayers.Clear();
        ClearGroundEffectDecisions();
    }

    private void OnLogout(int _, int __) => Refresh();

    private void OnTerritoryChanged(uint _) => Refresh();

    private bool ShouldHideUnimportantNpc(GameObject* gameObject) =>
        config.HideUnimportantNpcs &&
        !gameObject->TargetableStatus.HasFlag(ObjectTargetableFlags.IsTargetable) &&
        gameObject->EventHandler == null;

    private static bool ShouldHide(AutoHideUnitConfig unitConfig) =>
        unitConfig.HideAll ||
        unitConfig.HideInCombat && DService.Instance().Condition[ConditionFlag.InCombat];

    private static bool ShouldTreatAsNonCharacterPet(GameObject* gameObject, int objectIndex) =>
        objectIndex < ObjectScanEnd &&
        objectIndex % 2 == 1 &&
        gameObject->ObjectKind != ClientObjectKind.Mount &&
        gameObject->OwnerId is not 0 and not InvalidEntityId &&
        (gameObject->ObjectKind == ClientObjectKind.Companion || gameObject->NamePlateIconId == 0);

    private static bool ShouldSuspendByCondition()
    {
        var condition = DService.Instance().Condition;
        return condition[ConditionFlag.BetweenAreas] ||
               condition[ConditionFlag.BetweenAreas51] ||
               condition[ConditionFlag.OccupiedInCutSceneEvent] ||
               condition[ConditionFlag.WatchingCutscene] ||
               condition[ConditionFlag.WatchingCutscene78] ||
               condition[ConditionFlag.DutyRecorderPlayback];
    }

    private static bool ShouldSuspendByTerritory(GameObject* localGameObject)
    {
        if (GameState.TerritoryIntendedUse == TerritoryIntendedUse.OccultCrescent)
        {
            return localGameObject->Position.Y < 0f;
        }

        return GameState.ContentFinderCondition != 0 ||
               GameState.IsInPVPArea ||
               GameState.TerritoryIntendedUse == TerritoryIntendedUse.IslandSanctuary;
    }

    private static bool IsObjectIdInParty(uint objectId)
    {
        var groupManager = GroupManager.Instance();
        if (groupManager != null &&
            groupManager->MainGroup.MemberCount > 0 &&
            groupManager->MainGroup.IsEntityIdInParty(objectId))
        {
            return true;
        }

        var crossRealm = InfoProxyCrossRealm.Instance();
        if (crossRealm == null || !crossRealm->IsInCrossRealmParty)
        {
            return false;
        }

        foreach (var group in crossRealm->CrossRealmGroups)
        {
            for (var index = 0; index < group.GroupMembers.Length; index++)
            {
                if (group.GroupMembers[index].EntityId == objectId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSameFreeCompany(Character* character, Character* localPlayer)
    {
        if (localPlayer->FreeCompanyTag[0] == 0 || localPlayer->CurrentWorld != localPlayer->HomeWorld)
        {
            return false;
        }

        for (var index = 0; index < 6; index++)
        {
            if (character->FreeCompanyTag[index] != localPlayer->FreeCompanyTag[index])
            {
                return false;
            }
        }

        return true;
    }

    private static GameObject* FindGameObject(nint address)
    {
        var objectManager = GameObjectManager.Instance();
        if (objectManager == null)
        {
            return null;
        }

        for (var index = 0; index < UnimportantNpcScanEnd; index++)
        {
            var gameObject = objectManager->Objects.IndexSorted[index].Value;
            if ((nint)gameObject == address)
            {
                return gameObject;
            }
        }

        return null;
    }

    private static bool TryGetGroundHealingVfxKind(uint actionId, out GroundHealingVfxKind kind)
    {
        switch (actionId)
        {
            case AsylumActionId:
                kind = GroundHealingVfxKind.Asylum;
                return true;
            case SacredSoilActionId:
                kind = GroundHealingVfxKind.SacredSoil;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string[] GetGroundHealingVfxPaths(GroundHealingVfxKind kind) =>
        kind == GroundHealingVfxKind.Asylum ? AsylumVfxPaths : SacredSoilVfxPaths;

    private enum GroundHealingVfxKind
    {
        Asylum,
        SacredSoil
    }
}
