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
    private int itemCountBeforeTurnIn;

    private uint scripCountBeforeTurnIn;

    private const uint APPRAISER_NPC_ID_1 = 1031690;

    private const uint APPRAISER_NPC_ID_2 = 1031677;

    private void BeginSubmission(string reason)
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            FailAutomation("自动提交仅支持在天穹街内启动（区域 886）。");
            return;
        }

        if (ReadInventory(activeItemID).ItemCount <= 0)
        {
            CompleteAutomation("生产结束，但背包中没有可提交的目标物品，可能已无法继续制作");
            return;
        }

        EnterPhase(AutomationPhase.MoveToAppraiser);
        status = $"{reason}；前往提交NPC";
    }

    private unsafe void DriveOpenAppraiser()
    {
        if (GetAddon("HWDSupply") != null)
        {
            EnterPhase(AutomationPhase.SelectJob);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = "提交界面已打开";
            return;
        }

        var talk = GetAddon("Talk");
        if (DateTime.UtcNow >= nextActionAt && IsOccupied() && talk != null)
        {
            talk->FireCallbackInt(0);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            return;
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            FailAutomation("未能打开 HWDSupply 提交界面。");
        }
    }

    private unsafe void DriveSelectJob(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        var addon = GetAddon("HWDSupply");
        if (addon == null)
        {
            FailAutomation("HWDSupply 提交界面意外关闭。");
            return;
        }

        addon->Callback(0, (int)recipe.JobID - 8);
        EnterPhase(AutomationPhase.SelectItem);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
        status = $"已选择{GetJobName(recipe.JobID)}提交列表";
    }

    private unsafe void DriveSelectItem(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (TryBeginAutoScripPurchase()) return;

        if (ReadInventory(activeItemID).ItemCount <= 0)
        {
            FinishSubmissionOrStartLottery(recipe);
            return;
        }

        var addon = GetAddon("HWDSupply");
        if (addon == null)
        {
            FailAutomation("HWDSupply 提交界面意外关闭。");
            return;
        }

        var row = FindSupplyRow(addon, activeItemID);
        if (row < 0)
        {
            if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation($"提交列表中未找到 {recipe.ItemName}；请确认物品收藏价值达到最低要求。");
            }
            return;
        }

        itemCountBeforeTurnIn = ReadInventory(activeItemID).ItemCount;
        scripCountBeforeTurnIn = ReadSkybuildersScrips();
        addon->Callback(1, row);
        EnterPhase(AutomationPhase.FillRequest);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
        status = $"正在提交：{recipe.ItemName}";
    }

    private unsafe void DriveFillRequest()
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (TryDetectCompletedTurnIn(out var currentCount, out var currentScrips))
        {
            EnterWaitSupplyRefresh(currentCount, currentScrips);
            return;
        }

        var requestBase = GetAddon("Request");
        if (requestBase == null)
        {
            if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation("提交物品后未出现 Request 窗口。");
            }
            return;
        }

        var request = (AddonRequest*)requestBase;
        if (request->HandOverButton != null && request->HandOverButton->IsEnabled)
        {
            ClickButton(requestBase, request->HandOverButton);
            EnterPhase(AutomationPhase.WaitTurnIn);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = "已点击交纳，等待完成";
            return;
        }

        var picker = GetAddon("ContextIconMenu");
        if (picker != null)
        {
            var icon = picker->AtkValuesCount > 11 && picker->AtkValues[11].Type == ValueType.UInt
                ? picker->AtkValues[11].UInt
                : 0u;
            picker->Callback(0, 0, icon, 0u);
        }
        else
        {
            requestBase->Callback(2, 0u, 0u, 0u);
        }

        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
        if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            FailAutomation("自动放入收藏品超时。");
        }
    }

    private unsafe void DriveWaitTurnIn(RecipeOption recipe)
    {
        var yesNoBase = GetAddon("SelectYesno");
        if (DateTime.UtcNow >= nextActionAt && IsOccupied() && yesNoBase != null)
        {
            var yesNo = (AddonSelectYesno*)yesNoBase;
            if (yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
            {
                ClickButton(yesNoBase, yesNo->YesButton);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            }
            return;
        }

        if (TryDetectCompletedTurnIn(out var currentCount, out var currentScrips))
        {
            EnterWaitSupplyRefresh(currentCount, currentScrips);
            return;
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
        {
            FailAutomation("交纳后物品数量没有减少。");
        }
    }

    private unsafe void DriveWaitSupplyRefresh(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        var request = GetAddon("Request");
        if (request != null && !PhaseTimedOut(TimeSpan.FromSeconds(2)))
        {
            status = $"等待本次提交窗口关闭（剩余 {itemCountBeforeTurnIn} 个）";
            return;
        }

        if (request != null)
        {
            CloseAddon("Request");
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            status = "正在关闭已完成的提交窗口";
            if (!PhaseTimedOut(TimeSpan.FromSeconds(5)))
            {
                return;
            }
        }

        if (TryBeginAutoScripPurchase()) return;

        if (GetAddon("HWDSupply") == null)
        {
            if (IsOccupied())
            {
                if (PhaseTimedOut(TimeSpan.FromSeconds(12)))
                {
                    FailAutomation("提交后未能返回物品列表。");
                }
                return;
            }

            EnterPhase(AutomationPhase.MoveToAppraiser);
            status = "提交界面已关闭，正在重新打开";
            return;
        }

        if (TryReadVoucherCount(out var vouchers, out _) && vouchers >= config.TicketThreshold)
        {
            ticketsToPlay = vouchers;
            CloseAddon("HWDSupply");
            EnterPhase(AutomationPhase.MoveToLottery);
            status = $"库啵好运票达到 {vouchers} 张，停止提交并前往抽奖";
            return;
        }

        if (itemCountBeforeTurnIn > 0)
        {
            EnterPhase(AutomationPhase.SelectItem);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
            status = $"继续提交，背包剩余 {itemCountBeforeTurnIn} 个";
        }
        else
        {
            FinishSubmissionOrStartLottery(recipe);
        }
    }

    private unsafe void FinishSubmissionOrStartLottery(RecipeOption recipe)
    {
        if (TryBeginAutoScripPurchase()) return;

        if (TryReadVoucherCount(out var vouchers, out _) && vouchers >= config.TicketThreshold)
        {
            ticketsToPlay = vouchers;
            CloseAddon("HWDSupply");
            EnterPhase(AutomationPhase.MoveToLottery);
            status = $"提交完成，持有 {vouchers} 张票，前往抽奖";
            return;
        }

        BeginNextCraftingCycle($"{recipe.ItemName} 已全部提交");
    }

    private bool TryDetectCompletedTurnIn(out int currentCount, out uint currentScrips)
    {
        currentCount = ReadInventory(activeItemID).ItemCount;
        currentScrips = ReadSkybuildersScrips();
        return currentCount < itemCountBeforeTurnIn || currentScrips > scripCountBeforeTurnIn;
    }

    private void EnterWaitSupplyRefresh(int currentCount, uint currentScrips)
    {
        turnInGeneration++;
        var detectedByScrips = currentScrips > scripCountBeforeTurnIn;
        itemCountBeforeTurnIn = currentCount;
        scripCountBeforeTurnIn = currentScrips;
        EnterPhase(AutomationPhase.WaitSupplyRefresh);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        status = detectedByScrips
            ? $"已通过天穹街振兴票变化确认提交，等待界面刷新（剩余 {currentCount} 个）"
            : $"已提交 1 件，等待提交界面刷新（剩余 {currentCount} 个）";
    }

    // SpecialShop rows verified against the Chinese client. Names/prices are not duplicated here.

    private static unsafe int FindSupplyRow(AtkUnitBase* addon, uint itemID)
    {
        var target = itemID + 500000;
        const int INDEX_OFFSET = 18;
        for (var i = 0; i + INDEX_OFFSET < addon->AtkValuesCount; i++)
        {
            ref var value = ref addon->AtkValues[i];
            if (value.Type != ValueType.UInt || value.UInt != target)
            {
                continue;
            }

            ref var index = ref addon->AtkValues[i + INDEX_OFFSET];
            return index.Type == ValueType.UInt ? (int)index.UInt : -1;
        }

        return -1;
    }
}
