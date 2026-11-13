using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Dalamud;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Quests;
using InstanceContent = Lumina.Excel.Sheets.InstanceContent;
using NativeGatheringPoint = FFXIVClientStructs.FFXIV.Client.Game.Object.GatheringPointObject;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    private sealed partial class QuestData
    {
        public QuestTarget? Next(QuestTarget target, HashSet<uint> blockedPrerequisites, out QuestBlocker[] blockers)
        {
            var path = new HashSet<QuestTarget>();
            if (!TryPlan(target.Normalize(), path, out var plan, out blockers, blockedPrerequisites: blockedPrerequisites)) return null;
            return plan.FirstOrDefault();
        }

        public bool TryGetStep(QuestTarget target, out QuestStep? step, out QuestBlocker[] blockers)
        {
            target = target.Normalize();
            step = null;
            blockers = [];
            if (target.Type != QuestTargetType.Quest)
            {
                blockers = [new("special_target_unimplemented", target.ID)];
                return false;
            }
            if (!TryDefinition(target.ID, out var definition))
            {
                blockers = [new("quest_row_unavailable", target.ID)];
                return false;
            }
            var state = ReadState(target.ID);
            if (IsSatisfied(target)) return false;
            if (!state.Accepted && pendingTurnIns.ContainsKey(target.ID))
            {
                blockers = [new("quest_repeat_completion_unconfirmed", target.ID)];
                return false;
            }
            if (!state.Accepted)
            {
                blockers = Conditions(definition);
                if (blockers.Length != 0) return false;
                if (TryEligibleJob(definition, out var classJobID, out var gearsetID) && LocalPlayerState.ClassJob != classJobID)
                {
                    step = new(QuestStepKind.SwitchJob, 0, 0, null, gearsetID, state.Sequence, ReadStateKey(target), new(), ClassJobID: classJobID);
                    return true;
                }
                if (definition.Issuer is not { } issuer || definition.IssuerID == 0)
                {
                    blockers = [new("quest_issuer_unresolved", target.ID)];
                    return false;
                }
                step = MakeStep(QuestStepKind.AcceptQuest, definition.IssuerID, issuer, state, target);
                return true;
            }
            if (state.Sequence == 255)
            {
                var locations = definition.Objectives.Where(static objective => objective.CompleteSequence == 255)
                    .SelectMany(static objective => objective.Locations).ToArray();
                var destination = ResolveLocation(definition, definition.TurnInID, locations, out var locationError);
                if (destination is null)
                {
                    blockers = [new(locationError ?? "quest_turnin_unresolved", target.ID)];
                    return false;
                }
                var completionScript = GetScript(definition, out var completionError);
                if (completionScript is null || completionError is not null)
                {
                    blockers = [new(completionError ?? "quest_script_unavailable", target.ID)];
                    return false;
                }
                if (!TryInteraction(completionScript, definition, state, target, definition.TurnInID, destination, null, out step, out blockers))
                    return false;
                if (step is { Kind: QuestStepKind.Interact }) step = step with { Kind = QuestStepKind.CompleteQuest };
                return step is not null;
            }
            var script = GetScript(definition, out var scriptError);
            if (script is null || scriptError is not null)
            {
                blockers = [new(scriptError ?? "quest_script_unavailable", target.ID)];
                return false;
            }
            var objectives = definition.Objectives.Where(objective => objective.CompleteSequence == state.Sequence)
                .Where(objective => !TryReadTodo(target.ID, objective.Index, out var completed) || !completed).ToArray();
            if (target.ID == 66558 && state.Sequence == 5 && objectives.Length == 1)
            {
                var objective = objectives[0];
                if (!definition.Symbols.TryGetValue("ITEM0", out var itemID) ||
                    !definition.Symbols.TryGetValue("EOBJECT0", out var objectID) ||
                    !definition.Listeners.Any(listener => listener.Sequence == state.Sequence &&
                        listener.TodoIndex == objective.Index && listener.EventType == 8 && listener.ObjectID == objectID) ||
                    !LuminaGetter.TryGetRow<EventItem>(itemID, out var item) || item.Quest.RowId != target.ID ||
                    !item.Action.IsValid || !item.Action.Value.TargetArea)
                {
                    blockers = [new("quest_action_binding_unresolved", target.ID)];
                    return false;
                }
                var location = ResolveLocation(definition, objectID, objective.Locations, out var locationError);
                if (location is null)
                {
                    blockers = [new(locationError ?? "target_location_unresolved", target.ID)];
                    return false;
                }
                step = MakeStep(QuestStepKind.UseItem, 0, location, state, target, objective.Index) with { Argument = itemID };
                return true;
            }
            if (target.ID == 66558 && objectives.Length == 1 && state.Sequence is 1 or 2 or 3 or 4 or 6)
            {
                var objective = objectives[0];
                var listeners = definition.Listeners.Where(listener => listener.Sequence == state.Sequence &&
                    listener.TodoIndex == objective.Index && listener.EventType == 1 &&
                    listener.ConditionType == 0 && listener.ConditionValue == 0 && listener.ConditionOperator == 3 &&
                    listener.Variable0 == 255 && listener.Variable1 == 255).ToArray();
                if (listeners.Length != 1)
                {
                    blockers = [new("objective_listener_ambiguous", target.ID)];
                    return false;
                }
                var objectID = listeners[0].ObjectID;
                var location = ResolveLocation(definition, objectID, objective.Locations, out var locationError);
                if (location is null)
                {
                    blockers = [new(locationError ?? "target_location_unresolved", target.ID)];
                    return false;
                }
                return TryInteraction(script, definition, state, target, objectID, location, objective.Index, out step, out blockers);
            }
            if (TryListenerStep(definition, state, target, objectives, false, out step, out blockers))
                return step is not null;
            var candidates = new HashSet<uint>();
            foreach (var objective in objectives)
                foreach (var location in objective.Locations)
                    if (location.ObjectID != 0) candidates.Add(location.ObjectID);
            foreach (var symbol in definition.Symbols)
                if (symbol.Key.StartsWith("ACTOR", StringComparison.Ordinal) || symbol.Key.StartsWith("EOBJECT", StringComparison.Ordinal))
                    if (symbol.Value is >= 1_000_000 and < 3_000_000) candidates.Add(symbol.Value);
            if (TryItemAction(script, definition, state, target, objectives, candidates, out step, out blockers))
                return step is not null;
            if (TryListenerStep(definition, state, target, objectives, true, out step, out blockers))
                return step is not null;
            if (objectives.Length == 1 && TryEventRange(definition, state.Sequence, objectives[0], out var ambush) &&
                definition.Listeners.Any(listener => listener.Sequence == state.Sequence && listener.EventType == 5))
            {
                var enemies = definition.Listeners.Where(listener => listener.Sequence == state.Sequence &&
                        listener.EventType == 5 && listener.ConditionType == 3 && IsUnconditionalListener(listener) &&
                        LuminaGetter.TryGetRow<Level>(listener.ConditionValue, out var level) &&
                        level.Type == 9 && level.Object.RowId == listener.ObjectID && level.Territory.RowId == ambush.TerritoryID)
                    .Select(listener => LuminaGetter.TryGetRow<Level>(listener.ConditionValue, out var level) ? Location(level) : null)
                    .OfType<QuestLocation>()
                    .ToArray();
                if (enemies.Select(static location => location.ObjectID).Distinct().ToArray() is [var enemyID])
                {
                    if (TryCombatJob(state, target, definition.PrimaryLevel, out step, out blockers)) return step is not null;
                    step = MakeStep(QuestStepKind.Combat, enemyID, ambush, state, target, objectives[0].Index) with
                    {
                        ActivateCombatByProximity = true
                    };
                    return true;
                }
            }
            if (definition.Listeners.Any(listener => listener.Sequence == state.Sequence && listener.EventType == 8 &&
                    listener.ConditionType == 1 && listener.TodoIndex < 24 && objectives.Any(objective => objective.Index == listener.TodoIndex)))
            {
                blockers = [new("quest_capture_item_unresolved", target.ID)];
                return false;
            }
            foreach (var objective in objectives)
            {
                var drops = definition.Listeners.Where(listener => listener.Sequence == state.Sequence &&
                    listener.EventType == 9 && listener.ConditionType == 1 && IsUnconditionalListener(listener) &&
                    listener.TodoIndex == objective.Index).ToArray();
                if (drops is not [var drop] || objective.Locations.Distinct().ToArray() is not [var ground] ||
                    !LuminaGetter.TryGetRow<BNpcBase>(drop.ObjectID, out _) ||
                    !LuminaGetter.TryGetRow<BNpcName>(drop.ConditionValue, out _)) continue;
                if (TryCombatJob(state, target, definition.PrimaryLevel, out step, out blockers)) return step is not null;
                step = MakeStep(QuestStepKind.Combat, drop.ObjectID, ground, state, target, objective.Index) with
                {
                    EnemyNameID = drop.ConditionValue,
                    EnemySearchRadius = 50f
                };
                return true;
            }
            if (TryObjectiveListenerInteraction(script, definition, state, target, objectives, out step, out blockers))
                return step is not null;
            var accepted = new List<uint>();
            string? conditionError = null;
            foreach (var candidate in candidates.Order())
            {
                if (!TryScriptCondition(script, definition, state, "IsAcceptEvent", candidate, out var canInteract, out var error))
                {
                    conditionError ??= error;
                    continue;
                }
                if (canInteract) accepted.Add(candidate);
            }
            if (accepted.Count > 1 && objectives.Length == 1 &&
                TrySelectQuestItemRecipient(script, definition, state, objectives[0], accepted, out var recipient))
                accepted = [recipient];
            foreach (var objective in objectives)
            {
                var matched = objective.Locations.Where(location => accepted.Contains(location.ObjectID)).ToArray();
                if (matched.Length != 1) continue;
                var location = matched[0];
                var resolved = ResolveLocation(definition, location.ObjectID, [location], out var locationError);
                if (resolved is null)
                {
                    blockers = [new(locationError ?? "target_location_unresolved", target.ID)];
                    return false;
                }
                return TryInteraction(script, definition, state, target, location.ObjectID, resolved, objective.Index, out step, out blockers);
            }
            if (accepted.Count == 1)
            {
                var objectID = accepted[0];
                var locations = objectives.SelectMany(static objective => objective.Locations).ToArray();
                var location = ResolveLocation(definition, objectID, locations, out var locationError);
                if (location is null)
                {
                    blockers = [new(locationError ?? "target_location_unresolved", target.ID)];
                    return false;
                }
                if (objectID == definition.IssuerID && locations.Length != 0 &&
                    locations.All(mark => mark.ObjectID != objectID &&
                        (mark.TerritoryID != location.TerritoryID || Vector3.DistanceSquared(mark.Position, location.Position) > 225)))
                {
                    blockers = [new("objective_action_unresolved", target.ID)];
                    return false;
                }
                return TryInteraction(script, definition, state, target, objectID, location,
                    objectives.Length == 1 ? objectives[0].Index : null, out step, out blockers);
            }
            if (accepted.Count == 0 && objectives.Length == 1 && TryEventRange(definition, state.Sequence, objectives[0], out var range))
            {
                step = MakeStep(QuestStepKind.WalkTo, 0, range, state, target, objectives[0].Index) with { AllowFlight = false };
                return true;
            }
            var unresolved = definition.Symbols.Keys.Any(static symbol => symbol.StartsWith("INSTANCEDUNGEON", StringComparison.Ordinal) ||
                symbol.StartsWith("QUESTBATTLE", StringComparison.Ordinal)) ? "quest_duty_binding_unresolved" : "objective_mechanism_unresolved";
            blockers = [new(conditionError ?? (accepted.Count > 1 ? "objective_target_ambiguous" : unresolved), target.ID)];
            return false;
        }

        private bool TrySelectQuestItemRecipient(QuestScript script, QuestDefinition definition, QuestState state,
            QuestObjective objective, List<uint> candidates, out uint recipient)
        {
            recipient = 0;
            var method = script.Contains("GetNpcTradeItemInfo") ? "GetNpcTradeItemInfo" :
                script.Contains("getNpcTradeItemInfo") ? "getNpcTradeItemInfo" : null;
            if (method is null || script.ParameterCount(method) != 4) return false;
            foreach (var objectID in candidates)
            {
                if (!definition.Listeners.Any(listener => listener.Sequence == state.Sequence && listener.EventType == 1 &&
                        listener.TodoIndex == objective.Index && listener.ObjectID == objectID)) continue;
                if (!script.TryEvaluate(method, definition.Symbols,
                        (getter, args) => ReadGetter(getter, args, definition, state, objectID),
                        [new LuaSelf(), 1d, (double)state.Sequence, (double)objectID], out var values, out _)) return false;
                if (values.Length == 0 || values[0] is null) continue;
                if (values[0] is not LuaTable trade || trade.Count != 14 || trade.Get(3d) is not false ||
                    trade.Values().Skip(3).Any(static value => value is not 0d && value is not false)) return false;
                uint itemID;
                uint quantity;
                try
                {
                    itemID = QuestScript.Unsigned(trade.Get(1d));
                    quantity = QuestScript.Unsigned(trade.Get(2d));
                }
                catch (LuaEvaluationException)
                {
                    return false;
                }
                if (quantity is 0 or > int.MaxValue || !LuminaGetter.TryGetRow<EventItem>(itemID, out var item) ||
                    item.Quest.RowId != definition.ID || CountItem(itemID, false) < quantity) continue;
                if (recipient != 0) return false;
                recipient = objectID;
            }
            return recipient != 0;
        }

        private unsafe bool TryListenerStep(QuestDefinition definition, QuestState state, QuestTarget target, QuestObjective[] objectives,
            bool combatOnly, out QuestStep? step, out QuestBlocker[] blockers)
        {
            step = null;
            blockers = [];
            foreach (var objective in objectives)
            {
                var listeners = definition.Listeners.Where(listener => listener.Sequence == state.Sequence &&
                    (combatOnly ? listener.EventType == 5 : listener.EventType is 2 or 18 or 23) &&
                    (listener.TodoIndex == objective.Index || listener.TodoIndex == 255 && objectives.Length == 1 &&
                        objective.Locations.Any(location => location.ObjectID == listener.ObjectID ||
                        listener.ConditionType == 3 && location.LevelID == listener.ConditionValue))).ToArray();
                if (listeners.Length == 0) continue;
                if (listeners.Length != 1)
                {
                    blockers = [new("objective_listener_ambiguous", target.ID)];
                    return true;
                }
                var listener = listeners[0];
                if (listener.EventType == 18)
                {
                    blockers = [new("quest_action_binding_unresolved", target.ID)];
                    return true;
                }
                if (listener.Variable0 != 255 || listener.Variable1 != 255 || listener.ConditionOperator != 3)
                {
                    blockers = [new("quest_listener_condition_unresolved", target.ID)];
                    return true;
                }
                QuestLocation? location = null;
                string? locationError = null;
                if (listener.ConditionType == 3 && LuminaGetter.TryGetRow<Level>(listener.ConditionValue, out var level) && level.Object.RowId == listener.ObjectID)
                    location = Location(level);
                else if (listener.ConditionType == 0 && listener.ConditionValue == 0)
                    location = ResolveLocation(definition, listener.ObjectID, objective.Locations, out locationError);
                if (location is null)
                {
                    blockers = [new(locationError ?? "quest_listener_location_unresolved", target.ID)];
                    return true;
                }
                if (listener.EventType == 5)
                {
                    if (location.ObjectType != 9 || !LuminaGetter.TryGetRow<BNpcBase>(listener.ObjectID, out _))
                    {
                        blockers = [new("quest_combat_target_unresolved", target.ID)];
                        return true;
                    }
                    var enemyLevels = DService.Instance().ObjectTable.OfType<IBattleNPC>().Where(enemy => enemy.IsValid() &&
                            enemy.DataID == listener.ObjectID && Vector3.DistanceSquared(enemy.Position, location.Position) <= 400)
                        .Select(static enemy => (ushort)enemy.Level).ToArray();
                    var combatLevel = enemyLevels.Length != 0 ? enemyLevels.Max() : definition.PrimaryLevel;
                    if (combatLevel == 0)
                    {
                        blockers = [new("quest_combat_level_unresolved", target.ID)];
                        return true;
                    }
                    if (TryCombatJob(state, target, combatLevel, out step, out blockers)) return true;
                    step = MakeStep(QuestStepKind.Combat, listener.ObjectID, location, state, target, objective.Index);
                    return true;
                }
                var tokens = QuotedText(TodoText(definition, objective.Index));
                if (listener.EventType == 2)
                {
                    var emotes = tokens.Length == 1
                        ? LuminaGetter.Get<Emote>().Where(emote => emote.RowId != 0 && emote.RowId <= ushort.MaxValue &&
                            string.Equals(emote.Name.ToString(), tokens[0], StringComparison.Ordinal)).Take(2).ToArray()
                        : [];
                    if (emotes.Length != 1)
                    {
                        blockers = [new("quest_emote_binding_unresolved", target.ID)];
                        return true;
                    }
                    step = MakeStep(QuestStepKind.Emote, listener.ObjectID, location, state, target, objective.Index) with { Argument = emotes[0].RowId };
                    return true;
                }
                if (listener.EventType == 23)
                {
                    if (tokens.Length != 1 || tokens[0].Length == 0 || tokens[0].Length > 400 ||
                        tokens[0].StartsWith('/') || tokens[0].Any(char.IsControl))
                    {
                        blockers = [new("quest_speech_text_unresolved", target.ID)];
                        return true;
                    }
                    step = MakeStep(QuestStepKind.Say, listener.ObjectID, location, state, target, objective.Index) with { Text = tokens[0] };
                    return true;
                }
            }
            return false;
        }

        private unsafe bool TryCombatJob(QuestState state, QuestTarget target, ushort level, out QuestStep? step, out QuestBlocker[] blockers)
        {
            step = null;
            blockers = [];
            var currentJob = Jobs().FirstOrDefault(job => job.RowId == LocalPlayerState.ClassJob);
            if (currentJob.Role is 1 or 2 or 3 or 4 && !currentJob.IsLimitedJob &&
                JobMatches(currentJob, 0, level, 0)) return false;
            if (RaptureGearsetModule.Instance() != null)
                foreach (var job in Jobs().OrderBy(job => job.RowId == state.AcceptClassJob ? 0 : 1).ThenBy(static job => job.RowId))
                    if (job.Role is 1 or 2 or 3 or 4 && !job.IsLimitedJob && JobMatches(job, 0, level, 0) &&
                        LocalPlayerState.TryFindClassJobGearset(job.RowId, out var gearsetID))
                    {
                        step = new(QuestStepKind.SwitchJob, 0, 0, null, gearsetID, state.Sequence, ReadStateKey(target), new(), ClassJobID: job.RowId);
                        return true;
                    }
            blockers = [new("quest_combat_level_or_gearset_required", level)];
            return true;
        }

        private static string[] QuotedText(string text)
        {
            var values = new List<string>();
            foreach (var pair in new[] { ('“', '”'), ('「', '」'), ('『', '』'), ('"', '"'), ('«', '»') })
                for (var offset = 0; offset < text.Length;)
                {
                    var begin = text.IndexOf(pair.Item1, offset);
                    if (begin < 0) break;
                    var end = text.IndexOf(pair.Item2, begin + 1);
                    if (end < 0) break;
                    values.Add(text[(begin + 1)..end]);
                    offset = end + 1;
                }
            return values.Distinct(StringComparer.Ordinal).ToArray();
        }

        private bool TryItemAction
        (
            QuestScript script,
            QuestDefinition definition,
            QuestState state,
            QuestTarget target,
            QuestObjective[] objectives,
            HashSet<uint> candidates,
            out QuestStep? step,
            out QuestBlocker[] blockers
        )
        {
            step = null;
            blockers = [];
            if (!script.Contains("IsEventItemUsable")) return false;
            if (script.ParameterCount("IsEventItemUsable") != 4)
            {
                blockers = [new("script_item_signature_unsupported", target.ID)];
                return true;
            }
            var itemIDs = definition.Symbols.Where(static symbol => symbol.Key.StartsWith("ITEM", StringComparison.Ordinal) ||
                    symbol.Key.StartsWith("EVENTITEM", StringComparison.Ordinal)).Select(static symbol => symbol.Value)
                .Where(static id => id != 0).Distinct().Order().ToArray();
            var usable = new List<(uint ItemID, uint ObjectID)>();
            string? conditionError = null;
            foreach (var itemID in itemIDs)
                foreach (var objectID in candidates.Order())
                {
                    if (!script.TryEvaluate("IsEventItemUsable", definition.Symbols,
                            (method, args) => ReadGetter(method, args, definition, state, objectID),
                            [new LuaSelf(), new LuaPlayer(), new LuaActor(objectID), (double)itemID], out var result, out var error))
                    {
                        conditionError ??= error;
                        continue;
                    }
                    if (result.FirstOrDefault() is not bool canUse)
                    {
                        conditionError ??= result.FirstOrDefault() is LuaUnknown unknown ? unknown.Reason : "script_item_result_unresolved";
                        continue;
                    }
                    if (canUse) usable.Add((itemID, objectID));
                }
            foreach (var objective in objectives)
            {
                var matches = usable.Where(candidate => objective.Locations.Any(location => location.ObjectID == candidate.ObjectID)).ToArray();
                if (matches.Length != 1) continue;
                var candidate = matches[0];
                var location = ResolveLocation(definition, candidate.ObjectID, objective.Locations, out var error);
                if (location is null)
                {
                    blockers = [new(error ?? "target_location_unresolved", target.ID)];
                    return true;
                }
                step = MakeStep(QuestStepKind.UseItem, candidate.ObjectID, location, state, target, objective.Index) with { Argument = candidate.ItemID };
                return true;
            }
            if (usable.Count == 1)
            {
                var candidate = usable[0];
                var location = ResolveLocation(definition, candidate.ObjectID, objectives.SelectMany(static objective => objective.Locations).ToArray(), out var error);
                if (location is null)
                {
                    blockers = [new(error ?? "target_location_unresolved", target.ID)];
                    return true;
                }
                step = MakeStep(QuestStepKind.UseItem, candidate.ObjectID, location, state, target,
                    objectives.Length == 1 ? objectives[0].Index : null) with { Argument = candidate.ItemID };
                return true;
            }
            if (usable.Count > 1 || conditionError is not null)
            {
                blockers = [new(conditionError ?? "objective_item_target_ambiguous", target.ID)];
                return true;
            }
            return false;
        }

        private unsafe bool TryInteraction
        (
            QuestScript script,
            QuestDefinition definition,
            QuestState state,
            QuestTarget target,
            uint objectID,
            QuestLocation location,
            int? todoIndex,
            out QuestStep? step,
            out QuestBlocker[] blockers
        )
        {
            step = null;
            blockers = [];
            var tradeMethod = script.Contains("GetNpcTradeItemInfo") ? "GetNpcTradeItemInfo" :
                script.Contains("getNpcTradeItemInfo") ? "getNpcTradeItemInfo" : null;
            if (tradeMethod is not null)
            {
                if (script.ParameterCount(tradeMethod) != 4)
                {
                    blockers = [new("script_trade_signature_unsupported", target.ID)];
                    return false;
                }
                for (var index = 1; index <= 8; index++)
                {
                    if (!script.TryEvaluate(tradeMethod, definition.Symbols,
                            (method, args) => ReadGetter(method, args, definition, state, objectID),
                            [new LuaSelf(), (double)index, (double)state.Sequence, (double)objectID], out var values, out var error))
                    {
                        blockers = [new(error ?? "script_trade_requirement_unresolved", target.ID)];
                        return false;
                    }
                    if (values.Length == 0 || values[0] is null) continue;
                    if (values[0] is not LuaTable trade || trade.Count != 14 || trade.Get(3d) is not bool requireHQ)
                    {
                        blockers = [new("script_trade_requirement_unsupported", target.ID)];
                        return false;
                    }
                    var requirements = trade.Values();
                    if (requirements.Skip(3).Any(static value => value is not 0d && value is not false))
                    {
                        blockers = [new("script_trade_additional_requirement_unsupported", target.ID)];
                        return false;
                    }
                    uint itemID;
                    uint quantity;
                    try
                    {
                        itemID = QuestScript.Unsigned(requirements[0]);
                        quantity = QuestScript.Unsigned(requirements[1]);
                    }
                    catch (LuaEvaluationException)
                    {
                        blockers = [new("script_trade_requirement_unresolved", target.ID)];
                        return false;
                    }
                    if (itemID == 0 || quantity is 0 or > int.MaxValue)
                    {
                        blockers = [new("script_trade_requirement_unresolved", target.ID)];
                        return false;
                    }
                    if (CountItem(itemID, requireHQ) >= quantity) continue;
                    if (TryGatherSupply(definition, state, target, itemID, (int)quantity, requireHQ, out step, out blockers))
                        return step is not null;
                    if (!TryPrepareCraft(itemID, (int)quantity, requireHQ, [], out var craftPlan, out error, out var missing))
                    {
                        blockers = [new(error ?? "material_source_unsupported", missing ?? itemID)];
                        return false;
                    }
                    if (craftPlan is null) continue;
                    step = new(QuestStepKind.Craft, 0, 0, null, itemID, state.Sequence, ReadStateKey(target),
                        new(ItemID: itemID, Quantity: (int)quantity, RequireHQ: requireHQ), Quantity: (int)quantity, RequireHQ: requireHQ);
                    return true;
                }
            }
            var currentJobID = LocalPlayerState.ClassJob;
            var currentJob = Jobs().FirstOrDefault(job => job.RowId == currentJobID);
            if (state.AcceptClassJob != 0 && currentJobID != state.AcceptClassJob &&
                !JobMatches(currentJob, definition.PrimaryJobCategory, definition.PrimaryLevel, definition.RequiredJob))
            {
                var acceptedJob = Jobs().FirstOrDefault(job => job.RowId == state.AcceptClassJob);
                var nextJobID = (uint)state.AcceptClassJob;
                byte gearsetID = 0;
                if (!JobMatches(acceptedJob, definition.PrimaryJobCategory, definition.PrimaryLevel, definition.RequiredJob) ||
                    RaptureGearsetModule.Instance() == null || !LocalPlayerState.TryFindClassJobGearset(nextJobID, out gearsetID))
                {
                    if (!TryEligibleJob(definition, out nextJobID, out gearsetID))
                    {
                        blockers = [new("quest_accepted_job_gearset_required", state.AcceptClassJob)];
                        return false;
                    }
                }
                step = new(QuestStepKind.SwitchJob, 0, 0, null, gearsetID, state.Sequence, ReadStateKey(target), new(), ClassJobID: nextJobID);
                return true;
            }
            step = MakeStep(QuestStepKind.Interact, objectID, location, state, target, todoIndex);
            return true;
        }

        private unsafe bool TryGatherSupply(QuestDefinition definition, QuestState state, QuestTarget target, uint itemID, int quantity,
            bool requireHQ, out QuestStep? step, out QuestBlocker[] blockers)
        {
            step = null;
            blockers = [];
            if (requireHQ) return false;
            var sources = new List<(QuestLocation Location, uint JobID, byte Level)>();
            foreach (var objective in definition.Objectives.Where(objective => objective.CompleteSequence == state.Sequence))
                foreach (var location in objective.Locations)
                    if (location.ObjectType == 14 && LuminaGetter.TryGetRow<GatheringPoint>(location.ObjectID, out var point) &&
                        LuminaGetter.TryGetRow<GatheringPointBase>(point.GatheringPointBase.RowId, out var node) && NodeHasItem(node, itemID))
                        sources.Add((location, node.GatheringType.RowId < 2 ? 16u : 17u, node.GatheringLevel));
            foreach (var candidate in DService.Instance().ObjectTable)
            {
                if (candidate.ObjectKind != ObjectKind.GatheringPoint || !candidate.IsValid() || !candidate.IsTargetable) continue;
                var native = (NativeGatheringPoint*)candidate.Address;
                var handler = native->Impl == null ? null : native->Impl->EventHandler;
                if (handler == null || !LuminaGetter.TryGetRow<GatheringPointBase>(handler->BaseRowId, out var node) || !NodeHasItem(node, itemID))
                    continue;
                sources.Add((new(0, DService.Instance().ClientState.TerritoryType, candidate.Position, 0),
                    node.GatheringType.RowId < 2 ? 16u : 17u, node.GatheringLevel));
            }
            if (sources.Count == 0) return false;
            var position = DService.Instance().ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
            foreach (var source in sources.OrderBy(source => source.Location.TerritoryID == DService.Instance().ClientState.TerritoryType ? 0 : 1)
                .ThenBy(source => Vector3.DistanceSquared(position, source.Location.Position)).ThenBy(static source => source.JobID))
            {
                var job = Jobs().FirstOrDefault(job => job.RowId == source.JobID);
                if (!JobMatches(job, 0, source.Level, source.JobID)) continue;
                if (LocalPlayerState.ClassJob != source.JobID)
                {
                    if (RaptureGearsetModule.Instance() == null || !LocalPlayerState.TryFindClassJobGearset(source.JobID, out var gearsetID)) continue;
                    step = new(QuestStepKind.SwitchJob, 0, 0, null, gearsetID, state.Sequence, ReadStateKey(target), new(), ClassJobID: source.JobID);
                    return true;
                }
                step = new(QuestStepKind.Gather, 0, source.Location.TerritoryID, source.Location.Position, itemID, state.Sequence,
                    ReadStateKey(target), new(ItemID: itemID, Quantity: quantity), Quantity: quantity, ClassJobID: source.JobID);
                return true;
            }
            return false;
        }

        private static bool NodeHasItem(GatheringPointBase node, uint itemID) => node.GatheringType.RowId < 4 &&
            node.Item.Any(item => item.RowId != 0 && LuminaGetter.TryGetRow<GatheringItem>(item.RowId, out var gatheringItem) &&
                gatheringItem.Item.RowId == itemID);

        private QuestTargetEvaluation EvaluateTarget(QuestTarget target)
        {
            target = target.Normalize();
            if (IsSatisfied(target)) return new(target, NameOf(target), QuestAvailability.Satisfied, [], [], "Satisfied");
            if (!TryPlan(target, [], out var plan, out var blockers) || plan.Count == 0)
                return new(target, NameOf(target), Availability(blockers), blockers, []);
            var prerequisites = plan.Where(entry => entry != target).ToArray();
            if (!TryGetStep(plan[0], out var step, out blockers) || step is null)
                return new(target, NameOf(target), Availability(blockers), blockers, prerequisites);
            return new(target, NameOf(target), QuestAvailability.Ready, [], prerequisites, "CurrentStepOnly");
        }

        private bool TryPlan
        (
            QuestTarget target,
            HashSet<QuestTarget> visiting,
            out List<QuestTarget> plan,
            out QuestBlocker[] blockers,
            bool isPrerequisite = false,
            HashSet<uint>? blockedPrerequisites = null
        )
        {
            plan = [];
            blockers = [];
            if (IsSatisfied(target) || isPrerequisite && IsPrerequisiteSatisfied(target)) return true;
            target = target.Normalize();
            if (target.Type == QuestTargetType.Quest && blockedPrerequisites?.Contains(target.ID) == true)
            {
                blockers = [new("quest_prerequisite_blocked", target.ID)];
                return false;
            }
            if (!visiting.Add(target))
            {
                blockers = [new("quest_dependency_cycle", target.ID)];
                return false;
            }
            try
            {
                if (target.Type != QuestTargetType.Quest)
                {
                    blockers = [new("special_target_unimplemented", target.ID)];
                    return false;
                }
                if (!TryDefinition(target.ID, out var definition))
                {
                    blockers = [new("quest_row_unavailable", target.ID)];
                    return false;
                }
                if (!ReadState(target.ID).Accepted && pendingTurnIns.ContainsKey(target.ID))
                {
                    blockers = [new("quest_repeat_completion_unconfirmed", target.ID)];
                    return false;
                }
                if (definition.Prerequisites.Length != 0)
                {
                    if (definition.PrerequisiteJoin > 2 || definition.PrerequisiteJoin == 0 && definition.Prerequisites.Length > 1)
                    {
                        blockers = [new("quest_prerequisite_relation_unsupported", target.ID)];
                        return false;
                    }
                    if (definition.PrerequisiteJoin == 2)
                    {
                        if (!definition.Prerequisites.Any(IsPrerequisiteSatisfied))
                        {
                            var choices = new List<(List<QuestTarget> Plan, QuestAvailability Availability)>();
                            foreach (var prerequisite in definition.Prerequisites)
                            {
                                if (TryPlan(prerequisite, visiting, out var branch, out var branchBlockers, true, blockedPrerequisites) && branch.Count != 0)
                                {
                                    var available = TryGetStep(branch[0], out var firstStep, out branchBlockers) && firstStep is not null
                                        ? QuestAvailability.Ready : Availability(branchBlockers);
                                    if (available is QuestAvailability.Ready or QuestAvailability.Waiting)
                                        choices.Add((branch, available));
                                }
                                if (blockers.Length == 0) blockers = branchBlockers;
                            }
                            if (choices.Count == 0) return false;
                            plan.AddRange(choices.OrderBy(choice => !ReadState(choice.Plan[0].ID).Accepted ? 1 : 0)
                                .ThenBy(static choice => choice.Availability == QuestAvailability.Ready ? 0 : 1)
                                .ThenBy(static choice => choice.Plan.Count).ThenBy(static choice => choice.Plan[0].ID).First().Plan);
                            blockers = [];
                        }
                    }
                    else
                        foreach (var prerequisite in definition.Prerequisites)
                        {
                            if (!TryPlan(prerequisite, visiting, out var branch, out blockers, true, blockedPrerequisites)) return false;
                            foreach (var entry in branch)
                                if (!plan.Contains(entry)) plan.Add(entry);
                        }
                }
                if (plan.Count == 0)
                {
                    blockers = Conditions(definition);
                    if (blockers.Length != 0) return false;
                }
                plan.Add(target);
                return true;
            }
            finally
            {
                visiting.Remove(target);
            }
        }

        private bool IsPrerequisiteSatisfied(QuestTarget target) => IsSatisfied(target) ||
            target.Type == QuestTargetType.Quest && DService.Instance().ClientState.IsLoggedIn &&
            IsValidQuestID(target.Normalize().ID) && !IsQuestAccepted(target) &&
            QuestManager.IsQuestComplete(target.Normalize().ID);

        public QuestBlocker? CheckAcceptance(QuestTarget target)
        {
            if (!TryDefinition(target.ID, out var definition))
                return new("quest_row_unavailable", target.ID);
            if (definition.Prerequisites.Length > 0 &&
                (definition.PrerequisiteJoin == 2
                    ? !definition.Prerequisites.Any(IsPrerequisiteSatisfied)
                    : !definition.Prerequisites.All(IsPrerequisiteSatisfied)))
                return new("quest_prerequisite_incomplete", target.ID);
            return Conditions(definition).FirstOrDefault();
        }

        private unsafe QuestBlocker[] Conditions(QuestDefinition definition)
        {
            var player = PlayerState.Instance();
            var manager = QuestManager.Instance();
            if (player == null || manager == null || !DService.Instance().ClientState.IsLoggedIn)
                return [new("not_logged_in")];
            if (manager->IsQuestAccepted(definition.ID)) return [];
            var blockers = new List<QuestBlocker>();
            if (definition.Expansion > player->MaxExpansion) blockers.Add(new("quest_expansion_unavailable", definition.Expansion));
            if (!TryEligibleJob(definition, out _, out _)) blockers.Add(new("quest_job_level_or_gearset_required", definition.ID));
            if ((definition.SecondaryJobCategory != 0 || definition.SecondaryLevel != 0) &&
                !Jobs().Any(job => JobMatches(job, definition.SecondaryJobCategory, definition.SecondaryLevel, 0)))
                blockers.Add(new("quest_secondary_job_required", definition.SecondaryJobCategory));
            if (definition.Company != 0 && definition.Company != player->GrandCompany) blockers.Add(new("quest_grand_company_required", definition.Company));
            if (definition.CompanyRank > player->GetGrandCompanyRank()) blockers.Add(new("quest_grand_company_rank_required", definition.CompanyRank));
            if (definition.Tribe != 0)
            {
                var tribe = (byte)definition.Tribe;
                var rank = player->GetBeastTribeRank(tribe);
                if (rank < definition.TribeRank)
                    blockers.Add(new("quest_society_rank_required", definition.TribeRank));
                else if (rank > 0 && rank == definition.TribeRank &&
                         !definition.Repeatable && definition.TribeReputationValue == ushort.MaxValue &&
                         player->GetBeastTribeCurrentReputation(tribe) < player->GetBeastTribeNeededReputation(tribe))
                    blockers.Add(new("quest_society_reputation_required", definition.ID));
            }
            if (definition.Mount != 0 && !player->IsMountUnlocked(definition.Mount)) blockers.Add(new("quest_mount_required", definition.Mount));
            if (definition.Festival != 0) blockers.Add(new("quest_festival_schedule_unresolved", definition.Festival));
            if (definition.BellStart != definition.BellEnd) blockers.Add(new("quest_time_window_unresolved", definition.ID));
            if (definition.Satisfaction != 0) blockers.Add(new("quest_satisfaction_condition_unresolved", definition.Satisfaction));
            if (definition.HouseRequired) blockers.Add(new("quest_house_condition_unresolved", definition.ID));
            if (definition.Repeatable && definition.RepeatFlag == 0) blockers.Add(new("quest_repeat_completion_unresolved", definition.ID));
            if (definition.Locks.Length != 0)
            {
                if (definition.LockJoin > 2 || definition.LockJoin == 0 && definition.Locks.Length > 1)
                    blockers.Add(new("quest_lock_relation_unsupported", definition.ID));
                else if (definition.LockJoin == 2 ? definition.Locks.Any(QuestManager.IsQuestComplete) : definition.Locks.All(QuestManager.IsQuestComplete))
                    blockers.Add(new("quest_mutually_exclusive", definition.ID));
            }
            if (definition.Instances.Length != 0)
            {
                if (definition.InstanceJoin > 2 || definition.InstanceJoin == 0 && definition.Instances.Length > 1)
                    blockers.Add(new("quest_duty_relation_unsupported", definition.ID));
                else if (!(definition.InstanceJoin == 2
                    ? definition.Instances.Any(UIState.IsInstanceContentCompleted)
                    : definition.Instances.All(UIState.IsInstanceContentCompleted)))
                    blockers.Add(new("quest_duty_prerequisite_required", definition.Instances[0]));
            }
            return [.. blockers];
        }

        private ClassJob[] Jobs() => classJobs ??= LuminaGetter.Get<ClassJob>().Where(static job => job.RowId != 0).ToArray();

        private unsafe bool TryEligibleJob(QuestDefinition definition, out uint classJobID, out byte gearsetID)
        {
            var currentJobID = LocalPlayerState.ClassJob;
            classJobID = currentJobID;
            gearsetID = 0;
            if (PlayerState.Instance() == null) return false;
            var current = Jobs().FirstOrDefault(job => job.RowId == currentJobID);
            if (current.RowId != 0 && JobMatches(current, definition.PrimaryJobCategory, definition.PrimaryLevel, definition.RequiredJob))
                return true;
            if (RaptureGearsetModule.Instance() == null) return false;
            foreach (var job in Jobs())
            {
                if (!JobMatches(job, definition.PrimaryJobCategory, definition.PrimaryLevel, definition.RequiredJob) ||
                    !LocalPlayerState.TryFindClassJobGearset(job.RowId, out gearsetID)) continue;
                classJobID = job.RowId;
                return true;
            }
            return false;
        }

        private static unsafe bool JobMatches(ClassJob job, uint categoryID, ushort level, uint requiredJobID)
        {
            var player = PlayerState.Instance();
            if (player == null || job.RowId == 0 || requiredJobID != 0 && job.RowId != requiredJobID ||
                job.ExpArrayIndex < 0 || job.ExpArrayIndex >= player->ClassJobLevels.Length ||
                player->ClassJobLevels[job.ExpArrayIndex] == 0 || player->ClassJobLevels[job.ExpArrayIndex] < level)
                return false;
            return categoryID == 0 || LuminaGetter.TryGetRow<ClassJobCategory>(categoryID, out var category) && category.IsClassJobIn(job.RowId);
        }

        private bool TryScriptCondition
        (
            QuestScript script,
            QuestDefinition definition,
            QuestState state,
            string name,
            uint objectID,
            out bool result,
            out string? error
        )
        {
            result = false;
            if (!script.TryEvaluate(name, definition.Symbols, (method, args) => ReadGetter(method, args, definition, state, objectID),
                [new LuaSelf(), new LuaPlayer(), new LuaActor(objectID), (double)objectID, 0d, 0d, 0d], out var values, out error)) return false;
            if (values.Length == 0 || values[0] is not bool value)
            {
                error = values.FirstOrDefault() is LuaUnknown unknown ? unknown.Reason : "script_condition_result_unresolved";
                return false;
            }
            result = value;
            return true;
        }

        private static unsafe object?[] ReadGetter(string name, object?[] args, QuestDefinition definition, QuestState state, uint objectID)
        {
            switch (name)
            {
                case "GetQuestId": return [(double)(definition.ID & 0xFFFF)];
                case "GetQuestSequence":
                    var questID = args.Length > 1 ? NormalizeID(QuestScript.Unsigned(args[1])) : definition.ID;
                    return [(double)QuestManager.GetQuestSequence(questID)];
                case "GetBaseId":
                    if (args.FirstOrDefault() is not LuaActor actor) throw new LuaEvaluationException("script_actor_receiver_unresolved");
                    return [(double)actor.ID];
                case "GetLayoutId":
                    if (args.FirstOrDefault() is not LuaActor layoutActor) throw new LuaEvaluationException("script_actor_receiver_unresolved");
                    var objects = DService.Instance().ObjectTable.Where(obj => obj.IsValid() && obj.DataID == layoutActor.ID).ToArray();
                    return objects.Length == 1 ? [(double)objects[0].LayoutID] : [new LuaUnknown("target_layout_unresolved")];
                case "IsQuestComplete":
                    if (args.Length < 2) throw new LuaEvaluationException("script_quest_argument_unresolved");
                    return [QuestManager.IsQuestComplete(NormalizeID(QuestScript.Unsigned(args[1])))];
                case "IsQuestAccepted":
                    if (args.Length < 2 || QuestManager.Instance() == null) throw new LuaEvaluationException("script_quest_argument_unresolved");
                    return [QuestManager.Instance()->IsQuestAccepted(NormalizeID(QuestScript.Unsigned(args[1])))];
                case "GetNumOfItems":
                    if (args.Length < 2 || InventoryManager.Instance() == null) throw new LuaEvaluationException("script_inventory_unavailable");
                    if (args.Length > 3) return [new LuaUnknown("script_item_count_scope_unverified")];
                    var itemID = QuestScript.Unsigned(args[1]);
                    var highQuality = false;
                    var normalQuality = false;
                    if (args.Length > 2 && args[2] is not null)
                    {
                        if (args[2] is LuaHostConstant hostFilter)
                        {
                            highQuality = hostFilter.Name == "NUM_OF_ITEMS_FILTER_HQ";
                            normalQuality = hostFilter.Name == "NUM_OF_ITEMS_FILTER_NQ";
                            if (!highQuality && !normalQuality && hostFilter.Name is not ("NUM_OF_ITEMS_FILTER_ALL" or "NUM_OF_ITEMS_FILTER_NQ_OR_HQ"))
                                throw new LuaEvaluationException("script_item_quality_unresolved");
                        }
                        else
                        {
                            var filter = QuestScript.Unsigned(args[2]);
                            highQuality = definition.Symbols.TryGetValue("NUM_OF_ITEMS_FILTER_HQ", out var hq) && filter == hq;
                            normalQuality = definition.Symbols.TryGetValue("NUM_OF_ITEMS_FILTER_NQ", out var nq) && filter == nq;
                            if (!highQuality && !normalQuality &&
                                (!definition.Symbols.TryGetValue("NUM_OF_ITEMS_FILTER_ALL", out var all) || filter != all))
                                throw new LuaEvaluationException("script_item_quality_unresolved");
                        }
                    }
                    var count = InventoryManager.Instance()->GetInventoryItemCount(itemID, highQuality);
                    if (!highQuality && !normalQuality) count += InventoryManager.Instance()->GetInventoryItemCount(itemID, true);
                    return [(double)count];
                case "IsUnlockLinkUnlocked":
                    if (args.Length < 2 || UIState.Instance() == null) throw new LuaEvaluationException("script_unlock_argument_unresolved");
                    return [UIState.Instance()->IsUnlockLinkUnlocked(QuestScript.Unsigned(args[1]))];
                case "GetQuestBitFlag8":
                    if (args.Length != 3 || args[0] is not LuaPlayer ||
                        NormalizeID(QuestScript.Unsigned(args[1])) != definition.ID || !state.Accepted || state.Variables.Length != 6)
                        return [new LuaUnknown("quest_variable_getter_unverified")];
                    var bit = QuestScript.Unsigned(args[2]);
                    if (bit is < 1 or > 8) return [new LuaUnknown("quest_variable_getter_unverified")];
                    return [(state.Variables[5] & (0x80 >> ((int)bit - 1))) != 0];
                default:
                    if (name.StartsWith("GetQuestUI8", StringComparison.Ordinal))
                    {
                        if (name.Length is not (12 or 13) || name[11] is < 'A' or > 'F' ||
                            name.Length == 13 && name[12] is not ('H' or 'L') || args.Length != 2 || args[0] is not LuaPlayer ||
                            NormalizeID(QuestScript.Unsigned(args[1])) != definition.ID || !state.Accepted || state.Variables.Length != 6)
                            return [new LuaUnknown("quest_variable_getter_unverified")];
                        var variable = state.Variables[name[11] - 'A'];
                        if (name.Length == 12) return [(double)variable];
                        return [(double)(name[12] == 'H' ? variable >> 4 : variable & 15)];
                    }
                    throw new LuaEvaluationException($"script_getter_unsupported:{name}");
            }
        }

        private QuestLocation? ResolveLocation(QuestDefinition definition, uint objectID, QuestLocation[] objectives, out string? error)
        {
            error = null;
            var live = DService.Instance().ObjectTable.Where(obj => obj.IsValid() && obj.IsTargetable && obj.DataID == objectID).ToArray();
            if (live.Length == 1)
                return new(objectID, DService.Instance().ClientState.TerritoryType, live[0].Position, 0);
            var direct = objectives.Where(location => location.ObjectID == objectID).Distinct().ToArray();
            if (direct.Length == 1) return direct[0];
            if (direct.Length > 1)
            {
                error = "target_instance_unresolved";
                return null;
            }
            if (objectID == definition.IssuerID && definition.Issuer is not null) return definition.Issuer;
            var territories = objectives.Select(static location => location.TerritoryID).Distinct().ToArray();
            var placements = new List<QuestLocation>();
            foreach (var territory in territories)
            {
                var locations = GetLayerPositions(territory, objectID, out var layerError);
                if (layerError is not null) error = layerError;
                placements.AddRange(locations);
            }
            if (placements.Count == 1) return placements[0];
            if (placements.Count > 1) error = "target_instance_unresolved";
            return null;
        }

        private static bool IsUnconditionalListener(QuestListener listener) =>
            listener.Variable0 == 255 && listener.Variable1 == 255 && listener.ConditionOperator == 3;

        private bool TryObjectiveListenerInteraction(QuestScript script, QuestDefinition definition, QuestState state,
            QuestTarget target, QuestObjective[] objectives, out QuestStep? step, out QuestBlocker[] blockers)
        {
            step = null;
            blockers = [];
            foreach (var objective in objectives)
            {
                var candidates = definition.Listeners.Where(listener => listener.Sequence == state.Sequence &&
                        listener.TodoIndex == objective.Index && listener.EventType == 1 &&
                        listener.ConditionType == 0 && listener.ConditionValue == 0 && IsUnconditionalListener(listener))
                    .Select(static listener => listener.ObjectID).Distinct().ToArray();
                if (candidates.Length == 0 || objective.Quantity != candidates.Length) continue;

                var placements = new List<QuestLocation>();
                foreach (var id in candidates)
                {
                    if (!TryScriptCondition(script, definition, state, "IsAcceptEvent", id, out var allowed, out var error))
                    {
                        blockers = [new(error ?? "script_condition_result_unresolved", target.ID)];
                        return true;
                    }
                    if (!allowed) continue;
                    var location = ResolveLocation(definition, id, objective.Locations, out error);
                    if (location is null)
                    {
                        blockers = [new(error ?? "target_location_unresolved", target.ID)];
                        return true;
                    }
                    placements.Add(location);
                }
                if (placements.Count == 0) continue;
                var services = DService.Instance();
                var territory = services.ClientState.TerritoryType;
                var position = services.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
                var live = services.ObjectTable.Where(static obj => obj.IsValid() && obj.IsTargetable)
                    .Select(static obj => obj.DataID).ToHashSet();
                var nearest = placements.OrderBy(location => location.TerritoryID == territory ? 0 : 1)
                    .ThenBy(location => location.TerritoryID == territory && live.Contains(location.ObjectID) ? 0 : 1)
                    .ThenBy(location => Vector3.DistanceSquared(location.Position, position)).First();
                TryInteraction(script, definition, state, target, nearest.ObjectID, nearest, objective.Index, out step, out blockers);
                return true;
            }
            return false;
        }

        private static bool TryEventRange(QuestDefinition definition, byte sequence, QuestObjective objective, out QuestLocation range)
        {
            range = new(0, 0, Vector3.Zero, 0);
            var candidates = new List<QuestLocation>();
            foreach (var symbol in definition.Symbols)
            {
                if (!symbol.Key.StartsWith("EVENTRANGE", StringComparison.Ordinal) ||
                    !LuminaGetter.TryGetRow<Level>(symbol.Value, out var level)) continue;
                var location = Location(level);
                if (location is null || location.Radius > 15) continue;
                if (location.ObjectID != 0 && (location.ObjectType != 49 ||
                    !definition.Listeners.Any(listener => listener.Sequence == sequence && listener.EventType == 10 &&
                        listener.ConditionType == 3 && listener.ConditionValue == level.RowId &&
                        listener.ObjectID == location.ObjectID && IsUnconditionalListener(listener)))) continue;
                if (objective.Locations.Any(mark => mark.TerritoryID == location.TerritoryID &&
                    Vector3.DistanceSquared(mark.Position, location.Position) <= MathF.Max(9, mark.Radius * mark.Radius)))
                    candidates.Add(location);
            }
            if (candidates.Distinct().ToArray() is not [var unique]) return false;
            range = unique;
            return true;
        }

        private QuestStep MakeStep(QuestStepKind kind, uint objectID, QuestLocation location, QuestState state, QuestTarget target, int? todoIndex = null) =>
            new(kind, objectID, location.TerritoryID, location.Position, 0, state.Sequence, ReadStateKey(target), new(todoIndex));

        internal static QuestAvailability Availability(QuestBlocker[] blockers) => blockers.Any(static blocker =>
            blocker.Code.Contains("unsupported", StringComparison.Ordinal) || blocker.Code.Contains("unresolved", StringComparison.Ordinal) ||
            blocker.Code.Contains("unimplemented", StringComparison.Ordinal) || blocker.Code.Contains("unverified", StringComparison.Ordinal) || blocker.Code.EndsWith("_ambiguous", StringComparison.Ordinal) ||
            blocker.Code.StartsWith("script_", StringComparison.Ordinal) && blocker.Code != "script_loading" || blocker.Code == "quest_script_unavailable")
            ? QuestAvailability.Unsupported :
            blockers.Any(static blocker => blocker.RetryAt is not null || blocker.Code is "script_loading" or "resource_loading") ? QuestAvailability.Waiting : QuestAvailability.Blocked;
    }
}
