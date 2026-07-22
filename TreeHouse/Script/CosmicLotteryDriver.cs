using FFXIVClientStructs.FFXIV.Client.Game;
using OmniToolbox.Config;
using OmenTools;
using OmenTools.Threading.TaskHelper;

namespace OmniToolbox.TreeHouse.Script;

internal sealed unsafe class CosmicLotteryDriver(
    CosmicLotteryConfig config,
    TaskHelper taskHelper,
    Func<bool> canRun)
{
    private const int DrawCost = 1_000;
    private const int MissRestartDelayMs = 1_000;
    private const int RewardTimeoutMs = 1_000;
    private const int ThresholdCheckIntervalMs = 1_000;

    private static readonly CosmicLotteryEntry[] Entries =
    [
        new(1237, 45691, 0),
        new(1291, 48146, 1),
        new(1310, 48147, 2),
        new(1319, 48148, 3)
    ];

    private CosmicLotteryEntry? activeEntry;
    private long nextThresholdCheckAt;
    private long lastDrawAt;
    private bool drawQueued;
    private bool chainRunning;
    private bool waitingReward;
    private bool drainingAfterTargetHit;
    private bool stoppedUntilBelowThreshold;
    private int generation;

    public bool IsBusy => drawQueued || chainRunning || waitingReward || drainingAfterTargetHit;

    public void Tick()
    {
        if (!canRun())
        {
            if (IsBusy)
            {
                Reset();
            }

            return;
        }

        ExpireWaitingReward();
        if (stoppedUntilBelowThreshold && !HasAnyCurrencyAtThreshold())
        {
            stoppedUntilBelowThreshold = false;
        }

        TryAutoPullIfDue();
    }

    public void OnInventoryRefresh()
    {
        if (canRun())
        {
            TryAutoPullIfDue();
        }
    }

    public void OnInventoryItemUpdate(InventoryType container, uint itemId)
    {
        if (!canRun() || itemId == 0)
        {
            return;
        }

        if (config.TargetItemIds.Contains(itemId))
        {
            HandleTargetReward();
            return;
        }

        if (IsRewardContainer(container) && IsPotentialReward(itemId) && IsBusy)
        {
            StopChainAndRestartAfterMiss();
        }
    }

    public bool TryGetCurrentEntry(out CosmicLotteryEntry entry)
    {
        var current = GetEntryForCurrentTerritory();
        if (current is null)
        {
            entry = default;
            return false;
        }

        entry = current.Value;
        return true;
    }

    public void QueueManual(CosmicLotteryEntry entry)
    {
        if (IsBusy || !canRun())
        {
            return;
        }

        stoppedUntilBelowThreshold = false;
        QueueLottery(entry, 0);
    }

    public void OnConfigChanged() => stoppedUntilBelowThreshold = false;

    public void Reset()
    {
        generation++;
        taskHelper.Abort();
        drawQueued = false;
        chainRunning = false;
        waitingReward = false;
        drainingAfterTargetHit = false;
        stoppedUntilBelowThreshold = false;
        activeEntry = null;
        nextThresholdCheckAt = 0;
        lastDrawAt = 0;
    }

    private void TryAutoPullIfDue()
    {
        var now = Environment.TickCount64;
        if (now < nextThresholdCheckAt)
        {
            return;
        }

        nextThresholdCheckAt = now + ThresholdCheckIntervalMs;
        if (IsBusy || stoppedUntilBelowThreshold)
        {
            return;
        }

        var entry = GetEntryWithEnoughCurrency(Math.Max(DrawCost, config.Threshold));
        if (entry is not null)
        {
            QueueLottery(entry.Value, 0);
        }
    }

    private void QueueLottery(CosmicLotteryEntry entry, int delayMs)
    {
        if (drawQueued)
        {
            return;
        }

        drawQueued = true;
        chainRunning = true;
        var queuedGeneration = generation;
        if (delayMs > 0)
        {
            taskHelper.DelayNext(delayMs);
        }

        taskHelper.Enqueue(() =>
        {
            drawQueued = false;
            if (queuedGeneration != generation || !canRun())
            {
                return true;
            }

            if (GetCurrencyCount(entry.CurrencyItemId) < DrawCost)
            {
                StopChain(false);
                return true;
            }

            CosmicLotteryNativeUi.ExecuteLottery(entry.LotteryType);
            activeEntry = entry;
            waitingReward = true;
            chainRunning = true;
            lastDrawAt = Environment.TickCount64;
            return true;
        });
    }

    private void HandleTargetReward()
    {
        var entry = activeEntry ?? GetEntryForCurrentTerritory();
        if (entry is null)
        {
            StopChain();
            return;
        }

        waitingReward = false;
        stoppedUntilBelowThreshold = false;
        if (drainingAfterTargetHit)
        {
            QueueNextDrainLottery(entry.Value);
            return;
        }

        BeginDrain(entry.Value);
    }

    private void BeginDrain(CosmicLotteryEntry entry)
    {
        if (GetCurrencyCount(entry.CurrencyItemId) < DrawCost)
        {
            StopChain(false);
            return;
        }

        generation++;
        chainRunning = true;
        waitingReward = false;
        drainingAfterTargetHit = true;
        activeEntry = entry;
        QueueNextDrainLottery(entry);
    }

    private void QueueNextDrainLottery(CosmicLotteryEntry entry)
    {
        QueueLottery(entry, Math.Max(0, config.DelayMs));
    }

    private void ExpireWaitingReward()
    {
        if (waitingReward &&
            lastDrawAt != 0 &&
            Environment.TickCount64 - lastDrawAt > RewardTimeoutMs)
        {
            StopChainAndRestartAfterMiss();
        }
    }

    private void StopChainAndRestartAfterMiss()
    {
        StopChain(false);
        nextThresholdCheckAt = Environment.TickCount64 + MissRestartDelayMs;
    }

    private void StopChain(bool blockUntilBelowThreshold = true)
    {
        generation++;
        taskHelper.Abort();
        drawQueued = false;
        chainRunning = false;
        waitingReward = false;
        drainingAfterTargetHit = false;
        activeEntry = null;
        stoppedUntilBelowThreshold = blockUntilBelowThreshold && HasAnyCurrencyAtThreshold();
    }

    private CosmicLotteryEntry? GetEntryWithEnoughCurrency(int threshold)
    {
        var current = GetEntryForCurrentTerritory();
        if (current is not null && GetCurrencyCount(current.Value.CurrencyItemId) >= threshold)
        {
            return current;
        }

        foreach (var entry in Entries)
        {
            if (GetCurrencyCount(entry.CurrencyItemId) >= threshold)
            {
                return entry;
            }
        }

        return null;
    }

    private static CosmicLotteryEntry? GetEntryForCurrentTerritory()
    {
        var territoryId = DService.Instance().ClientState.TerritoryType;
        foreach (var entry in Entries)
        {
            if (entry.TerritoryId == territoryId)
            {
                return entry;
            }
        }

        return null;
    }

    private bool HasAnyCurrencyAtThreshold() =>
        GetEntryWithEnoughCurrency(Math.Max(DrawCost, config.Threshold)) is not null;

    private static uint GetCurrencyCount(uint itemId) => CurrencyManager.Instance()->GetItemCount(itemId);

    private static bool IsRewardContainer(InventoryType container) =>
        container is InventoryType.Inventory1 or
            InventoryType.Inventory2 or
            InventoryType.Inventory3 or
            InventoryType.Inventory4 or
            InventoryType.Cosmopouch1 or
            InventoryType.Cosmopouch2;

    private static bool IsPotentialReward(uint itemId)
    {
        foreach (var entry in Entries)
        {
            if (entry.CurrencyItemId == itemId)
            {
                return false;
            }
        }

        return true;
    }
}

internal readonly record struct CosmicLotteryEntry(uint TerritoryId, uint CurrencyItemId, uint LotteryType);
