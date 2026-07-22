using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.OmenService;
using IGameObject = OmenTools.Dalamud.Services.ObjectTable.Abstractions.ObjectKinds.IGameObject;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static unsafe class NpcShopPurchaseNativeUi
{
    public static bool IsShopOpen()
    {
        var shop = Addons.Shop;
        return shop != null && shop->IsAddonAndNodesReady();
    }

    public static bool IsShopMenuOpen()
    {
        var iconMenu = Addons.SelectIconString;
        if (iconMenu != null && iconMenu->IsAddonAndNodesReady())
        {
            return true;
        }

        var stringMenu = Addons.SelectString;
        return stringMenu != null && stringMenu->IsAddonAndNodesReady();
    }

    public static bool TrySelectMenu(string text) =>
        AddonSelectIconStringEvent.Select(text) || AddonSelectStringEvent.Select(text);

    public static bool TryInteract(uint npcId)
    {
        var services = DService.Instance();
        var player = services.ObjectTable.LocalPlayer;
        if (player is null)
        {
            return false;
        }

        IGameObject? target = null;
        var bestDistanceSquared = float.MaxValue;
        foreach (var gameObject in services.ObjectTable)
        {
            if (!gameObject.IsValid() ||
                !gameObject.IsTargetable ||
                gameObject.ObjectKind != ObjectKind.EventNpc ||
                gameObject.DataID != npcId)
            {
                continue;
            }

            var distanceSquared = Vector3.DistanceSquared(player.Position, gameObject.Position);
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            target = gameObject;
            bestDistanceSquared = distanceSquared;
        }

        return target.Interact();
    }

    public static PurchaseTriggerResult TriggerPurchase(uint itemId, uint quantity)
    {
        var shop = Addons.Shop;
        var agent = AgentShop.Instance();
        if (shop == null ||
            !shop->IsAddonAndNodesReady() ||
            agent == null ||
            !agent->IsAgentActive())
        {
            return PurchaseTriggerResult.NotReady;
        }

        var items = agent->ItemReceiveSpan;
        for (var index = 0; index < items.Length; index++)
        {
            if (items[index].ItemId != itemId)
            {
                continue;
            }

            shop->Callback(0, index, (int)quantity, 0);
            return PurchaseTriggerResult.Triggered;
        }

        return PurchaseTriggerResult.ItemNotFound;
    }

    public static bool TryConfirmPurchase()
    {
        if (AddonSelectYesnoEvent.ClickYes())
        {
            return true;
        }

        var dialog = Addons.ShopExchangeItemDialog;
        if (dialog == null || !dialog->IsAddonAndNodesReady())
        {
            return false;
        }

        dialog->Callback(0);
        return true;
    }

    public static void CancelPurchaseDialog()
    {
        if (AddonSelectYesnoEvent.ClickNo())
        {
            return;
        }

        var dialog = Addons.ShopExchangeItemDialog;
        if (dialog != null && dialog->IsVisible)
        {
            dialog->Close(true);
        }
    }

    public static void CloseShop()
    {
        var shop = Addons.Shop;
        if (shop != null && shop->IsVisible)
        {
            shop->Close(true);
        }
    }
}

internal enum PurchaseTriggerResult
{
    NotReady,
    ItemNotFound,
    Triggered
}
