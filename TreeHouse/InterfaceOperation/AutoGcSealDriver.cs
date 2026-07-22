using FFXIVClientStructs.FFXIV.Client.Game;
using OmniToolbox.Config;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal enum AutoGcSealStage
{
    Idle,
    Begin,
    OpenSupply,
    SelectSupplyMenu,
    WaitSupplyList,
    SelectExpertTab,
    Deliver,
    ConfirmDelivery,
    RefreshSupply,
    CloseSupply,
    CloseSupplyMenu,
    OpenExchange,
    SelectExchangeTier,
    SelectExchangeCategory,
    SelectExchangeItem,
    ConfirmExchange,
    WaitExchangeApplied,
    CloseExchange,
    ReopenSupply,
    Complete
}

internal sealed class AutoGcSealDriver(AutoGcSealConfig config, AutoGcSealInterop interop)
{
    private const int OpenInterfaceTimeoutMs = 5_000;
    private const int CloseMenuWaitMs = 1_200;
    private const int ExchangeConfirmationWaitMs = 850;
    private const int MaxAgentNotReadyAttempts = 150;
    private const int MaxUnchangedDeliveryAttempts = 10;

    private readonly HashSet<ulong> skippedSlots = [];
    private AutoGcSealRunMode? mode;
    private AutoGcSealStage stage;
    private AutoGcSealDeliveryItem lastAttempt;
    private AutoGcSealDeliveryItem recentProtectedSelection;
    private long recentProtectedSelectionAt;
    private long continueAt;
    private long stageDeadline;
    private int agentNotReadyAttempts;
    private ulong lastDeliveredSlot = ulong.MaxValue;
    private int unchangedDeliveryAttempts;

    public bool IsRunning => mode == AutoGcSealRunMode.Automatic;

    public bool IsActive => mode.HasValue;

    public void BeginAutomatic()
    {
        ResetOperation();
        mode = AutoGcSealRunMode.Automatic;
        stage = AutoGcSealStage.Begin;
    }

    public void BeginOpenSupply()
    {
        ResetOperation();
        mode = AutoGcSealRunMode.OpenSupply;
        stage = AutoGcSealStage.Begin;
    }

    public void BeginOpenExchange()
    {
        ResetOperation();
        mode = AutoGcSealRunMode.OpenExchange;
        stage = AutoGcSealStage.Begin;
    }

    public void RequestSupply()
    {
        if (!IsRunning || stage is AutoGcSealStage.ConfirmDelivery or
            AutoGcSealStage.ConfirmExchange or AutoGcSealStage.WaitExchangeApplied)
        {
            return;
        }

        if (AutoGcSealInterop.IsExchangeOpen())
        {
            AutoGcSealInterop.TryCloseExchange();
            SetStage(AutoGcSealStage.ReopenSupply, 800, OpenInterfaceTimeoutMs);
            return;
        }

        SetStage(AutoGcSealStage.OpenSupply, timeoutMs: OpenInterfaceTimeoutMs);
    }

    public void Stop()
    {
        mode = null;
        stage = AutoGcSealStage.Idle;
        ResetTransientState();
    }

    public bool Tick()
    {
        if (!IsActive)
        {
            return true;
        }

        if (!interop.CanOperate())
        {
            CompleteOperation();
            return true;
        }

        if (HandleSupplyReward() || HandleSelectYesNo())
        {
            return !IsActive;
        }

        if (Environment.TickCount64 < continueAt)
        {
            return false;
        }

        switch (stage)
        {
            case AutoGcSealStage.Begin:
                BeginCurrentOperation();
                break;
            case AutoGcSealStage.OpenSupply:
                OpenSupply();
                break;
            case AutoGcSealStage.SelectSupplyMenu:
                SelectSupplyMenu();
                break;
            case AutoGcSealStage.WaitSupplyList:
                WaitForSupplyList();
                break;
            case AutoGcSealStage.SelectExpertTab:
                SelectExpertTab();
                break;
            case AutoGcSealStage.Deliver:
                DeliverNextItem();
                break;
            case AutoGcSealStage.ConfirmDelivery:
                if (HasStageTimedOut())
                {
                    ClearLastSelections();
                    SetStage(AutoGcSealStage.RefreshSupply, timeoutMs: OpenInterfaceTimeoutMs);
                }

                break;
            case AutoGcSealStage.RefreshSupply:
                RefreshSupplyList();
                break;
            case AutoGcSealStage.CloseSupply:
                CloseSupplyList();
                break;
            case AutoGcSealStage.CloseSupplyMenu:
                CloseSupplyMenu();
                break;
            case AutoGcSealStage.OpenExchange:
                OpenExchange();
                break;
            case AutoGcSealStage.SelectExchangeTier:
                SelectExchangeTier();
                break;
            case AutoGcSealStage.SelectExchangeCategory:
                SelectExchangeCategory();
                break;
            case AutoGcSealStage.SelectExchangeItem:
                SelectExchangeItem();
                break;
            case AutoGcSealStage.ConfirmExchange:
                WaitForExchangeConfirmation();
                break;
            case AutoGcSealStage.WaitExchangeApplied:
                ContinueAfterExchange();
                break;
            case AutoGcSealStage.CloseExchange:
                CloseExchange();
                break;
            case AutoGcSealStage.ReopenSupply:
                ReopenSupply();
                break;
            case AutoGcSealStage.Complete:
                CompleteOperation();
                break;
        }

        return !IsActive;
    }

    public void RecordProtectedSelection(AutoGcSealDeliveryItem item)
    {
        recentProtectedSelection = item;
        recentProtectedSelectionAt = Environment.TickCount64;
    }

    public bool HandleSupplyReward()
    {
        if (!AutoGcSealInterop.IsSupplyRewardOpen())
        {
            return false;
        }

        if (TryGetProtectedDelivery(out var protectedItem))
        {
            if (!AutoGcSealInterop.TryConfirmSupplyReward(false))
            {
                return false;
            }

            SkipInventorySlot(protectedItem);
            ClearLastSelections();
            if (IsRunning)
            {
                SetStage(AutoGcSealStage.RefreshSupply, 220, OpenInterfaceTimeoutMs);
            }

            return true;
        }

        if (!IsRunning || !AutoGcSealInterop.TryConfirmSupplyReward(true))
        {
            return false;
        }

        ClearLastSelections();
        SetStage(AutoGcSealStage.RefreshSupply, 150, OpenInterfaceTimeoutMs);
        return true;
    }

    public bool HandleSelectYesNo()
    {
        if (!IsActive)
        {
            return false;
        }

        if (stage == AutoGcSealStage.ConfirmDelivery)
        {
            var confirm = !AutoGcSealExclusions.Contains(config, lastAttempt.ItemId) &&
                          (!config.SkipHqItems || !AutoGcSealInterop.IsHighQuality(lastAttempt));
            if (!AutoGcSealInterop.TryConfirmYesNo(confirm))
            {
                return false;
            }

            if (!confirm)
            {
                SkipInventorySlot(lastAttempt);
            }

            ClearLastSelections();
            SetStage(AutoGcSealStage.RefreshSupply, confirm ? 580 : 220, OpenInterfaceTimeoutMs);
            return true;
        }

        if (stage != AutoGcSealStage.ConfirmExchange ||
            !AutoGcSealInterop.TryConfirmYesNo(true))
        {
            return false;
        }

        SetStage(AutoGcSealStage.WaitExchangeApplied, 500);
        return true;
    }

    private void BeginCurrentOperation()
    {
        if (AutoGcSealInterop.IsSupplyOpen())
        {
            SetStage(
                mode == AutoGcSealRunMode.OpenExchange
                    ? AutoGcSealStage.CloseSupply
                    : AutoGcSealStage.SelectExpertTab,
                timeoutMs: OpenInterfaceTimeoutMs);
            return;
        }

        SetStage(
            mode == AutoGcSealRunMode.OpenExchange ||
            IsRunning && AutoGcSealInterop.ShouldExchange(config)
                ? AutoGcSealStage.OpenExchange
                : AutoGcSealStage.OpenSupply,
            timeoutMs: OpenInterfaceTimeoutMs);
    }

    private void OpenSupply()
    {
        if (AutoGcSealInterop.IsSupplyOpen())
        {
            SetStage(AutoGcSealStage.SelectExpertTab, timeoutMs: OpenInterfaceTimeoutMs);
            return;
        }

        if (AutoGcSealInterop.IsSelectionMenuOpen())
        {
            SetStage(AutoGcSealStage.SelectSupplyMenu, timeoutMs: OpenInterfaceTimeoutMs);
            return;
        }

        if (HasStageTimedOut())
        {
            CompleteOperation();
            return;
        }

        if (interop.TryOpenSupply())
        {
            SetStage(AutoGcSealStage.SelectSupplyMenu, 150, OpenInterfaceTimeoutMs);
        }
        else
        {
            Delay(300);
        }
    }

    private void SelectSupplyMenu()
    {
        if (AutoGcSealInterop.IsSupplyOpen())
        {
            SetStage(AutoGcSealStage.SelectExpertTab, timeoutMs: OpenInterfaceTimeoutMs);
            return;
        }

        if (interop.TrySelectSupplyMenu())
        {
            SetStage(AutoGcSealStage.WaitSupplyList, 100, OpenInterfaceTimeoutMs);
            return;
        }

        if (HasStageTimedOut())
        {
            CompleteOperation();
        }
    }

    private void WaitForSupplyList()
    {
        if (AutoGcSealInterop.IsSupplyOpen())
        {
            SetStage(AutoGcSealStage.SelectExpertTab, timeoutMs: OpenInterfaceTimeoutMs);
            return;
        }

        if (HasStageTimedOut())
        {
            CompleteOperation();
        }
    }

    private void SelectExpertTab()
    {
        if (AutoGcSealInterop.IsExpertDeliveryTabReady())
        {
            if (mode == AutoGcSealRunMode.OpenSupply)
            {
                CompleteOperation();
            }
            else
            {
                SetStage(AutoGcSealStage.Deliver, 50);
            }

            return;
        }

        if (!AutoGcSealInterop.IsSupplyOpen())
        {
            SetStage(AutoGcSealStage.OpenSupply, timeoutMs: OpenInterfaceTimeoutMs);
            return;
        }

        if (HasStageTimedOut())
        {
            CompleteOperation();
            return;
        }

        if (AutoGcSealInterop.TrySelectExpertDeliveryTab())
        {
            Delay(100);
        }
    }

    private void DeliverNextItem()
    {
        if (AutoGcSealInterop.ShouldExchange(config))
        {
            SetStage(AutoGcSealStage.CloseSupply, timeoutMs: OpenInterfaceTimeoutMs);
            return;
        }

        var result = interop.ScanFirstDelivery(config, skippedSlots, out var item);
        if (result == AutoGcSealDeliveryScanResult.NotReady)
        {
            if (++agentNotReadyAttempts > MaxAgentNotReadyAttempts)
            {
                CompleteOperation();
            }

            return;
        }

        agentNotReadyAttempts = 0;
        if (result == AutoGcSealDeliveryScanResult.Empty ||
            AutoGcSealInterop.WouldExceedSealCap(item.SealReward))
        {
            SetStage(AutoGcSealStage.CloseSupply, timeoutMs: OpenInterfaceTimeoutMs);
            return;
        }

        var slotKey = AutoGcSealInterop.GetInventorySlotKey(item);
        if (slotKey == lastDeliveredSlot)
        {
            if (++unchangedDeliveryAttempts > MaxUnchangedDeliveryAttempts)
            {
                CompleteOperation();
                return;
            }

            AutoGcSealInterop.TryRefreshSupplyList();
            Delay(100);
            return;
        }

        if (!AutoGcSealInterop.TryHandIn(item))
        {
            return;
        }

        lastDeliveredSlot = slotKey;
        unchangedDeliveryAttempts = 0;
        lastAttempt = item;
        SetStage(AutoGcSealStage.ConfirmDelivery, timeoutMs: OpenInterfaceTimeoutMs);
    }

    private void RefreshSupplyList()
    {
        if (!AutoGcSealInterop.IsSupplyOpen())
        {
            SetStage(AutoGcSealStage.OpenSupply, timeoutMs: OpenInterfaceTimeoutMs);
            return;
        }

        if (AutoGcSealInterop.TryRefreshSupplyList())
        {
            SetStage(AutoGcSealStage.Deliver, 100);
            return;
        }

        if (HasStageTimedOut())
        {
            CompleteOperation();
        }
    }

    private void CloseSupplyList()
    {
        if (!AutoGcSealInterop.IsSupplyOpen() || AutoGcSealInterop.TryCloseSupplyList())
        {
            SetStage(AutoGcSealStage.CloseSupplyMenu, 250, CloseMenuWaitMs);
            return;
        }

        if (HasStageTimedOut())
        {
            CompleteOperation();
        }
    }

    private void CloseSupplyMenu()
    {
        if (AutoGcSealInterop.IsSelectionMenuOpen() && !HasStageTimedOut())
        {
            if (AutoGcSealInterop.TryCloseSelectionMenu())
            {
                Delay(150);
            }

            return;
        }

        if (!HasStageTimedOut())
        {
            Delay(150);
            return;
        }

        if (mode == AutoGcSealRunMode.OpenExchange ||
            IsRunning && AutoGcSealInterop.ShouldExchange(config))
        {
            SetStage(AutoGcSealStage.OpenExchange, 300, OpenInterfaceTimeoutMs);
        }
        else
        {
            CompleteOperation();
        }
    }

    private void OpenExchange()
    {
        if (AutoGcSealInterop.IsExchangeOpen())
        {
            if (mode == AutoGcSealRunMode.OpenExchange)
            {
                CompleteOperation();
            }
            else
            {
                SetStage(AutoGcSealStage.SelectExchangeTier);
            }

            return;
        }

        if (HasStageTimedOut())
        {
            CompleteOperation();
            return;
        }

        if (AutoGcSealInterop.TryCloseConversation())
        {
            Delay(250);
            return;
        }

        if (interop.TryOpenExchange())
        {
            Delay(500);
        }
        else
        {
            Delay(300);
        }
    }

    private void SelectExchangeTier()
    {
        if (!AutoGcSealInterop.TryGetExchangePlan(config, out var item, out _) ||
            !AutoGcSealInterop.TrySelectExchangeTier(item))
        {
            SetStage(AutoGcSealStage.CloseExchange, 500);
            return;
        }

        SetStage(AutoGcSealStage.SelectExchangeCategory, 500);
    }

    private void SelectExchangeCategory()
    {
        if (!AutoGcSealInterop.TryGetExchangePlan(config, out var item, out _) ||
            !AutoGcSealInterop.TrySelectExchangeCategory(item))
        {
            SetStage(AutoGcSealStage.CloseExchange, 500);
            return;
        }

        SetStage(AutoGcSealStage.SelectExchangeItem, 500);
    }

    private void SelectExchangeItem()
    {
        if (!AutoGcSealInterop.TryGetExchangePlan(config, out var item, out var quantity) ||
            !AutoGcSealInterop.TryTriggerExchange(item, quantity))
        {
            SetStage(AutoGcSealStage.CloseExchange, 500);
            return;
        }

        SetStage(AutoGcSealStage.ConfirmExchange, 200, ExchangeConfirmationWaitMs);
    }

    private void WaitForExchangeConfirmation()
    {
        if (HasStageTimedOut())
        {
            SetStage(AutoGcSealStage.WaitExchangeApplied, 500);
        }
    }

    private void ContinueAfterExchange()
    {
        var shouldExchange = AutoGcSealInterop.ShouldExchange(config);
        SetStage(
            shouldExchange
                ? AutoGcSealStage.SelectExchangeTier
                : AutoGcSealStage.CloseExchange,
            shouldExchange ? 0 : 500);
    }

    private void CloseExchange()
    {
        if (AutoGcSealInterop.IsExchangeOpen() && !AutoGcSealInterop.TryCloseExchange())
        {
            Delay(200);
            return;
        }

        lastDeliveredSlot = ulong.MaxValue;
        unchangedDeliveryAttempts = 0;
        SetStage(AutoGcSealStage.ReopenSupply, 800, OpenInterfaceTimeoutMs);
    }

    private void ReopenSupply()
    {
        if (AutoGcSealInterop.IsExchangeOpen())
        {
            AutoGcSealInterop.TryCloseExchange();
            if (HasStageTimedOut())
            {
                CompleteOperation();
                return;
            }

            Delay(200);
            return;
        }

        SetStage(AutoGcSealStage.OpenSupply, timeoutMs: OpenInterfaceTimeoutMs);
    }

    private bool TryGetProtectedDelivery(out AutoGcSealDeliveryItem item)
    {
        if (lastAttempt.ItemId != 0 && AutoGcSealExclusions.Contains(config, lastAttempt.ItemId))
        {
            item = lastAttempt;
            return true;
        }

        if (recentProtectedSelection.ItemId != 0 &&
            Environment.TickCount64 - recentProtectedSelectionAt <= 2_000 &&
            AutoGcSealExclusions.Contains(config, recentProtectedSelection.ItemId))
        {
            item = recentProtectedSelection;
            return true;
        }

        if (interop.TryGetCurrentDeliveryItem(out item) &&
            AutoGcSealExclusions.Contains(config, item.ItemId))
        {
            return true;
        }

        item = default;
        return false;
    }

    private void SkipInventorySlot(AutoGcSealDeliveryItem item)
    {
        if (item.Container != 0 && item.Container != InventoryType.Invalid)
        {
            skippedSlots.Add(AutoGcSealInterop.GetInventorySlotKey(item));
        }
    }

    private void ClearLastSelections()
    {
        lastAttempt = default;
        recentProtectedSelection = default;
        recentProtectedSelectionAt = 0;
    }

    private void SetStage(AutoGcSealStage nextStage, int delayMs = 0, int timeoutMs = 0)
    {
        stage = nextStage;
        continueAt = Environment.TickCount64 + Math.Max(0, delayMs);
        stageDeadline = timeoutMs > 0 ? Environment.TickCount64 + timeoutMs : 0;
    }

    private void Delay(int milliseconds) =>
        continueAt = Environment.TickCount64 + Math.Max(0, milliseconds);

    private bool HasStageTimedOut() =>
        stageDeadline > 0 && Environment.TickCount64 >= stageDeadline;

    private void CompleteOperation()
    {
        mode = null;
        stage = AutoGcSealStage.Complete;
        ResetTransientState();
    }

    private void ResetOperation()
    {
        mode = null;
        stage = AutoGcSealStage.Idle;
        ResetTransientState();
    }

    private void ResetTransientState()
    {
        skippedSlots.Clear();
        agentNotReadyAttempts = 0;
        lastDeliveredSlot = ulong.MaxValue;
        unchangedDeliveryAttempts = 0;
        continueAt = 0;
        stageDeadline = 0;
        ClearLastSelections();
    }
}
