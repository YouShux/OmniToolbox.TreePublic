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
    private bool vnavPathStartedByModule;

    private DateTime? movableSince;

    private Vector3 initialDestination;

    private const uint FIRMAMENT_TERRITORY_ID = 886;

    private const string FIRMAMENT_TELEPORT_COMMAND = "/omni bts 无名众人广场";

    private void DriveWaitForFirmament()
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType == FIRMAMENT_TERRITORY_ID)
        {
            movableSince = null;
            EnterPhase(AutomationPhase.WaitForPlayerMovable);
            status = "已进入天穹街，等待玩家连续可动 1 秒";
            return;
        }

        status = "等待进入天穹街（区域 886）";
        if (PhaseTimedOut(TimeSpan.FromMinutes(3)))
        {
            FailAutomation("执行传送指令后，3 分钟内未进入天穹街（区域 886）。");
        }
    }

    private void DriveWaitForPlayerMovable()
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            movableSince = null;
            status = "区域尚未稳定，等待进入天穹街（区域 886）";
            return;
        }

        if (!IsPlayerMovable())
        {
            movableSince = null;
            status = "已进入天穹街，等待玩家恢复可动";
            return;
        }

        movableSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - movableSince.Value < TimeSpan.FromSeconds(1))
        {
            status = "玩家已可动，等待状态持续 1 秒";
            return;
        }

        if (!vnavmeshIPC.IsPluginEnabled())
        {
            FailAutomation("vnavmesh 未安装或未启用。");
            return;
        }

        if (!vnavmeshIPC.GetIsNavReady())
        {
            status = "玩家已可动，等待 vnavmesh 导航网格准备完成";
            if (PhaseTimedOut(TimeSpan.FromSeconds(90)))
            {
                FailAutomation("进入天穹街后，vnavmesh 导航网格未能准备完成。");
            }
            return;
        }

        initialDestination = new Vector3(
            38f + (Random.Shared.NextSingle() * 9f),
            -16f,
            162f + (Random.Shared.NextSingle() * 14f));
        try
        {
            vnavmeshIPC.PathfindAndMoveTo(initialDestination, false);
            vnavPathStartedByModule = true;
        }
        catch (Exception ex)
        {
            FailAutomation($"无法执行初始导航：{ex.Message}");
            return;
        }
        EnterPhase(AutomationPhase.MoveToInitialPoint);
        nextActionAt = DateTime.UtcNow.AddSeconds(5);
        status = $"正在前往随机生产点：{FormatPosition(initialDestination)}";
    }

    private void DriveMoveToInitialPoint(RecipeOption recipe)
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            FailAutomation("前往随机生产点时离开了天穹街（区域 886）。");
            return;
        }

        var player = OmenTools.DService.Instance().ObjectTable.LocalPlayer;
        if (player is null)
        {
            status = "等待读取玩家位置";
            return;
        }

        if (Vector3.Distance(player.Position, initialDestination) <= 2f)
        {
            StopOwnedVnavPath();
            StartCraftingCycle(recipe, "已到达随机生产点");
            return;
        }

        if (!vnavmeshIPC.GetIsPathfindRunning() && DateTime.UtcNow >= nextActionAt)
        {
            try
            {
                vnavmeshIPC.PathfindAndMoveTo(initialDestination, false);
                vnavPathStartedByModule = true;
            }
            catch (Exception ex)
            {
                FailAutomation($"无法再次执行初始导航：{ex.Message}");
                return;
            }
            nextActionAt = DateTime.UtcNow.AddSeconds(5);
        }

        status = $"正在前往随机生产点：{FormatPosition(initialDestination)}";
        if (PhaseTimedOut(TimeSpan.FromSeconds(90)))
        {
            FailAutomation("前往随机生产点超时。");
        }
    }

    private void StopOwnedVnavPath()
    {
        if (!vnavPathStartedByModule)
        {
            return;
        }

        try
        {
            vnavmeshIPC.StopPathfind();
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to stop owned navigation.");
            lastError = $"停止导航失败：{ex.Message}";
        }
        finally
        {
            vnavPathStartedByModule = false;
        }
    }
}
