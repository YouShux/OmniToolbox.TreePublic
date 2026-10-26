using System.Globalization;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Packets.Upstream;
using GameEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;
using GameEventHandlerContent = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandlerContent;
using GameEventID = FFXIVClientStructs.FFXIV.Client.Game.Event.EventId;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private void DriveStartNPCEvent(uint[] npcIDs, AutomationPhase nextPhase, string label)
    {
        if (IsOccupied())
        {
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForNPCInteraction"), label);
            return;
        }

        var npc = DService.Instance().ObjectTable
            .FirstOrDefault(o => npcIDs.Contains(GetBaseID(o.Address)) && o.IsTargetable);
        if (npc is null)
        {
            FailAutomation(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.NPCNotFound"), label));
            return;
        }

        if (!SendNPCEventStart(npc.Address))
        {
            FailAutomation(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.NPCEventFailed"), label));
            return;
        }

        EnterPhase(nextPhase);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
        status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.OpeningNPCEvent"), label);
    }

    private static unsafe uint ReadSkybuildersScrips()
    {
        var manager = CurrencyManager.Instance();
        return manager == null ? 0 : manager->GetItemCount(SKYBUILDERS_SCRIP_ITEM_ID);
    }

    private static unsafe bool ClickButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (addon == null || button == null)
        {
            return false;
        }

        var node = button->AtkComponentBase.OwnerNode;
        if (node == null)
        {
            return false;
        }

        var registered = node->AtkResNode.AtkEventManager.Event;
        if (registered == null)
        {
            return false;
        }

        addon->ClickComponent(node, (int)registered->Param, registered->State.EventType);
        return true;
    }

    private static unsafe uint GetBaseID(nint address) =>
        address == nint.Zero ? 0 : ((GameObject*)address)->BaseId;

    private static unsafe bool SendNPCEventStart(nint address)
    {
        var gameObject = (GameObject*)address;
        if (gameObject == null || !TryResolveNPCEventID(gameObject, out var eventID))
        {
            return false;
        }

        var objectID = gameObject->GetGameObjectId();
        new EventStartPackt(objectID, eventID).Send();
        return true;
    }

    private static unsafe bool TryResolveNPCEventID(
        GameObject* gameObject,
        out GameEventID eventID)
    {
        eventID = default;

        if (gameObject->EventId.Id != 0)
        {
            eventID = gameObject->EventId;
            if (eventID.ContentId == GameEventHandlerContent.HwdDev)
            {
                return true;
            }
        }

        if (gameObject->EventHandler != null)
        {
            var primary = gameObject->EventHandler->GetEventId();
            if (primary.Id != 0)
            {
                if (primary.ContentId == GameEventHandlerContent.HwdDev)
                {
                    eventID = primary;
                    return true;
                }

                if (eventID.Id == 0)
                {
                    eventID = primary;
                }
            }
        }

        var handlers = stackalloc GameEventHandler*[32];
        var handlerCount = Math.Clamp(gameObject->GetEventHandlersImpl(handlers), 0, 32);
        for (var index = 0; index < handlerCount; index++)
        {
            var handler = handlers[index];
            if (handler == null)
            {
                continue;
            }

            var candidate = handler->GetEventId();
            if (candidate.Id == 0)
            {
                continue;
            }

            if (candidate.ContentId == GameEventHandlerContent.HwdDev)
            {
                eventID = candidate;
                return true;
            }

            if (eventID.Id == 0)
            {
                eventID = candidate;
            }
        }

        return eventID.Id != 0;
    }

    private static bool IsOccupied()
    {
        var condition = DService.Instance().Condition;
        return condition[ConditionFlag.Occupied]
            || condition[ConditionFlag.OccupiedInEvent]
            || condition[ConditionFlag.OccupiedInQuestEvent]
            || condition[ConditionFlag.Occupied30]
            || condition[ConditionFlag.Occupied33]
            || condition[ConditionFlag.Occupied38]
            || condition[ConditionFlag.Occupied39];
    }

    private static unsafe bool IsPlayerMovable()
    {
        var services = DService.Instance();
        var player = services.ObjectTable.LocalPlayer;
        if (!services.ClientState.IsLoggedIn || player is null)
        {
            return false;
        }

        var condition = services.Condition;
        return !condition[ConditionFlag.BetweenAreas]
            && !condition[ConditionFlag.BetweenAreas51]
            && !condition[ConditionFlag.Occupied]
            && !condition[ConditionFlag.Occupied30]
            && !condition[ConditionFlag.Occupied33]
            && !condition[ConditionFlag.Occupied38]
            && !condition[ConditionFlag.Occupied39]
            && !condition[ConditionFlag.OccupiedInEvent]
            && !condition[ConditionFlag.OccupiedInQuestEvent]
            && !condition[ConditionFlag.OccupiedInCutSceneEvent]
            && !condition[ConditionFlag.OccupiedSummoningBell]
            && !condition[ConditionFlag.WatchingCutscene]
            && !condition[ConditionFlag.WatchingCutscene78]
            && !condition[ConditionFlag.Unconscious]
            && !condition[ConditionFlag.Casting]
            && !condition[ConditionFlag.Casting87]
            && !condition[ConditionFlag.Mounting]
            && !condition[ConditionFlag.Mounting71]
            && !condition[ConditionFlag.BeingMoved]
            && !condition[ConditionFlag.InThatPosition]
            && GetAddon("NowLoading") == null
            && GetAddon("FadeMiddle") == null
            && GetAddon("FadeBack") == null;
    }

    private static string FormatPosition(Vector3 position) =>
        $"{position.X.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        $"{position.Y.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        position.Z.ToString("0.0", CultureInfo.InvariantCulture);

    private static unsafe AtkUnitBase* GetAddon(string name)
    {
        var addon = DService.Instance().GameGUI.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible ? addon : null;
    }

    private static unsafe void CloseAddon(string name)
    {
        var addon = GetAddon(name);
        if (addon != null)
        {
            addon->Close(true);
        }
    }

    private static InventorySnapshot ReadInventory(uint itemID)
    {
        var rewards = default(ScripRewards);
        foreach (var recipe in Recipes)
        {
            if (recipe.ItemID != itemID) continue;
            rewards = recipe.Rewards;
            break;
        }

        var freeSlots = 0;
        var totalSlots = 0;
        var itemCount = 0;
        var pendingScrips = 0;
        foreach (var type in MainInventoryTypes)
        {
            var items = DService.Instance().GameInventory.GetInventoryItems(type);
            totalSlots += items.Length;
            foreach (ref readonly var item in items)
            {
                if (item.IsEmpty)
                {
                    freeSlots++;
                }
                else if (item.BaseItemId == itemID)
                {
                    itemCount += item.Quantity;
                    if (item.IsCollectable)
                        pendingScrips += rewards.GetScrips(item.SpiritbondOrCollectability) * item.Quantity;
                }
            }
        }

        return new InventorySnapshot(freeSlots, totalSlots, itemCount, pendingScrips);
    }

    private readonly record struct InventorySnapshot(int FreeSlots, int TotalSlots, int ItemCount, int PendingScrips);

    private static readonly GameInventoryType[] MainInventoryTypes =
    [
        GameInventoryType.Inventory1,
        GameInventoryType.Inventory2,
        GameInventoryType.Inventory3,
        GameInventoryType.Inventory4
    ];

    #region 常量

    private const uint SKYBUILDERS_SCRIP_ITEM_ID = 28063;

    #endregion
}
