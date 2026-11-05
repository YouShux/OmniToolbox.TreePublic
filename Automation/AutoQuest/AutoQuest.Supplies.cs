using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Quests;
using NativeGatheringPoint = FFXIVClientStructs.FFXIV.Client.Game.Object.GatheringPointObject;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    private unsafe string? DriveGathering(Session current, QuestTarget target, QuestStep step)
    {
        current.Phase = QuestExecutionPhase.Gathering;
        if (step.RequireHQ)
            return "gathering_quality_unsupported";
        var count = CountItem(step.Argument, false);
        if (count >= step.Quantity)
        {
            StopGathering(current);
            return null;
        }
        if (DService.Instance().ObjectTable.LocalPlayer is not { } player || player.ClassJob.RowId is not (16 or 17))
            return "gathering_job_required";
        var now = Environment.TickCount64;
        var framework = EventFramework.Instance();
        var ownsEvent = current.GatheringObjectID != 0 && framework != null &&
                        (framework->EventState1.ObjectId == current.GatheringObjectID ||
                         framework->EventState2.ObjectId == current.GatheringObjectID);
        if (DService.Instance().Condition.IsOccupiedInEvent && !ownsEvent)
            return "external_interaction_busy";
        if (current.GatheringProgressAt == 0 || count != current.GatheringCount)
        {
            current.GatheringProgressAt = now;
            if (!current.GatheringPending)
                current.GatheringCount = count;
        }
        var addon = DService.Instance().GameGUI.GetAddonByName<AddonGathering>("Gathering");
        if (addon != null && addon->IsVisible && !ownsEvent)
            return "external_gathering_busy";
        if (current.GatheringPending && count != current.GatheringCount)
        {
            current.GatheringPending = false;
            current.ActionSent = false;
            current.GatheringCount = count;
        }
        if (current.GatheringPending && (addon == null || !addon->IsVisible))
            return now - current.GatheringActionAt >= 15_000 ? "gathering_no_progress" : null;
        IGameObject? node = null;
        GatheringPointBase? nodeBase = null;
        foreach (var candidate in DService.Instance().ObjectTable)
        {
            if (candidate.ObjectKind != ObjectKind.GatheringPoint || !candidate.IsValid() ||
                addon != null && addon->IsVisible && candidate.GameObjectID != current.GatheringObjectID ||
                !candidate.IsTargetable && candidate.GameObjectID != current.GatheringObjectID)
                continue;
            var native = (NativeGatheringPoint*)candidate.Address;
            var handler = native->Impl == null ? null : native->Impl->EventHandler;
            if (handler == null || !LuminaGetter.TryGetRow<GatheringPointBase>(handler->BaseRowId, out var row))
                continue;
            if (row.GatheringType.RowId / 2 != (player.ClassJob.RowId == 16 ? 0 : 1))
                continue;
            var contains = false;
            foreach (var item in row.Item)
                if (item.RowId != 0 && LuminaGetter.TryGetRow<GatheringItem>(item.RowId, out var gatheringItem) && gatheringItem.Item.RowId == step.Argument)
                { contains = true; break; }
            if (!contains)
                continue;
            if (node is null || Vector3.DistanceSquared(player.Position, candidate.Position) < Vector3.DistanceSquared(player.Position, node.Position))
            {
                node = candidate;
                nodeBase = row;
            }
        }
        if (node is null)
        {
            if (ownsEvent)
                return now - current.GatheringProgressAt > 15_000 ? "gathering_no_progress" : null;
            if (step.Position is { } anchor)
            {
                var error = Approach(current, step, anchor, 4);
                if (current.OwnsTravel)
                    current.GatheringProgressAt = now;
                if (error is not null || current.OwnsTravel)
                    return error;
            }
            return "gathering_node_or_time_unresolved";
        }
        if (nodeBase is { } point && player.Level < point.GatheringLevel)
            return "level_insufficient";
        if (addon != null && addon->IsVisible)
        {
            if (current.GatheringObjectID != node.GameObjectID)
                return "external_gathering_busy";
            var native = (NativeGatheringPoint*)node.Address;
            var integrity = native->Impl == null ? (byte)0 : native->Impl->RemainingCount;
            if (current.GatheringPending)
            {
                if (count == current.GatheringCount && integrity == current.GatheringIntegrity)
                    return now - current.GatheringActionAt >= 15_000 ? "gathering_no_progress" : null;
                current.GatheringPending = false;
                current.ActionSent = false;
                current.GatheringProgressAt = now;
            }
            if (DService.Instance().Condition[ConditionFlag.ExecutingGatheringAction])
                return null;
            for (var index = 0; index < addon->ItemIds.Length; index++)
                if (addon->ItemIds[index] == step.Argument)
                {
                    current.GatheringCount = count;
                    current.GatheringIntegrity = integrity;
                    current.GatheringPending = true;
                    current.GatheringActionAt = now;
                    addon->FireCallbackInt(index);
                    current.ActionSent = true;
                    return null;
                }
            return "gathering_item_unavailable";
        }
        var travelError = Approach(current, step, node.Position, 3);
        if (current.OwnsTravel)
            current.GatheringProgressAt = now;
        if (travelError is not null || current.OwnsTravel)
            return travelError;
        if (now - current.GatheringProgressAt > 180_000)
            return "gathering_no_progress";
        if (DService.Instance().Condition.IsOccupiedInEvent)
            return null;
        if (current.GatheringPending)
            return now - current.GatheringActionAt >= 15_000 ? "gathering_no_progress" : null;
        current.GatheringObjectID = node.GameObjectID;
        TargetManager.Instance().InteractWithObject(node);
        current.NextActionAt = Environment.TickCount64 + 600;
        return null;
    }

    private static unsafe void StopGathering(Session current)
    {
        if (current.GatheringObjectID == 0)
            return;
        if (!DService.Instance().ClientState.IsLoggedIn || current.Operation.CharacterContentID != DalamudServices.PlayerState.ContentId)
        {
            DrainGathering(current);
            return;
        }
        var framework = EventFramework.Instance();
        if (framework != null && (framework->EventState1.ObjectId == current.GatheringObjectID || framework->EventState2.ObjectId == current.GatheringObjectID))
        {
            var addon = DService.Instance().GameGUI.GetAddonByName<AddonGathering>("Gathering");
            if (addon != null && addon->IsVisible)
                addon->Close(true);
        }
        DrainGathering(current);
    }

    private static unsafe bool DrainGathering(Session current)
    {
        if (current.GatheringObjectID == 0)
            return true;
        var services = DService.Instance();
        if (services.ClientState.IsLoggedIn && current.Operation.CharacterContentID == DalamudServices.PlayerState.ContentId)
        {
            if (services.Condition[ConditionFlag.ExecutingGatheringAction])
                return false;
            var framework = EventFramework.Instance();
            if (framework != null && services.Condition.IsOccupiedInEvent &&
                (framework->EventState1.ObjectId == current.GatheringObjectID || framework->EventState2.ObjectId == current.GatheringObjectID))
                return false;
        }
        current.GatheringObjectID = 0;
        current.GatheringPending = false;
        current.GatheringProgressAt = 0;
        current.GatheringActionAt = 0;
        return true;
    }

    private static string? DriveFishing(Session current, QuestStep step)
    {
        current.Phase = QuestExecutionPhase.Fishing;
        if (CountItem(step.Argument, false) >= step.Quantity)
            return null;
        var state = DService.Instance().PI.GetIpcSubscriber<bool>("AutoHook.GetPluginState");
        return state.HasFunction ? "autohook_preset_restore_unsupported" : "autohook_unavailable";
    }

    private static unsafe string? DriveHandOver(Session current, AddonRequest* request)
    {
        if (!IsOwnedQuestEvent(current))
            return "external_interaction_busy";
        if (current.HandOverSent)
            return null;
        var state = UIState.Instance();
        var agent = AgentNpcTrade.Instance();
        var manager = InventoryManager.Instance();
        var handIn = manager == null ? null : manager->GetInventoryContainer(InventoryType.HandIn);
        if (state == null || agent == null || handIn == null || !handIn->IsLoaded)
            return "resource_loading";
        var requests = state->NpcTrade.Requests;
        if (requests.Count == 0 || requests.Count > requests.Items.Length || requests.Count != request->EntryCount ||
            requests.Count > handIn->Size)
            return "handover_items_unresolved";
        var requirements = new HandOverRequirement[requests.Count];
        var totals = new Dictionary<uint, (long Total, long HQ)>();
        for (var index = 0; index < requests.Count; index++)
        {
            var item = requests.Items[index];
            if (item.ItemId == 0 || item.RequiredQuantity <= 0 || item.WantCollectible || item.MinCollectibility != 0 ||
                item.WantMateriaFilledSlots != 0 || item.WantMateriaIds.ContainsAnyExcept((ushort)0))
                return "handover_item_requirement_unsupported";
            var itemID = item.ItemId is >= 1_000_000 and < 2_000_000 ? item.ItemId - 1_000_000 : item.ItemId;
            var requireHQ = item.WantHQ || item.ItemId is >= 1_000_000 and < 2_000_000;
            if (itemID >= 2_000_000 && requireHQ)
                return "handover_item_requirement_unsupported";
            requirements[index] = new(itemID, item.RequiredQuantity, requireHQ);
            var total = totals.GetValueOrDefault(itemID);
            totals[itemID] = (total.Total + item.RequiredQuantity, total.HQ + (requireHQ ? item.RequiredQuantity : 0));
        }
        foreach (var (itemID, amount) in totals)
            if (CountItem(itemID, false) < amount.Total || CountItem(itemID, true) < amount.HQ)
                return "material_source_unsupported";
        var progress = current.HandOver;
        if (progress is null)
        {
            progress = new((nint)request, ((AtkUnitBase*)request)->Id, requirements);
            current.HandOver = progress;
        }
        else if (progress.Request != (nint)request || progress.AddonID != ((AtkUnitBase*)request)->Id ||
                 !progress.Requirements.AsSpan().SequenceEqual(requirements))
            return "handover_items_changed";
        for (var index = 0; index < requirements.Length; index++)
        {
            var slot = handIn->GetInventorySlot(index);
            if (slot == null || slot->GetBaseItemId() == 0)
                continue;
            if (!MatchesHandOver(slot, requirements[index]) || slot->Quantity != requirements[index].Quantity)
                return "handover_slot_mismatch";
            if (progress.Slot == index)
            {
                progress.Slot = -1;
                progress.OptionSent = false;
                progress.SentHQ = false;
                progress.ConfirmationSent = false;
                progress.SentAt = Environment.TickCount64;
            }
        }
        if (progress.Slot >= 0 && Environment.TickCount64 - progress.SentAt >= 15_000)
            return "handover_no_progress";
        if (request->HandOverButton == null)
            return "handover_unavailable";
        if (request->HandOverButton->IsEnabled)
        {
            for (var index = 0; index < requirements.Length; index++)
                if (!MatchesHandOver(handIn->GetInventorySlot(index), requirements[index]))
                    return "handover_slot_mismatch";
            current.HandOverSent = ClickQuestButton((AtkUnitBase*)request, request->HandOverButton);
            return current.HandOverSent ? null : "handover_unavailable";
        }
        for (ushort index = 0; index < requirements.Length; index++)
        {
            var requirement = requirements[index];
            var slot = handIn->GetInventorySlot(index);
            if (MatchesHandOver(slot, requirement))
                continue;
            if (progress.OptionSent)
                return null;
            var picker = DService.Instance().GameGUI.GetAddonByName<AtkUnitBase>("ContextIconMenu");
            if (progress.Slot < 0)
            {
                if (picker != null && picker->IsVisible)
                    return "handover_context_busy";
                progress.Slot = index;
                progress.SentAt = Environment.TickCount64;
                agent->SelectTurnInSlot(index);
                return null;
            }
            if (progress.Slot != index || agent->SelectedTurnInSlot != index || picker == null || !picker->IsVisible || !picker->IsReady)
                return null;
            if (agent->SelectedTurnInSlotItemOptions <= 0 || agent->SelectedTurnInSlotItemOptions > agent->SelectedTurnInSlotItemOptionValues.Length)
                return "handover_item_unavailable";
            var choice = -1;
            InventoryItem* selected = null;
            for (var option = 0; option < agent->SelectedTurnInSlotItemOptions; option++)
            {
                var candidate = agent->SelectedTurnInSlotItemOptionValues[option].Value;
                if (!MatchesHandOver(candidate, requirement) || !IsHandOverSource(candidate, requirement.ItemID))
                    continue;
                if (selected != null && (!selected->IsHighQuality() || candidate->IsHighQuality()))
                    continue;
                choice = option;
                selected = candidate;
            }
            if (selected == null)
                return "handover_quantity_unsupported";
            progress.OptionSent = true;
            progress.SentHQ = selected->IsHighQuality();
            progress.ItemID = selected->GetItemId();
            progress.Container = selected->GetInventoryType();
            progress.InventorySlot = selected->GetSlot();
            progress.SentAt = Environment.TickCount64;
            AgentId.NpcTrade.SendEvent(1, 0, choice, progress.ItemID, 0U, 0);
            return null;
        }
        return Environment.TickCount64 - progress.SentAt >= 15_000 ? "handover_no_progress" : null;
    }

    private static unsafe bool TryConfirmHandOverHQ(Session current, out string? error)
    {
        error = null;
        if (current.HandOver is not { OptionSent: true, SentHQ: true } progress || !IsOwnedQuestEvent(current))
            return false;
        var services = DService.Instance();
        var confirmation = services.GameGUI.GetAddonByName<AddonSelectYesno>("SelectYesno");
        if (confirmation == null || !confirmation->IsVisible)
            return false;
        var request = services.GameGUI.GetAddonByName<AddonRequest>("Request");
        var state = UIState.Instance();
        var manager = InventoryManager.Instance();
        var candidate = manager == null ? null : manager->GetInventorySlot(progress.Container, progress.InventorySlot);
        if (request == null || !request->IsVisible || (nint)request != progress.Request || ((AtkUnitBase*)request)->Id != progress.AddonID ||
            state == null || state->NpcTrade.Requests.Count != progress.Requirements.Length ||
            progress.Slot < 0 || progress.Slot >= progress.Requirements.Length ||
            candidate == null || candidate->GetItemId() != progress.ItemID ||
            !MatchesHandOver(candidate, progress.Requirements[progress.Slot]))
        {
            error = "handover_items_changed";
            return true;
        }
        var currentRequirement = state->NpcTrade.Requests.Items[progress.Slot];
        var currentID = currentRequirement.ItemId is >= 1_000_000 and < 2_000_000
            ? currentRequirement.ItemId - 1_000_000 : currentRequirement.ItemId;
        if (currentID != progress.Requirements[progress.Slot].ItemID ||
            currentRequirement.RequiredQuantity != progress.Requirements[progress.Slot].Quantity ||
            (currentRequirement.WantHQ || currentRequirement.ItemId is >= 1_000_000 and < 2_000_000) !=
            progress.Requirements[progress.Slot].RequireHQ)
        {
            error = "handover_items_changed";
            return true;
        }
        if (Environment.TickCount64 - progress.SentAt >= 15_000)
        {
            error = "handover_no_progress";
            return true;
        }
        if (confirmation->PromptText == null)
            return false;
        var prompt = confirmation->PromptText->NodeText.ToString();
        var expected = false;
        foreach (var id in new uint[] { 5450, 11514, 102434 })
            if (LuminaGetter.TryGetRow<Addon>(id, out var text) && text.Text.ToString() == prompt)
            {
                expected = true;
                break;
            }
        if (!expected)
            return false;
        if (!progress.ConfirmationSent)
        {
            progress.ConfirmationSent = ClickQuestButton((AtkUnitBase*)confirmation, confirmation->YesButton);
            if (!progress.ConfirmationSent)
                error = "handover_unavailable";
        }
        return true;
    }

    private static unsafe bool MatchesHandOver(InventoryItem* item, HandOverRequirement requirement) =>
        item != null && item->GetBaseItemId() == requirement.ItemID && item->GetQuantity() >= requirement.Quantity &&
        (!requirement.RequireHQ || item->IsHighQuality()) && !item->IsCollectable();

    private static unsafe bool IsHandOverSource(InventoryItem* item, uint itemID) =>
        itemID >= 2_000_000 ? item->GetInventoryType() == InventoryType.KeyItems :
            item->GetInventoryType() is InventoryType.Inventory1 or InventoryType.Inventory2 or InventoryType.Inventory3 or
                InventoryType.Inventory4 or InventoryType.Crystals;

    private sealed class HandOverProgress(nint request, ushort addonID, HandOverRequirement[] requirements)
    {
        public nint Request { get; } = request;
        public ushort AddonID { get; } = addonID;
        public HandOverRequirement[] Requirements { get; } = requirements;
        public int Slot { get; set; } = -1;
        public bool OptionSent { get; set; }
        public bool SentHQ { get; set; }
        public bool ConfirmationSent { get; set; }
        public uint ItemID { get; set; }
        public InventoryType Container { get; set; }
        public ushort InventorySlot { get; set; }
        public long SentAt { get; set; } = Environment.TickCount64;
    }

    private readonly record struct HandOverRequirement(uint ItemID, int Quantity, bool RequireHQ);
}
