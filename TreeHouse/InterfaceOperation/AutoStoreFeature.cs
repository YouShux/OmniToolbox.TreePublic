using Dalamud.Game.ClientState.Conditions;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;
using OmniToolbox.Teleport;
using OmniToolbox.Verification;
using OmenTools;
using OmenTools.Threading.TaskHelper;
using OmenTools.Threading.TaskHelper.Enums;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed class AutoStoreFeature : TreeHouseFeatureBase, IAutoStoreService
{
    private const int OperationIntervalMs = 100;
    private const int OperationTimeoutMs = 30_000;
    private const int FacilityOperationTimeoutMs = 180_000;

    private readonly AutoStoreConfig config;
    private readonly AutoPathService autoPathService;
    private readonly AutoStoreDriver driver;
    private readonly FeatureGate featureGate;
    private readonly AutoStoreHotkey hotkey;
    private readonly IInnEntryService innEntryService;
    private readonly PlayerInventoryService inventoryService;
    private readonly AutoStoreListWindow listWindow;
    private readonly TeleportService teleportService;
    private AutoStoreOperation? activeOperation;
    private FeatureLifetime? runtimeLifetime;
    private TaskHelper? taskHelper;

    internal AutoStoreFeature(
        AutoStoreConfig config,
        PlayerInventoryService inventoryService,
        FeatureGate featureGate,
        IInnEntryService innEntryService,
        AutoPathService autoPathService,
        TeleportService teleportService,
        Action saveConfig)
        : base(new(
            "auto-store",
            "Feature.AutoStore.Title",
            "Feature.AutoStore.Description",
            TreeHouseCategory.InterfaceOperation,
            true,
            false,
            true))
    {
        this.config = config;
        driver = new(inventoryService);
        this.featureGate = featureGate;
        this.innEntryService = innEntryService;
        this.autoPathService = autoPathService;
        this.inventoryService = inventoryService;
        this.teleportService = teleportService;
        listWindow = new(config, inventoryService, this, saveConfig);
        hotkey = new(config, ToggleList);
    }

    public bool IsAvailable => IsEnabled && taskHelper is not null;

    public bool IsCacheCollectionAvailable => DalamudServices.PlayerState.ContentId != 0;

    public bool IsBusy => taskHelper?.IsBusy == true;

    public override bool HasSettings => true;

    public override bool DrawSettings() => AutoStorePanel.Draw(config, hotkey, IsAvailable, OpenList);

    public bool RequestRetrieve(uint itemId)
    {
        if (!CanStart() || itemId == 0)
        {
            return false;
        }

        return EnqueueRetrieve(new(itemId));
    }

    public bool RequestRetrieve(IReadOnlyList<uint> itemIds)
    {
        if (itemIds.Count == 0)
        {
            return false;
        }

        var requests = new AutoStoreItemRequest[itemIds.Count];
        for (var index = 0; index < itemIds.Count; index++)
        {
            requests[index] = new(itemIds[index]);
        }

        return RequestRetrieve(requests);
    }

    public bool RequestRetrieve(IReadOnlyList<AutoStoreItemRequest> items)
    {
        if (!CanStart() || items.Count == 0)
        {
            return false;
        }

        var seen = new HashSet<uint>();
        var queued = false;
        for (var index = 0; index < items.Count; index++)
        {
            var request = items[index];
            if (request.ItemId == 0 || !seen.Add(request.ItemId))
            {
                continue;
            }

            if (queued)
            {
                taskHelper!.DelayNext(OperationIntervalMs, "Auto store interval");
            }

            queued |= EnqueueRetrieve(new(request.ItemId, Math.Max(1, request.Quantity)));
        }

        return queued;
    }

    public bool RequestStore(uint itemId, string destination)
    {
        if (!CanStart() || itemId == 0 || !AutoStoreDestination.TryParse(destination, out var target))
        {
            return false;
        }

        EnqueueStore(new(itemId), target);
        return true;
    }

    public bool RequestStore(IReadOnlyList<AutoStoreItemRequest> items, AutoStoreListTarget destination)
    {
        if (!CanStart() || items.Count == 0 || !TryGetDestination(destination, out var target))
        {
            return false;
        }

        var seen = new HashSet<uint>();
        var queued = false;
        for (var index = 0; index < items.Count; index++)
        {
            var request = items[index];
            if (request.ItemId == 0 || !seen.Add(request.ItemId))
            {
                continue;
            }

            if (queued)
            {
                taskHelper!.DelayNext(OperationIntervalMs, "Auto store interval");
            }

            EnqueueStore(new(request.ItemId, Math.Max(1, request.Quantity)), target);
            queued = true;
        }

        return queued;
    }

    public bool RequestCacheCollection(AutoStoreCacheCollection target)
    {
        if (!IsCacheCollectionAvailable || IsBusy)
        {
            return false;
        }

        taskHelper ??= CreateTaskHelper();
        switch (target)
        {
            case AutoStoreCacheCollection.Retainer:
                for (var index = 0; index < 10; index++)
                {
                    if (index > 0)
                    {
                        taskHelper!.DelayNext(OperationIntervalMs, "Character cache collection interval");
                    }

                    var retainerIndex = index;
                    EnqueueCacheCollection(
                        AutoStoreDestinationKind.Retainer,
                        retainerIndex,
                        () => AutoStoreNativeUi.IsRetainerIndexUnavailable(retainerIndex));
                }
                break;
            case AutoStoreCacheCollection.GlamourDresserAndArmoire:
                EnqueueCacheCollection(AutoStoreDestinationKind.GlamourDresser);
                taskHelper!.DelayNext(OperationIntervalMs, "Character cache collection interval");
                EnqueueCacheCollection(AutoStoreDestinationKind.Armoire);
                break;
            case AutoStoreCacheCollection.FreeCompanyChest:
                EnqueueCacheCollection(AutoStoreDestinationKind.FreeCompanyChest);
                break;
            default:
                return false;
        }

        return true;
    }

    public bool OpenList()
    {
        if (!IsAvailable)
        {
            return false;
        }

        listWindow.Open();
        return true;
    }

    public bool ToggleList()
    {
        if (!IsAvailable)
        {
            return false;
        }

        listWindow.Toggle();
        return true;
    }

    public void Cancel()
    {
        CancelOperation(activeOperation);
        taskHelper?.Abort();
    }

    protected override void OnEnable()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        taskHelper ??= CreateTaskHelper();

        var lifetime = new FeatureLifetime();
        try
        {
            DalamudServices.PluginInterface.UiBuilder.Draw += listWindow.Draw;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= listWindow.Draw);
            hotkey.Register(lifetime);
            runtimeLifetime = lifetime;
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            hotkey.Reset();
            listWindow.Close();
            CancelOperation(activeOperation);
            taskHelper?.Abort();
        }
    }

    protected override void OnDispose()
    {
        CancelOperation(activeOperation);
        taskHelper?.Abort();
        taskHelper?.Dispose();
        taskHelper = null;
    }

    private bool CanStart() => IsAvailable && !IsBusy;

    private static TaskHelper CreateTaskHelper() => new()
    {
        RetryIntervalMS = OperationIntervalMs,
        TimeoutMS = OperationTimeoutMs
    };

    private bool EnqueueRetrieve(AutoStoreItemRequest request)
    {
        var itemId = request.ItemId;
        var quantityState = request.Quantity == int.MaxValue
            ? null
            : new AutoStoreQuantityState(
                inventoryService,
                itemId,
                request.Quantity,
                ItemInventoryLocation.Inventory,
                false,
                remaining => driver.Retrieve(itemId, remaining));
        bool Retrieve() => quantityState?.Execute() ?? driver.Retrieve(itemId);
        Func<bool>? isComplete = quantityState is null ? null : quantityState.IsComplete;

        var locations = inventoryService.GetCounts().TryGetValue(itemId, out var count)
            ? count.Locations
            : ItemInventoryLocation.None;
        var queued = false;

        if ((locations & ItemInventoryLocation.Saddlebag) != 0)
        {
            Enqueue(
                Retrieve,
                $"Retrieve item {itemId}",
                AutoStoreDestinationKind.Saddlebag,
                isComplete: isComplete);
            queued = true;
        }

        if ((locations & ItemInventoryLocation.FreeCompanyChest) != 0)
        {
            Enqueue(
                Retrieve,
                $"Retrieve item {itemId}",
                AutoStoreDestinationKind.FreeCompanyChest,
                isComplete: isComplete);
            queued = true;
        }

        var unmappedRetainer = false;
        if ((locations & ItemInventoryLocation.Retainer) != 0)
        {
            var seenRetainers = new HashSet<ulong>();
            foreach (var item in inventoryService.GetItemsSnapshot())
            {
                if (item.ItemId != itemId ||
                    item.Location != ItemInventoryLocation.Retainer ||
                    item.HolderId == 0 ||
                    !seenRetainers.Add(item.HolderId))
                {
                    continue;
                }

                if (!AutoStoreNativeUi.TryGetRetainerIndex(item.HolderId, out var retainerIndex))
                {
                    unmappedRetainer = true;
                    continue;
                }

                Enqueue(
                    Retrieve,
                    $"Retrieve item {itemId}",
                    AutoStoreDestinationKind.Retainer,
                    retainerIndex: retainerIndex,
                    isComplete: isComplete);
                queued = true;
            }
        }

        if ((locations & ItemInventoryLocation.GlamourDresser) != 0)
        {
            Enqueue(
                Retrieve,
                $"Retrieve item {itemId}",
                AutoStoreDestinationKind.GlamourDresser,
                isComplete: isComplete);
            queued = true;
        }

        if ((locations & ItemInventoryLocation.Armoire) != 0)
        {
            Enqueue(
                Retrieve,
                $"Retrieve item {itemId}",
                AutoStoreDestinationKind.Armoire,
                isComplete: isComplete);
            queued = true;
        }

        if (!queued || unmappedRetainer)
        {
            Enqueue(Retrieve, $"Retrieve item {itemId}", isComplete: isComplete);
        }

        return true;
    }

    private void EnqueueStore(AutoStoreItemRequest request, AutoStoreDestination target)
    {
        var quantityState = request.Quantity == int.MaxValue
            ? null
            : new AutoStoreQuantityState(
                inventoryService,
                request.ItemId,
                request.Quantity,
                target.Kind == AutoStoreDestinationKind.Saddlebag
                    ? ItemInventoryLocation.Inventory
                    : ItemInventoryLocation.Inventory | ItemInventoryLocation.Saddlebag,
                true,
                remaining => driver.Store(request.ItemId, target, remaining));
        bool Store() => quantityState?.Execute() ??
                        driver.Store(request.ItemId, target);
        Func<bool>? isComplete = quantityState is null ? null : quantityState.IsComplete;
        Enqueue(
            Store,
            $"Store item {request.ItemId}",
            target.Kind,
            true,
            target.RetainerIndex,
            isComplete);
    }

    private void EnqueueCacheCollection(
        AutoStoreDestinationKind facility,
        int retainerIndex = -1,
        Func<bool>? isComplete = null)
    {
        bool Capture()
        {
            if (AutoStoreNativeUi.Prepare(
                    facility,
                    false,
                    retainerIndex,
                    false,
                    out _) != AutoStorePreparation.Ready)
            {
                return false;
            }

            inventoryService.CaptureLoadedInteractionCaches(facility switch
            {
                AutoStoreDestinationKind.Retainer =>
                    ItemInventoryLocation.Retainer | ItemInventoryLocation.RetainerMarket,
                AutoStoreDestinationKind.GlamourDresser => ItemInventoryLocation.GlamourDresser,
                AutoStoreDestinationKind.Armoire => ItemInventoryLocation.Armoire,
                AutoStoreDestinationKind.FreeCompanyChest => ItemInventoryLocation.FreeCompanyChest,
                _ => 0
            });
            return true;
        }

        Enqueue(
            Capture,
            $"Collect {facility} character cache",
            facility,
            retainerIndex: retainerIndex,
            isComplete: isComplete);
    }

    private void Enqueue(
        Func<bool> action,
        string name,
        AutoStoreDestinationKind? facility = null,
        bool isStore = false,
        int retainerIndex = -1,
        Func<bool>? isComplete = null)
    {
        var operation = new AutoStoreOperation(
            action,
            isComplete,
            facility,
            isStore,
            retainerIndex,
            innEntryService,
            autoPathService,
            teleportService);
        taskHelper!.Enqueue(
            () => Execute(operation),
            name,
            timeoutMS: facility is null or AutoStoreDestinationKind.Saddlebag
                ? OperationTimeoutMs
                : FacilityOperationTimeoutMs,
            timeoutBehaviour: TaskAbortBehaviour.AbortCurrent,
            exceptionBehaviour: TaskAbortBehaviour.AbortCurrent,
            timeoutAction: () => CancelOperation(operation),
            exceptionAction: () => CancelOperation(operation));
    }

    private bool Execute(AutoStoreOperation operation)
    {
        activeOperation = operation;
        if (!operation.Execute())
        {
            return false;
        }

        operation.Finish();
        if (ReferenceEquals(activeOperation, operation))
        {
            activeOperation = null;
        }

        return true;
    }

    private void CancelOperation(AutoStoreOperation? operation)
    {
        operation?.Cancel();
        if (ReferenceEquals(activeOperation, operation))
        {
            activeOperation = null;
        }
    }

    private static bool SupportsInnFallback(AutoStoreDestinationKind? facility) =>
        facility is AutoStoreDestinationKind.Retainer or
            AutoStoreDestinationKind.GlamourDresser or
            AutoStoreDestinationKind.Armoire;

    private static bool TryGetDestination(AutoStoreListTarget target, out AutoStoreDestination destination)
    {
        if (target is >= AutoStoreListTarget.Retainer1 and <= AutoStoreListTarget.Retainer10)
        {
            destination = new(AutoStoreDestinationKind.Retainer, (int)target - (int)AutoStoreListTarget.Retainer1);
            return true;
        }

        destination = target switch
        {
            AutoStoreListTarget.Saddlebag => new(AutoStoreDestinationKind.Saddlebag),
            AutoStoreListTarget.GlamourDresser => new(AutoStoreDestinationKind.GlamourDresser),
            AutoStoreListTarget.Armoire => new(AutoStoreDestinationKind.Armoire),
            AutoStoreListTarget.FreeCompanyChest => new(AutoStoreDestinationKind.FreeCompanyChest),
            _ => default
        };
        return target is >= AutoStoreListTarget.Saddlebag and <= AutoStoreListTarget.FreeCompanyChest;
    }

    private sealed class AutoStoreOperation(
        Func<bool> action,
        Func<bool>? isComplete,
        AutoStoreDestinationKind? facility,
        bool isStore,
        int retainerIndex,
        IInnEntryService innEntryService,
        AutoPathService autoPathService,
        TeleportService teleportService)
    {
        private const uint FreeCompanyChestTerritoryId = 129;
        private const int FacilitySettleIntervalMs = 300;
        private const int LocalFacilitySettleIntervalMs = 600;
        private const int SaddlebagOpenIntervalMs = 250;
        private const int ObjectInteractIntervalMs = 2_000;
        private const int PostTeleportSettleIntervalMs = 1_000;
        private const int TeleportRetryIntervalMs = 12_000;
        private const int TeleportUnavailableRetryIntervalMs = 500;

        private bool actionCompleted;
        private bool cancelled;
        private bool openedFacility;
        private bool ownsAutoPath;
        private bool pathAttempted;
        private bool requestedInnEntry;
        private bool teleportRequested;
        private long finishAfter;
        private long nextOpenAttempt;
        private long nextTeleportAttempt;
        private long territoryReadyAfter;

        public bool Execute()
        {
            if (cancelled)
            {
                return true;
            }

            if (isComplete?.Invoke() == true)
            {
                return true;
            }

            if (facility.HasValue)
            {
                var now = Environment.TickCount64;
                if (ownsAutoPath)
                {
                    switch (autoPathService.Tick())
                    {
                        case AutoPathStage.WaitingForLanding:
                        case AutoPathStage.WaitingForNavigation:
                        case AutoPathStage.Moving:
                            return false;
                        case AutoPathStage.Arrived:
                            ReleaseAutoPath();
                            nextOpenAttempt = now + FacilitySettleIntervalMs;
                            return false;
                        case AutoPathStage.Cancelled:
                            ReleaseAutoPath();
                            return true;
                        case AutoPathStage.Failed:
                            ReleaseAutoPath();
                            break;
                    }
                }

                if (facility == AutoStoreDestinationKind.FreeCompanyChest &&
                    !PrepareFreeCompanyChestTerritory(now))
                {
                    return false;
                }

                var supportsInnFallback = SupportsInnFallback(facility);
                if (supportsInnFallback && innEntryService.IsRunning)
                {
                    return false;
                }

                var canInteract = now >= nextOpenAttempt;
                var preparation = AutoStoreNativeUi.Prepare(
                    facility.Value,
                    isStore,
                    retainerIndex,
                    canInteract,
                    out var openedByCall);
                openedFacility |= openedByCall;

                if (preparation == AutoStorePreparation.Unavailable)
                {
                    if (!pathAttempted && autoPathService.IsRunning)
                    {
                        return false;
                    }

                    if (!pathAttempted && canInteract)
                    {
                        nextOpenAttempt = now + ObjectInteractIntervalMs;
                        if (autoPathService.Begin(gameObject =>
                                AutoStoreNativeUi.IsFacilityObject(facility.Value, gameObject)))
                        {
                            ownsAutoPath = true;
                            pathAttempted = true;
                            return false;
                        }
                    }

                    if (!supportsInnFallback)
                    {
                        return false;
                    }

                    if (innEntryService.IsEnteredInnRoom)
                    {
                        return false;
                    }

                    if (!requestedInnEntry && innEntryService.RequestEnterNearestInnForCacheCollection())
                    {
                        requestedInnEntry = true;
                        return false;
                    }

                    return false;
                }

                if (preparation == AutoStorePreparation.Waiting)
                {
                    if (canInteract)
                    {
                        nextOpenAttempt = now + (facility == AutoStoreDestinationKind.Saddlebag
                            ? SaddlebagOpenIntervalMs
                            : ObjectInteractIntervalMs);
                    }

                    return false;
                }
            }

            if (isComplete?.Invoke() == true)
            {
                return true;
            }

            if (actionCompleted)
            {
                return Environment.TickCount64 >= finishAfter;
            }

            if (!action())
            {
                return false;
            }

            if (openedFacility &&
                facility is AutoStoreDestinationKind.GlamourDresser or AutoStoreDestinationKind.Armoire)
            {
                actionCompleted = true;
                finishAfter = Environment.TickCount64 + FacilitySettleIntervalMs;
                return false;
            }

            return true;
        }

        public void Finish()
        {
            ReleaseAutoPath();
            if (requestedInnEntry)
            {
                innEntryService.Cancel();
                requestedInnEntry = false;
            }

            if (!openedFacility || !facility.HasValue)
            {
                return;
            }

            AutoStoreNativeUi.Close(facility.Value);
            openedFacility = false;
        }

        public void Cancel()
        {
            cancelled = true;
            Finish();
        }

        private void ReleaseAutoPath()
        {
            if (!ownsAutoPath)
            {
                return;
            }

            autoPathService.Cancel();
            ownsAutoPath = false;
        }

        private bool PrepareFreeCompanyChestTerritory(long now)
        {
            var services = DService.Instance();
            var condition = services.Condition;
            if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
            {
                territoryReadyAfter = 0;
                return false;
            }

            if (services.ClientState.TerritoryType == FreeCompanyChestTerritoryId)
            {
                if (territoryReadyAfter == 0)
                {
                    territoryReadyAfter = now + (teleportRequested
                        ? PostTeleportSettleIntervalMs
                        : LocalFacilitySettleIntervalMs);
                }

                return now >= territoryReadyAfter;
            }

            territoryReadyAfter = 0;
            if (now < nextTeleportAttempt)
            {
                return false;
            }

            if (teleportService.TryTeleportToTerritory(FreeCompanyChestTerritoryId))
            {
                teleportRequested = true;
                nextTeleportAttempt = now + TeleportRetryIntervalMs;
            }
            else
            {
                nextTeleportAttempt = now + TeleportUnavailableRetryIntervalMs;
            }

            return false;
        }
    }

    private sealed class AutoStoreQuantityState(
        PlayerInventoryService inventoryService,
        uint itemId,
        int quantity,
        ItemInventoryLocation trackedLocations,
        bool decreases,
        Func<int, bool> move)
    {
        private int baselineQuantity;
        private int trackedQuantity;
        private long trackedRevision = -1;
        private long waitingRevision = -1;
        private bool initialized;

        public bool IsComplete() => GetRemainingQuantity() == 0;

        public bool Execute()
        {
            var remainingQuantity = GetRemainingQuantity();
            if (remainingQuantity == 0)
            {
                return true;
            }

            var revision = inventoryService.GetSnapshotRevision();
            if (waitingRevision == revision)
            {
                return false;
            }

            if (move(remainingQuantity))
            {
                return true;
            }

            waitingRevision = revision;
            return false;
        }

        private int GetRemainingQuantity()
        {
            var current = GetTrackedQuantity();
            if (!initialized)
            {
                baselineQuantity = current;
                initialized = true;
            }

            var transferred = decreases
                ? baselineQuantity - current
                : current - baselineQuantity;
            return Math.Max(0, quantity - Math.Max(0, transferred));
        }

        private int GetTrackedQuantity()
        {
            var revision = inventoryService.GetSnapshotRevision();
            if (revision == trackedRevision)
            {
                return trackedQuantity;
            }

            long total = 0;
            foreach (var item in inventoryService.GetItemsSnapshot())
            {
                if (item.ItemId != itemId || (item.Location & trackedLocations) == 0)
                {
                    continue;
                }

                total += item.Quantity;
            }

            trackedRevision = revision;
            trackedQuantity = (int)Math.Min(total, int.MaxValue);
            return trackedQuantity;
        }
    }
}
