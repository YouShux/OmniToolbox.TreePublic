using System.Globalization;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private int itemCountBeforeTurnIn;

    private uint scripCountBeforeTurnIn;

    private void BeginSubmission(string reason)
    {
        if (DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SubmissionRequiresFirmament"));
            return;
        }

        if (ReadInventory(activeItemID).ItemCount <= 0)
        {
            CompleteAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.NoItemsToSubmit"));
            return;
        }

        EnterPhase(AutomationPhase.MoveToAppraiser);
        status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.MovingToAppraiser"), reason);
    }

    private unsafe void DriveOpenAppraiser()
    {
        if (GetAddon("HWDSupply") != null)
        {
            EnterPhase(AutomationPhase.SelectJob);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SupplyOpened");
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
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SupplyOpenFailed"));
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
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SupplyUnexpectedlyClosed"));
            return;
        }

        addon->Callback(0, (int)recipe.JobID - 8);
        EnterPhase(AutomationPhase.SelectItem);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
        status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SupplyJobSelected"), GetJobName(recipe.JobID));
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
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SupplyUnexpectedlyClosed"));
            return;
        }

        var row = FindSupplyRow(addon, activeItemID);
        if (row < 0)
        {
            if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SupplyItemNotFound"), recipe.ItemName));
            }
            return;
        }

        itemCountBeforeTurnIn = ReadInventory(activeItemID).ItemCount;
        scripCountBeforeTurnIn = ReadSkybuildersScrips();
        addon->Callback(1, row);
        EnterPhase(AutomationPhase.FillRequest);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
        status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SubmittingItem"), recipe.ItemName);
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
                FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.RequestNotOpened"));
            }
            return;
        }

        var request = (AddonRequest*)requestBase;
        if (request->HandOverButton != null && request->HandOverButton->IsEnabled)
        {
            ClickButton(requestBase, request->HandOverButton);
            EnterPhase(AutomationPhase.WaitTurnIn);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForTurnIn");
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
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.FillingRequestTimedOut"));
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
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.TurnInNotCompleted"));
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
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForRequestClose"), itemCountBeforeTurnIn);
            return;
        }

        if (request != null)
        {
            CloseAddon("Request");
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ClosingCompletedRequest");
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
                    FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SupplyRefreshFailed"));
                }
                return;
            }

            EnterPhase(AutomationPhase.MoveToAppraiser);
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ReopeningSupply");
            return;
        }

        if (TryReadVoucherCount(out var vouchers, out _) && vouchers >= config.TicketThreshold)
        {
            ticketsToPlay = vouchers;
            CloseAddon("HWDSupply");
            EnterPhase(AutomationPhase.MoveToLottery);
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.TicketThresholdReached"), vouchers);
            return;
        }

        if (itemCountBeforeTurnIn > 0)
        {
            EnterPhase(AutomationPhase.SelectItem);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ContinuingSubmission"), itemCountBeforeTurnIn);
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
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.SubmissionCompletedWithTickets"), vouchers);
            return;
        }

        BeginNextCraftingCycle(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.AllItemsSubmitted"), recipe.ItemName));
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
            ? string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.TurnInConfirmedByScrips"), currentCount)
            : string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.TurnInConfirmedByInventory"), currentCount);
    }

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

    #region 常量

    private const uint APPRAISER_NPC_ID_1 = 1031690;

    private const uint APPRAISER_NPC_ID_2 = 1031677;

    #endregion
}
