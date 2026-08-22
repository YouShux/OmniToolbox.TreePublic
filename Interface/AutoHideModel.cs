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
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.OmenService;
using BattleNpcSubKind = Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind;
using ClientObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;
using ModuleBase = OmniToolbox.Common.Module.Abstractions.ModuleBase;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class AutoHideModel(AutoHideModelConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AutoHideModelTitle"),
        Description = OmniLoc.Get("AutoHideModelDescription"),
        Category = ModuleCategory.Interface
    };

    private const int ObjectScanStart = 1;
    private const int ObjectScanEnd = 200;
    private const int UnimportantNPCScanStart = 489;
    private const int UnimportantNPCScanEnd = 630;
    private const float UnimportantNPCVisibilityDistanceSquared = 25f;
    private const uint InvalidEntityID = 0xE0000000;
    private const uint EarthlyStarNameID = 6565;
    private const uint AsylumActionID = 3569;
    private const uint SacredSoilActionID = 188;
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
        var changed = DrawVisibilityTable(config);
        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        changed |= DrawCheckbox(
            "Feature.AutoHideModel.IncludeSelf",
            "includeSelf",
            config.IncludeSelf,
            value => config.IncludeSelf = value);
        ImGui.SameLine(0f, OmniTheme.Scale(16f));
        changed |= DrawCheckbox(
            "Feature.AutoHideModel.HideGroundHealingEffects",
            "groundEffects",
            config.HideGroundHealingEffects,
            value => config.HideGroundHealingEffects = value);
        ImGui.SameLine(0f, OmniTheme.Scale(16f));
        changed |= DrawCheckbox(
            "Feature.AutoHideModel.ShowTargetOfTarget",
            "targetOfTarget",
            config.ShowTargetOfTarget,
            value => config.ShowTargetOfTarget = value);
        ImGui.SameLine(0f, OmniTheme.Scale(16f));
        changed |= DrawCheckbox(
            "Feature.AutoHideModel.ReduceOnScreenPlayers",
            "reduceOnScreenPlayers",
            config.ReduceOnScreenPlayers,
            value => config.ReduceOnScreenPlayers = value);
        if (!changed)
        {
            return false;
        }

        Refresh();
        return true;
    }

    private static bool DrawVisibilityTable(AutoHideModelConfig config)
    {
        using var table = ImRaii.Table(
            "##autoHideModelSettings",
            8,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(78f));
        ImGui.TableSetupColumn("##hideAll", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(82f));
        ImGui.TableSetupColumn("##combat", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(82f));
        ImGui.TableSetupColumn("##party", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(82f));
        ImGui.TableSetupColumn("##friend", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(82f));
        ImGui.TableSetupColumn("##company", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(82f));
        ImGui.TableSetupColumn("##dead", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(82f));
        ImGui.TableSetupColumn("##npc", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(82f));
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        DrawHeader(string.Empty);
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.HideAll"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.HideInCombat"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.ShowParty"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.ShowFriends"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.ShowFreeCompany"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.ShowDead"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.HideUnimportantNpcs"));

        var changed = DrawUnitRow(
            "Feature.AutoHideModel.Players",
            "players",
            config.Players,
            true,
            config.HideUnimportantNpcs,
            value => config.HideUnimportantNpcs = value);
        changed |= DrawUnitRow(
            "Feature.AutoHideModel.Pets",
            "pets",
            config.Pets,
            false,
            false,
            null);
        changed |= DrawUnitRow(
            "Feature.AutoHideModel.Chocobos",
            "chocobos",
            config.Chocobos,
            false,
            false,
            null);
        changed |= DrawUnitRow(
            "Feature.AutoHideModel.Minions",
            "minions",
            config.Minions,
            false,
            false,
            null);
        return changed;
    }

    private static bool DrawUnitRow(
        string labelKey,
        string id,
        AutoHideUnitConfig config,
        bool showDeadOption,
        bool hideUnimportantNpcs,
        Action<bool>? setHideUnimportantNpcs)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        var changed = DrawCenteredCheckbox(
            $"{id}HideAll",
            config.HideAll,
            value => config.HideAll = value);
        changed |= DrawCenteredCheckbox(
            $"{id}Combat",
            config.HideInCombat,
            value => config.HideInCombat = value);
        changed |= DrawCenteredCheckbox(
            $"{id}Party",
            config.ShowParty,
            value => config.ShowParty = value);
        changed |= DrawCenteredCheckbox(
            $"{id}Friends",
            config.ShowFriends,
            value => config.ShowFriends = value);
        changed |= DrawCenteredCheckbox(
            $"{id}Company",
            config.ShowFreeCompany,
            value => config.ShowFreeCompany = value);
        if (showDeadOption)
        {
            changed |= DrawCenteredCheckbox(
                $"{id}Dead",
                config.ShowDead,
                value => config.ShowDead = value);
        }
        else
        {
            DrawDisabledCell();
        }

        if (setHideUnimportantNpcs is not null)
        {
            changed |= DrawCenteredCheckbox(
                $"{id}UnimportantNpcs",
                hideUnimportantNpcs,
                setHideUnimportantNpcs);
        }
        else
        {
            DrawDisabledCell();
        }

        return changed;
    }

    private static bool DrawCenteredCheckbox(string id, bool value, Action<bool> setValue)
    {
        ImGui.TableNextColumn();
        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() +
            MathF.Max(0f, (ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight()) * 0.5f));
        if (!OmniControls.Checkbox($"##autoHideModel{id}", ref value))
        {
            return false;
        }

        setValue(value);
        return true;
    }

    private static void DrawHeader(string text)
    {
        ImGui.TableNextColumn();
        if (text.Length == 0)
        {
            return;
        }

        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() +
            MathF.Max(0f, (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f));
        ImGui.TextUnformatted(text);
    }

    private static void DrawDisabledCell()
    {
        ImGui.TableNextColumn();
        var text = "-";
        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() +
            MathF.Max(0f, (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f));
        ImGui.TextDisabled(text);
    }

    private static bool DrawCheckbox(
        string key,
        string id,
        bool value,
        Action<bool> setValue)
    {
        if (!OmniControls.Checkbox($"{OmniLoc.Get(key)}##autoHideModel{id}", ref value))
        {
            return false;
        }

        setValue(value);
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
            RestoreNonPlayerVisibility();
        }
    }

    private void UpdateVisibility()
    {
        if (ShouldSuspendByCondition())
        {
            RestoreNonPlayerVisibility();
            return;
        }

        var objectManager = GameObjectManager.Instance();
        if (objectManager == null)
        {
            return;
        }

        var localGameObject = objectManager->Objects.IndexSorted[0].Value;
        if (localGameObject == null || localGameObject->EntityId == InvalidEntityID)
        {
            return;
        }

        if (ShouldSuspendByTerritory())
        {
            RestoreNonPlayerVisibility();
            return;
        }

        var namePlate = RaptureAtkUnitManager.Instance()->GetAddonByName("NamePlate");
        if ((namePlate == null || !namePlate->IsVisible) &&
            !DService.Instance().Condition[ConditionFlag.Performing])
        {
            RestoreNonPlayerVisibility();
            return;
        }

        var localPlayer = (Character*)localGameObject;
        var isBound = GameState.TerritoryIntendedUse != TerritoryIntendedUse.OccultCrescent &&
                      DService.Instance().Condition[ConditionFlag.BoundByDuty] &&
                      localGameObject->EventId.ContentId != EventHandlerContent.TreasureHuntDirector;
        RefreshPlayerContainers(objectManager, localPlayer);
        var playerCount = 0;
        for (var index = ObjectScanStart; index < UnimportantNPCScanEnd; index++)
        {
            var gameObject = objectManager->Objects.IndexSorted[index].Value;
            if (gameObject == null || gameObject == localGameObject)
            {
                continue;
            }

            if (index >= UnimportantNPCScanStart)
            {
                SetVisibility(gameObject, !ShouldHideUnimportantNPC(gameObject, localGameObject));
                continue;
            }

            if (!gameObject->IsCharacter())
            {
                ProcessNonCharacterPet(gameObject, localPlayer, index);
                continue;
            }

            var character = (Character*)gameObject;
            if (gameObject->EntityId == InvalidEntityID && gameObject->ObjectKind != ClientObjectKind.Companion)
            {
                continue;
            }

            switch (gameObject->ObjectKind)
            {
                case ClientObjectKind.Pc:
                    var reducePlayerCount = index < ObjectScanEnd && index % 2 == 0;
                    if (reducePlayerCount)
                    {
                        playerCount++;
                    }

                    SetVisibility(
                        gameObject,
                        isBound || ShouldShowPlayer(character, reducePlayerCount ? playerCount : 0));
                    break;
                case ClientObjectKind.BattleNpc
                    when (BattleNpcSubKind)gameObject->SubKind == BattleNpcSubKind.Pet &&
                         character->NameId == EarthlyStarNameID:
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
                gameObject->EntityId == InvalidEntityID)
            {
                continue;
            }

            var character = (Character*)gameObject;
            if (character->IsFriend)
            {
                friendPlayers.Add(gameObject->EntityId);
            }

            if (IsObjectIDInParty(gameObject->EntityId))
            {
                partyPlayers.Add(gameObject->EntityId);
            }

            if (IsSameFreeCompany(character, localPlayer))
            {
                freeCompanyPlayers.Add(gameObject->EntityId);
            }
        }
    }

    private bool ShouldShowPlayer(Character* character, int playerCount)
    {
        var entityID = character->GameObject.EntityId;
        var showByConfig = config.Players.ShowDead && character->GameObject.IsDead() ||
                           config.Players.ShowFriends && friendPlayers.Contains(entityID) ||
                           config.Players.ShowFreeCompany && freeCompanyPlayers.Contains(entityID) ||
                           config.Players.ShowParty && partyPlayers.Contains(entityID) ||
                           IsTargetOfTarget(character);
        if (ShouldHide(config.Players))
        {
            return showByConfig;
        }

        var target = TargetSystem.Instance()->Target;
        return !config.ReduceOnScreenPlayers ||
               playerCount < 10 ||
               character->GameObject.NamePlateIconId != 0 ||
               target != null && target->EntityId == entityID ||
               showByConfig;
    }

    private void ProcessOwnedObject(
        GameObject* gameObject,
        uint ownerID,
        Character* localPlayer,
        AutoHideUnitConfig unitConfig,
        bool forceShow)
    {
        if (ownerID == localPlayer->GameObject.EntityId)
        {
            SetVisibility(gameObject, !config.IncludeSelf || !unitConfig.HideAll);
            return;
        }

        SetVisibility(gameObject, forceShow || ShouldShowOwnedObject(ownerID, unitConfig));
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

    private bool ShouldShowOwnedObject(uint ownerID, AutoHideUnitConfig unitConfig)
    {
        if (!ShouldHide(unitConfig))
        {
            return true;
        }

        return unitConfig.ShowFriends && friendPlayers.Contains(ownerID) ||
               unitConfig.ShowFreeCompany && freeCompanyPlayers.Contains(ownerID) ||
               unitConfig.ShowParty && IsObjectIDInParty(ownerID);
    }

    private bool ShouldHideGroundEffectOwner(uint ownerID, Character* localPlayer) =>
        config.HideGroundHealingEffects &&
        ownerID != localPlayer->GameObject.EntityId &&
        !IsObjectIDInParty(ownerID);

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

    private void RestoreNonPlayerVisibility()
    {
        foreach (var address in hiddenObjects)
        {
            var gameObject = FindGameObject(address);
            if (gameObject != null && gameObject->ObjectKind != ClientObjectKind.Pc)
            {
                gameObject->RenderFlags &= ~InvisibleFlags;
            }
        }
    }

    private void OnActionEffect(
        uint casterEntityID,
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
                    casterEntityID != localPlayer.EntityID && !IsObjectIDInParty(casterEntityID));
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Auto hide model action effect handling failed.");
        }
        finally
        {
            actionEffectHook!.Original(
                casterEntityID,
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
                typeof(AutoHideModel),
                GetGroundHealingVfxPaths(kind));
        }
        else
        {
            GameResourceManager.Instance().RemoveFromBlacklist(
                typeof(AutoHideModel),
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
                typeof(AutoHideModel),
                AsylumVfxPaths);
            asylumResourceBlocked = false;
        }

        if (sacredSoilResourceBlocked)
        {
            GameResourceManager.Instance().RemoveFromBlacklist(
                typeof(AutoHideModel),
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

    private void OnLogout(int _, int unusedReason) => Refresh();

    private void OnTerritoryChanged(uint _) => Refresh();

    private bool ShouldHideUnimportantNPC(GameObject* gameObject, GameObject* localGameObject) =>
        config.HideUnimportantNpcs &&
        !gameObject->TargetableStatus.HasFlag(ObjectTargetableFlags.IsTargetable) &&
        gameObject->EventHandler == null &&
        Vector3.DistanceSquared(gameObject->Position, localGameObject->Position) >
        UnimportantNPCVisibilityDistanceSquared;

    private static bool ShouldHide(AutoHideUnitConfig unitConfig) =>
        unitConfig.HideAll ||
        unitConfig.HideInCombat && DService.Instance().Condition[ConditionFlag.InCombat];

    private static bool ShouldTreatAsNonCharacterPet(GameObject* gameObject, int objectIndex) =>
        objectIndex < ObjectScanEnd &&
        objectIndex % 2 == 1 &&
        gameObject->ObjectKind != ClientObjectKind.Mount &&
        gameObject->OwnerId is not 0 and not InvalidEntityID &&
        (gameObject->ObjectKind == ClientObjectKind.Companion || gameObject->NamePlateIconId == 0);

    private static bool ShouldSuspendByCondition()
    {
        var condition = DService.Instance().Condition;
        return condition[ConditionFlag.BetweenAreas] ||
               condition[ConditionFlag.BetweenAreas51] ||
               condition[ConditionFlag.OccupiedInEvent] ||
               condition[ConditionFlag.OccupiedInQuestEvent] ||
               condition[ConditionFlag.OccupiedInCutSceneEvent] ||
               condition[ConditionFlag.WatchingCutscene] ||
               condition[ConditionFlag.WatchingCutscene78] ||
               condition[ConditionFlag.DutyRecorderPlayback];
    }

    private static bool ShouldSuspendByTerritory()
    {
        if (GameState.TerritoryIntendedUse == TerritoryIntendedUse.OccultCrescent)
        {
            return false;
        }

        return GameState.ContentFinderCondition != 0 ||
               GameState.IsInPVPArea ||
               GameState.TerritoryIntendedUse == TerritoryIntendedUse.IslandSanctuary;
    }

    private static bool IsObjectIDInParty(uint objectID)
    {
        var groupManager = GroupManager.Instance();
        if (groupManager != null &&
            groupManager->MainGroup.MemberCount > 0 &&
            groupManager->MainGroup.IsEntityIdInParty(objectID))
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
                if (group.GroupMembers[index].EntityId == objectID)
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

        for (var index = 0; index < UnimportantNPCScanEnd; index++)
        {
            var gameObject = objectManager->Objects.IndexSorted[index].Value;
            if ((nint)gameObject == address)
            {
                return gameObject;
            }
        }

        return null;
    }

    private static bool TryGetGroundHealingVfxKind(uint actionID, out GroundHealingVfxKind kind)
    {
        switch (actionID)
        {
            case AsylumActionID:
                kind = GroundHealingVfxKind.Asylum;
                return true;
            case SacredSoilActionID:
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

[Serializable]
public sealed class AutoHideModelConfig
{
    public AutoHideUnitConfig Players { get; set; } = new()
    {
        ShowParty = true,
        ShowFriends = true,
        ShowFreeCompany = true,
        ShowDead = true
    };

    public AutoHideUnitConfig Pets { get; set; } = new()
    {
        HideAll = true,
        ShowParty = true,
        ShowFriends = true,
        ShowFreeCompany = true
    };

    public AutoHideUnitConfig Chocobos { get; set; } = new()
    {
        HideAll = true,
        ShowParty = true,
        ShowFriends = true,
        ShowFreeCompany = true
    };

    public AutoHideUnitConfig Minions { get; set; } = new()
    {
        HideAll = true,
        ShowParty = true,
        ShowFriends = true,
        ShowFreeCompany = true
    };

    public bool IncludeSelf { get; set; } = true;

    public bool HideGroundHealingEffects { get; set; } = true;

    public bool ShowTargetOfTarget { get; set; } = true;

    public bool ReduceOnScreenPlayers { get; set; } = true;

    public bool HideUnimportantNpcs { get; set; } = true;
}

[Serializable]
public sealed class AutoHideUnitConfig
{
    public bool HideAll { get; set; }

    public bool HideInCombat { get; set; }

    public bool ShowParty { get; set; }

    public bool ShowFriends { get; set; }

    public bool ShowFreeCompany { get; set; }

    public bool ShowDead { get; set; }
}
