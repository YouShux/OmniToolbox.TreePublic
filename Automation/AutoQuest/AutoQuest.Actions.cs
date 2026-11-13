using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Dalamud;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Extensions;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.OmenService;
using OmniToolbox.Quests;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    private string? ExecuteStep(Session current, QuestTarget target, QuestStep step)
    {
        var services = DService.Instance();
        if (services.Condition.IsBetweenAreas)
        {
            current.AcceptOfferIdleSince = 0;
            current.Phase = QuestExecutionPhase.Waiting;
            return null;
        }
        var ownsQuestEvent = IsOwnedQuestEvent(current);
        if (ownsQuestEvent)
            current.AcceptOfferIdleSince = 0;
        if (ownsQuestEvent && services.Condition.IsWatchingCutscene)
        {
            current.Phase = QuestExecutionPhase.Interacting;
            current.StepStartedAt = Environment.TickCount64;
            return null;
        }
        if (Environment.TickCount64 < current.NextActionAt)
            return null;
        current.NextActionAt = Environment.TickCount64 + 300;
        if (step.Kind == QuestStepKind.SwitchJob && current.ActionSent && Environment.TickCount64 - current.StepStartedAt > 30_000)
            return "class_job_switch_unconfirmed";
        if (!current.OwnsTravel && Environment.TickCount64 - current.StepStartedAt > 180_000 &&
            step.Kind is not (QuestStepKind.Combat or QuestStepKind.Duty or QuestStepKind.Craft or QuestStepKind.Gather or QuestStepKind.Fish))
            return "interaction_timeout";

        if (ownsQuestEvent)
        {
            current.Phase = QuestExecutionPhase.Interacting;
            return DriveQuestUI(current, target, step);
        }
        if (current.Request.AllowEurekaEntry && step.ObjectID == 1024517 && current.OwnedActorID != 0 &&
            IsRodneyEntranceEvent(current.OwnedActorID))
            return "eureka_quest_unavailable";
        if (step.Kind == QuestStepKind.AcceptQuest && current.ActionSent && !current.AcceptSent)
        {
            if (GetQuestIssuerDialogue(current.OwnedActorID) is { } dialogueID)
            {
                current.OwnedEventID = dialogueID;
                current.RelatedID = target.ID;
                return "quest_accept_not_offered";
            }
            // 有些接取限制由服务器在 NPC 对话里告知，任务表不一定能预检。
            if (services.Condition.IsOccupiedInEvent || services.Condition.IsWatchingCutscene || !UIModule.IsScreenReady())
                current.AcceptOfferIdleSince = 0;
            else
            {
                var now = Environment.TickCount64;
                if (current.AcceptOfferIdleSince == 0) current.AcceptOfferIdleSince = now;
                if (now - current.AcceptOfferIdleSince >= 5_000)
                {
                    current.RelatedID = target.ID;
                    return "quest_accept_not_offered";
                }
            }
            return null;
        }
        if (step.Kind == QuestStepKind.Duty)
            return DriveDuty(current, step);
        if (step.Kind == QuestStepKind.Craft)
            return DriveCraft(current, step);
        if (step.Kind == QuestStepKind.Fish)
            return DriveFishing(current, step);
        if (step.Kind == QuestStepKind.Gather)
            return DriveGathering(current, target, step);
        if (step.Kind == QuestStepKind.Unsupported)
            return "mechanism_unsupported";
        if (step.Kind == QuestStepKind.SwitchJob)
        {
            if (DService.Instance().Condition.IsOccupiedInEvent || DService.Instance().Condition[ConditionFlag.InCombat])
                return "character_busy";
            if (!current.ActionSent)
            {
                if (step.Argument > byte.MaxValue || !LocalPlayerState.SwitchGearset((byte)step.Argument))
                    return "gearset_unavailable";
                current.ActionSent = true;
            }
            return null;
        }

        if (step.Kind == QuestStepKind.Combat)
        {
            if (!current.OwnsCombat)
            {
                IGameObject? combatTarget;
                if (step.EnemyNameID != 0)
                {
                    var searchError = ApproachRoamingEnemy(current, step, out var enemy);
                    if (searchError is not null || enemy is null)
                        return searchError;
                    combatTarget = enemy;
                }
                else
                    combatTarget = FindStepObject(target, step, out _);
                if (combatTarget is null)
                {
                    if (step.Position is not { } combatAnchor)
                        return "target_location_unresolved";
                    var anchorError = Approach(current, step, combatAnchor, step.ActivateCombatByProximity ? 1f : 10f);
                    if (anchorError is not null || current.OwnsTravel)
                        return anchorError;
                    if (step.ActivateCombatByProximity && Environment.TickCount64 - current.StepStartedAt < 12_000)
                        return null;
                    return "target_not_found";
                }
                if (step.EnemyNameID == 0)
                {
                    var navigationError = Approach(current, step, combatTarget.Position, 20);
                    if (navigationError is not null || current.OwnsTravel)
                        return navigationError;
                }
                if (TargetManager.Target?.GameObjectID != combatTarget.GameObjectID)
                {
                    TargetManager.Target = combatTarget;
                    if (TargetManager.Target?.GameObjectID != combatTarget.GameObjectID)
                        return "combat_target_unconfirmed";
                }
                if (!combat.TryStart(current.Operation.OperationID, out var error))
                    return error ?? "combat_unavailable";
                current.OwnsCombat = true;
                current.ActionSent = true;
                current.StepStartedAt = Environment.TickCount64;
                LogTransition(current, "combat-target-engaged", $"actor={combatTarget.GameObjectID:X} dataID={combatTarget.DataID} nameID={step.EnemyNameID}");
            }
            current.Phase = QuestExecutionPhase.Fighting;
            var status = combat.Tick(current.Operation.OperationID);
            if (status is { State: QuestWorkState.Running, Error: null } &&
                step.EnemyNameID == 0 &&
                (TargetManager.Target is not IBattleNPC { IsDead: false, IsTargetable: true, CurrentHp: > 0 } selected ||
                 selected.DataID != step.ObjectID) &&
                FindStepObject(target, step, out _) is { } nextEnemy)
            {
                TargetManager.Target = nextEnemy;
                if (TargetManager.Target?.GameObjectID == nextEnemy.GameObjectID)
                    LogTransition(current, "combat-target-switched", $"actor={nextEnemy.GameObjectID:X} dataID={nextEnemy.DataID}");
            }
            if (status.State == QuestWorkState.Failed)
                return status.Error ?? "combat_failed";
            if (Environment.TickCount64 - current.StepStartedAt > 180_000)
                return "combat_objective_unconfirmed";
            if (status.State == QuestWorkState.Succeeded)
            {
                current.OwnsCombat = false;
                if (step.EnemyNameID == 0)
                    current.ActionSent = false;
                else
                {
                    current.CombatSearchPoint = 0;
                    current.CombatSearchPass = 0;
                    current.UnreachableCombatActors.Clear();
                    LogTransition(current, "combat-rescan");
                }
                return null;
            }
            return null;
        }

        if (current.ActionSent)
            return null;
        var gameObject = FindStepObject(target, step, out var ambiguity);
        if (ambiguity)
            return "target_instance_unresolved";
        if (DService.Instance().Condition.IsOccupiedInEvent)
            return "external_interaction_busy";
        var position = gameObject?.Position ?? step.Position;
        if (position is null)
            return "target_location_unresolved";
        var radius = step.Kind == QuestStepKind.WalkTo ? 1f : 3f;
        var approach = Approach(current, step, position.Value, radius);
        if (approach is not null || current.OwnsTravel)
            return approach;
        if (step.Kind == QuestStepKind.WalkTo)
        {
            current.ActionSent = true;
            return null;
        }
        if (step.ObjectID != 0 && gameObject is null)
            return "target_not_found";
        current.Phase = QuestExecutionPhase.Interacting;
        if (gameObject is not null && TargetManager.Target?.GameObjectID != gameObject.GameObjectID)
        {
            TargetManager.Target = gameObject;
            if (step.Kind == QuestStepKind.Emote)
                return null;
        }
        if (step.Kind is QuestStepKind.AcceptQuest or QuestStepKind.CompleteQuest or QuestStepKind.Interact)
        {
            if (gameObject is null)
                return "target_not_found";
            if (step.Kind == QuestStepKind.AcceptQuest && data.CheckAcceptance(target) is { } blocker)
            {
                current.RelatedID = blocker.RelatedID;
                return blocker.Code;
            }
            current.OwnedEventID = target.ID;
            current.OwnedActorID = gameObject.GameObjectID;
            TargetManager.Instance().InteractWithObject(gameObject);
            current.ActionSent = true;
            current.StepStartedAt = Environment.TickCount64;
            current.NextActionAt = Environment.TickCount64 + 600;
            return null;
        }
        if (step.Argument == 0 && step.Kind != QuestStepKind.Say)
            return "action_argument_unresolved";
        var actionManager = UseActionManager.Instance();
        var targetID = gameObject?.GameObjectID ?? 0xE0000000;
        var sent = step.Kind switch
        {
            QuestStepKind.UseItem => step.ObjectID == 0
                ? actionManager.UseActionLocation(step.Argument >= 2_000_000 ? ActionType.EventItem : ActionType.Item, step.Argument, location: position.Value)
                : actionManager.UseAction(step.Argument >= 2_000_000 ? ActionType.EventItem : ActionType.Item, step.Argument, targetID),
            QuestStepKind.Action => actionManager.UseAction(ActionType.Action, step.Argument, targetID),
            QuestStepKind.Emote => ExecuteEmote(step.Argument, targetID),
            QuestStepKind.Say => SendQuestSpeech(step.Text),
            _ => false
        };
        if (!sent)
            return "quest_action_failed";
        if (step.Kind == QuestStepKind.Emote && gameObject is not null)
        {
            current.OwnedEventID = target.ID;
            current.OwnedActorID = gameObject.GameObjectID;
        }
        current.ActionSent = true;
        return null;
    }


    private string? ApproachRoamingEnemy(Session current, QuestStep step, out IBattleNPC? enemy)
    {
        enemy = null;
        var services = DService.Instance();
        if (services.ObjectTable.LocalPlayer is not { } player)
            return "character_not_ready";
        var id = current.Operation.OperationID;
        var now = Environment.TickCount64;
        var atTerritory = services.ClientState.TerritoryType == step.TerritoryID;
        if (atTerritory)
        {
            var distance = float.MaxValue;
            foreach (var candidate in services.ObjectTable.OfType<IBattleNPC>())
            {
                if (!candidate.IsValid() || !candidate.IsTargetable || candidate.IsDead || candidate.CurrentHp == 0 ||
                    candidate.BattleNPCKind != BattleNpcSubKind.Combatant || candidate.FateID != 0 ||
                    candidate.DataID != step.ObjectID || candidate.NameID != step.EnemyNameID ||
                    current.UnreachableCombatActors.Contains(candidate.GameObjectID))
                    continue;
                var candidateDistance = Vector3.DistanceSquared(player.Position, candidate.Position);
                if (candidate.GameObjectID == current.CombatTravelActor || candidateDistance < distance)
                {
                    enemy = candidate;
                    distance = candidateDistance;
                    if (candidate.GameObjectID == current.CombatTravelActor)
                        break;
                }
            }
        }

        if (current.OwnsTravel)
        {
            if (atTerritory && !current.CancellingCombatTravel &&
                (current.CombatTravelActor == 0 && enemy is not null ||
                 current.CombatTravelActor != 0 && enemy?.GameObjectID != current.CombatTravelActor))
            {
                travel.Cancel(id);
                current.CancellingCombatTravel = true;
            }
            var status = travel.Tick(id);
            if (status.State == QuestWorkState.Running)
            {
                current.Phase = QuestExecutionPhase.Travelling;
                enemy = null;
                return null;
            }

            current.OwnsTravel = false;
            var cancelled = current.CancellingCombatTravel;
            current.CancellingCombatTravel = false;
            var actor = current.CombatTravelActor;
            current.CombatTravelActor = 0;
            if (!cancelled)
            {
                if (status.State == QuestWorkState.Succeeded && actor == 0)
                    current.CombatSearchPointUntil = now + 1_500;
                else if (status.State != QuestWorkState.Succeeded)
                {
                    if (!atTerritory || status.Error is not ("navigation_failed" or "navigation_timeout" or "navigation_route_exhausted"))
                        return status.Error ?? "navigation_failed";
                    if (actor != 0)
                    {
                        current.UnreachableCombatActors.Add(actor);
                        enemy = null;
                    }
                    else
                        current.CombatSearchPoint++;
                    LogTransition(current, "combat-route-unreachable", $"actor={actor:X} point={current.CombatSearchPoint} reason={status.Error}");
                    enemy = null;
                    return null;
                }
            }
        }

        if (enemy is not null)
        {
            current.CombatSearchPointUntil = 0;
            current.CombatRespawnUntil = 0;
            if (Vector3.DistanceSquared(player.Position, enemy.Position) <= 20f * 20f)
                return null;
            current.CombatTravelActor = enemy.GameObjectID;
            LogTransition(current, "combat-target-approach", $"actor={enemy.GameObjectID:X} position={enemy.Position}");
            var result = Approach(current, step, enemy.Position, 16f);
            if (!current.OwnsTravel)
                current.CombatTravelActor = 0;
            enemy = null;
            return result;
        }

        if (step.Position is not { } anchor)
            return "target_location_unresolved";
        if (!atTerritory)
            return Approach(current, step, anchor, 10f);

        current.Phase = QuestExecutionPhase.Waiting;
        if (current.CombatSearchPointUntil != 0)
        {
            if (now < current.CombatSearchPointUntil)
                return null;
            current.CombatSearchPointUntil = 0;
            current.CombatSearchPoint++;
        }
        if (current.CombatSearchPoint >= CombatSearchOffsets.Length)
        {
            if (current.CombatRespawnUntil == 0)
            {
                current.CombatRespawnUntil = now + 8_000;
                LogTransition(current, "combat-search-wait", $"pass={current.CombatSearchPass + 1}");
            }
            if (now < current.CombatRespawnUntil)
                return null;
            if (++current.CombatSearchPass >= 2)
                return "quest_combat_search_exhausted";
            current.CombatSearchPoint = 0;
            current.CombatRespawnUntil = 0;
            current.UnreachableCombatActors.Clear();
        }

        var offset = CombatSearchOffsets[current.CombatSearchPoint] * step.EnemySearchRadius;
        var point = anchor + new Vector3(offset.X, 0f, offset.Y);
        LogTransition(current, "combat-search-point", $"pass={current.CombatSearchPass + 1} point={current.CombatSearchPoint + 1} position={point}");
        var error = Approach(current, step, point, 10f);
        if (!current.OwnsTravel && error is null)
            current.CombatSearchPointUntil = now + 1_500;
        return error;
    }

    private static readonly Vector2[] CombatSearchOffsets =
        [Vector2.Zero, new(0.8f, 0f), new(0f, 0.8f), new(-0.8f, 0f), new(0f, -0.8f)];

    private string? Approach(Session current, QuestStep step, Vector3 position, float radius)
    {
        var services = DService.Instance();
        var territory = step.TerritoryID == 0 ? services.ClientState.TerritoryType : step.TerritoryID;
        if (current.OwnsTravel)
        {
            var status = travel.Tick(current.Operation.OperationID);
            LogTransition(current, $"travel-{status.State}", status.Error);
            if (status.State == QuestWorkState.Failed)
                return status.Error ?? "navigation_failed";
            if (status.State == QuestWorkState.Running)
            {
                current.Phase = QuestExecutionPhase.Travelling;
                return null;
            }
            if (status.State != QuestWorkState.Succeeded)
                return "navigation_unconfirmed";
            current.OwnsTravel = false;
            current.StepStartedAt = Environment.TickCount64;
        }
        var localPlayer = services.ObjectTable.LocalPlayer;
        if (localPlayer is null)
            return "character_not_ready";
        if (services.ClientState.TerritoryType == territory && Vector3.DistanceSquared(localPlayer.Position, position) <= radius * radius)
            return null;
        if (!travel.TryStart(current.Operation.OperationID, new(territory, position, radius, step.AllowFlight)
            {
                AllowEurekaEntry = current.Request.AllowEurekaEntry
            }, out var error))
            return error ?? "navigation_unavailable";
        current.OwnsTravel = true;
        LogTransition(current, "travel-start", $"territory={territory} destination={position} radius={radius} allowFlight={step.AllowFlight}");
        current.StepStartedAt = Environment.TickCount64;
        current.Phase = QuestExecutionPhase.Travelling;
        return null;
    }

    private static unsafe IGameObject? FindStepObject(QuestTarget target, QuestStep step, out bool ambiguity)
    {
        ambiguity = false;
        if (step.ObjectID == 0)
            return null;
        IGameObject? found = null;
        if (step.TerritoryID != 0 && DService.Instance().ClientState.TerritoryType != step.TerritoryID)
            return null;
        var framework = EventFramework.Instance();
        var handler = framework == null ? null : framework->GetEventHandlerById(target.ID);
        foreach (var candidate in DService.Instance().ObjectTable)
        {
            if (!candidate.IsValid() || candidate.DataID != step.ObjectID || !candidate.IsTargetable)
                continue;
            if (step.Kind == QuestStepKind.Combat && (candidate is not IBattleNPC { IsDead: false, CurrentHp: > 0 } enemy ||
                    step.EnemyNameID != 0 && enemy.NameID != step.EnemyNameID))
                continue;
            var radius = step.Kind == QuestStepKind.Combat ? step.EnemySearchRadius : 20f;
            if (step.Position is { } anchor && Vector3.DistanceSquared(candidate.Position, anchor) > radius * radius)
                continue;
            if (step.Kind != QuestStepKind.Combat && handler != null && handler->EventObjects.Count != 0)
            {
                var bound = false;
                foreach (var entry in handler->EventObjects)
                    if (entry.Value == candidate.ToStruct()) { bound = true; break; }
                if (!bound)
                    continue;
            }
            if (found is not null)
            {
                if (step.Kind == QuestStepKind.Combat && DService.Instance().ObjectTable.LocalPlayer is { } player)
                {
                    if (Vector3.DistanceSquared(player.Position, candidate.Position) < Vector3.DistanceSquared(player.Position, found.Position))
                        found = candidate;
                    continue;
                }
                ambiguity = true;
                return null;
            }
            found = candidate;
        }
        return found;
    }

    private static unsafe bool IsOwnedQuestEvent(Session current)
    {
        if (current.OwnedEventID == 0 || current.OwnedActorID == 0 || !DService.Instance().ClientState.IsLoggedIn ||
            current.Operation.CharacterContentID != DalamudServices.PlayerState.ContentId)
            return false;
        var framework = EventFramework.Instance();
        if (framework != null && (DService.Instance().Condition.IsOccupiedInEvent || DService.Instance().Condition.IsWatchingCutscene) &&
                                 ((framework->EventState1.EventId.Id == current.OwnedEventID && framework->EventState1.ObjectId == current.OwnedActorID) ||
                                  (framework->EventState2.EventId.Id == current.OwnedEventID && framework->EventState2.ObjectId == current.OwnedActorID)))
            return true;
        var selector = EventHandlerSelector.Instance();
        return selector != null && selector->OptionsCount != 0 && selector->Target != null &&
               selector->Target->GetGameObjectId() == current.OwnedActorID;
    }

    private static unsafe bool IsRodneyEntranceEvent(ulong actorID)
    {
        var services = DService.Instance();
        var framework = EventFramework.Instance();
        var menu = services.GameGUI.GetAddonByName<AtkUnitBase>("SelectString");
        return framework != null && menu != null && menu->IsVisible &&
               (framework->EventState1.EventId.Id == 721355 && framework->EventState1.ObjectId == actorID ||
                framework->EventState2.EventId.Id == 721355 && framework->EventState2.ObjectId == actorID);
    }

    private static unsafe uint? GetQuestIssuerDialogue(ulong actorID)
    {
        var framework = EventFramework.Instance();
        var menu = DService.Instance().GameGUI.GetAddonByName<AtkUnitBase>("SelectString");
        if (actorID == 0 || framework == null || menu == null || !menu->IsVisible ||
            !DService.Instance().Condition.IsOccupiedInEvent)
            return null;
        var primary = framework->EventState1;
        var secondary = framework->EventState2;
        if (primary.ObjectId == actorID && primary.EventId.ContentId is EventHandlerContent.CustomTalk or EventHandlerContent.DefaultTalk)
            return primary.EventId.Id;
        if (secondary.ObjectId == actorID && secondary.EventId.ContentId is EventHandlerContent.CustomTalk or EventHandlerContent.DefaultTalk)
            return secondary.EventId.Id;
        return null;
    }

    private unsafe string? DriveQuestUI(Session current, QuestTarget target, QuestStep step)
    {
        var selector = EventHandlerSelector.Instance();
        if (selector != null && selector->OptionsCount != 0 && selector->Target != null &&
            selector->Target->GetGameObjectId() == current.OwnedActorID)
        {
            for (var index = 0; index < selector->OptionsCount && index < selector->Options.Length; index++)
                if (selector->Options[index].Handler != null && selector->Options[index].Handler->Info.EventId.Id == target.ID)
                {
                    EventFramework.Instance()->InteractWithHandlerFromSelector(index);
                    return null;
                }
            if (current.Request.AllowEurekaEntry && step.ObjectID == 1024517)
                return "eureka_quest_unavailable";
            if (step.Kind != QuestStepKind.AcceptQuest)
                return "quest_event_selector_unresolved";
            current.RelatedID = target.ID;
            return "quest_accept_not_offered";
        }
        var services = DService.Instance();
        var accept = services.GameGUI.GetAddonByName<AddonJournalAccept>("JournalAccept");
        if (accept != null && accept->IsVisible)
        {
            if (step.Kind != QuestStepKind.AcceptQuest)
                return "unexpected_quest_accept";
            if (current.AcceptSent)
                return null;
            if (data.CheckAcceptance(target) is { } blocker)
            {
                current.RelatedID = blocker.RelatedID;
                return blocker.Code;
            }
            current.AcceptSent = ClickQuestButton((AtkUnitBase*)accept, accept->AcceptButton);
            return current.AcceptSent ? null : "quest_accept_unavailable";
        }
        var result = services.GameGUI.GetAddonByName<AddonJournalResult>("JournalResult");
        if (result != null && result->IsVisible)
        {
            if (step.Kind != QuestStepKind.CompleteQuest)
                return "unexpected_quest_reward";
            return DriveQuestReward(current, result, target);
        }
        if (TryConfirmHandOverHQ(current, out var handOverError))
            return handOverError;
        foreach (var addonName in new[] { "SelectString", "SelectIconString", "SelectYesno" })
        {
            var addon = services.GameGUI.GetAddonByName<AtkUnitBase>(addonName);
            if (addon == null || !addon->IsVisible)
                continue;
            if (step.ChoiceIndex is not { } index)
                return "dialogue_choice_unresolved";
            addon->FireCallbackInt(index);
            return null;
        }
        var request = services.GameGUI.GetAddonByName<AddonRequest>("Request");
        if (request != null && request->IsVisible)
            return DriveHandOver(current, request);
        var talk = services.GameGUI.GetAddonByName<AtkUnitBase>("Talk");
        if (talk != null && talk->IsVisible)
            return AddonTalkEvent.ClickNext() ? null : "dialogue_advance_unavailable";
        return null;
    }

    private static unsafe bool ClickQuestButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (button == null || !button->IsEnabled || button->AtkComponentBase.OwnerNode == null)
            return false;
        var node = button->AtkComponentBase.OwnerNode;
        var registered = node->AtkResNode.AtkEventManager.Event;
        if (registered == null)
            return false;
        addon->ClickComponent(node, (int)registered->Param, registered->State.EventType);
        return true;
    }

    private static unsafe bool ExecuteEmote(uint id, ulong targetID)
    {
        var manager = EmoteManager.Instance();
        if (id > ushort.MaxValue || manager == null || !manager->CanExecuteEmote((ushort)id))
            return false;
        var option = new EmoteController.PlayEmoteOption { TargetId = targetID };
        return manager->ExecuteEmote((ushort)id, &option);
    }

    private static bool SendQuestSpeech(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 400 || text.Contains('\n') || text.Contains('\r'))
            return false;
        ChatManager.Instance().SendMessage("/say " + text);
        return true;
    }

    private void StopOwned(Session current)
    {
        if (current.CleanupRequested)
            return;
        current.CleanupRequested = true;
        RequestOwnedStops(current);
    }

    private bool RequestOwnedStops(Session current)
    {
        Action[] stops =
        [
            () => { if (current.OwnsTravel) travel.Cancel(current.Operation.OperationID); },
            () => { if (current.OwnsCombat) combat.Cancel(current.Operation.OperationID); },
            () => StopCraft(current),
            () => StopSupplyShop(current),
            () => StopGathering(current),
            () => CancelOwnedInteraction(current)
        ];
        var stopped = true;
        foreach (var stop in stops)
            try
            {
                stop();
            }
            catch (Exception exception)
            {
                stopped = false;
                ReportCleanupError(current, exception);
            }
        return stopped;
    }

    private static unsafe void CancelOwnedInteraction(Session current)
    {
        if (!DService.Instance().ClientState.IsLoggedIn || current.Operation.CharacterContentID != DalamudServices.PlayerState.ContentId)
            return;
        CloseOwnedSelector(current.OwnedActorID);
        if (!IsOwnedQuestEvent(current))
            return;
        var framework = EventFramework.Instance();
        var handler = framework == null ? null : framework->GetEventHandlerById(current.OwnedEventID);
        if (handler != null)
            handler->CancelInteraction();
    }

    private static unsafe void CloseOwnedSelector(ulong actorID)
    {
        var selector = EventHandlerSelector.Instance();
        if (actorID == 0 || selector == null || selector->Target == null ||
            selector->Target->GetGameObjectId() != actorID || selector->AddonId > ushort.MaxValue)
            return;
        var manager = RaptureAtkUnitManager.Instance();
        var addon = manager == null ? null : manager->GetAddonById((ushort)selector->AddonId);
        if (addon != null && addon->IsVisible)
            addon->Close(true);
    }

    private bool DrainOwned(Session current)
    {
        if (!RequestOwnedStops(current))
            return false;
        try
        {
            var id = current.Operation.OperationID;
            if (current.OwnsTravel && travel.Tick(id).State != QuestWorkState.Running)
                current.OwnsTravel = false;
            if (current.OwnsCombat && combat.Tick(id).State != QuestWorkState.Running)
                current.OwnsCombat = false;
            if (current.OwnsTravel || current.OwnsCombat || !DrainPlugins(current) || !DrainGathering(current) || IsOwnedQuestEvent(current))
                return false;
            current.OwnedEventID = 0;
            current.OwnedActorID = 0;
            current.CleanupRequested = false;
            current.CleanupErrorReported = false;
            return true;
        }
        catch (Exception exception)
        {
            ReportCleanupError(current, exception);
            return false;
        }
    }

    private static void ReportCleanupError(Session current, Exception exception)
    {
        if (current.CleanupErrorReported)
            return;
        current.CleanupErrorReported = true;
        DalamudServices.PluginLog.Warning(exception, "AutoQuest is waiting for owned work to stop.");
    }

    private async Task DrainAsync(Session current)
    {
        await DalamudServices.Framework.RunOnFrameworkThread(() =>
        {
            current.Phase = QuestExecutionPhase.Stopping;
            StopOwned(current);
        });
        while (!await DalamudServices.Framework.RunOnFrameworkThread(() => DrainOwned(current)))
            await Task.Delay(100);
    }
}
