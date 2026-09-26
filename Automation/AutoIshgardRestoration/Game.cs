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
    private static readonly GameInventoryType[] MainInventoryTypes =
    [
        GameInventoryType.Inventory1,
        GameInventoryType.Inventory2,
        GameInventoryType.Inventory3,
        GameInventoryType.Inventory4
    ];

    // The whitelist defines scope; client sheets supply all recipe metadata.

    private const uint SKYBUILDERS_SCRIP_ITEM_ID = 28063;

    private void DriveStartNPCEvent(uint[] npcIDs, AutomationPhase nextPhase, string label)
    {
        if (IsOccupied())
        {
            status = $"等待交互结束后打开{label}";
            return;
        }

        var npc = OmenTools.DService.Instance().ObjectTable
            .FirstOrDefault(o => npcIDs.Contains(GetBaseID(o.Address)) && o.IsTargetable);
        if (npc is null)
        {
            FailAutomation($"未找到{label}。");
            return;
        }

        if (!SendNPCEventStart(npc.Address))
        {
            FailAutomation($"无法打开{label}界面。");
            return;
        }

        EnterPhase(nextPhase);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
        status = $"正在打开{label}界面";
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

        var evt = default(AtkEvent);
        evt.Target = (AtkEventTarget*)node;
        evt.Listener = (AtkEventListener*)addon;
        var eventData = default(AtkEventData);
        addon->ReceiveEvent(registered->State.EventType, (int)registered->Param, &evt, &eventData);
        return true;
    }

    private static unsafe uint GetBaseID(nint address) =>
        address == nint.Zero ? 0 : ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address)->BaseId;

    private static unsafe bool SendNPCEventStart(nint address)
    {
        var gameObject = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address;
        if (gameObject == null || !TryResolveNPCEventID(gameObject, out var eventID))
        {
            return false;
        }

        var objectID = gameObject->GetGameObjectId();
        new EventStartPackt(objectID, eventID).Send();
        return true;
    }

    private static unsafe bool TryResolveNPCEventID(
        FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* gameObject,
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
        var condition = OmenTools.DService.Instance().Condition;
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
        var services = OmenTools.DService.Instance();
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

    private static bool TrySendCommand(string command)
    {
        try
        {
            return OmenTools.DService.Instance().Command.ProcessCommand(command);
        }
        catch (Exception ex)
        {
            // Command routing can be called by the alias before the game host has
            // finished initializing. Do not let logging turn a failed dispatch into
            // an unhandled exception in that window.
            try
            {
                DalamudServices.PluginLog?.Warning(ex, "AutoIshgardRestoration: failed to send command.");
            }
            catch
            {
                // The return value is the only useful result before host startup.
            }
            return false;
        }
    }

    private static string FormatPosition(Vector3 position) =>
        $"{position.X.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        $"{position.Y.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        position.Z.ToString("0.0", CultureInfo.InvariantCulture);

    private static unsafe AtkUnitBase* GetAddon(string name)
    {
        var addon = OmenTools.DService.Instance().GameGUI.GetAddonByName<AtkUnitBase>(name);
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
        var freeSlots = 0;
        var totalSlots = 0;
        var itemCount = 0;
        foreach (var type in MainInventoryTypes)
        {
            var items = OmenTools.DService.Instance().GameInventory.GetInventoryItems(type);
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
                }
            }
        }

        return new InventorySnapshot(freeSlots, totalSlots, itemCount);
    }

    private readonly record struct InventorySnapshot(int FreeSlots, int TotalSlots, int ItemCount);
}
