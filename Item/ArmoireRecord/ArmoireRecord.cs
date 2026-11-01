using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Notifications;
using OmniToolbox.TreeHouse.InterfaceOperation;
using OmniToolbox.UI.Theme;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class ArmoireRecord : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Item/ArmoireRecord-1.png",
        RequiresPrivateProvider = true,
        Commands =
        [
            new ModuleCommand("Feature.ArmoireRecord.CommandDescription", OmniCommandCatalog.Format("armoirecheck"))
        ]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "ArmoireRecordTitle",
            ["Description"] = "ArmoireRecordDescription",
            ["Common.ListSeparator"] = "Common.ListSeparator",
            ["Feature.ArmoireRecord.ChatPrefix"] = "Feature.ArmoireRecord.ChatPrefix",
            ["Feature.ArmoireRecord.CheckOnOpen"] = "Feature.ArmoireRecord.CheckOnOpen",
            ["Feature.ArmoireRecord.CheckOnOpen.Help"] = "Feature.ArmoireRecord.CheckOnOpen.Help",
            ["Feature.ArmoireRecord.CheckPrompt"] = "Feature.ArmoireRecord.CheckPrompt",
            ["Feature.ArmoireRecord.CommandDescription"] = "Feature.ArmoireRecord.CommandDescription",
            ["Feature.ArmoireRecord.DataUnavailable"] = "Feature.ArmoireRecord.DataUnavailable",
            ["Feature.ArmoireRecord.Empty"] = "Feature.ArmoireRecord.Empty",
            ["Feature.ArmoireRecord.NoDye"] = "Feature.ArmoireRecord.NoDye",
            ["Feature.ArmoireRecord.Retrieve"] = "Feature.ArmoireRecord.Retrieve",
            ["Feature.ArmoireRecord.Retrieving"] = "Feature.ArmoireRecord.Retrieving",
            ["Feature.ArmoireRecord.Scope.Armory"] = "Feature.ArmoireRecord.Scope.Armory",
            ["Feature.ArmoireRecord.Scope.Backpack"] = "Feature.ArmoireRecord.Scope.Backpack",
            ["Feature.ArmoireRecord.Scope.Dyed"] = "Feature.ArmoireRecord.Scope.Dyed",
            ["Feature.ArmoireRecord.Scope.Exclude"] = "Feature.ArmoireRecord.Scope.Exclude",
            ["Feature.ArmoireRecord.Scope.Exclude.Help"] = "Feature.ArmoireRecord.Scope.Exclude.Help",
            ["Feature.ArmoireRecord.Scope.FreeCompanyChest"] = "Feature.ArmoireRecord.Scope.FreeCompanyChest",
            ["Feature.ArmoireRecord.Scope.Gearsets"] = "Feature.ArmoireRecord.Scope.Gearsets",
            ["Feature.ArmoireRecord.Scope.Gearsets.Help"] = "Feature.ArmoireRecord.Scope.Gearsets.Help",
            ["Feature.ArmoireRecord.Scope.GlamourDresser"] = "Feature.ArmoireRecord.Scope.GlamourDresser",
            ["Feature.ArmoireRecord.Scope.Retainers"] = "Feature.ArmoireRecord.Scope.Retainers",
            ["Feature.ArmoireRecord.Scope.Saddlebag"] = "Feature.ArmoireRecord.Scope.Saddlebag",
            ["Feature.ArmoireRecord.Scope.Title"] = "Feature.ArmoireRecord.Scope.Title",
            ["Feature.ArmoireRecord.Scope.Title.Help"] = "Feature.ArmoireRecord.Scope.Title.Help",
            ["Feature.ArmoireRecord.Status.DirectStore"] = "Feature.ArmoireRecord.Status.DirectStore",
            ["Feature.ArmoireRecord.Status.MoveToBackpack"] = "Feature.ArmoireRecord.Status.MoveToBackpack",
            ["Feature.ArmoireRecord.Store"] = "Feature.ArmoireRecord.Store",
            ["Feature.ArmoireRecord.Storing"] = "Feature.ArmoireRecord.Storing",
            ["Feature.ArmoireRecord.Summary"] = "Feature.ArmoireRecord.Summary",
            ["Feature.ArmoireRecord.Title"] = "Feature.ArmoireRecord.Title",
            ["Feature.ArmoireRecord.UnknownDye"] = "Feature.ArmoireRecord.UnknownDye",
            ["Feature.MultiToolbar.GearsetNumber"] = "Feature.MultiToolbar.GearsetNumber"
        });

    public override string ModuleName => nameof(ArmoireRecord);

    private readonly ArmoireRecordConfig                             config;
    private readonly IInventorySnapshotReader                        inventoryService;
    private readonly IAutoStoreService                               autoStoreService;
    private readonly Queue<uint>                                     storeQueue           = [];
    private readonly HashSet<uint>                                   storeRows            = [];
    private readonly Dictionary<ArmoireRecordScanKey, ScanAggregate> aggregates           = [];
    private readonly List<ArmoireRecordItem>                         scanItems            = [];
    private ArmoireRecordNativeUI?                                   window;
    private bool                                                     cabinetOpen;
    private bool                                                     hasChecked;
    private bool                                                     isStoring;
    private bool                                                     retrieveRequested;
    private bool                                                     lastCabinetLoaded;
    private long                                                     lastSnapshotRevision = -1;
    private long                                                     nextStoreTick;

    public ArmoireRecord
    (
        ArmoireRecordConfig      config,
        IInventorySnapshotReader inventoryService,
        IAutoStoreService        autoStoreService
    )
    {
        this.config           = config;
        this.inventoryService = inventoryService;
        this.autoStoreService = autoStoreService;
    }

    private bool IsRetrieving => retrieveRequested && autoStoreService.IsBusy;

    private int DirectStoreCount
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

    protected override void OnStart(FeatureLifetime activation)
    {
        var lifetime = Activation;
        lifetime.Add(() =>
        {
            window = null;
            ResetState();
        });
        window = new(this);
        lifetime.Add(window.Dispose);

        if (!FrameworkManager.Instance().Reg(OnUpdate, 16))
        {
            throw new InvalidOperationException("Armoire record update registration failed.");
        }

        lifetime.OnStopping(() => FrameworkManager.Instance().Unreg(OnUpdate));
        var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        lifetime.OnStopping(addonEvents.Dispose);
        addonEvents.Register(AddonEvent.PostSetup, CABINET_ADDON_NAME, OnCabinetAddon);
        addonEvents.Register(AddonEvent.PostRequestedUpdate, CABINET_ADDON_NAME, OnCabinetAddon);
        addonEvents.Register(AddonEvent.PostRefresh, CABINET_ADDON_NAME, OnCabinetAddon);
        addonEvents.Register(AddonEvent.PreFinalize, CABINET_ADDON_NAME, OnCabinetAddon);
        OpenForCurrentCabinet();
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => ArmoireRecordPanel.Draw(config);

    public bool RequestCheck()
    {
        if (!IsEnabled || Activation.IsStopping)
        {
            return false;
        }

        hasChecked = true;
        NotifyScanResults();
        RefreshWindow();
        return true;
    }

    protected override bool OnInterruptAutomation()
    {
        if (!isStoring && !retrieveRequested)
        {
            return false;
        }

        isStoring         = false;
        retrieveRequested = false;
        nextStoreTick     = 0;
        storeQueue.Clear();
        storeRows.Clear();
        RefreshWindow();
        return true;
    }

    private void OnCabinetAddon
    (
        AddonEvent type,
        AddonArgs  _
    )
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
        if (AddonHelper.TryGetByName(CABINET_ADDON_NAME, out var addon) && addon->IsVisible)
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
        if (!IsEnabled || Activation.IsStopping || isStoring || IsRetrieving || autoStoreService.IsBusy || !TryScan())
        {
            return;
        }

        storeRows.Clear();
        storeQueue.Clear();
        var dyedRows = config.ExcludeDyedItems || !config.ScanDyedItems ? GetDyedDirectRows() : null;
        for (var index = 0; index < scanItems.Count; index++)
        {
            var item = scanItems[index];
            if (item.CanDirectStore &&
                (dyedRows is null || !dyedRows.Contains(item.CabinetRowID)) &&
                storeRows.Add(item.CabinetRowID))
            {
                storeQueue.Enqueue(item.CabinetRowID);
            }
        }

        if (storeQueue.Count == 0)
        {
            return;
        }

        isStoring     = true;
        nextStoreTick = 0;
        RefreshWindow();
    }

    private void StartRetrieveItems()
    {
        if (!IsEnabled || Activation.IsStopping ||
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
            if (item.CanRetrieve && seen.Add(item.ItemID))
            {
                itemIds.Add(item.ItemID);
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

        if (!storeQueue.TryDequeue(out var rowID))
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
            CabinetCommand.Store(rowID);
            ExecuteCommandManager.Instance().ExecuteCommand(ExecuteCommandFlag.RefreshInventory);
        }
        catch (Exception)
        {
        }

        nextStoreTick = Environment.TickCount64 + 100;
    }

    private void RefreshWindow()
    {
        if (window?.IsOpen != true)
        {
            return;
        }

        if (!hasChecked)
        {
            lastSnapshotRevision = inventoryService.GetSnapshotRevision();
            window.UpdateData(
                OmniLoc.Get(this, "Feature.ArmoireRecord.CheckPrompt"),
                [],
                isStoring,
                IsRetrieving,
                autoStoreService.IsAvailable,
                StartRetrieveItems,
                StartStoreDirectItems);
            return;
        }

        lastSnapshotRevision = inventoryService.GetSnapshotRevision();
        lastCabinetLoaded    = IsCabinetLoaded();
        if (!TryScan())
        {
            window.UpdateData(
                OmniLoc.Get(this, "Feature.ArmoireRecord.DataUnavailable"),
                [],
                isStoring,
                IsRetrieving,
                autoStoreService.IsAvailable,
                StartRetrieveItems,
                StartStoreDirectItems);
            return;
        }

        window.UpdateData(
            string.Format(
                OmniLoc.Get(this, "Feature.ArmoireRecord.Summary"),
                scanItems.Count,
                DirectStoreCount),
            scanItems,
            isStoring,
            IsRetrieving,
            autoStoreService.IsAvailable,
            StartRetrieveItems,
            StartStoreDirectItems);
    }

    private void NotifyScanResults()
    {
        if (!TryScan())
        {
            return;
        }

        if (scanItems.Count == 0)
        {
            OmniNotifier.Chat(OmniLoc.Get(this, "Feature.ArmoireRecord.Empty"));
            return;
        }

        var builder = new SeStringBuilder()
                .AddUiForeground(1)
            .Append(OmniLoc.Get(this, "Feature.ArmoireRecord.ChatPrefix"))
            .AddUiForegroundOff();
        for (var index = 0; index < scanItems.Count; index++)
        {
            var item = scanItems[index];
            builder
                .Append("\n")
                .AddItemLink(item.ItemID, false)
                .AddUiForeground(item.IsDyed ? (ushort)43 : (ushort)1)
                .Append(" ")
                .Append(FormatDyeText(item.Stain0, item.Stain1))
                .AddUiForegroundOff();
        }

        OmniNotifier.Chat(builder.Build());
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
        var gearsetItems = config.ScanGearsets || config.ExcludeGearsetItems
            ? GetGearsetItems()
            : [];
        HashSet<uint>? gearsetItemIDs = null;
        if (config.ExcludeGearsetItems)
        {
            gearsetItemIDs = [];
            for (var index = 0; index < gearsetItems.Count; index++)
            {
                gearsetItemIDs.Add(gearsetItems[index].ItemID);
            }
        }
        for (var index = 0; index < snapshot.Count; index++)
        {
            var item = snapshot[index];
            if (!MatchesScope(item.Location) ||
                item.Location == ItemInventoryLocation.Armoire ||
                gearsetItemIDs?.Contains(item.ItemID) == true ||
                (config.ExcludeDyedItems || !config.ScanDyedItems) && IsDyed(item))
            {
                continue;
            }

            if (item.Location == ItemInventoryLocation.GlamourDresser &&
                LuminaGetter.TryGetRow<MirageStoreSetItem>(item.ItemID, out var set) &&
                AddSetItems(set, item, uiState))
            {
                continue;
            }

            AddCandidate(item.ItemID, item, uiState);
        }

        if (config.ScanGearsets && !config.ExcludeGearsetItems)
        {
            for (var index = 0; index < gearsetItems.Count; index++)
            {
                AddCandidate(gearsetItems[index].ItemID, gearsetItems[index], uiState);
            }
        }

        foreach (var (key, aggregate) in aggregates)
        {
            if (!LuminaGetter.TryGetRow<Item>(key.ItemID, out var item))
            {
                continue;
            }

            var name = item.Name.ExtractText();
            scanItems.Add(new(
                key.ItemID,
                aggregate.CabinetRowID,
                string.IsNullOrWhiteSpace(name) ? $"Item {key.ItemID}" : name,
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

    private bool AddSetItems
    (
        MirageStoreSetItem    set,
        InventorySnapshotItem item,
        UIState*              uiState
    )
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

    private bool AddCandidate
    (
        uint                  itemID,
        InventorySnapshotItem item,
        UIState*              uiState
    )
    {
        if (itemID == 0 ||
            !inventoryService.ArmoireRows.TryGetValue(itemID, out var cabinetRowID) ||
            uiState->Cabinet.IsItemInCabinet(cabinetRowID))
        {
            return false;
        }

        var key = new ArmoireRecordScanKey(itemID, item.Stain0, item.Stain1);
        if (!aggregates.TryGetValue(key, out var aggregate))
        {
            aggregate = new(cabinetRowID);
            aggregates.Add(key, aggregate);
        }

        var directStore = IsDirectStoreLocation(item.Location);
        aggregate.Quantity += item.Quantity;
        aggregate.CanDirectStore |= directStore;
        aggregate.CanRetrieve |= !directStore;
        aggregate.Locations.Add(FormatLocation(item));
        return true;
    }

    private static unsafe List<InventorySnapshotItem> GetGearsetItems()
    {
        var items = new List<InventorySnapshotItem>();
        var module = RaptureGearsetModule.Instance();
        if (module is null)
        {
            return items;
        }

        for (var gearsetIndex = 0; gearsetIndex < 100; gearsetIndex++)
        {
            var gearset = module->GetGearset(gearsetIndex);
            if (gearset is null || !gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
            {
                continue;
            }

            var gearsetName = string.IsNullOrWhiteSpace(gearset->NameString)
                ? string.Format(OmniLoc.Get("Feature.MultiToolbar.GearsetNumber"), gearsetIndex + 1)
                : gearset->NameString;
            foreach (var gearsetItem in gearset->Items)
            {
                if (gearsetItem.ItemId == 0)
                {
                    continue;
                }

                items.Add(new(
                    gearsetItem.ItemId,
                    1,
                    false,
                    0,
                    0,
                    default,
                    0,
                    ItemInventoryLocation.Gearset,
                    HolderName: gearsetName));
            }
        }

        return items;
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
                inventoryService.ArmoireRows.TryGetValue(item.ItemID, out var rowID))
            {
                rows.Add(rowID);
            }
        }

        return rows;
    }

    private bool MatchesScope(ItemInventoryLocation location) =>
        (config.ExcludedLocations & location) == 0 &&
        (config.ScanBackpack && location == ItemInventoryLocation.Inventory ||
        config.ScanArmory && location == ItemInventoryLocation.Armory ||
        config.ScanSaddlebag && location == ItemInventoryLocation.Saddlebag ||
        config.ScanRetainers && location == ItemInventoryLocation.Retainer ||
        config.ScanFreeCompanyChest && location == ItemInventoryLocation.FreeCompanyChest ||
        config.ScanGlamourDresser && location == ItemInventoryLocation.GlamourDresser ||
        config.ScanGearsets && location == ItemInventoryLocation.Gearset);

    private static string FormatDyeText
    (
        byte stain0,
        byte stain1
    )
    {
        if (stain0 == 0 && stain1 == 0)
        {
            return OmniLoc.Get("Feature.ArmoireRecord.NoDye");
        }

        var names = new List<string>(2);
        if (stain0 != 0)
        {
            names.Add(GetStainName(stain0));
        }

        if (stain1 != 0)
        {
            names.Add(GetStainName(stain1));
        }

        return string.Join(' ', names);
    }

    private static string GetStainName(byte stainID)
    {
        if (LuminaGetter.TryGetRow<Stain>(stainID, out var stain))
        {
            var name = stain.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return string.Format(OmniLoc.Get("Feature.ArmoireRecord.UnknownDye"), stainID);
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
        ItemInventoryLocation.Gearset when !string.IsNullOrWhiteSpace(item.HolderName) => item.HolderName,
        ItemInventoryLocation.Gearset => OmniLoc.Get("Feature.ArmoireRecord.Scope.Gearsets"),
        _ => string.Empty
    };

    internal bool SyncWindowPosition()
    {
        if (window is null ||
            !AddonHelper.TryGetByName(CABINET_ADDON_NAME, out var addon) ||
            !addon->IsVisible)
        {
            return false;
        }

        AtkUnitBase* recordAddon = window;
        if (recordAddon == null || recordAddon->WindowNode == null || addon->WindowNode == null)
        {
            return true;
        }

        OmniNativeWindowAttachment.SyncBeside(window, recordAddon, addon, 1f, 0f, preferRight: true);

        return true;
    }

    private static void CloseCabinetInterface()
    {
        AddonWindowControl.Close("CabinetWithdraw");
        AddonWindowControl.Close(CABINET_ADDON_NAME);
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
        catch (Exception)
        {
        }
    }

    private void CloseWindow()
    {
        cabinetOpen          = false;
        hasChecked           = false;
        lastCabinetLoaded    = false;
        lastSnapshotRevision = -1;
        window?.Close();
    }

    private void ResetState()
    {
        cabinetOpen          = false;
        hasChecked           = false;
        isStoring            = false;
        retrieveRequested    = false;
        lastCabinetLoaded    = false;
        lastSnapshotRevision = -1;
        nextStoreTick        = 0;
        storeQueue.Clear();
        storeRows.Clear();
        aggregates.Clear();
        scanItems.Clear();
    }

    private readonly record struct ArmoireRecordScanKey
    (
        uint ItemID,
        byte Stain0,
        byte Stain1
    );

    private sealed class ScanAggregate(uint cabinetRowID)
    {
        public uint CabinetRowID { get; } = cabinetRowID;

        public int Quantity { get; set; }

        public bool CanDirectStore { get; set; }

        public bool CanRetrieve { get; set; }

        public HashSet<string> Locations { get; } = new(StringComparer.Ordinal);
    }

    #region 常量

    private const string CABINET_ADDON_NAME = "Cabinet";

    #endregion
}
