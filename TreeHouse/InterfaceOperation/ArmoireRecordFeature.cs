using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed unsafe class ArmoireRecordFeature : TreeHouseFeatureBase, IArmoireRecordService
{
    private const string CabinetAddonName = "Cabinet";
    private const uint UpdateIntervalMs = 100;
    private const int StoreIntervalMs = 100;

    private readonly ArmoireRecordConfig config;
    private readonly PlayerInventoryService inventoryService;
    private readonly IAutoStoreService autoStoreService;
    private readonly Queue<uint> storeQueue = [];
    private readonly HashSet<uint> storeRows = [];
    private readonly Dictionary<ArmoireRecordScanKey, ScanAggregate> aggregates = [];
    private readonly List<ArmoireRecordItem> scanItems = [];
    private FeatureLifetime? runtimeLifetime;
    private ArmoireRecordNativeUi? window;
    private bool cabinetOpen;
    private bool hasChecked;
    private bool isStoring;
    private bool retrieveRequested;
    private bool lastCabinetLoaded;
    private long lastSnapshotRevision = -1;
    private long nextStoreTick;

    public ArmoireRecordFeature(
        ArmoireRecordConfig config,
        PlayerInventoryService inventoryService,
        IAutoStoreService autoStoreService)
        : base(new(
            "armoire-record",
            "Feature.ArmoireRecord.Title",
            "Feature.ArmoireRecord.Description",
            TreeHouseCategory.InterfaceOperation,
            true))
    {
        this.config = config;
        this.inventoryService = inventoryService;
        this.autoStoreService = autoStoreService;
    }

    public bool IsStoring => isStoring;

    public bool IsRetrieving => retrieveRequested && autoStoreService.IsBusy;

    public int ItemCount => scanItems.Count;

    public int DirectStoreCount
    {
        get
        {
            var count = 0;
            for (var index = 0; index < scanItems.Count; index++)
            {
                if (scanItems[index].CanDirectStore)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => ArmoireRecordPanel.Draw(config, this, IsEnabled);

    public bool RequestCheck()
    {
        if (!IsEnabled || window?.IsOpen != true)
        {
            return false;
        }

        hasChecked = true;
        EnsureCabinetRequested();
        RefreshWindow();
        return true;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            window = new();
            lifetime.Add(window.Dispose);

            if (!FrameworkManager.Instance().Reg(OnUpdate, UpdateIntervalMs))
            {
                throw new InvalidOperationException("Armoire record update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnUpdate));
            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PostSetup, CabinetAddonName, OnCabinetAddon);
            addonEvents.Register(AddonEvent.PostRequestedUpdate, CabinetAddonName, OnCabinetAddon);
            addonEvents.Register(AddonEvent.PostRefresh, CabinetAddonName, OnCabinetAddon);
            addonEvents.Register(AddonEvent.PreFinalize, CabinetAddonName, OnCabinetAddon);
            runtimeLifetime = lifetime;
            OpenForCurrentCabinet();
        }
        catch
        {
            lifetime.Dispose();
            window = null;
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
            window = null;
            ResetState();
        }
    }

    private void OnCabinetAddon(AddonEvent type, AddonArgs _)
    {
        if (type == AddonEvent.PreFinalize)
        {
            CloseWindow();
            return;
        }

        cabinetOpen = true;
        EnsureCabinetRequested();
        window?.Open();
        if (config.CheckOnCabinetOpen)
        {
            hasChecked = true;
        }

        RefreshWindow();
    }

    private void OnUpdate(IFramework _)
    {
        ProcessStoreQueue();
        if (retrieveRequested && !autoStoreService.IsBusy)
        {
            retrieveRequested = false;
            RefreshWindow();
        }

        if (!cabinetOpen || window is null)
        {
            return;
        }

        if (!SyncWindowPosition())
        {
            CloseWindow();
            return;
        }

        if (!hasChecked)
        {
            if (lastSnapshotRevision < 0)
            {
                RefreshWindow();
            }

            return;
        }

        var cabinetLoaded = IsCabinetLoaded();
        if (inventoryService.GetSnapshotRevision() != lastSnapshotRevision ||
            cabinetLoaded != lastCabinetLoaded)
        {
            lastCabinetLoaded = cabinetLoaded;
            RefreshWindow();
        }
    }

    private void OpenForCurrentCabinet()
    {
        if (AddonHelper.TryGetByName(CabinetAddonName, out AtkUnitBase* addon) && addon->IsVisible)
        {
            cabinetOpen = true;
            window?.Open();
            if (config.CheckOnCabinetOpen)
            {
                hasChecked = true;
            }

            EnsureCabinetRequested();
            RefreshWindow();
        }
    }

    private void StartStoreDirectItems()
    {
        if (!IsEnabled || isStoring || IsRetrieving || autoStoreService.IsBusy || !TryScan())
        {
            return;
        }

        storeRows.Clear();
        storeQueue.Clear();
        HashSet<uint>? dyedRows = config.ExcludeDyedItems ? GetDyedDirectRows() : null;
        for (var index = 0; index < scanItems.Count; index++)
        {
            var item = scanItems[index];
            if (item.CanDirectStore &&
                (dyedRows is null || !dyedRows.Contains(item.CabinetRowId)) &&
                storeRows.Add(item.CabinetRowId))
            {
                storeQueue.Enqueue(item.CabinetRowId);
            }
        }

        if (storeQueue.Count == 0)
        {
            return;
        }

        isStoring = true;
        nextStoreTick = 0;
        RefreshWindow();
    }

    private void StartRetrieveItems()
    {
        if (!IsEnabled ||
            isStoring ||
            IsRetrieving ||
            !autoStoreService.IsAvailable ||
            !TryScan())
        {
            return;
        }

        var itemIds = new List<uint>();
        var seen = new HashSet<uint>();
        for (var index = 0; index < scanItems.Count; index++)
        {
            var item = scanItems[index];
            if (item.CanRetrieve && seen.Add(item.ItemId))
            {
                itemIds.Add(item.ItemId);
            }
        }

        if (itemIds.Count == 0 || !autoStoreService.RequestRetrieve(itemIds))
        {
            return;
        }

        retrieveRequested = true;
        CloseCabinetInterface();
        RefreshWindow();
    }

    private void ProcessStoreQueue()
    {
        if (!isStoring || Environment.TickCount64 < nextStoreTick)
        {
            return;
        }

        if (!storeQueue.TryDequeue(out var rowId))
        {
            isStoring = false;
            RefreshWindow();
            return;
        }

        if (!IsCabinetLoaded())
        {
            storeQueue.Clear();
            isStoring = false;
            RefreshWindow();
            return;
        }

        try
        {
            CabinetCommand.Store(rowId);
            ExecuteCommandManager.Instance().ExecuteCommand(ExecuteCommandFlag.RefreshInventory);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Armoire record failed to store cabinet row {CabinetRowId}.", rowId);
        }

        nextStoreTick = Environment.TickCount64 + StoreIntervalMs;
    }

    private void RefreshWindow()
    {
        if (window?.IsOpen != true)
        {
            return;
        }

        var scope = GetScopeText();
        if (!hasChecked)
        {
            lastSnapshotRevision = inventoryService.GetSnapshotRevision();
            window.UpdateData(
                OmniLoc.Get("Feature.ArmoireRecord.CheckPrompt"),
                scope,
                [],
                isStoring,
                IsRetrieving,
                autoStoreService.IsAvailable,
                RequestCheck,
                StartRetrieveItems,
                StartStoreDirectItems);
            return;
        }

        lastSnapshotRevision = inventoryService.GetSnapshotRevision();
        lastCabinetLoaded = IsCabinetLoaded();
        if (!TryScan())
        {
            window.UpdateData(
                OmniLoc.Get("Feature.ArmoireRecord.DataUnavailable"),
                scope,
                [],
                isStoring,
                IsRetrieving,
                autoStoreService.IsAvailable,
                RequestCheck,
                StartRetrieveItems,
                StartStoreDirectItems);
            return;
        }

        window.UpdateData(
            string.Format(
                OmniLoc.Get("Feature.ArmoireRecord.Summary"),
                scanItems.Count,
                DirectStoreCount),
            scope,
            scanItems,
            isStoring,
            IsRetrieving,
            autoStoreService.IsAvailable,
            RequestCheck,
            StartRetrieveItems,
            StartStoreDirectItems);
    }

    private bool TryScan()
    {
        scanItems.Clear();
        aggregates.Clear();
        var uiState = UIState.Instance();
        if (uiState is null || !uiState->Cabinet.IsCabinetLoaded())
        {
            EnsureCabinetRequested();
            return false;
        }

        var snapshot = inventoryService.GetItemsSnapshot();
        for (var index = 0; index < snapshot.Count; index++)
        {
            var item = snapshot[index];
            if (!MatchesScope(item.Location) ||
                item.Location == ItemInventoryLocation.Armoire ||
                config.ExcludeDyedItems && IsDyed(item))
            {
                continue;
            }

            if (item.Location == ItemInventoryLocation.GlamourDresser &&
                LuminaGetter.TryGetRow<MirageStoreSetItem>(item.ItemId, out var set) &&
                AddSetItems(set, item, uiState))
            {
                continue;
            }

            AddCandidate(item.ItemId, item, uiState);
        }

        foreach (var (key, aggregate) in aggregates)
        {
            if (!LuminaGetter.TryGetRow<Item>(key.ItemId, out var item))
            {
                continue;
            }

            var name = item.Name.ExtractText();
            scanItems.Add(new(
                key.ItemId,
                aggregate.CabinetRowId,
                string.IsNullOrWhiteSpace(name) ? $"Item {key.ItemId}" : name,
                item.Icon,
                aggregate.Quantity,
                string.Join(OmniLoc.Get("Common.ListSeparator"), aggregate.Locations),
                key.Stain0,
                key.Stain1,
                aggregate.CanDirectStore,
                aggregate.CanRetrieve));
        }

        scanItems.Sort(ArmoireRecordItem.Compare);
        return true;
    }

    private bool AddSetItems(MirageStoreSetItem set, InventorySnapshotItem item, UIState* uiState)
    {
        var added = false;
        added |= AddCandidate(set.MainHand.RowId, item, uiState);
        added |= AddCandidate(set.OffHand.RowId, item, uiState);
        added |= AddCandidate(set.Head.RowId, item, uiState);
        added |= AddCandidate(set.Body.RowId, item, uiState);
        added |= AddCandidate(set.Hands.RowId, item, uiState);
        added |= AddCandidate(set.Legs.RowId, item, uiState);
        added |= AddCandidate(set.Feet.RowId, item, uiState);
        added |= AddCandidate(set.Earrings.RowId, item, uiState);
        added |= AddCandidate(set.Necklace.RowId, item, uiState);
        added |= AddCandidate(set.Bracelets.RowId, item, uiState);
        added |= AddCandidate(set.Ring.RowId, item, uiState);
        return added;
    }

    private bool AddCandidate(uint itemId, InventorySnapshotItem item, UIState* uiState)
    {
        if (itemId == 0 ||
            !inventoryService.ArmoireRows.TryGetValue(itemId, out var cabinetRowId) ||
            uiState->Cabinet.IsItemInCabinet(cabinetRowId))
        {
            return false;
        }

        var key = new ArmoireRecordScanKey(itemId, item.Stain0, item.Stain1);
        if (!aggregates.TryGetValue(key, out var aggregate))
        {
            aggregate = new(cabinetRowId);
            aggregates.Add(key, aggregate);
        }

        var directStore = IsDirectStoreLocation(item.Location);
        aggregate.Quantity += item.Quantity;
        aggregate.CanDirectStore |= directStore;
        aggregate.CanRetrieve |= !directStore;
        aggregate.Locations.Add(FormatLocation(item));
        return true;
    }

    private HashSet<uint> GetDyedDirectRows()
    {
        var rows = new HashSet<uint>();
        var snapshot = inventoryService.GetItemsSnapshot();
        for (var index = 0; index < snapshot.Count; index++)
        {
            var item = snapshot[index];
            if (MatchesScope(item.Location) &&
                IsDirectStoreLocation(item.Location) &&
                IsDyed(item) &&
                inventoryService.ArmoireRows.TryGetValue(item.ItemId, out var rowId))
            {
                rows.Add(rowId);
            }
        }

        return rows;
    }

    private bool MatchesScope(ItemInventoryLocation location) =>
        config.ScanBackpack && location == ItemInventoryLocation.Inventory ||
        config.ScanArmory && location == ItemInventoryLocation.Armory ||
        config.ScanSaddlebag && location == ItemInventoryLocation.Saddlebag ||
        config.ScanRetainers && location == ItemInventoryLocation.Retainer ||
        config.ScanFreeCompanyChest && location == ItemInventoryLocation.FreeCompanyChest ||
        config.ScanGlamourDresser && location == ItemInventoryLocation.GlamourDresser;

    private string GetScopeText()
    {
        var labels = new List<string>(6);
        AddScopeLabel(labels, config.ScanBackpack, "Feature.ArmoireRecord.Scope.Backpack");
        AddScopeLabel(labels, config.ScanArmory, "Feature.ArmoireRecord.Scope.Armory");
        AddScopeLabel(labels, config.ScanSaddlebag, "Feature.ArmoireRecord.Scope.Saddlebag");
        AddScopeLabel(labels, config.ScanRetainers, "Feature.ArmoireRecord.Scope.Retainers");
        AddScopeLabel(labels, config.ScanFreeCompanyChest, "Feature.ArmoireRecord.Scope.FreeCompanyChest");
        AddScopeLabel(labels, config.ScanGlamourDresser, "Feature.ArmoireRecord.Scope.GlamourDresser");
        return labels.Count == 0
            ? OmniLoc.Get("Feature.ArmoireRecord.Scope.None")
            : string.Format(
                OmniLoc.Get("Feature.ArmoireRecord.Scope.Value"),
                string.Join(OmniLoc.Get("Common.ListSeparator"), labels));
    }

    private static void AddScopeLabel(List<string> labels, bool enabled, string key)
    {
        if (enabled)
        {
            labels.Add(OmniLoc.Get(key));
        }
    }

    private static string FormatLocation(InventorySnapshotItem item) => item.Location switch
    {
        ItemInventoryLocation.Inventory => OmniLoc.Get("Feature.ArmoireRecord.Scope.Backpack"),
        ItemInventoryLocation.Armory => OmniLoc.Get("Feature.ArmoireRecord.Scope.Armory"),
        ItemInventoryLocation.Saddlebag => OmniLoc.Get("Feature.ArmoireRecord.Scope.Saddlebag"),
        ItemInventoryLocation.Retainer when !string.IsNullOrWhiteSpace(item.HolderName) => item.HolderName,
        ItemInventoryLocation.Retainer => OmniLoc.Get("Feature.ArmoireRecord.Scope.Retainers"),
        ItemInventoryLocation.FreeCompanyChest => OmniLoc.Get("Feature.ArmoireRecord.Scope.FreeCompanyChest"),
        ItemInventoryLocation.GlamourDresser => OmniLoc.Get("Feature.ArmoireRecord.Scope.GlamourDresser"),
        _ => string.Empty
    };

    private bool SyncWindowPosition()
    {
        if (window is null ||
            !AddonHelper.TryGetByName(CabinetAddonName, out AtkUnitBase* addon) ||
            !addon->IsVisible)
        {
            return false;
        }

        window.SetWindowPosition(new(addon->X + addon->GetScaledWidth(true) - 12f, addon->Y));
        return true;
    }

    private static void CloseCabinetInterface()
    {
        AddonWindowControl.Close("CabinetWithdraw");
        AddonWindowControl.Close(CabinetAddonName);
    }

    private static bool IsDirectStoreLocation(ItemInventoryLocation location) =>
        location is ItemInventoryLocation.Inventory or ItemInventoryLocation.Armory;

    private static bool IsDyed(InventorySnapshotItem item) => item.Stain0 != 0 || item.Stain1 != 0;

    private static bool IsCabinetLoaded()
    {
        var uiState = UIState.Instance();
        return uiState is not null && uiState->Cabinet.IsCabinetLoaded();
    }

    private static void EnsureCabinetRequested()
    {
        if (IsCabinetLoaded())
        {
            return;
        }

        try
        {
            CabinetCommand.Request();
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Armoire record failed to request cabinet data.");
        }
    }

    private void CloseWindow()
    {
        cabinetOpen = false;
        hasChecked = false;
        lastCabinetLoaded = false;
        lastSnapshotRevision = -1;
        window?.Close();
    }

    private void ResetState()
    {
        cabinetOpen = false;
        hasChecked = false;
        isStoring = false;
        retrieveRequested = false;
        lastCabinetLoaded = false;
        lastSnapshotRevision = -1;
        nextStoreTick = 0;
        storeQueue.Clear();
        storeRows.Clear();
        aggregates.Clear();
        scanItems.Clear();
    }

    private sealed class ScanAggregate(uint cabinetRowId)
    {
        public uint CabinetRowId { get; } = cabinetRowId;

        public int Quantity { get; set; }

        public bool CanDirectStore { get; set; }

        public bool CanRetrieve { get; set; }

        public HashSet<string> Locations { get; } = new(StringComparer.Ordinal);
    }
}
