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
    private DateTime nextCheckAt;

    private DateTime phaseStartedAt;

    private DateTime nextActionAt;

    private bool running;

    private uint activeRecipeID;

    private uint activeItemID;

    private AutomationPhase phase;

    private string status = "待机";

    private string lastError = string.Empty;

    private void StartProduction()
    {
        if (running)
        {
            return;
        }

        lastError = string.Empty;
        var playerState = DalamudServices.PlayerState;
        if (!playerState.IsLoaded)
        {
            Fail("角色尚未登录。");
            return;
        }

        var currentJobID = playerState.ClassJob.RowId;
        if (currentJobID is < 8 or > 15)
        {
            Fail("当前不是生产职业，请切换职业。");
            return;
        }

        var recipe = FindRecipe(config.SelectedRecipeID);
        if (recipe is null || recipe.Value.JobID != currentJobID)
        {
            Fail("请先选择你要生产的物品。");
            return;
        }

        if (playerState.Level < recipe.Value.Level)
        {
            Fail($"当前职业等级不足，需要 {recipe.Value.Level} 级。");
            return;
        }

        try
        {
            if (artisanIsBusy?.InvokeFunc() == true)
            {
                Fail("Artisan 正在执行其他任务。");
                return;
            }

            running = true;
            turnInGeneration = 0;
            purchasedGeneration = -1;
            activeRecipeID = recipe.Value.RecipeID;
            activeItemID = recipe.Value.ItemID;
            nextCheckAt = DateTime.UtcNow;

            if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
            {
                if (!TrySendCommand(FIRMAMENT_TELEPORT_COMMAND))
                {
                    FailAutomation("无法执行传送指令 /omni bts 无名众人广场。");
                    return;
                }

                EnterPhase(AutomationPhase.WaitForFirmament);
                status = "正在前往无名众人广场，等待进入天穹街（区域 886）";
                return;
            }

            StartCraftingCycle(recipe.Value, "开始");
        }
        catch (Exception ex)
        {
            FailAutomation($"无法启动自动流程：{ex.Message}");
        }
    }

    private void StopProduction(string reason)
    {
        CleanupScripPurchase();
        StopOwnedArtisan();
        StopOwnedVnavPath();

        running = false;
        phase = AutomationPhase.Idle;
        activeRecipeID = 0;
        activeItemID = 0;
        ticketsToPlay = 0;
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        craftCycleStartingItemCount = 0;
        movableSince = null;
        initialDestination = default;
        artisanStartedByModule = false;
        vnavPathStartedByModule = false;
        status = reason;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (startPending)
        {
            startPending = false;
            if (!IsEnabled)
            {
                return;
            }

            try
            {
                InitializeArtisan();
                StartProduction();
            }
            catch (Exception ex) { FailAutomation($"无法启动自动流程：{ex.Message}"); }
            return;
        }
        if (!running || DateTime.UtcNow < nextCheckAt)
        {
            return;
        }

        nextCheckAt = DateTime.UtcNow.AddMilliseconds(100);
        try
        {
            UpdateVoucherCount(DateTime.UtcNow);
            if (IsScripPurchasePhase())
            {
                DriveScripPurchase();
                return;
            }

            var activeRecipe = FindRecipe(activeRecipeID);
            if (activeRecipe is null || activeRecipe.Value.ItemID != activeItemID)
            {
                StopProduction("运行数据无效，已停止");
                return;
            }

            DriveAutomation(activeRecipe.Value);
        }
        catch (Exception ex)
        {
            FailAutomation($"监控异常：{ex.Message}");
        }
    }

    private void DriveAutomation(RecipeOption recipe)
    {
        switch (phase)
        {
            case AutomationPhase.WaitForFirmament:
                DriveWaitForFirmament();
                break;
            case AutomationPhase.WaitForPlayerMovable:
                DriveWaitForPlayerMovable();
                break;
            case AutomationPhase.MoveToInitialPoint:
                DriveMoveToInitialPoint(recipe);
                break;
            case AutomationPhase.WaitArtisanStart:
            {
                var snapshot = ReadInventory(activeItemID);
                if (ShouldStop(snapshot, out var reason))
                {
                    RequestArtisanStop();
                    EnterPhase(AutomationPhase.WaitArtisanStop);
                    status = $"{reason}；等待 Artisan 停止";
                }
                else if (artisanIsBusy?.InvokeFunc() == true)
                {
                    EnterPhase(AutomationPhase.Crafting);
                    status = $"Artisan 生产中：{recipe.ItemName}";
                }
                else if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
                {
                    artisanStartedByModule = false;
                    if (snapshot.ItemCount > craftCycleStartingItemCount)
                    {
                        BeginSubmission("Artisan 已完成本轮生产");
                    }
                    else
                    {
                        CompleteAutomation($"无法继续制作 {recipe.ItemName}，可能缺少材料或不满足制作条件");
                    }
                }

                break;
            }
            case AutomationPhase.Crafting:
            {
                var snapshot = ReadInventory(activeItemID);
                if (ShouldStop(snapshot, out var reason))
                {
                    RequestArtisanStop();
                    EnterPhase(AutomationPhase.WaitArtisanStop);
                    status = $"{reason}；等待 Artisan 停止";
                }
                else if (artisanIsBusy?.InvokeFunc() == false)
                {
                    artisanStartedByModule = false;
                    if (snapshot.ItemCount > craftCycleStartingItemCount)
                    {
                        BeginSubmission("Artisan 已结束本轮生产");
                    }
                    else
                    {
                        CompleteAutomation($"无法继续制作 {recipe.ItemName}，可能缺少材料或不满足制作条件");
                    }
                }

                break;
            }
            case AutomationPhase.WaitArtisanStop:
                if (artisanIsBusy?.InvokeFunc() == false)
                {
                    artisanStartedByModule = false;
                    BeginSubmission("Artisan 已停止");
                }
                else if (PhaseTimedOut(TimeSpan.FromSeconds(30)))
                {
                    FailAutomation("等待 Artisan 停止超时。");
                }
                break;
            case AutomationPhase.MoveToAppraiser:
                DriveStartNPCEvent([APPRAISER_NPC_ID_1, APPRAISER_NPC_ID_2],
                    AutomationPhase.OpenAppraiser, "提交NPC");
                break;
            case AutomationPhase.OpenAppraiser:
                DriveOpenAppraiser();
                break;
            case AutomationPhase.SelectJob:
                DriveSelectJob(recipe);
                break;
            case AutomationPhase.SelectItem:
                DriveSelectItem(recipe);
                break;
            case AutomationPhase.FillRequest:
                DriveFillRequest();
                break;
            case AutomationPhase.WaitTurnIn:
                DriveWaitTurnIn(recipe);
                break;
            case AutomationPhase.WaitSupplyRefresh:
                DriveWaitSupplyRefresh(recipe);
                break;
            case AutomationPhase.MoveToLottery:
                DriveStartNPCEvent([LOTTERY_NPC_ID], AutomationPhase.OpenLottery, "库啵好运道NPC");
                break;
            case AutomationPhase.OpenLottery:
                DriveOpenLottery();
                break;
            case AutomationPhase.PlayLottery:
                DrivePlayLottery();
                break;
            case AutomationPhase.PostLottery:
                DrivePostLottery(recipe);
                break;
            case AutomationPhase.PrepareNextCycle:
                DrivePrepareNextCycle(recipe);
                break;
        }
    }

    private unsafe void BeginNextCraftingCycle(string reason)
    {
        CloseAddon("Request");
        CloseAddon("HWDSupply");
        EnterPhase(AutomationPhase.PrepareNextCycle);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        status = $"{reason}；准备下一轮生产";
    }

    private unsafe void DrivePrepareNextCycle(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (GetAddon("Request") != null || GetAddon("HWDSupply") != null)
        {
            CloseAddon("Request");
            CloseAddon("HWDSupply");
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = "正在关闭提交界面，准备下一轮生产";
            return;
        }

        if (IsOccupied())
        {
            status = "等待交互结束，准备下一轮生产";
            if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
            {
                FailAutomation("准备下一轮生产时，玩家长时间处于不可操作状态。");
            }
            return;
        }

        StartCraftingCycle(recipe, "开始下一轮");
    }

    private void EnterPhase(AutomationPhase next)
    {
        phase = next;
        phaseStartedAt = DateTime.UtcNow;
    }

    private bool PhaseTimedOut(TimeSpan timeout) => DateTime.UtcNow - phaseStartedAt > timeout;

    private void CompleteAutomation(string message)
    {
        running = false;
        phase = AutomationPhase.Idle;
        activeRecipeID = 0;
        activeItemID = 0;
        ticketsToPlay = 0;
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        craftCycleStartingItemCount = 0;
        movableSince = null;
        initialDestination = default;
        status = message;
    }

    private void FailAutomation(string message)
    {
        CleanupScripPurchase();
        StopOwnedArtisan();
        StopOwnedVnavPath();

        running = false;
        phase = AutomationPhase.Idle;
        lastError = message;
        status = "自动流程已停止";
    }

    private bool ShouldStop(InventorySnapshot snapshot, out string reason)
    {
        if (config.StopMode == 0 && snapshot.FreeSlots <= config.MinimumFreeSlots)
        {
            reason = $"背包只剩 {snapshot.FreeSlots} 格，已停止生产并准备提交";
            return true;
        }

        if (config.StopMode == 1 && snapshot.ItemCount >= config.TargetItemCount)
        {
            reason = $"目标物品已达到 {snapshot.ItemCount} 个，已停止生产并准备提交";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private void Fail(string message)
    {
        lastError = message;
        status = "无法开始";
    }

    private enum AutomationPhase
    {
        Idle,
        WaitForFirmament,
        WaitForPlayerMovable,
        MoveToInitialPoint,
        WaitArtisanStart,
        Crafting,
        WaitArtisanStop,
        MoveToAppraiser,
        OpenAppraiser,
        SelectJob,
        SelectItem,
        FillRequest,
        WaitTurnIn,
        WaitSupplyRefresh,
        MoveToLottery,
        OpenLottery,
        PlayLottery,
        PostLottery,
        PrepareNextCycle,
        PrepareScripPurchase,
        OpenScripShop,
        BuyScripItem,
        VerifyScripPurchase,
        CloseScripShop
    }
}
