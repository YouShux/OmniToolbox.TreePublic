using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmniToolbox.UI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private static readonly Regex VoucherPattern = new("^(\\d+)/(\\d+)$", RegexOptions.Compiled);

    private int ticketsToPlay;

    private bool lotteryScratched;

    private bool lotteryTicketCounted;

    private int lotteryScratchAttempts;

    private DateTime nextVoucherReadAt;

    private int voucherCount = -1;

    private int voucherLimit = -1;

    private unsafe void DriveOpenLottery()
    {
        var lottery = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (lottery != null)
        {
            if (!lottery->AtkUnitBase.IsReady)
            {
                status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryInitializing");
                if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
                {
                    FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryNotInitialized"));
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
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryOpenFailed"));
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
                status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryTicketComplete"), ticketsToPlay);
            }
            else if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryClosedEarly"));
            }
            return;
        }

        if (!addon->AtkUnitBase.IsReady)
        {
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryPreparing");
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
                status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryResultClosing"), ticketsToPlay);
            }
        }
        else if (addon->Stage == 2)
        {
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryRevealing"), ticketsToPlay);
        }
        else if (!lotteryScratched && DateTime.UtcNow >= nextActionAt)
        {
            var evt = new AtkEvent
            {
                Param = RIGHT_LOTTERY_EVENT_PARAM
            };
            var eventData = default(AtkEventData);
            addon->AtkUnitBase.ReceiveEvent(
                AtkEventType.ButtonClick,
                RIGHT_LOTTERY_EVENT_PARAM,
                &evt,
                &eventData);
            lotteryScratchAttempts++;
            nextActionAt = DateTime.UtcNow.AddMilliseconds(1500);
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryScratchAttempt"), lotteryScratchAttempts);
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(30)))
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryRevealTimedOut"));
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
                status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.NextLotteryInitializing");
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
                status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryContinuing");
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
            var npc = DService.Instance().ObjectTable
                .FirstOrDefault(o => GetBaseID(o.Address) == LOTTERY_NPC_ID && o.IsTargetable);
            if (npc is null)
            {
                FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryNPCMissing"));
                return;
            }

            if (!SendNPCEventStart(npc.Address))
            {
                FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryNPCOpenFailed"));
                return;
            }

            EnterPhase(AutomationPhase.OpenLottery);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            lotteryScratched = false;
            lotteryTicketCounted = false;
            lotteryScratchAttempts = 0;
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryRemaining"), ticketsToPlay);
            return;
        }

        if (ReadInventory(activeItemID).ItemCount > 0)
        {
            EnterPhase(AutomationPhase.MoveToAppraiser);
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryReturnToSubmission"), recipe.ItemName);
        }
        else
        {
            BeginNextCraftingCycle(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.SubmissionLotteryComplete"));
        }
    }

    private void PrepareLotteryCard()
    {
        EnterPhase(AutomationPhase.PlayLottery);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.LotteryScratchPreparing"), ticketsToPlay);
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
            var match = VoucherPattern.Match(normalized);
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
        count >= 0 && limit >= 0 ? $"{count}/{limit}" : OmniLoc.Get("Feature.AutoIshgardRestoration.UI.VoucherUnknown");

    #region 常量

    private const int RIGHT_LOTTERY_EVENT_PARAM = 22;
    private const uint LOTTERY_NPC_ID = 1031692;

    #endregion
}
