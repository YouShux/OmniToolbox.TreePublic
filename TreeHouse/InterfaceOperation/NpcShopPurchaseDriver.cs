using Dalamud.Game.ClientState.Objects.Enums;
using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Teleport;
using OmenTools;
using OmenTools.Info.Game.ItemSource;
using OmenTools.Info.Game.ItemSource.Enums;
using OmenTools.Info.Game.ItemSource.Models;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal sealed class NpcShopPurchaseDriver(
    PlayerInventoryService inventoryService,
    AutoPathConfig autoPathConfig,
    TeleportService teleportService,
    AutoPathService autoPathService)
{
    private const int MenuSettleMs = 300;
    private const int InteractionIntervalMs = 1_000;
    private const int ItemLookupTimeoutMs = 2_000;
    private const float InteractionDistance = 4f;

    private PurchaseState state;
    private string statusKey = string.Empty;
    private string[] shopPath = [];
    private ShopNPCLocation? vendorLocation;
    private uint itemId;
    private uint vendorId;
    private uint remainingQuantity;
    private uint batchQuantity;
    private int shopPathIndex;
    private int batchStartCount;
    private long nextActionTick;
    private long stateStartedTick;
    private bool openedShop;
    private bool ownsAutoPath;
    private bool ownsTeleport;

    public bool IsActive => state != PurchaseState.Idle;

    public string StatusKey => statusKey;

    public bool Begin(uint requestedItemId, uint quantity)
    {
        Reset();
        itemId = requestedItemId;
        remainingQuantity = quantity;
        state = PurchaseState.ResolveSource;
        statusKey = "Feature.NpcShopPurchase.Status.Source";
        return !Tick();
    }

    public bool Tick()
    {
        if (state == PurchaseState.Idle)
        {
            return true;
        }

        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn || services.ObjectTable.LocalPlayer is null)
        {
            Fail("Feature.NpcShopPurchase.Status.PlayerUnavailable");
            return true;
        }

        return state switch
        {
            PurchaseState.ResolveSource => ResolveSource(),
            PurchaseState.Teleport => AdvanceTeleport(),
            PurchaseState.AutoPath => AdvanceAutoPath(),
            PurchaseState.OpenShop => OpenShop(),
            PurchaseState.SelectItem => SelectItem(),
            PurchaseState.ConfirmPurchase => ConfirmPurchase(),
            PurchaseState.WaitInventory => WaitInventory(),
            _ => true
        };
    }

    public void Cancel() => Stop("Feature.NpcShopPurchase.Status.Cancelled");

    public void Fail(string reasonKey) => Stop(reasonKey);

    public void SetStatus(string key) => statusKey = key;

    public void Reset()
    {
        ReleaseTeleport();
        ReleaseAutoPath();
        if (state is PurchaseState.ConfirmPurchase or PurchaseState.WaitInventory)
        {
            NpcShopPurchaseNativeUi.CancelPurchaseDialog();
        }

        CloseOwnedShop();
        state = PurchaseState.Idle;
        statusKey = string.Empty;
        shopPath = [];
        vendorLocation = null;
        itemId = 0;
        vendorId = 0;
        remainingQuantity = 0;
        batchQuantity = 0;
        shopPathIndex = 0;
        batchStartCount = 0;
        nextActionTick = 0;
        stateStartedTick = 0;
    }

    private bool ResolveSource()
    {
        var result = ItemSourceInfo.Query(itemId);
        if (result.State == ItemSourceQueryState.Building)
        {
            statusKey = "Feature.NpcShopPurchase.Status.Source";
            return false;
        }

        if (result.State != ItemSourceQueryState.Ready || result.Data is null)
        {
            Fail("Feature.NpcShopPurchase.Status.SourceUnavailable");
            return true;
        }

        var vendor = SelectVendor(result.Data, autoPathConfig.Enabled, out var vendorDistanceSquared);
        if (vendor is null)
        {
            Fail(HasGilVendor(result.Data)
                ? "Feature.NpcShopPurchase.Status.VendorUnavailable"
                : "Feature.NpcShopPurchase.Status.NoGilShop");
            return true;
        }

        vendorId = vendor.ID;
        var location = vendor.Location;
        if (location is null)
        {
            Fail("Feature.NpcShopPurchase.Status.VendorUnavailable");
            return true;
        }

        vendorLocation = location;
        shopPath = string.IsNullOrWhiteSpace(vendor.ShopName)
            ? []
            : vendor.ShopName.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!autoPathConfig.Enabled || vendorDistanceSquared <= InteractionDistance * InteractionDistance)
        {
            state = PurchaseState.OpenShop;
            statusKey = "Feature.NpcShopPurchase.Status.OpeningShop";
            nextActionTick = 0;
            return false;
        }

        var waitForLanding = vendorDistanceSquared == float.MaxValue &&
                             teleportService.TryTeleportToNearest(location);
        if (waitForLanding && teleportService.IsBusy)
        {
            ownsTeleport = true;
            state = PurchaseState.Teleport;
            statusKey = "Feature.NpcShopPurchase.Status.WaitingLanding";
            return false;
        }

        if ((!waitForLanding &&
             DService.Instance().ClientState.TerritoryType != location.TerritoryID) ||
            !autoPathService.Begin(vendorId, location, waitForLanding))
        {
            Fail("Feature.NpcShopPurchase.Status.PathUnavailable");
            return true;
        }

        ownsAutoPath = true;
        state = PurchaseState.AutoPath;
        statusKey = waitForLanding
            ? "Feature.NpcShopPurchase.Status.WaitingLanding"
            : "Feature.NpcShopPurchase.Status.WaitingNavigation";
        return false;
    }

    private bool AdvanceTeleport()
    {
        if (teleportService.IsBusy)
        {
            statusKey = "Feature.NpcShopPurchase.Status.WaitingLanding";
            return false;
        }

        ownsTeleport = false;
        var location = vendorLocation;
        if (!teleportService.LastAethernetSucceeded ||
            location is null ||
            DService.Instance().ClientState.TerritoryType != location.TerritoryID ||
            !autoPathService.Begin(vendorId, location, waitForLanding: false))
        {
            Fail("Feature.NpcShopPurchase.Status.PathUnavailable");
            return true;
        }

        ownsAutoPath = true;
        state = PurchaseState.AutoPath;
        statusKey = "Feature.NpcShopPurchase.Status.WaitingNavigation";
        return false;
    }

    private bool AdvanceAutoPath()
    {
        switch (autoPathService.Tick())
        {
            case AutoPathStage.WaitingForLanding:
                statusKey = "Feature.NpcShopPurchase.Status.WaitingLanding";
                return false;
            case AutoPathStage.WaitingForNavigation:
                statusKey = "Feature.NpcShopPurchase.Status.WaitingNavigation";
                return false;
            case AutoPathStage.Moving:
                statusKey = "Feature.NpcShopPurchase.Status.Pathfinding";
                return false;
            case AutoPathStage.Arrived:
                ReleaseAutoPath();
                state = PurchaseState.OpenShop;
                statusKey = "Feature.NpcShopPurchase.Status.OpeningShop";
                nextActionTick = 0;
                return false;
            case AutoPathStage.Cancelled:
                Stop("Feature.NpcShopPurchase.Status.Cancelled");
                return true;
            case AutoPathStage.Failed:
                Fail("Feature.NpcShopPurchase.Status.PathUnavailable");
                return true;
            default:
                return false;
        }
    }

    private bool OpenShop()
    {
        if (NpcShopPurchaseNativeUi.IsShopOpen())
        {
            state = PurchaseState.SelectItem;
            statusKey = "Feature.NpcShopPurchase.Status.SelectingItem";
            stateStartedTick = Environment.TickCount64;
            nextActionTick = stateStartedTick + MenuSettleMs;
            return false;
        }

        var now = Environment.TickCount64;
        if (now < nextActionTick)
        {
            return false;
        }

        if (NpcShopPurchaseNativeUi.IsShopMenuOpen())
        {
            if (shopPathIndex < shopPath.Length && NpcShopPurchaseNativeUi.TrySelectMenu(shopPath[shopPathIndex]))
            {
                shopPathIndex++;
                nextActionTick = now + MenuSettleMs;
            }

            return false;
        }

        if (NpcShopPurchaseNativeUi.TryInteract(vendorId))
        {
            openedShop = true;
        }

        nextActionTick = now + InteractionIntervalMs;
        return false;
    }

    private bool SelectItem()
    {
        var now = Environment.TickCount64;
        if (now < nextActionTick)
        {
            return false;
        }

        batchQuantity = Math.Min(remainingQuantity, 99u);
        batchStartCount = GetOwnedCount();
        switch (NpcShopPurchaseNativeUi.TriggerPurchase(itemId, batchQuantity))
        {
            case PurchaseTriggerResult.Triggered:
                state = PurchaseState.ConfirmPurchase;
                statusKey = "Feature.NpcShopPurchase.Status.Confirming";
                stateStartedTick = now;
                nextActionTick = now + 200;
                return false;
            case PurchaseTriggerResult.ItemNotFound when now - stateStartedTick >= ItemLookupTimeoutMs:
                Fail("Feature.NpcShopPurchase.Status.ItemUnavailable");
                return true;
            default:
                return false;
        }
    }

    private bool ConfirmPurchase()
    {
        if (HasReceivedBatch())
        {
            return CompleteBatch();
        }

        var now = Environment.TickCount64;
        if (now < nextActionTick)
        {
            return false;
        }

        if (NpcShopPurchaseNativeUi.TryConfirmPurchase() || now - stateStartedTick >= 1_500)
        {
            state = PurchaseState.WaitInventory;
            statusKey = "Feature.NpcShopPurchase.Status.WaitingInventory";
        }

        return false;
    }

    private bool WaitInventory() => HasReceivedBatch() && CompleteBatch();

    private bool CompleteBatch()
    {
        remainingQuantity -= batchQuantity;
        batchQuantity = 0;
        if (remainingQuantity == 0)
        {
            CloseOwnedShop();
            state = PurchaseState.Idle;
            statusKey = "Feature.NpcShopPurchase.Status.Completed";
            return true;
        }

        state = PurchaseState.SelectItem;
        statusKey = "Feature.NpcShopPurchase.Status.SelectingItem";
        stateStartedTick = Environment.TickCount64;
        nextActionTick = stateStartedTick + MenuSettleMs;
        return false;
    }

    private bool HasReceivedBatch() => GetOwnedCount() >= batchStartCount + (int)batchQuantity;

    private int GetOwnedCount() =>
        inventoryService.GetCounts().TryGetValue(itemId, out var count) ? count.Total : 0;

    private static ShopNPCInfos? SelectVendor(
        ItemSourceInfo source,
        bool allowTravel,
        out float selectedDistanceSquared)
    {
        var services = DService.Instance();
        var player = services.ObjectTable.LocalPlayer!;
        var currentTerritory = services.ClientState.TerritoryType;
        ShopNPCInfos? selected = null;
        ShopNPCInfos? fallback = null;
        selectedDistanceSquared = float.MaxValue;
        var bestMatchesTerritory = false;

        foreach (var npc in source.NPCInfos)
        {
            if (!UsesGil(npc.CostInfos))
            {
                continue;
            }

            if (allowTravel &&
                npc is { ID: not 0, Location: { } npcLocation } &&
                (fallback?.Location is not { } fallbackLocation ||
                 fallbackLocation.TerritoryID != currentTerritory && npcLocation.TerritoryID == currentTerritory))
            {
                fallback = npc;
            }

            foreach (var gameObject in services.ObjectTable)
            {
                if (!gameObject.IsValid() ||
                    !gameObject.IsTargetable ||
                    gameObject.ObjectKind != ObjectKind.EventNpc ||
                    gameObject.DataID != npc.ID)
                {
                    continue;
                }

                var matchesTerritory = npc.Location is not null && npc.Location.TerritoryID == currentTerritory;
                var distanceSquared = Vector3.DistanceSquared(player.Position, gameObject.Position);
                if (selected is not null &&
                    (bestMatchesTerritory && !matchesTerritory ||
                     bestMatchesTerritory == matchesTerritory && distanceSquared >= selectedDistanceSquared))
                {
                    continue;
                }

                selected = npc;
                selectedDistanceSquared = distanceSquared;
                bestMatchesTerritory = matchesTerritory;
            }
        }

        return selected ?? fallback;
    }

    private static bool HasGilVendor(ItemSourceInfo source)
    {
        foreach (var npc in source.NPCInfos)
        {
            if (UsesGil(npc.CostInfos))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsesGil(IReadOnlyList<ShopItemCostInfo> costs)
        => costs.Count == 1 && costs[0].ItemID == 1;

    private void Stop(string key)
    {
        ReleaseTeleport();
        ReleaseAutoPath();
        if (state is PurchaseState.ConfirmPurchase or PurchaseState.WaitInventory)
        {
            NpcShopPurchaseNativeUi.CancelPurchaseDialog();
        }

        CloseOwnedShop();
        state = PurchaseState.Idle;
        statusKey = key;
        remainingQuantity = 0;
        batchQuantity = 0;
    }

    private void ReleaseTeleport()
    {
        if (!ownsTeleport)
        {
            return;
        }

        teleportService.Cancel();
        ownsTeleport = false;
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

    private void CloseOwnedShop()
    {
        if (openedShop)
        {
            NpcShopPurchaseNativeUi.CloseShop();
            openedShop = false;
        }
    }

    private enum PurchaseState
    {
        Idle,
        ResolveSource,
        Teleport,
        AutoPath,
        OpenShop,
        SelectItem,
        ConfirmPurchase,
        WaitInventory
    }
}
