using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
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
        if (DService.Instance().Condition.IsBetweenAreas)
        {
            current.Phase = QuestExecutionPhase.Waiting;
            return null;
        }
        if (IsOwnedQuestEvent(current) && DService.Instance().Condition.IsWatchingCutscene)
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

        if (IsOwnedQuestEvent(current))
        {
            current.Phase = QuestExecutionPhase.Interacting;
            return DriveQuestUI(current, target, step);
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

        var gameObject = FindStepObject(target, step, out var ambiguity);
        if (ambiguity)
            return "target_instance_unresolved";
        if (step.Kind == QuestStepKind.Combat)
        {
            if (!current.OwnsCombat)
            {
                if (gameObject is null)
                {
                    if (step.Position is not { } combatAnchor)
                        return "target_location_unresolved";
                    var anchorError = Approach(current, step, combatAnchor, 10);
                    return anchorError ?? (current.OwnsTravel ? null : "target_not_found");
                }
                var navigationError = Approach(current, step, gameObject.Position, 20);
                if (navigationError is not null || current.OwnsTravel)
                    return navigationError;
                TargetManager.Target = gameObject;
                if (!combat.TryStart(current.Operation.OperationID, out var error))
                    return error ?? "combat_unavailable";
                current.OwnsCombat = true;
                current.ActionSent = true;
            }
            current.Phase = QuestExecutionPhase.Fighting;
            var status = combat.Tick(current.Operation.OperationID);
            if (status.State == QuestWorkState.Failed)
                return status.Error ?? "combat_failed";
            if (Environment.TickCount64 - current.StepStartedAt > 180_000)
                return "combat_objective_unconfirmed";
            if (status.State == QuestWorkState.Succeeded)
            {
                current.OwnsCombat = false;
                current.ActionSent = false;
                return null;
            }
            return null;
        }

        if (current.ActionSent)
            return null;
        if (DService.Instance().Condition.IsOccupiedInEvent)
            return "external_interaction_busy";
        var position = gameObject?.Position ?? step.Position;
        if (position is null)
            return "target_location_unresolved";
        var approach = Approach(current, step, position.Value, step.Kind == QuestStepKind.WalkTo ? 1 : 3);
        if (approach is not null || current.OwnsTravel)
            return approach;
        if (step.Kind == QuestStepKind.WalkTo)
        {
            current.ActionSent = true;
            return null;
        }
        if (step.ObjectID != 0 && gameObject is null)
            return "target_not_found";
        if (gameObject is not null)
            TargetManager.Target = gameObject;
        current.Phase = QuestExecutionPhase.Interacting;
        if (step.Kind is QuestStepKind.AcceptQuest or QuestStepKind.CompleteQuest or QuestStepKind.Interact)
        {
            if (gameObject is null)
                return "target_not_found";
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
            QuestStepKind.Emote => ExecuteEmote(step.Argument),
            QuestStepKind.Say => SendQuestSpeech(step.Text),
            _ => false
        };
        if (!sent)
            return "quest_action_failed";
        current.ActionSent = true;
        return null;
    }

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
        if (!travel.TryStart(current.Operation.OperationID, new(territory, position, radius, step.AllowFlight), out var error))
            return error ?? "navigation_unavailable";
        current.OwnsTravel = true;
        LogTransition(current, "travel-start", $"territory={territory} destination={position} radius={radius}");
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
            if (step.Kind == QuestStepKind.Combat && candidate is not IBattleNPC { IsDead: false, CurrentHp: > 0 })
                continue;
            if (step.Position is { } anchor && Vector3.DistanceSquared(candidate.Position, anchor) > 400)
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
        return selector != null && selector->Target != null && selector->Target->GetGameObjectId() == current.OwnedActorID;
    }

    private unsafe string? DriveQuestUI(Session current, QuestTarget target, QuestStep step)
    {
        var selector = EventHandlerSelector.Instance();
        if (selector != null && selector->Target != null)
        {
            for (var index = 0; index < selector->OptionsCount && index < selector->Options.Length; index++)
                if (selector->Options[index].Handler != null && selector->Options[index].Handler->Info.EventId.Id == target.ID)
                {
                    EventFramework.Instance()->InteractWithHandlerFromSelector(index);
                    return null;
                }
            return "quest_event_selector_unresolved";
        }
        var services = DService.Instance();
        var accept = services.GameGUI.GetAddonByName<AddonJournalAccept>("JournalAccept");
        if (accept != null && accept->IsVisible)
        {
            if (step.Kind != QuestStepKind.AcceptQuest)
                return "unexpected_quest_accept";
            if (current.AcceptSent)
                return null;
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

    private static unsafe bool ExecuteEmote(uint id)
    {
        var manager = FFXIVClientStructs.FFXIV.Client.Game.Control.EmoteManager.Instance();
        return id <= ushort.MaxValue && manager != null && manager->CanExecuteEmote((ushort)id) && manager->ExecuteEmote((ushort)id);
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
