using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;
using OmniToolbox.Notifications;
using OmniToolbox.TreeHouse.InterfaceOperation;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class ArmoireRecord : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ArmoireRecordTitle"),
        Description = OmniLoc.Get("ArmoireRecordDescription"),
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Item/ArmoireRecord-1.png",
        RequiresPrivateProvider = true,
        Commands =
        [
            new ModuleCommand("Feature.ArmoireRecord.CommandDescription", "/omni 收藏柜记录")
        ]
    };

    private const string CabinetAddonName = "Cabinet";

    private readonly ArmoireRecordConfig config;
    private readonly PlayerInventoryService inventoryService;
    private readonly IAutoStoreService autoStoreService;
    private readonly Queue<uint> storeQueue = [];
    private readonly HashSet<uint> storeRows = [];
    private readonly Dictionary<ArmoireRecordScanKey, ScanAggregate> aggregates = [];
    private readonly List<ArmoireRecordItem> scanItems = [];
    private FeatureLifetime? runtimeLifetime;
    private ArmoireRecordNativeUI? window;
    private bool cabinetOpen;
    private bool hasChecked;
    private bool isStoring;
    private bool retrieveRequested;
    private bool lastCabinetLoaded;
    private long lastSnapshotRevision = -1;
    private long nextStoreTick;
    private Vector2? lastWindowPosition;

    public ArmoireRecord(
        ArmoireRecordConfig config,
        PlayerInventoryService inventoryService,
        IAutoStoreService autoStoreService)
    {
        this.config = config;
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

    public override bool HasSettings => true;

    public override bool DrawSettings() => ArmoireRecordPanel.Draw(config);

    public bool RequestCheck()
    {
        if (!IsEnabled)
        {
            OmniNotifier.Chat(OmniLoc.Get("Feature.ArmoireRecord.Disabled"));
            return false;
        }

        hasChecked = true;
        NotifyScanResults();
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

            if (!FrameworkManager.Instance().Reg(OnUpdate, 16))
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

    protected override bool OnInterruptAutomation()
    {
        if (!isStoring && !retrieveRequested)
        {
            return false;
        }

        isStoring = false;
        retrieveRequested = false;
        nextStoreTick = 0;
        storeQueue.Clear();
        storeRows.Clear();
        RefreshWindow();
        return true;
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
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Armoire record failed to store cabinet row {CabinetRowId}.", rowID);
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
                OmniLoc.Get("Feature.ArmoireRecord.CheckPrompt"),
                [],
                isStoring,
                IsRetrieving,
                autoStoreService.IsAvailable,
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
                OmniLoc.Get("Feature.ArmoireRecord.Summary"),
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
            OmniNotifier.Chat(OmniLoc.Get("Feature.ArmoireRecord.DataUnavailable"));
            return;
        }

        if (scanItems.Count == 0)
        {
            OmniNotifier.Chat(OmniLoc.Get("Feature.ArmoireRecord.Empty"));
            return;
        }

        var builder = new SeStringBuilder()
                .AddUiForeground(1)
            .Append(OmniLoc.Get("Feature.ArmoireRecord.ChatPrefix"))
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
                LuminaGetter.TryGetRow<MirageStoreSetItem>(item.ItemID, out var set) &&
                AddSetItems(set, item, uiState))
            {
                continue;
            }

            AddCandidate(item.ItemID, item, uiState);
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

    private bool AddCandidate(uint itemID, InventorySnapshotItem item, UIState* uiState)
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
        config.ScanBackpack && location == ItemInventoryLocation.Inventory ||
        config.ScanArmory && location == ItemInventoryLocation.Armory ||
        config.ScanSaddlebag && location == ItemInventoryLocation.Saddlebag ||
        config.ScanRetainers && location == ItemInventoryLocation.Retainer ||
        config.ScanFreeCompanyChest && location == ItemInventoryLocation.FreeCompanyChest ||
        config.ScanGlamourDresser && location == ItemInventoryLocation.GlamourDresser;

    private static string FormatDyeText(byte stain0, byte stain1)
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

        AtkUnitBase* recordAddon = window;
        if (recordAddon != null && MathF.Abs(recordAddon->Scale - addon->Scale) > 0.001f)
        {
            recordAddon->SetScale(addon->Scale / AtkUnitBase.GetGlobalUIScale(), true);
        }

        var position = new Vector2(addon->X + addon->GetScaledWidth(true) + 1f, addon->Y);
        if (lastWindowPosition != position)
        {
            window.SetWindowPosition(position);
            lastWindowPosition = position;
        }

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
        lastWindowPosition = null;
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
        lastWindowPosition = null;
        storeQueue.Clear();
        storeRows.Clear();
        aggregates.Clear();
        scanItems.Clear();
    }

    private sealed class ScanAggregate(uint cabinetRowID)
    {
        public uint CabinetRowID { get; } = cabinetRowID;

        public int Quantity { get; set; }

        public bool CanDirectStore { get; set; }

        public bool CanRetrieve { get; set; }

        public HashSet<string> Locations { get; } = new(StringComparer.Ordinal);
    }
}

[Serializable]
public sealed class ArmoireRecordConfig
{
    public bool CheckOnCabinetOpen { get; set; } = true;
    public bool ExcludeDyedItems { get; set; } = true;
    public bool ScanBackpack { get; set; } = true;
    public bool ScanArmory { get; set; } = true;
    public bool ScanSaddlebag { get; set; } = true;
    public bool ScanRetainers { get; set; } = true;
    public bool ScanFreeCompanyChest { get; set; } = true;
    public bool ScanGlamourDresser { get; set; } = true;
}

internal sealed record ArmoireRecordItem(
    uint ItemID,
    uint CabinetRowID,
    string Name,
    uint IconID,
    int Quantity,
    string LocationsText,
    byte Stain0,
    byte Stain1,
    bool CanDirectStore,
    bool CanRetrieve)
{
    public bool IsDyed => Stain0 != 0 || Stain1 != 0;

    public static int Compare(ArmoireRecordItem left, ArmoireRecordItem right)
    {
        var directCompare = right.CanDirectStore.CompareTo(left.CanDirectStore);
        if (directCompare != 0)
        {
            return directCompare;
        }

        var itemIDCompare = right.ItemID.CompareTo(left.ItemID);
        return itemIDCompare != 0
            ? itemIDCompare
            : string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
    }
}

internal readonly record struct ArmoireRecordScanKey(uint ItemID, byte Stain0, byte Stain1);

internal static class ArmoireRecordPanel
{
    public static bool Draw(ArmoireRecordConfig config)
    {
        var changed = DrawTopSettings(config);

        ImGui.Spacing();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ArmoireRecord.Scope.Title"));
        changed |= DrawScopes(config);

        return changed;
    }

    private static bool DrawTopSettings(ArmoireRecordConfig config)
    {
        var style = ImGui.GetStyle();
        using var spacing = ImRaii.PushStyle(
                ImGuiStyleVar.CellPadding,
                new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y))
            .Push(
                ImGuiStyleVar.ItemSpacing,
                new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
        using var table = ImRaii.Table(
            "##armoireRecordTopSettings",
            4,
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        SetupColumns();
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var changed = DrawCheckbox(
            "Feature.ArmoireRecord.CheckOnOpen",
            "checkOnOpen",
            config.CheckOnCabinetOpen,
            out var checkOnOpen);
        if (changed)
        {
            config.CheckOnCabinetOpen = checkOnOpen;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.ArmoireRecord.CheckOnOpen.Help"));
        ImGui.TableNextColumn();
        if (DrawCheckbox(
            "Feature.ArmoireRecord.ExcludeDyed",
            "excludeDyed",
            config.ExcludeDyedItems,
            out var excludeDyed))
        {
            config.ExcludeDyedItems = excludeDyed;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.ArmoireRecord.ExcludeDyed.Help"));
        return changed;
    }

    private static bool DrawScopes(ArmoireRecordConfig config)
    {
        var style = ImGui.GetStyle();
        using var spacing = ImRaii.PushStyle(
                ImGuiStyleVar.CellPadding,
                new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y))
            .Push(
                ImGuiStyleVar.ItemSpacing,
                new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
        using var table = ImRaii.Table(
            "##armoireRecordScopes",
            4,
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        SetupColumns();
        var changed = false;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.ArmoireRecord.Scope.Backpack", "backpack", config.ScanBackpack, out var backpack))
        {
            config.ScanBackpack = backpack;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.ArmoireRecord.Scope.Armory", "armory", config.ScanArmory, out var armory))
        {
            config.ScanArmory = armory;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.ArmoireRecord.Scope.Saddlebag", "saddlebag", config.ScanSaddlebag, out var saddlebag))
        {
            config.ScanSaddlebag = saddlebag;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.ArmoireRecord.Scope.Retainers", "retainers", config.ScanRetainers, out var retainers))
        {
            config.ScanRetainers = retainers;
            changed = true;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (DrawCheckbox(
            "Feature.ArmoireRecord.Scope.FreeCompanyChest",
            "freeCompanyChest",
            config.ScanFreeCompanyChest,
            out var freeCompanyChest))
        {
            config.ScanFreeCompanyChest = freeCompanyChest;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox(
            "Feature.ArmoireRecord.Scope.GlamourDresser",
            "glamourDresser",
            config.ScanGlamourDresser,
            out var glamourDresser))
        {
            config.ScanGlamourDresser = glamourDresser;
            changed = true;
        }

        return changed;
    }

    private static void SetupColumns()
    {
        ImGui.TableSetupColumn("##armoireRecordColumn0", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##armoireRecordColumn1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##armoireRecordColumn2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##armoireRecordColumn3", ImGuiTableColumnFlags.WidthStretch, 1.25f);
    }

    private static bool DrawCheckbox(string labelKey, string id, bool current, out bool value)
    {
        value = current;
        return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##armoireRecord{id}", ref value);
    }
}
