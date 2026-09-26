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
    private ICallGateSubscriber<ushort, int, object>? craftItem;

    private ICallGateSubscriber<bool>? artisanIsBusy;

    private ICallGateSubscriber<bool>? getStopRequest;

    private ICallGateSubscriber<bool, object>? setStopRequest;

    private bool artisanStartedByModule;

    private int craftCycleStartingItemCount;

    private void InitializeArtisan()
    {
        craftItem = DalamudServices.PluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
        artisanIsBusy = DalamudServices.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
        getStopRequest = DalamudServices.PluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
        setStopRequest = DalamudServices.PluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetStopRequest");
    }

    private void ReleaseArtisan()
    {
        craftItem = null;
        artisanIsBusy = null;
        getStopRequest = null;
        setStopRequest = null;
    }

    private void StartCraftingCycle(RecipeOption recipe, string reason)
    {
        var snapshot = ReadInventory(recipe.ItemID);
        if (ShouldStop(snapshot, out var stopReason))
        {
            BeginSubmission(stopReason);
            return;
        }

        if (artisanIsBusy?.InvokeFunc() == true)
        {
            FailAutomation("Artisan 正在执行其他任务，无法开始下一轮生产。");
            return;
        }

        if (getStopRequest?.InvokeFunc() == true)
        {
            setStopRequest?.InvokeAction(false);
        }

        var amount = config.StopMode == 1
            ? Math.Max(1, config.TargetItemCount - snapshot.ItemCount)
            : 9999;
        craftCycleStartingItemCount = snapshot.ItemCount;
        if (craftItem is null)
        {
            FailAutomation("Artisan 插件接口不可用。");
            return;
        }

        artisanStartedByModule = true;
        craftItem.InvokeAction((ushort)recipe.RecipeID, amount);
        EnterPhase(AutomationPhase.WaitArtisanStart);
        status = $"{reason}；正在启动 Artisan 制作 {recipe.ItemName}";
    }

    private void RequestArtisanStop()
    {
        if (!artisanStartedByModule)
        {
            return;
        }

        if (getStopRequest?.InvokeFunc() != true)
        {
            setStopRequest?.InvokeAction(true);
        }
    }

    private void StopOwnedArtisan()
    {
        if (!artisanStartedByModule)
        {
            return;
        }

        try
        {
            if (getStopRequest?.InvokeFunc() != true)
            {
                setStopRequest?.InvokeAction(true);
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to stop owned Artisan production.");
            lastError = $"停止 Artisan 失败：{ex.Message}";
        }
        finally
        {
            artisanStartedByModule = false;
        }
    }
}
