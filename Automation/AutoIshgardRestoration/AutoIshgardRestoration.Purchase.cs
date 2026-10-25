using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Packets.Upstream;
using OmniToolbox.UI;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private string scripProductSearch = string.Empty;

    private ScripProduct? pendingScripProduct;

    private bool scripEventOwned;

    private bool scripEventEnded;

    private bool scripQuantitySent;

    private bool scripConfirmSent;

    private int scripRemaining;

    private int scripPurchased;

    private int scripBatch;

    private int scripSelectedIndex = -1;

    private uint scripBalanceBefore;

    private int scripItemCountBefore;

    private int scripVouchersBefore = -1;

    private int turnInGeneration;

    private int purchasedGeneration = -1;

    private Dictionary<AutomationPhase, Action<ScripProduct>> scripHandlers = [];

    private void EnsureScripHandlers()
    {
        if (scripHandlers.Count != 0) return;
        scripHandlers = new()
        {
            [AutomationPhase.PrepareScripPurchase] = DrivePrepareScripPurchase,
            [AutomationPhase.OpenScripShop] = DriveOpenScripShop,
            [AutomationPhase.BuyScripItem] = DriveBuyScripItem,
            [AutomationPhase.VerifyScripPurchase] = DriveVerifyScripPurchase,
            [AutomationPhase.CloseScripShop] = DriveCloseScripShop
        };
    }

    private ScripProduct? GetSelectedScripProduct() => scripProducts.FirstOrDefault(
        x => x.ShopID == config.ScripShopID && x.ItemID == config.ScripItemID);

    private int GetConfiguredScripLimit(ScripProduct product) =>
        CalculateConfiguredScripLimit(config.ScripThreshold, product.Price, product.Unique);

    private static int CalculateConfiguredScripLimit(int threshold, int price, bool unique) =>
        CalculateScripLimit(Math.Clamp(threshold, 1, 10000), price, int.MaxValue, unique, 0);

    private static int CalculateScripLimit(int balance, int price, int capacity, bool unique, int owned)
    {
        if (balance < 0 || price <= 0 || capacity <= 0 || owned < 0) return 0;
        var limit = Math.Min(Math.Clamp(balance, 0, 10000) / price, capacity);
        return unique ? Math.Min(limit, owned == 0 ? 1 : 0) : limit;
    }

    private static unsafe int GetScripPurchaseLimit(ScripProduct product)
    {
        var capacity = 0;
        var owned = 0;
        foreach (var type in MainInventoryTypes)
        {
            foreach (ref readonly var item in DService.Instance().GameInventory.GetInventoryItems(type))
            {
                if (item.IsEmpty) capacity += product.StackSize;
                else if (item.BaseItemId == product.ItemID)
                {
                    owned += item.Quantity;
                    if (!item.IsHq) capacity += Math.Max(0, product.StackSize - item.Quantity);
                }
            }
        }
        if (product.Unique)
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null) return 0;
            owned = Math.Max(owned, inventory->GetInventoryItemCount(product.ItemID, false, true, true, 0) +
                                    inventory->GetInventoryItemCount(product.ItemID, true, true, true, 0));
        }
        return CalculateScripLimit((int)ReadSkybuildersScrips(), product.Price, capacity, product.Unique, owned);
    }

    private bool IsScripPurchasePhase() => phase is AutomationPhase.PrepareScripPurchase or AutomationPhase.OpenScripShop
        or AutomationPhase.BuyScripItem or AutomationPhase.VerifyScripPurchase or AutomationPhase.CloseScripShop;

    private bool TryBeginAutoScripPurchase()
    {
        if (!config.AutoBuyScrips || purchasedGeneration == turnInGeneration ||
            ReadSkybuildersScrips() < Math.Clamp(config.ScripThreshold, 1, 10000)) return false;
        purchasedGeneration = turnInGeneration;
        BeginScripPurchase();
        return true;
    }

    private unsafe void BeginScripPurchase()
    {
        EnsureScripCatalog();
        var product = GetSelectedScripProduct();
        if (product is null)
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseItemRequired"));
            return;
        }
        var amount = Math.Min(GetConfiguredScripLimit(product),
            Math.Min(Math.Max(1, config.ScripQuantity), GetScripPurchaseLimit(product)));
        if (amount <= 0)
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseUnavailable"));
            return;
        }
        pendingScripProduct = product;
        scripRemaining = amount;
        scripPurchased = 0;
        scripVouchersBefore = TryReadVoucherCount(out var vouchers, out _) ? vouchers : -1;
        scripEventOwned = scripEventEnded = false;
        running = true;
        EnterPhase(AutomationPhase.PrepareScripPurchase);
        nextCheckAt = nextActionAt = DateTime.UtcNow;
        status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchasePreparing");
    }

    private static unsafe bool IsScripShopActive()
    {
        var agent = AgentShop.Instance();
        return agent != null && agent->IsAgentActive();
    }

    private static unsafe bool HasScripTransactionDialog() =>
        GetAddon("ShopExchangeCurrencyDialog") != null || GetAddon("SelectYesno") != null;

    private unsafe void DriveScripPurchase()
    {
        EnsureScripHandlers();
        var services = DService.Instance();
        var product = pendingScripProduct;
        if (product is null || !services.ClientState.IsLoggedIn ||
            services.ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID ||
            services.Condition[ConditionFlag.BetweenAreas] || services.Condition[ConditionFlag.BetweenAreas51] ||
            services.Condition[ConditionFlag.InCombat])
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseStateInvalid"));
            return;
        }
        if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
        {
            FailAutomation(phase == AutomationPhase.VerifyScripPurchase
                ? OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseResultUnknown")
                : OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseTimedOut"));
            return;
        }
        if (DateTime.UtcNow < nextActionAt) return;
        if (scripHandlers.TryGetValue(phase, out var handler))
        {
            handler(product);
        }
    }

    private unsafe void DrivePrepareScripPurchase(ScripProduct product)
    {
        CloseAddon("Request");
        CloseAddon("HWDSupply");
        if (IsOccupied() || GetAddon("HWDSupply") != null || GetAddon("Request") != null) return;
        if (!IsPlayerMovable() || IsScripShopActive() || HasScripTransactionDialog()) return;
        var player = DService.Instance().ObjectTable.LocalPlayer;
        if (player is null) return;
        scripEventOwned = true;
        var entityID = ((GameObject*)player.Address)->EntityId;
        new EventStartPackt(entityID, product.ShopID).Send();
        EnterPhase(AutomationPhase.OpenScripShop);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseShopOpening");
    }

    private void DriveOpenScripShop(ScripProduct product)
    {
        if (!IsScripShopActive()) return;
        EnterPhase(AutomationPhase.BuyScripItem);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
    }

    private unsafe void DriveBuyScripItem(ScripProduct product)
    {
        if (HasScripTransactionDialog()) return;
        var agent = AgentShop.Instance();
        if (agent == null || !agent->IsAgentActive() || agent->ItemReceive == null) return;
        scripSelectedIndex = -1;
        for (var i = 0; i < agent->ItemReceiveSpan.Length; i++)
        {
            if (agent->ItemReceiveSpan[i].ItemId == product.ItemID)
            {
                scripSelectedIndex = i;
                break;
            }
        }
        if (scripSelectedIndex < 0) return;
        scripBatch = Math.Min(Math.Min(scripRemaining, GetScripPurchaseLimit(product)), Math.Min(99, product.StackSize));
        if (scripBatch <= 0)
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseConditionsChanged"));
            return;
        }
        scripBalanceBefore = ReadSkybuildersScrips();
        scripItemCountBefore = ReadInventory(product.ItemID).ItemCount;
        scripQuantitySent = scripConfirmSent = false;
        EnterPhase(AutomationPhase.VerifyScripPurchase);
        AgentId.Shop.SendEvent(1, 0, scripSelectedIndex, scripBatch, 0);
        status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.Purchasing"), product.Name, scripBatch);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(400);
    }

    private void DriveVerifyScripPurchase(ScripProduct product)
    {
        if (ScripPurchaseApplied(scripBalanceBefore, ReadSkybuildersScrips(), scripItemCountBefore,
                ReadInventory(product.ItemID).ItemCount, product.Price, scripBatch))
        {
            scripPurchased += scripBatch;
            scripRemaining -= scripBatch;
            EnterPhase(scripRemaining > 0 ? AutomationPhase.BuyScripItem : AutomationPhase.CloseScripShop);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.Purchased"), product.Name, scripPurchased);
            return;
        }
        ConfirmScripPurchase(product);
    }

    private unsafe void DriveCloseScripShop(ScripProduct product)
    {
        EndScripEvent();
        if (IsScripShopActive() || IsOccupied() || HasScripTransactionDialog() || GetAddon("ShopExchangeCurrency") != null) return;
        pendingScripProduct = null;
        scripEventOwned = false;
        var recipe = FindRecipe(activeRecipeID);
        if (recipe is null)
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ProductionConfigChanged"));
            return;
        }
        if (scripVouchersBefore >= config.TicketThreshold)
        {
            ticketsToPlay = scripVouchersBefore;
            EnterPhase(AutomationPhase.MoveToLottery);
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseContinueLottery");
        }
        else if (ReadInventory(activeItemID).ItemCount > 0 || scripVouchersBefore < 0)
        {
            EnterPhase(AutomationPhase.MoveToAppraiser);
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseContinueSubmission");
        }
        else BeginNextCraftingCycle(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseComplete"));
    }

    private static bool ScripPurchaseApplied(uint beforeBalance, uint nowBalance, int beforeItems, int nowItems, int price, int quantity) =>
        price > 0 && quantity > 0 && beforeBalance >= (long)price * quantity &&
        nowBalance == beforeBalance - (long)price * quantity && nowItems == beforeItems + (long)quantity;

    private unsafe void ConfirmScripPurchase(ScripProduct product)
    {
        var agent = AgentShop.Instance();
        if (phase != AutomationPhase.VerifyScripPurchase || !scripEventOwned || scripEventEnded || agent == null || !agent->IsAgentActive() ||
            agent->SelectedItemIndex != scripSelectedIndex || scripSelectedIndex < 0 ||
            scripSelectedIndex >= agent->ItemReceiveSpan.Length ||
            agent->ItemReceiveSpan[scripSelectedIndex].ItemId != product.ItemID) return;
        var dialog = GetAddon("ShopExchangeCurrencyDialog");
        if (!scripQuantitySent && dialog != null && dialog->IsReady)
        {
            scripQuantitySent = true;
            dialog->Callback(0, scripBatch);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(400);
            return;
        }
        var yesNo = (AddonSelectYesno*)GetAddon("SelectYesno");
        if (!scripConfirmSent && yesNo != null && yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
        {
            scripConfirmSent = true;
            if (!ClickButton((AtkUnitBase*)yesNo, yesNo->YesButton)) scripConfirmSent = false;
            nextActionAt = DateTime.UtcNow.AddMilliseconds(400);
        }
    }

    private unsafe void EndScripEvent()
    {
        if (!scripEventOwned || scripEventEnded || pendingScripProduct is not { } product) return;
        scripEventEnded = true;
        new EventCompletePackt(product.ShopID, 0).Send();
        CloseAddon("ShopExchangeCurrencyDialog");
        CloseAddon("ShopExchangeCurrency");
    }

    private void CleanupScripPurchase()
    {
        if (scripEventOwned)
        {
            EndScripEvent();
        }
        scripEventOwned = false;
        pendingScripProduct = null;
    }
}
