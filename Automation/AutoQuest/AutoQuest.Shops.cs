using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Extensions;
using OmenTools.Info.Game.ItemSource;
using OmenTools.Info.Game.ItemSource.Enums;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Quests;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    private ShopWork? shopWork;

    private static bool TryPrepareShop(uint itemID, int quantity, out CraftPlan? plan, out string? error)
    {
        plan = null;
        error = null;
        var result = ItemSourceInfo.Query(itemID);
        if (result.State == ItemSourceQueryState.Building)
        {
            error = "resource_loading";
            return false;
        }
        if (result.Data is not { } sources)
            return false;
        foreach (var vendor in sources.NPCInfos
                     .Where(static vendor => vendor.Location is { TerritoryID: not 0 } &&
                         vendor.CostInfos is [{ ItemID: 1, Collectablity: null }])
                     .OrderBy(vendor => vendor.Location.TerritoryID == DService.Instance().ClientState.TerritoryType ? 0 : 1)
                     .ThenBy(static vendor => vendor.ID))
        {
            if (!LuminaGetter.TryGetRow<ENpcBase>(vendor.ID, out var npc))
                continue;
            foreach (var entry in npc.ENpcData)
            {
                if (entry.RowId >> 16 != 4 || !LuminaGetter.TryGetSubRowAll<GilShopItem>(entry.RowId, out var items))
                    continue;
                var found = false;
                foreach (var item in items)
                    if (item.Item.RowId == itemID) { found = true; break; }
                if (!found)
                    continue;
                plan = new(null, itemID, quantity, false, new(vendor.ID, entry.RowId, vendor.Location.TerritoryID));
                return true;
            }
        }
        return false;
    }

    private unsafe string? DriveSupplyShop(Session current, ShopWork work)
    {
        if (work.Owner != current.Operation.OperationID)
            return "plugin_busy";
        current.Phase = QuestExecutionPhase.Interacting;
        var count = CountItem(work.ItemID, false);
        if (count >= work.Quantity)
        {
            StopSupplyShop(current);
            if (DrainSupplyShop(current))
                current.ActionSent = false;
            return null;
        }
        if (work.Stopping)
            return "supply_purchase_cancelled";
        var services = DService.Instance();
        var framework = EventFramework.Instance();
        var active = IsSupplyShopEvent(work);
        if (work.PendingQuantity != 0)
        {
            if (count >= work.CountBefore + work.PendingQuantity)
            {
                work.PendingQuantity = 0;
                work.ProgressAt = Environment.TickCount64;
            }
            else
            {
                var confirmation = services.GameGUI.GetAddonByName<AddonSelectYesno>("SelectYesno");
                var proxy = ShopEventHandler.AgentProxy.Instance();
                var handler = proxy == null ? null : proxy->Handler;
                if (!work.Confirmed && active && handler != null && handler->Info.EventId.Id == work.Source.EventID &&
                    handler->TransactionType == 1 && handler->TransactionItemId == work.ItemID &&
                    handler->TransactionItemCount == work.PendingQuantity && confirmation != null && confirmation->IsVisible)
                    work.Confirmed = ClickQuestButton((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)confirmation, confirmation->YesButton);
                return Environment.TickCount64 - work.ProgressAt > 30_000 ? "supply_purchase_unconfirmed" : null;
            }
        }
        if (!current.OwnsTravel && Environment.TickCount64 - work.ProgressAt > 180_000)
            return "supply_shop_timeout";
        if (active)
        {
            var addon = services.GameGUI.GetAddonByName<AddonShop>("Shop");
            if (addon == null || !addon->IsVisible)
                return "supply_shop_menu_unsupported";
            var proxy = ShopEventHandler.AgentProxy.Instance();
            var handler = proxy == null ? null : proxy->Handler;
            if (handler == null || handler->Info.EventId.Id != work.Source.EventID ||
                handler->ItemsCount is < 0 or > 60 || handler->VisibleItemsCount is < 0 or > 60)
                return "supply_shop_state_unavailable";
            if (handler->WaitingForTransactionToFinish || handler->StartingBuy)
                return null;
            for (var index = 0; index < handler->VisibleItemsCount; index++)
            {
                var itemIndex = handler->VisibleItems[index];
                if (itemIndex < 0 || itemIndex >= handler->ItemsCount)
                    return "supply_shop_state_unavailable";
                var item = handler->Items[itemIndex];
                if (item.ItemId != work.ItemID)
                    continue;
                var amount = Math.Min(Math.Min(work.Quantity - count, 99), item.StackSize);
                var inventory = InventoryManager.Instance();
                if (inventory == null || amount <= 0 || item.PriceBuy < 0)
                    return "supply_shop_state_unavailable";
                if ((ulong)item.PriceBuy * (uint)amount > inventory->GetGil())
                    return "gil_insufficient";
                if (inventory->GetEmptySlotsInBag() == 0 && count == 0)
                    return "inventory_full";
                work.CountBefore = count;
                work.PendingQuantity = amount;
                work.Confirmed = false;
                work.ProgressAt = Environment.TickCount64;
                handler->BuyItemIndex = itemIndex;
                handler->ExecuteBuy(amount);
                current.ActionSent = true;
                return null;
            }
            return "supply_shop_item_locked_or_unavailable";
        }
        var selector = EventHandlerSelector.Instance();
        if (work.ActorID != 0 && selector != null && selector->Target != null && selector->Target->GetGameObjectId() == work.ActorID)
        {
            for (var index = 0; index < selector->OptionsCount && index < selector->Options.Length; index++)
                if (selector->Options[index].Handler != null && selector->Options[index].Handler->Info.EventId.Id == work.Source.EventID)
                {
                    if (framework == null)
                        return "supply_shop_state_unavailable";
                    framework->InteractWithHandlerFromSelector(index);
                    return null;
                }
            return "supply_shop_event_unavailable";
        }
        if (services.Condition.IsOccupiedInEvent)
            return "external_interaction_busy";
        if (work.ActorID != 0)
            return null;
        IGameObject? actor = null;
        if (services.ClientState.TerritoryType == work.Source.TerritoryID)
            foreach (var candidate in services.ObjectTable)
                if (candidate.DataID == work.Source.NPCID && candidate.IsValid() && candidate.IsTargetable)
                {
                    if (actor is not null)
                        return "supply_location_unresolved";
                    actor = candidate;
                }
        string? locationError = null;
        var destination = actor is not null
            ? new QuestTravelTarget(work.Source.TerritoryID, actor.Position)
            : data.LocateSupply(work.Source.TerritoryID, work.Source.NPCID, out locationError);
        if (destination is null)
            return locationError ?? "supply_location_unresolved";
        var step = new QuestStep(QuestStepKind.Craft, work.Source.NPCID, destination.TerritoryID, destination.Position, work.ItemID,
            0, string.Empty, new());
        var navigation = Approach(current, step, destination.Position, 3);
        if (navigation is not null || current.OwnsTravel)
            return navigation;
        if (actor is null)
            return "target_not_found";
        work.ActorID = actor.GameObjectID;
        work.ProgressAt = Environment.TickCount64;
        TargetManager.Instance().InteractWithObject(actor);
        current.NextActionAt = Environment.TickCount64 + 600;
        return null;
    }

    private static unsafe bool IsSupplyShopEvent(ShopWork work)
    {
        var framework = EventFramework.Instance();
        return work.ActorID != 0 && framework != null && DService.Instance().ClientState.IsLoggedIn &&
            work.CharacterContentID == DalamudServices.PlayerState.ContentId && DService.Instance().Condition.IsOccupiedInEvent &&
            (framework->EventState1.EventId.Id == work.Source.EventID && framework->EventState1.ObjectId == work.ActorID ||
             framework->EventState2.EventId.Id == work.Source.EventID && framework->EventState2.ObjectId == work.ActorID);
    }

    private unsafe void StopSupplyShop(Session current)
    {
        if (shopWork is not { } work || work.Owner != current.Operation.OperationID)
            return;
        work.Stopping = true;
        if (!DService.Instance().ClientState.IsLoggedIn || work.CharacterContentID != DalamudServices.PlayerState.ContentId)
            return;
        CloseOwnedSelector(work.ActorID);
        if (!IsSupplyShopEvent(work))
            return;
        var confirmation = DService.Instance().GameGUI.GetAddonByName<AddonSelectYesno>("SelectYesno");
        if (work.PendingQuantity != 0 && confirmation != null && confirmation->IsVisible)
            ClickQuestButton((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)confirmation, confirmation->NoButton);
        var framework = EventFramework.Instance();
        var handler = framework == null ? null : framework->GetEventHandlerById(work.Source.EventID);
        if (handler != null)
            handler->CancelInteraction();
        var addon = DService.Instance().GameGUI.GetAddonByName<AddonShop>("Shop");
        if (addon != null && addon->IsVisible)
            addon->Close(true);
    }

    private bool DrainSupplyShop(Session current)
    {
        if (shopWork is not { } work || work.Owner != current.Operation.OperationID)
            return true;
        if (IsSupplyShopEvent(work))
            return false;
        shopWork = null;
        return true;
    }

    private sealed record QuestShopSource(uint NPCID, uint EventID, uint TerritoryID);

    private sealed class ShopWork(string owner, uint itemID, int quantity, QuestShopSource source)
    {
        public string Owner { get; } = owner;
        public uint ItemID { get; } = itemID;
        public int Quantity { get; } = quantity;
        public QuestShopSource Source { get; } = source;
        public ulong CharacterContentID { get; } = DalamudServices.PlayerState.ContentId;
        public long ProgressAt { get; set; } = Environment.TickCount64;
        public ulong ActorID { get; set; }
        public int CountBefore { get; set; }
        public int PendingQuantity { get; set; }
        public bool Confirmed { get; set; }
        public bool Stopping { get; set; }
    }
}
