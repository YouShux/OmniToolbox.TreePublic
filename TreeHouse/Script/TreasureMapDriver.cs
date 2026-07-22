using Dalamud.Game.ClientState.Conditions;
using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.PublicBridge;
using OmniToolbox.Teleport;
using OmniToolbox.TreeHouse.InterfaceOperation;
using OmenTools;

namespace OmniToolbox.TreeHouse.Script;

internal sealed class TreasureMapDriver(
    TreasureMapConfig config,
    PlayerInventoryService inventoryService,
    IMarketPurchaseService marketPurchaseService,
    IAutoStoreService autoStoreService,
    TeleportService teleportService,
    IPrivateFeatureProvider privateFeatureProvider,
    Func<bool> canRun)
{
    private const int TargetMapCount = 3;
    private const int PurchaseTimeoutMs = 60_000;
    private const int StoreTimeoutMs = 30_000;
    private const int ServiceSettleTimeoutMs = 2_500;
    private const int DecipherSelectTimeoutMs = 18_000;
    private const int DecipherConfirmTimeoutMs = 8_000;
    private const int DecipherTimeoutMs = 15_000;
    private const int StepDelayMs = 1_500;
    private const int MoneyBagIntervalMs = 1_000;

    private TreasureMapDefinition activeMap;
    private TreasureMapState state;
    private string statusKey = "Feature.TreasureMap.Status.Ready";
    private long nextActionAt;
    private long deadlineAt;
    private long serviceStoppedAt;
    private long confirmStartedAt;
    private long nextMoneyBagAt;
    private int inventoryBeforeOperation;
    private int saddlebagBeforeOperation;
    private int maxUnitPrice;
    private uint lastRankId;
    private uint lastSubRowId;
    private bool mapTrackerInitialized;
    private bool moneyBagTrackingActive;

    public bool IsBusy => state != TreasureMapState.Idle;

    public string StatusKey => statusKey;

    public bool RequestStart(uint itemId, int maxUnitPrice)
    {
        if (IsBusy)
        {
            statusKey = "Feature.TreasureMap.Status.Busy";
            return false;
        }

        if (!canRun())
        {
            statusKey = "Feature.TreasureMap.Status.Unavailable";
            return false;
        }

        if (!config.EnableOneClickPurchase)
        {
            statusKey = "Feature.TreasureMap.Status.OneClickDisabled";
            return false;
        }

        if (itemId == 0 || maxUnitPrice < 0)
        {
            statusKey = "Feature.TreasureMap.Status.InvalidRequest";
            return false;
        }

        if (!TreasureMapNativeUi.TryResolveMap(itemId, out activeMap))
        {
            statusKey = "Feature.TreasureMap.Status.InvalidMap";
            return false;
        }

        state = TreasureMapState.Advance;
        statusKey = "Feature.TreasureMap.Status.Preparing";
        this.maxUnitPrice = maxUnitPrice;
        nextActionAt = 0;
        deadlineAt = 0;
        serviceStoppedAt = 0;
        return true;
    }

    public void Tick()
    {
        if (!canRun())
        {
            if (IsBusy)
            {
                Stop("Feature.TreasureMap.Status.Unavailable", true);
            }

            ResetMoneyBagTracking();
            return;
        }

        var now = Environment.TickCount64;
        TrackDecipheredMap();
        TickMoneyBagCollection(now);
        if (!IsBusy || now < nextActionAt)
        {
            return;
        }

        if (!config.EnableOneClickPurchase)
        {
            Stop("Feature.TreasureMap.Status.OneClickDisabled", true);
            return;
        }

        switch (state)
        {
            case TreasureMapState.Advance:
                Advance(now);
                break;
            case TreasureMapState.WaitingPurchase:
                WaitForPurchase(now);
                break;
            case TreasureMapState.WaitingStore:
                WaitForStore(now);
                break;
            case TreasureMapState.UseDecipher:
                UseDecipher(now);
                break;
            case TreasureMapState.SelectMap:
                SelectMap(now);
                break;
            case TreasureMapState.ConfirmDecipher:
                ConfirmDecipher(now);
                break;
            case TreasureMapState.WaitingDecipher:
                WaitForDecipher(now);
                break;
        }
    }

    public void OnConfigChanged()
    {
        if (!config.EnableOneClickPurchase && IsBusy)
        {
            Stop("Feature.TreasureMap.Status.OneClickDisabled", true);
        }

        if (!config.CollectMoneyBags)
        {
            ResetMoneyBagTracking();
        }
    }

    public void Cancel() => Stop("Feature.TreasureMap.Status.Cancelled", true);

    public void Reset()
    {
        Stop("Feature.TreasureMap.Status.Ready", true);
        ResetMoneyBagTracking();
    }

    private void Advance(long now)
    {
        var counts = GetCounts();
        if (counts.Saddlebag == 0)
        {
            if (counts.Inventory == 0)
            {
                BeginPurchase(1, counts.Inventory, now);
            }
            else
            {
                BeginStore(counts, now);
            }

            return;
        }

        if (!counts.Deciphered)
        {
            if (counts.Inventory == 0)
            {
                BeginPurchase(1, counts.Inventory, now);
            }
            else
            {
                state = TreasureMapState.UseDecipher;
                statusKey = "Feature.TreasureMap.Status.Deciphering";
                deadlineAt = now + DecipherTimeoutMs;
                nextActionAt = now;
            }

            return;
        }

        if (counts.Total < TargetMapCount)
        {
            BeginPurchase(TargetMapCount - counts.Total, counts.Inventory, now);
            return;
        }

        Stop("Feature.TreasureMap.Status.Completed", false);
    }

    private void BeginPurchase(int quantity, int inventoryCount, long now)
    {
        if (!marketPurchaseService.IsAvailable || !marketPurchaseService.CanPurchaseInCurrentArea)
        {
            Stop("Feature.TreasureMap.Status.MarketUnavailable", false);
            return;
        }

        if (!marketPurchaseService.RequestPurchase(
                activeMap.ItemId,
                false,
                (uint)quantity,
                maxUnitPrice))
        {
            Stop("Feature.TreasureMap.Status.PurchaseFailed", false);
            return;
        }

        inventoryBeforeOperation = inventoryCount;
        state = TreasureMapState.WaitingPurchase;
        statusKey = "Feature.TreasureMap.Status.Buying";
        deadlineAt = now + PurchaseTimeoutMs;
        serviceStoppedAt = 0;
        nextActionAt = now;
    }

    private void WaitForPurchase(long now)
    {
        if (GetCounts().Inventory > inventoryBeforeOperation)
        {
            ContinueAfterStep(now);
            return;
        }

        if (!marketPurchaseService.IsAvailable || now >= deadlineAt)
        {
            Stop("Feature.TreasureMap.Status.PurchaseFailed", true);
            return;
        }

        if (marketPurchaseService.IsBusy)
        {
            serviceStoppedAt = 0;
            return;
        }

        serviceStoppedAt = serviceStoppedAt == 0 ? now : serviceStoppedAt;
        if (now - serviceStoppedAt >= ServiceSettleTimeoutMs)
        {
            Stop("Feature.TreasureMap.Status.PurchaseFailed", false);
        }
    }

    private void BeginStore(TreasureMapCounts counts, long now)
    {
        if (!autoStoreService.IsAvailable ||
            !autoStoreService.RequestStore(
                [new AutoStoreItemRequest(activeMap.ItemId, 1)],
                AutoStoreListTarget.Saddlebag))
        {
            Stop("Feature.TreasureMap.Status.StoreUnavailable", false);
            return;
        }

        inventoryBeforeOperation = counts.Inventory;
        saddlebagBeforeOperation = counts.Saddlebag;
        state = TreasureMapState.WaitingStore;
        statusKey = "Feature.TreasureMap.Status.Storing";
        deadlineAt = now + StoreTimeoutMs;
        serviceStoppedAt = 0;
        nextActionAt = now;
    }

    private void WaitForStore(long now)
    {
        var counts = GetCounts();
        if (counts.Saddlebag > saddlebagBeforeOperation || counts.Inventory < inventoryBeforeOperation)
        {
            ContinueAfterStep(now);
            return;
        }

        if (!autoStoreService.IsAvailable || now >= deadlineAt)
        {
            Stop("Feature.TreasureMap.Status.StoreFailed", true);
            return;
        }

        if (autoStoreService.IsBusy)
        {
            serviceStoppedAt = 0;
            return;
        }

        serviceStoppedAt = serviceStoppedAt == 0 ? now : serviceStoppedAt;
        if (now - serviceStoppedAt >= ServiceSettleTimeoutMs)
        {
            Stop("Feature.TreasureMap.Status.StoreFailed", false);
        }
    }

    private void UseDecipher(long now)
    {
        if (GetCounts().Deciphered)
        {
            ContinueAfterStep(now);
            return;
        }

        if (now >= deadlineAt)
        {
            Stop("Feature.TreasureMap.Status.DecipherFailed", false);
            return;
        }

        if (DService.Instance().Condition[ConditionFlag.OccupiedInEvent] ||
            DService.Instance().Condition[ConditionFlag.Casting] ||
            !TreasureMapNativeUi.TryUseDecipher())
        {
            return;
        }

        state = TreasureMapState.SelectMap;
        statusKey = "Feature.TreasureMap.Status.Selecting";
        deadlineAt = now + DecipherSelectTimeoutMs;
    }

    private void SelectMap(long now)
    {
        if (GetCounts().Deciphered)
        {
            ContinueAfterStep(now);
            return;
        }

        if (now >= deadlineAt)
        {
            Stop("Feature.TreasureMap.Status.DecipherFailed", false);
            return;
        }

        if (!TreasureMapNativeUi.TrySelectMap(activeMap.Name))
        {
            return;
        }

        state = TreasureMapState.ConfirmDecipher;
        statusKey = "Feature.TreasureMap.Status.Confirming";
        confirmStartedAt = now;
        deadlineAt = now + DecipherConfirmTimeoutMs;
    }

    private void ConfirmDecipher(long now)
    {
        if (GetCounts().Deciphered)
        {
            ContinueAfterStep(now);
            return;
        }

        if (TreasureMapNativeUi.TryConfirmDecipher(activeMap.Name) ||
            now - confirmStartedAt >= 2_000 && !TreasureMapNativeUi.IsDecipherConfirmationVisible())
        {
            state = TreasureMapState.WaitingDecipher;
            statusKey = "Feature.TreasureMap.Status.WaitingDecipher";
            deadlineAt = now + DecipherTimeoutMs;
            return;
        }

        if (now >= deadlineAt)
        {
            Stop("Feature.TreasureMap.Status.DecipherFailed", false);
        }
    }

    private void WaitForDecipher(long now)
    {
        if (GetCounts().Deciphered)
        {
            ContinueAfterStep(now);
            return;
        }

        if (now >= deadlineAt)
        {
            Stop("Feature.TreasureMap.Status.DecipherFailed", false);
        }
    }

    private TreasureMapCounts GetCounts()
    {
        var inventory = 0;
        var saddlebag = 0;
        foreach (var item in inventoryService.GetItemsSnapshot())
        {
            if (item.ItemId != activeMap.ItemId)
            {
                continue;
            }

            if (item.Location == ItemInventoryLocation.Inventory)
            {
                inventory += item.Quantity;
            }
            else if (item.Location == ItemInventoryLocation.Saddlebag)
            {
                saddlebag += item.Quantity;
            }
        }

        return new(
            inventory,
            saddlebag,
            TreasureMapNativeUi.TryGetCurrentMap(out var rankId, out _) && rankId == activeMap.RankId);
    }

    private void ContinueAfterStep(long now)
    {
        state = TreasureMapState.Advance;
        statusKey = "Feature.TreasureMap.Status.Preparing";
        nextActionAt = now + StepDelayMs;
        deadlineAt = 0;
        serviceStoppedAt = 0;
    }

    private void TrackDecipheredMap()
    {
        TreasureMapNativeUi.TryGetCurrentMap(out var rankId, out var subRowId);
        if (!mapTrackerInitialized)
        {
            lastRankId = rankId;
            lastSubRowId = subRowId;
            mapTrackerInitialized = true;
            return;
        }

        if (rankId == lastRankId && subRowId == lastSubRowId)
        {
            return;
        }

        lastRankId = rankId;
        lastSubRowId = subRowId;
        if (rankId == 0 ||
            !TreasureMapNativeUi.TryResolveLocation(rankId, subRowId, out var location))
        {
            return;
        }

        if (config.OpenMapAfterDecipher)
        {
            TreasureMapNativeUi.OpenMap(location);
        }

        if (config.TeleportAfterDecipher)
        {
            teleportService.TryTeleportToNearest(location.TerritoryId, location.Position);
        }
    }

    private void TickMoneyBagCollection(long now)
    {
        if (!config.CollectMoneyBags || !privateFeatureProvider.IsAvailable)
        {
            ResetMoneyBagTracking();
            return;
        }

        if (now < nextMoneyBagAt)
        {
            return;
        }

        moneyBagTrackingActive = true;
        nextMoneyBagAt = now + MoneyBagIntervalMs;
        privateFeatureProvider.TryCollectNextTreasureMapMoneyBag();
    }

    private void ResetMoneyBagTracking()
    {
        if (moneyBagTrackingActive)
        {
            privateFeatureProvider.ResetTreasureMapMoneyBagTracking();
        }

        moneyBagTrackingActive = false;
        nextMoneyBagAt = 0;
    }

    private void Stop(string key, bool cancelOwnedOperation)
    {
        if (cancelOwnedOperation)
        {
            if (state == TreasureMapState.WaitingPurchase)
            {
                marketPurchaseService.Cancel();
            }
            else if (state == TreasureMapState.WaitingStore)
            {
                autoStoreService.Cancel();
            }
        }

        state = TreasureMapState.Idle;
        activeMap = default;
        statusKey = key;
        nextActionAt = 0;
        deadlineAt = 0;
        serviceStoppedAt = 0;
        confirmStartedAt = 0;
        inventoryBeforeOperation = 0;
        saddlebagBeforeOperation = 0;
        maxUnitPrice = 0;
    }
}
