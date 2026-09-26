using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.Dalamud;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Info.Game.Packets.Upstream;
using GameEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;
using GameEventHandlerContent = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandlerContent;
using GameEventID = FFXIVClientStructs.FFXIV.Client.Game.Event.EventId;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

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

    private ScripProduct? SelectedScripProduct() => scripProducts.FirstOrDefault(
        x => x.ShopID == config.ScripShopID && x.ItemID == config.ScripItemID);

    private static bool MatchesScripSearch(string name, string search) =>
        name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool DrawScripPurchaseSettings()
    {
        ImGui.Separator();
        var changed = false;
        using (ImRaii.Disabled(running))
        {
            var enabled = config.AutoBuyScrips;
            if (OmniControls.Checkbox("天穹街振兴票自动购买", ref enabled))
            {
                config.AutoBuyScrips = enabled;
                changed = true;
            }
            if (!enabled) return changed;
            EnsureScripCatalog();
            ImGui.TextUnformatted($"天穹街振兴票：{ReadSkybuildersScrips()} / 10000");
            ImGui.TextUnformatted("天穹街振兴票达到数量时开始购买");
            var threshold = config.ScripThreshold;
            ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
            if (OmniControls.InputInt("##scripThreshold", ref threshold))
            {
                config.ScripThreshold = Math.Clamp(threshold, 1, 10000);
                changed = true;
            }
            changed |= ImGui.IsItemDeactivatedAfterEdit();

            var selected = SelectedScripProduct();
            ImGui.SetNextItemWidth(GetLabeledControlWidth(520f, "购买物品"));
            var popupHeight = MathF.Max(1f, MathF.Min(OmniTheme.Scale(360f), ImGui.GetMainViewport().WorkSize.Y * 0.8f));
            ImGui.SetNextWindowSizeConstraints(new Vector2(0f, popupHeight), new Vector2(float.MaxValue, popupHeight));
            using (var combo = ImRaii.Combo("购买物品", selected is null ? "请选择购买物品" : $"{selected.Name}（{selected.Price} 票）"))
            {
                if (combo)
                {
                    ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
                    if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
                    var searchChanged = OmniControls.InputTextWithHint(
                        "##scripProductSearch", "输入物品名称搜索", ref scripProductSearch, 128,
                        ImGui.GetContentRegionAvail().X);
                    ImGui.Separator();
                    var productSelected = false;
                    // Keep the search field visible while only the results scroll.
                    using (var results = ImRaii.Child("##scripProductResults",
                               new Vector2(0f, MathF.Max(1f, ImGui.GetContentRegionAvail().Y))))
                    {
                        if (results)
                        {
                            if (searchChanged) ImGui.SetScrollY(0f);
                            var hasMatches = false;
                            ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
                            foreach (var product in scripProducts)
                            {
                                if (!MatchesScripSearch(product.Name, scripProductSearch)) continue;
                                hasMatches = true;
                                if (OmniControls.RoundedSelectable($"{product.Name}（{product.Price} 票）##{product.ShopID}-{product.ItemID}",
                                        selected == product, size: new Vector2(0f, OmniTheme.SmallButtonSize().Y)))
                                {
                                    config.ScripShopID = product.ShopID;
                                    config.ScripItemID = product.ItemID;
                                    selected = product;
                                    config.ScripQuantity = Math.Min(Math.Max(1, config.ScripQuantity), Math.Max(1, GetConfiguredScripLimit(product)));
                                    changed = true;
                                    productSelected = true;
                                }
                            }
                            if (!hasMatches) ImGui.TextDisabled("没有匹配的物品");
                        }
                    }
                    if (productSelected) ImGui.CloseCurrentPopup();
                }
            }

            if (!string.IsNullOrEmpty(scripCatalogError)) ImGui.TextWrapped(scripCatalogError);
            var limit = selected is null ? 0 : GetConfiguredScripLimit(selected);
            ImGui.TextUnformatted($"每次购买数量（按触发票数最多 {limit} 个）");
            // Configuration uses the trigger budget; live currency/capacity is checked only when buying.
            var quantity = limit == 0 ? 0 : Math.Clamp(config.ScripQuantity, 1, limit);
            if (limit > 0 && quantity != config.ScripQuantity)
            {
                config.ScripQuantity = quantity;
                changed = true;
            }
            ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
            using (ImRaii.Disabled(limit == 0))
            {
                if (OmniControls.InputInt("##scripQuantity", ref quantity) && limit > 0)
                {
                    config.ScripQuantity = Math.Clamp(quantity, 1, limit);
                    changed = true;
                }
                changed |= ImGui.IsItemDeactivatedAfterEdit();
            }
            if (selected is not null)
                ImGui.TextUnformatted($"预计花费：{(long)quantity * selected.Price} 票");
        }
        return changed;
    }

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
            foreach (ref readonly var item in OmenTools.DService.Instance().GameInventory.GetInventoryItems(type))
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
        var product = SelectedScripProduct();
        if (product is null)
        {
            FailAutomation("请先选择振兴票购买物品。");
            return;
        }
        var amount = Math.Min(GetConfiguredScripLimit(product),
            Math.Min(Math.Max(1, config.ScripQuantity), GetScripPurchaseLimit(product)));
        if (amount <= 0)
        {
            FailAutomation("无法购买所选物品：触发票数或余额不足、背包已满或已持有唯一物品。");
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
        status = "暂停提交，准备购买振兴票商品";
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
        var services = OmenTools.DService.Instance();
        var product = pendingScripProduct;
        if (product is null || !services.ClientState.IsLoggedIn ||
            services.ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID ||
            services.Condition[ConditionFlag.BetweenAreas] || services.Condition[ConditionFlag.BetweenAreas51] ||
            services.Condition[ConditionFlag.InCombat])
        {
            FailAutomation("当前状态无法继续购买，已停止。");
            return;
        }
        if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
        {
            FailAutomation(phase == AutomationPhase.VerifyScripPurchase
                ? "未能确认购买结果，已停止以避免重复购买。请检查振兴票和背包。"
                : "购买流程等待超时，已停止。请检查游戏提示。");
            return;
        }
        if (DateTime.UtcNow < nextActionAt) return;
        switch (phase)
        {
            case AutomationPhase.PrepareScripPurchase:
                CloseAddon("Request");
                CloseAddon("HWDSupply");
                if (IsOccupied() || GetAddon("HWDSupply") != null || GetAddon("Request") != null) return;
                if (!IsPlayerMovable() || IsScripShopActive() || HasScripTransactionDialog()) return;
                var player = services.ObjectTable.LocalPlayer;
                if (player is null) return;
                // Directly start the SpecialShop event without pathfinding or NPC interaction.
                scripEventOwned = true;
                var entityID = ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address)->EntityId;
                new EventStartPackt(entityID, product.ShopID).Send();
                EnterPhase(AutomationPhase.OpenScripShop);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
                status = "正在打开振兴票商店";
                break;
            case AutomationPhase.OpenScripShop:
                if (!IsScripShopActive()) return;
                EnterPhase(AutomationPhase.BuyScripItem);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
                break;
            case AutomationPhase.BuyScripItem:
                if (HasScripTransactionDialog()) return;
                var agent = AgentShop.Instance();
                if (agent == null || !agent->IsAgentActive() || agent->ItemReceive == null) return;
                scripSelectedIndex = -1;
                for (var i = 0; i < agent->ItemReceiveSpan.Length; i++)
                    if (agent->ItemReceiveSpan[i].ItemId == product.ItemID) { scripSelectedIndex = i; break; }
                if (scripSelectedIndex < 0) return;
                scripBatch = Math.Min(Math.Min(scripRemaining, GetScripPurchaseLimit(product)), Math.Min(99, product.StackSize));
                if (scripBatch <= 0)
                {
                    FailAutomation("购买条件已变化，余额或背包空间不足，已停止。");
                    return;
                }
                scripBalanceBefore = ReadSkybuildersScrips();
                scripItemCountBefore = ReadInventory(product.ItemID).ItemCount;
                scripQuantitySent = scripConfirmSent = false;
                // Transition before dispatch: another plugin may confirm synchronously.
                EnterPhase(AutomationPhase.VerifyScripPurchase);
                AgentId.Shop.SendEvent(1, 0, scripSelectedIndex, scripBatch, 0);
                status = $"正在购买：{product.Name} × {scripBatch}";
                nextActionAt = DateTime.UtcNow.AddMilliseconds(400);
                break;
            case AutomationPhase.VerifyScripPurchase:
                if (ScripPurchaseApplied(scripBalanceBefore, ReadSkybuildersScrips(), scripItemCountBefore,
                        ReadInventory(product.ItemID).ItemCount, product.Price, scripBatch))
                {
                    scripPurchased += scripBatch;
                    scripRemaining -= scripBatch;
                    EnterPhase(scripRemaining > 0 ? AutomationPhase.BuyScripItem : AutomationPhase.CloseScripShop);
                    nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
                    status = $"已购买：{product.Name} × {scripPurchased}";
                    return;
                }
                ConfirmScripPurchase(product);
                break;
            case AutomationPhase.CloseScripShop:
                EndScripEvent();
                if (IsScripShopActive() || IsOccupied() || HasScripTransactionDialog() || GetAddon("ShopExchangeCurrency") != null) return;
                pendingScripProduct = null;
                scripEventOwned = false;
                var recipe = FindRecipe(activeRecipeID);
                if (recipe is null) { FailAutomation("生产配置已变化，请重新启动。"); return; }
                if (scripVouchersBefore >= config.TicketThreshold)
                {
                    ticketsToPlay = scripVouchersBefore;
                    EnterPhase(AutomationPhase.MoveToLottery);
                    status = "购买完成，继续库啵好运道";
                }
                else if (ReadInventory(activeItemID).ItemCount > 0 || scripVouchersBefore < 0)
                {
                    EnterPhase(AutomationPhase.MoveToAppraiser);
                    status = "购买完成，继续提交物品";
                }
                else BeginNextCraftingCycle("振兴票购买完成");
                break;
        }
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
            // The prompt and item label can be separate nodes and vary by client language.
            // Match the pending transaction and selected item ID above, not localized dialog text.
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
            try { EndScripEvent(); }
            catch (Exception ex)
            {
                // Log cleanup failures without retrying the purchase or hiding the original error.
                DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to close owned scrip shop event.");
            }
        }
        scripEventOwned = false;
        pendingScripProduct = null;
    }
}
