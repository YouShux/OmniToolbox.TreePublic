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
    private static readonly Regex VOUCHER_PATTERN = new("^(\\d+)/(\\d+)$", RegexOptions.Compiled);

    private int ticketsToPlay;

    private bool lotteryScratched;

    private bool lotteryTicketCounted;

    private int lotteryScratchAttempts;

    private DateTime nextVoucherReadAt;

    private int voucherCount = -1;

    private int voucherLimit = -1;

    private const int RIGHT_LOTTERY_EVENT_PARAM = 22;

    private const uint LOTTERY_NPC_ID = 1031692;

    private unsafe void DriveOpenLottery()
    {
        var lottery = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (lottery != null)
        {
            if (!lottery->AtkUnitBase.IsReady)
            {
                status = "等待库啵好运道界面初始化";
                if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
                {
                    FailAutomation("HWDLottery 界面已显示但未完成初始化。");
                }
                return;
            }

            PrepareLotteryCard();
            return;
        }

        if (DateTime.UtcNow >= nextActionAt)
        {
            var yesNoBase = GetAddon("SelectYesno");
            var talk = GetAddon("Talk");
            if (yesNoBase != null)
            {
                var yesNo = (AddonSelectYesno*)yesNoBase;
                if (yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
                {
                    ClickButton(yesNoBase, yesNo->YesButton);
                }
            }
            else if (talk != null)
            {
                talk->FireCallbackInt(0);
            }

            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            FailAutomation("未能打开 HWDLottery 抽奖界面。");
        }
    }

    private unsafe void DrivePlayLottery()
    {
        var addon = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (addon == null)
        {
            if (lotteryTicketCounted)
            {
                EnterPhase(AutomationPhase.PostLottery);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
                status = $"已完成一张票，剩余 {ticketsToPlay} 张";
            }
            else if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation("抽奖界面在刮奖前关闭。");
            }
            return;
        }

        if (!addon->AtkUnitBase.IsReady)
        {
            status = "等待库啵好运道界面准备完成";
            return;
        }

        if (addon->Stage >= 2)
        {
            lotteryScratched = true;
        }

        if (lotteryTicketCounted && addon->Stage < 2 && ticketsToPlay > 0)
        {
            PrepareLotteryCard();
            return;
        }

        if (addon->Stage == 3 && addon->CloseButton != null && addon->CloseButton->IsEnabled)
        {
            if (!lotteryTicketCounted)
            {
                ticketsToPlay = Math.Max(0, ticketsToPlay - 1);
                lotteryTicketCounted = true;
            }

            if (DateTime.UtcNow >= nextActionAt)
            {
                ClickButton(&addon->AtkUnitBase, addon->CloseButton);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
                status = $"本张票已揭晓，正在关闭结果（剩余 {ticketsToPlay} 张）";
            }
        }
        else if (addon->Stage == 2)
        {
            status = $"库啵好运道揭晓中（剩余 {ticketsToPlay} 张）";
        }
        else if (!lotteryScratched && DateTime.UtcNow >= nextActionAt)
        {
            var evt = new AtkEvent { Param = RIGHT_LOTTERY_EVENT_PARAM };
            var eventData = default(AtkEventData);
            addon->AtkUnitBase.ReceiveEvent(
                AtkEventType.ButtonClick,
                RIGHT_LOTTERY_EVENT_PARAM,
                &evt,
                &eventData);
            lotteryScratchAttempts++;
            nextActionAt = DateTime.UtcNow.AddMilliseconds(1500);
            status = $"已触发右侧三格按钮，第 {lotteryScratchAttempts} 次尝试，等待界面确认";
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(30)))
        {
            FailAutomation("等待库啵好运道揭晓超时。");
        }
    }

    private unsafe void DrivePostLottery(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        var lottery = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (lottery != null)
        {
            if (!lottery->AtkUnitBase.IsReady)
            {
                status = "等待下一张库啵好运票界面初始化";
                return;
            }

            if (lottery->Stage < 2 && ticketsToPlay > 0)
            {
                PrepareLotteryCard();
            }
            else
            {
                EnterPhase(AutomationPhase.PlayLottery);
                nextActionAt = DateTime.UtcNow;
                status = "正在继续抽奖";
            }
            return;
        }

        var talk = GetAddon("Talk");
        if (talk != null)
        {
            talk->FireCallbackInt(0);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            return;
        }

        var yesNoBase = GetAddon("SelectYesno");
        if (yesNoBase != null)
        {
            var yesNo = (AddonSelectYesno*)yesNoBase;
            if (yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
            {
                ClickButton(yesNoBase, yesNo->YesButton);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            }
            return;
        }

        if (IsOccupied())
        {
            return;
        }

        if (ticketsToPlay > 0)
        {
            var npc = OmenTools.DService.Instance().ObjectTable
                .FirstOrDefault(o => GetBaseID(o.Address) == LOTTERY_NPC_ID && o.IsTargetable);
            if (npc is null)
            {
                FailAutomation("未找到库啵好运道NPC。");
                return;
            }

            if (!SendNPCEventStart(npc.Address))
            {
                FailAutomation("无法打开库啵好运道NPC界面。");
                return;
            }

            EnterPhase(AutomationPhase.OpenLottery);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            lotteryScratched = false;
            lotteryTicketCounted = false;
            lotteryScratchAttempts = 0;
            status = $"继续抽奖，剩余 {ticketsToPlay} 张";
            return;
        }

        if (ReadInventory(activeItemID).ItemCount > 0)
        {
            EnterPhase(AutomationPhase.MoveToAppraiser);
            status = $"抽奖完成，返回提交剩余的 {recipe.ItemName}";
        }
        else
        {
            BeginNextCraftingCycle("物品提交及库啵好运道抽奖完成");
        }
    }

    private void PrepareLotteryCard()
    {
        EnterPhase(AutomationPhase.PlayLottery);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        status = $"准备选择右侧三个格子，剩余 {ticketsToPlay} 张";
    }

    private static unsafe bool TryReadVoucherCount(out int current, out int limit)
    {
        current = -1;
        limit = -1;
        var addon = GetAddon("HWDSupply");
        if (addon == null)
        {
            return false;
        }

        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            ref var value = ref addon->AtkValues[i];
            if (value.Type != ValueType.String || value.String.Value == null)
            {
                continue;
            }

            var text = Marshal.PtrToStringUTF8((nint)value.String.Value);
            if (text is null)
            {
                continue;
            }

            var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            var match = VOUCHER_PATTERN.Match(normalized);
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out var count)
                && int.TryParse(match.Groups[2].Value, out var parsedLimit)
                && parsedLimit == 10)
            {
                current = count;
                limit = 10;
                return true;
            }
        }

        return false;
    }

    private void UpdateVoucherCount(DateTime now)
    {
        if (now < nextVoucherReadAt)
        {
            return;
        }

        nextVoucherReadAt = now.AddMilliseconds(500);
        TryReadVoucherCount(out voucherCount, out voucherLimit);
    }

    private static string FormatVoucherCount(int count, int limit) =>
        count >= 0 && limit >= 0 ? $"{count}/{limit}" : "未识别";
}
