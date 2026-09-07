using System.Globalization;
using System.Text;
using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Notifications;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;

namespace OmniToolbox.TreePublic;

public sealed unsafe class MoreGearSetList : ModuleBase
{
    private const string GearSetListAddonName = "GearSetList";
    private const string BannerEditorAddonName = "BannerEditor";
    private const string MiragePlateAddonName = "MiragePrismMiragePlate";
    private const uint JobIconBase = 62100;
    private const int BorrowWaitFrames = 45;
    private const int SoulCrystalSlot = 13;

    private static float ScreenPadding => OmniTheme.Scale(8f);
    private static float CompanionGap => OmniTheme.Scale(4f);

    private static readonly string[] SlotNames =
    [
        "主手",
        "副手",
        "头部",
        "身体",
        "手部",
        "腰带",
        "腿部",
        "脚部",
        "耳饰",
        "项链",
        "手镯",
        "右戒指",
        "左戒指",
        "灵魂水晶"
    ];

    private static readonly InventoryType[] SearchContainers =
    [
        InventoryType.EquippedItems,
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryWaist,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
        InventoryType.ArmorySoulCrystal,
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4
    ];

    private static readonly uint[] JobOptionIDs =
    [
        19, 21, 32, 37,
        24, 28, 33, 40,
        20, 22, 30, 34, 39, 41,
        23, 31, 38,
        25, 27, 35, 36, 42,
        8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18
    ];

    private static readonly string[] EnglishJobNames =
    [
        string.Empty,
        "Gladiator", "Pugilist", "Marauder", "Lancer", "Archer", "Conjurer", "Thaumaturge",
        "Carpenter", "Blacksmith", "Armorer", "Goldsmith", "Leatherworker", "Weaver", "Alchemist", "Culinarian",
        "Miner", "Botanist", "Fisher",
        "Paladin", "Monk", "Warrior", "Dragoon", "Bard", "White Mage", "Black Mage",
        "Arcanist", "Summoner", "Scholar", "Rogue", "Ninja",
        "Machinist", "Dark Knight", "Astrologian", "Samurai", "Red Mage", "Blue Mage",
        "Gunbreaker", "Dancer", "Reaper", "Sage", "Viper", "Pictomancer"
    ];

    private static List<MoreGearSetListJobChoice>? jobChoices;
    private static List<string[]>? searchAliasGroups;

    private readonly MoreGearSetListConfig config;
    private readonly System.Action saveConfig;
    private TaskHelper? taskHelper;
    private MoreGearSetListNativeUI? nativeUI;
    private string nativeFingerprint = string.Empty;
    private bool windowOpen;
    private bool expectOpen;
    private bool placeWindow;
    private bool followNative;
    private float nativeScale = 1f;
    private Vector2 windowPos;
    private Vector2 windowSize;
    private MoreGearSetListEntry? borrowedEntry;
    private int borrowedGearsetID = -1;
    private BorrowKind borrowKind;
    private int borrowWaitLeft;
    private bool borrowSeenOpen;
    private bool borrowedHasSnapshot;
    private RaptureGearsetModule.GearsetEntry borrowedSnapshot;
    private string gearSnapshotKey = string.Empty;
    private readonly Dictionary<int, (uint ItemID, bool HQ)> equippedSnapshot = [];
    private readonly Dictionary<(uint ItemID, bool HQ), int> availableSnapshot = [];

    public override ModuleInfo Info { get; } = new()
    {
        Title       = "更多的套装列表",
        Description = "提供一个额外的套装列表用来保存你更多的套装，原生列表打开时会自动打开。",
        Category    = ModuleCategory.Item,
        Author      = "咕咕",
        Commands    =
        [
            new ModuleCommand("打开额外的套装列表", "/omni 套装列表")
        ]
    };

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        MoreGearSetListPanel.Draw(this);
        return false;
    }

    public MoreGearSetList() : this(new MoreGearSetListConfig())
    {
    }

    public MoreGearSetList(MoreGearSetListConfig config) : this(config, static () => { })
    {
    }

    public MoreGearSetList(MoreGearSetListConfig config, System.Action saveConfig)
    {
        this.config     = config;
        this.saveConfig = saveConfig;
        windowSize      = new(OmniTheme.Scale(268f), OmniTheme.Scale(480f));
    }

    #region 生命周期

    protected override void OnEnable()
    {
        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS       = 8_000
        };
        nativeUI = new(
            HandleNativeSave,
            HandleNativeUpdate,
            HandleNativeDelete,
            HandleNativeImport,
            TryApply,
            RefreshNative,
            this);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, GearSetListAddonName, OnGearSetListSetup);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, GearSetListAddonName, OnGearSetListFinalize);
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 16))
        {
            throw new InvalidOperationException("MoreGearSetList update registration failed.");
        }

        ShowBesideNativeIfVisible();
    }

    protected override void OnDisable()
    {
        ReleaseBorrowedGearset(true);
        saveConfig();
        FrameworkManager.Instance().Unreg(OnFrameworkUpdate);
        DalamudServices.AddonLifecycle.UnregisterListener(OnGearSetListSetup);
        DalamudServices.AddonLifecycle.UnregisterListener(OnGearSetListFinalize);
        nativeUI?.Close();
        nativeUI?.Dispose();
        nativeUI          = null;
        nativeFingerprint = string.Empty;
        windowOpen        = false;
        expectOpen        = false;
        placeWindow       = false;
        followNative      = false;
        taskHelper?.Abort();
        taskHelper?.Dispose();
        taskHelper = null;
        gearSnapshotKey = string.Empty;
        equippedSnapshot.Clear();
        availableSnapshot.Clear();
    }

    public void ToggleList()
    {
        if (nativeUI is null) return;

        if (windowOpen && nativeUI.IsOpen)
        {
            followNative = false;
            CloseList();
            return;
        }

        followNative = false;
        if (TryGetNativeAddon(out var addon))
        {
            CaptureNativeRect(addon);
            placeWindow = true;
        }

        OpenList();
    }

    private void OnGearSetListSetup(AddonEvent type, AddonArgs args)
    {
        if (nativeUI is null || borrowedGearsetID >= 0)
        {
            return;
        }

        ShowBesideNative((AtkUnitBase*)args.Addon.Address);
    }

    private void OnGearSetListFinalize(AddonEvent type, AddonArgs args)
    {
        if (!followNative)
        {
            return;
        }

        followNative = false;
        CloseList();
    }

    private void ShowBesideNativeIfVisible()
    {
        if (TryGetNativeAddon(out var addon))
            ShowBesideNative(addon);
    }

    private void ShowBesideNative(AtkUnitBase* addon)
    {
        CaptureNativeRect(addon);
        followNative = true;
        placeWindow  = true;
        OpenList();
    }

    private void OpenList()
    {
        if (nativeUI is null)
        {
            return;
        }

        nativeUI.Size = windowSize;
        if (windowOpen && nativeUI.IsOpen)
        {
            return;
        }

        windowOpen = true;
        expectOpen = true;
        nativeUI.Open();
        RefreshNative();
    }

    private void CloseList()
    {
        nativeUI?.Close();
        windowOpen        = false;
        expectOpen        = false;
        placeWindow       = false;
        nativeFingerprint = string.Empty;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        TickBorrowedGearset();
        if (nativeUI is null)
        {
            return;
        }

        if (expectOpen && nativeUI.IsOpen)
            expectOpen = false;

        if (!expectOpen && windowOpen && !nativeUI.IsOpen)
        {
            windowOpen        = false;
            followNative      = false;
            nativeFingerprint = string.Empty;
            return;
        }

        if (!windowOpen)
            return;

        if (placeWindow && TryPlaceWindow())
            placeWindow = false;

        RefreshNative();
    }

    private void CaptureNativeRect(AtkUnitBase* addon)
    {
        if (addon == null)
        {
            return;
        }

        nativeScale = addon->Scale > 0f ? addon->Scale : 1f;
        if (addon->WindowNode == null)
        {
            return;
        }

        var state = addon->WindowNode->GetNodeState();
        windowPos  = state.TopLeft;
        windowSize = new(
            MathF.Max(state.Width, OmniTheme.Scale(240f)),
            MathF.Max(state.Height, OmniTheme.Scale(420f)));
    }

    private bool TryPlaceWindow()
    {
        if (nativeUI is null || !nativeUI.IsOpen)
        {
            return false;
        }

        AtkUnitBase* extraAddon = nativeUI;
        if (extraAddon == null || extraAddon->WindowNode == null)
        {
            return false;
        }

        if (nativeScale > 0f && MathF.Abs(extraAddon->Scale - nativeScale) > 0.001f)
        {
            extraAddon->SetScale(nativeScale / AtkUnitBase.GetGlobalUIScale(), true);
        }

        extraAddon->SetSize((ushort)windowSize.X, (ushort)windowSize.Y);
        nativeUI.Size = windowSize;
        var extraState = extraAddon->WindowNode->GetNodeState();
        var width = extraState.Width > 0f ? extraState.Width : windowSize.X;
        var height = extraState.Height > 0f ? extraState.Height : windowSize.Y;
        var display = ImGui.GetMainViewport().WorkSize;
        var x = windowPos.X + windowSize.X + CompanionGap;
        if (x + width > display.X - ScreenPadding)
        {
            x = windowPos.X - width - CompanionGap;
        }

        nativeUI.SetWindowPosition(new(
            Math.Clamp(x, ScreenPadding, MathF.Max(ScreenPadding, display.X - width - ScreenPadding)),
            Math.Clamp(windowPos.Y, ScreenPadding, MathF.Max(ScreenPadding, display.Y - height - ScreenPadding))));
        return true;
    }

    private static bool TryGetNativeAddon(out AtkUnitBase* addon)
    {
        addon = null;
        return AddonHelper.TryGetByName(GearSetListAddonName, out addon) &&
               addon != null &&
               addon->IsVisible;
    }

    #endregion

    #region 套装持久化

    private void RefreshNative()
    {
        if (nativeUI is null)
        {
            return;
        }

        UpdateGearSnapshot();
        var fingerprint = BuildNativeFingerprint();
        if (fingerprint == nativeFingerprint)
        {
            return;
        }

        nativeFingerprint = fingerprint;
        var rows = BuildNativeRows();
        nativeUI.UpdateData(
            rows,
            DService.Instance().ClientState.IsLoggedIn,
            GetNativeEmptyText(rows.Count));
    }

    private void RefreshList()
    {
        nativeFingerprint = string.Empty;
        RefreshNative();
    }

    private void HandleNativeSave() =>
        TrySaveCurrent();

    private void HandleNativeUpdate(MoreGearSetListEntry entry) =>
        TryUpdate(entry);

    private void HandleNativeDelete(MoreGearSetListEntry entry) =>
        TryDelete(entry);

    internal bool TryDelete(MoreGearSetListEntry entry)
    {
        var record = GetCurrentCharacterRecord(false);
        if (record is null || !record.Sets.Remove(entry))
        {
            return false;
        }

        saveConfig();
        RefreshList();
        return true;
    }

    internal bool TryDeleteMany(IReadOnlyList<MoreGearSetListEntry> entries)
    {
        var record = GetCurrentCharacterRecord(false);
        if (record is null || entries.Count == 0)
        {
            return false;
        }

        var removed = false;
        foreach (var entry in entries)
        {
            removed |= record.Sets.Remove(entry);
        }

        if (!removed)
        {
            return false;
        }

        saveConfig();
        RefreshList();
        return true;
    }

    private void HandleNativeImport() =>
        TryImportNative();

    internal int GetSetIndex(MoreGearSetListEntry entry)
    {
        var record = GetCurrentCharacterRecord(false);
        return record?.Sets.IndexOf(entry) ?? -1;
    }

    internal int GetSetCount() =>
        GetCurrentCharacterRecord(false)?.Sets.Count ?? 0;

    internal bool TryMove(MoreGearSetListEntry entry, int delta)
    {
        var record = GetCurrentCharacterRecord(false);
        if (record is null)
        {
            return false;
        }

        var index = record.Sets.IndexOf(entry);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= record.Sets.Count)
        {
            return false;
        }

        (record.Sets[index], record.Sets[target]) = (record.Sets[target], record.Sets[index]);
        saveConfig();
        RefreshList();
        nativeUI?.SetSelected(entry);
        return true;
    }

    #endregion

    #region 原生套装交互

    internal void ShowItems(MoreGearSetListEntry entry)
    {
        if (TryOpenBorrowed(entry, BorrowKind.Items))
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(entry.Name) ? "套装" : entry.Name;
        OmniNotifier.Popup(title, FormatSetItems(entry));
    }

    internal void OpenPreview(MoreGearSetListEntry entry) =>
        TryOpenBorrowed(entry, BorrowKind.Preview);

    internal void OpenGlamourPlate(MoreGearSetListEntry entry) =>
        TryOpenBorrowed(entry, BorrowKind.Glamour);

    internal void OpenPortrait(MoreGearSetListEntry entry) =>
        TryOpenBorrowed(entry, BorrowKind.Portrait);

    private bool TryOpenBorrowed(MoreGearSetListEntry entry, BorrowKind kind)
    {
        if (!TryBorrowGearset(entry))
        {
            return false;
        }

        var agent = AgentGearSet.Instance();
        if (agent == null)
        {
            ReleaseBorrowedGearset(false);
            return false;
        }

        borrowKind     = kind;
        borrowWaitLeft = BorrowWaitFrames;
        borrowSeenOpen = false;
        switch (kind)
        {
            case BorrowKind.Items:
                agent->OpenGearsetItemList(borrowedGearsetID);
                break;
            case BorrowKind.Preview:
                agent->OpenGearsetPreview(borrowedGearsetID);
                break;
            case BorrowKind.Glamour:
                if (entry.GlamourPlateID > 0)
                {
                    agent->ChangeGlamourPlateLink(borrowedGearsetID);
                }
                else
                {
                    agent->LinkToGlamourPlate(borrowedGearsetID);
                }

                break;
            case BorrowKind.Portrait:
                agent->OpenBannerEditorForGearset(borrowedGearsetID);
                break;
        }

        return true;
    }

    private bool TryBorrowGearset(MoreGearSetListEntry entry)
    {
        ReleaseBorrowedGearset(true);
        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            return false;
        }

        var gearsetID = FindBorrowSlot(module);
        RaptureGearsetModule.GearsetEntry* gearset;
        if (gearsetID >= 0)
        {
            gearset = module->GetGearset(gearsetID);
            if (gearset == null || gearset->Id != gearsetID)
            {
                return false;
            }

            borrowedSnapshot    = *gearset;
            borrowedHasSnapshot = true;
        }
        else
        {
            gearsetID = module->CreateGearset();
            if (gearsetID is < 0 or > 99)
            {
                return false;
            }

            gearset = module->GetGearset(gearsetID);
            if (gearset == null || gearset->Id != gearsetID)
            {
                module->DeleteGearset(gearsetID);
                return false;
            }

            borrowedHasSnapshot = false;
        }

        WriteGearsetItems(gearset, entry);
        borrowedEntry     = entry;
        borrowedGearsetID = gearsetID;
        return true;
    }

    private static int FindBorrowSlot(RaptureGearsetModule* module)
    {
        var current = module->CurrentGearsetIndex;
        if (current is >= 0 and <= 99 && module->IsValidGearset(current))
        {
            return current;
        }

        for (var index = 99; index >= 0; index--)
        {
            var gearset = module->GetGearset(index);
            if (gearset != null &&
                gearset->Id == index &&
                gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
            {
                return index;
            }
        }

        return -1;
    }

    private void TickBorrowedGearset()
    {
        if (borrowedGearsetID < 0)
        {
            return;
        }

        if (IsBorrowUIOpen())
        {
            borrowSeenOpen = true;
            return;
        }

        if (!borrowSeenOpen)
        {
            if (borrowWaitLeft > 0)
            {
                borrowWaitLeft--;
                return;
            }

            ReleaseBorrowedGearset(false);
            return;
        }

        ReleaseBorrowedGearset(true);
    }

    private bool IsBorrowUIOpen()
    {
        var agent = AgentGearSet.Instance();
        return borrowKind switch
        {
            BorrowKind.Preview => agent != null &&
                                  (agent->GearsetIdOfPreviewAddon == borrowedGearsetID ||
                                   agent->GearsetPreviewTexture != null ||
                                   agent->ChildAddonId != 0),
            BorrowKind.Items   => agent != null &&
                                  (agent->GearsetIdOfDisplayAddon == borrowedGearsetID ||
                                   agent->ChildAddonId != 0),
            BorrowKind.Glamour => IsAddonVisible(MiragePlateAddonName),
            BorrowKind.Portrait => IsAddonVisible(BannerEditorAddonName) ||
                                   (agent != null && agent->ChildAddonId != 0),
            _ => false
        };
    }

    private static bool IsAddonVisible(string name) =>
        AddonHelper.TryGetByName(name, out AtkUnitBase* addon) &&
        addon != null &&
        addon->IsVisible;

    private void ReleaseBorrowedGearset(bool sync)
    {
        if (borrowedGearsetID < 0)
        {
            return;
        }

        var module = RaptureGearsetModule.Instance();
        if (sync && borrowedEntry is not null && module != null)
        {
            var gearset = module->GetGearset(borrowedGearsetID);
            if (gearset != null && gearset->Id == borrowedGearsetID)
            {
                borrowedEntry.GlamourPlateID = gearset->GlamourSetLink;
                borrowedEntry.BannerIndex    = gearset->BannerIndex;
                saveConfig();
                nativeFingerprint = string.Empty;
            }
        }

        if (module != null)
        {
            var gearset = module->GetGearset(borrowedGearsetID);
            if (borrowedHasSnapshot &&
                gearset != null &&
                gearset->Id == borrowedGearsetID)
            {
                *gearset = borrowedSnapshot;
            }
            else if (!borrowedHasSnapshot)
            {
                module->DeleteGearset(borrowedGearsetID);
            }
        }

        borrowedEntry        = null;
        borrowedGearsetID    = -1;
        borrowKind           = BorrowKind.None;
        borrowWaitLeft       = 0;
        borrowSeenOpen       = false;
        borrowedHasSnapshot  = false;
    }

    private List<MoreGearSetListRow> BuildNativeRows()
    {
        var rows = new List<MoreGearSetListRow>();
        var record = GetCurrentCharacterRecord(false);
        if (record is null)
        {
            return rows;
        }

        for (var index = 0; index < record.Sets.Count; index++)
        {
            var entry = record.Sets[index];
            if (!MatchesEntry(entry))
            {
                continue;
            }

            var current = IsCurrentSet(entry);
            var missing = !current && HasMissingItems(entry);
            rows.Add(new()
            {
                Entry       = entry,
                Number      = index + 1,
                Name        = string.IsNullOrWhiteSpace(entry.Name) ? "-" : entry.Name,
                IconID      = GetJobIconID(entry.ClassJobID),
                Status      = current ? "当前" : missing ? "缺件" : string.Empty,
                StatusColor = current ? 45u : missing ? 17u : 3u
            });
        }

        return rows;
    }

    private string BuildNativeFingerprint()
    {
        var builder = new StringBuilder();
        builder.Append(GetCurrentClassJobID())
               .Append('|')
               .Append(MoreGearSetListPanel.SearchText)
               .Append('|')
               .Append((int)MoreGearSetListPanel.JobFilterKind)
               .Append('|')
               .Append(MoreGearSetListPanel.FilterClassJobID)
               .Append('|')
               .Append(DService.Instance().ClientState.IsLoggedIn)
               .Append('|')
               .Append(gearSnapshotKey);
        var record = GetCurrentCharacterRecord(false);
        if (record is null)
        {
            return builder.ToString();
        }

        foreach (var entry in record.Sets)
        {
            builder.Append(entry.Name)
                   .Append(entry.ClassJobID)
                   .Append(';');
        }

        return builder.ToString();
    }

    private string GetNativeEmptyText(int visibleCount)
    {
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            return "登录后可保存套装。";
        }

        var record = GetCurrentCharacterRecord(false);
        if (record is null || record.Sets.Count == 0)
        {
            return "还没有套装。点新建套装把当前装备存下来。";
        }

        return visibleCount == 0 ? "没有符合筛选的套装。" : string.Empty;
    }

    #endregion

    #region 套装读写

    private MoreGearSetListCharacterRecord? GetCurrentCharacterRecord(bool create) =>
        TryGetCurrentCharacter(out var key, out var name, out var world, out var contentID)
            ? GetOrCreateRecord(key, name, world, contentID, create)
            : null;

    private bool TrySaveCurrent()
    {
        var record = GetCurrentCharacterRecord(true);
        if (record is null)
        {
            return false;
        }

        if (!TryCaptureEquipped(out var items, out var classJobID, out var jobName))
        {
            return false;
        }

        var index = record.Sets.Count + 1;
        record.Sets.Add(new()
        {
            Name        = string.IsNullOrWhiteSpace(jobName) ? $"套装 {index}" : $"{jobName} {index}",
            ClassJobID  = classJobID,
            NativeIndex = -1,
            Items       = items
        });
        saveConfig();
        RefreshList();
        return true;
    }

    private bool TryUpdate(MoreGearSetListEntry entry)
    {
        if (!TryCaptureEquipped(out var items, out var classJobID, out _))
        {
            return false;
        }

        entry.ClassJobID = classJobID;
        entry.Items      = items;
        saveConfig();
        RefreshList();
        return true;
    }

    internal bool TryRename(MoreGearSetListEntry entry, string name)
    {
        var text = name.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            var index = GetSetIndex(entry);
            text = $"套装 {(index >= 0 ? index + 1 : 1)}";
        }

        if (entry.Name == text)
        {
            return false;
        }

        entry.Name = text;
        saveConfig();
        RefreshList();
        return true;
    }

    private bool TryImportNative()
    {
        var record = GetCurrentCharacterRecord(true);
        if (record is null)
        {
            return false;
        }

        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            return false;
        }

        var imported = 0;
        var skipped = 0;
        for (var index = 0; index < 100; index++)
        {
            var gearset = module->GetGearset(index);
            if (gearset == null ||
                gearset->Id != index ||
                !gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
            {
                continue;
            }

            if (HasNativeIndex(record.Sets, index))
            {
                skipped++;
                continue;
            }

            record.Sets.Add(CaptureFromNative(gearset, index));
            imported++;
        }

        if (imported == 0)
        {
            return false;
        }

        saveConfig();
        RefreshList();
        return true;
    }

    #endregion

    #region 装备

    private void TryApply(MoreGearSetListEntry entry)
    {
        if (taskHelper is null)
        {
            return;
        }

        if (!TryGetCurrentCharacter(out _, out _, out _, out _))
        {
            return;
        }

        if (DService.Instance().Condition.IsOccupiedInEvent)
        {
            OmniNotifier.Chat("当前无法更换装备。");
            return;
        }

        var manager = InventoryManager.Instance();
        var equipped = manager == null ? null : manager->GetInventoryContainer(InventoryType.EquippedItems);
        if (manager == null || equipped == null || !equipped->IsLoaded)
        {
            OmniNotifier.Chat("当前无法更换装备。");
            return;
        }

        var gearItems = GetApplyItems(entry);
        var crystal = GetSoulCrystal(entry);
        var currentJob = GetCurrentClassJobID();
        var needsJobSwitch = entry.ClassJobID != 0 && currentJob != 0 && entry.ClassJobID != currentJob;
        if (needsJobSwitch && crystal is null && GetMainHand(gearItems) is null)
        {
            OmniNotifier.Chat("这套没有灵魂水晶，无法切换职业。");
            return;
        }

        var checkItems = new List<MoreGearSetListItem>();
        if (needsJobSwitch && crystal is not null)
        {
            checkItems.Add(crystal);
        }

        checkItems.AddRange(gearItems);
        if (checkItems.Count == 0)
        {
            OmniNotifier.Chat("这套没有可更换的装备。");
            return;
        }

        if (TryCollectMissing(manager, checkItems, out var missing))
        {
            OmniNotifier.Chat($"套装「{entry.Name}」缺少：\n{FormatMissing(missing)}");
            return;
        }

        if (TryApplyByGearset(entry))
        {
            return;
        }

        taskHelper.Abort();
        if (needsJobSwitch)
        {
            var mainHand = GetMainHand(gearItems);
            var originalJob = currentJob;
            var targetJob = entry.ClassJobID;
            if (mainHand is not null)
            {
                taskHelper.Enqueue(
                    () => TryEquipStep(mainHand, checkItems),
                    name: "SwitchMainHand",
                    timeoutMS: 3_000,
                    timeoutAction: () => OmniNotifier.Chat("更换主手失败。"));
                taskHelper.Enqueue(
                    () =>
                    {
                        var job = GetCurrentClassJobID();
                        return job == targetJob || job != originalJob;
                    },
                    name: "WaitClass",
                    timeoutMS: 5_000,
                    timeoutAction: () => OmniNotifier.Chat("切换职业超时。"));
            }

            if (crystal is not null)
            {
                taskHelper.Enqueue(
                    () => GetCurrentClassJobID() == targetJob || TryEquipStep(crystal, checkItems),
                    name: "SwitchCrystal",
                    timeoutMS: 3_000,
                    timeoutAction: () => OmniNotifier.Chat("装备灵魂水晶失败。"));
                taskHelper.Enqueue(
                    () => GetCurrentClassJobID() == targetJob,
                    name: "WaitJob",
                    timeoutMS: 5_000,
                    timeoutAction: () => OmniNotifier.Chat("切换职业超时。"));
            }
        }

        taskHelper.Enqueue(
            () => TryEquipRemaining(entry, gearItems),
            name: "ApplyGear",
            timeoutMS: 8_000,
            timeoutAction: () => OmniNotifier.Chat($"套装「{entry.Name}」未能全部穿上。"));
    }

    private bool TryApplyByGearset(MoreGearSetListEntry entry)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null || !TryBorrowGearset(entry))
        {
            return false;
        }

        try
        {
            return module->EquipGearset(
                borrowedGearsetID,
                (byte)Math.Clamp(entry.GlamourPlateID, 0, 20)) == 0;
        }
        finally
        {
            ReleaseBorrowedGearset(false);
        }
    }

    private static void WriteGearsetItems(
        RaptureGearsetModule.GearsetEntry* gearset,
        MoreGearSetListEntry entry)
    {
        var dest = gearset->Items;
        for (var slot = 0; slot < dest.Length; slot++)
        {
            dest[slot] = default;
            foreach (var item in entry.Items)
            {
                if (item.Slot != slot || item.ItemID == 0)
                {
                    continue;
                }

                dest[slot].ItemId = item.HQ ? item.ItemID + 1_000_000u : item.ItemID;
                break;
            }
        }

        if (entry.ClassJobID != 0)
        {
            gearset->ClassJob = (byte)entry.ClassJobID;
        }

        if (!string.IsNullOrWhiteSpace(entry.Name) &&
            Encoding.UTF8.GetByteCount(entry.Name.Trim()) < 48)
        {
            gearset->NameString = entry.Name.Trim();
        }

        gearset->GlamourSetLink = (byte)Math.Clamp(entry.GlamourPlateID, 0, 20);
        gearset->BannerIndex    = (byte)Math.Clamp(entry.BannerIndex, 0, 255);
    }

    private static string GetJobName(uint classJobID)
    {
        if (classJobID == 0 || !LuminaGetter.TryGetRow<ClassJob>(classJobID, out var job))
        {
            return string.Empty;
        }

        var name = job.Name.ToString().Trim();
        return string.IsNullOrWhiteSpace(name) ? job.Abbreviation.ToString().Trim() : name;
    }

    private static IReadOnlyList<MoreGearSetListJobChoice> GetJobChoices()
    {
        if (jobChoices is not null)
        {
            return jobChoices;
        }

        jobChoices =
        [
            new("全部", MoreGearSetListJobFilterKind.All, 0),
            new("当前", MoreGearSetListJobFilterKind.Current, 0)
        ];
        foreach (var classJobID in JobOptionIDs)
        {
            var name = GetJobName(classJobID);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            jobChoices.Add(new(name, MoreGearSetListJobFilterKind.Job, classJobID));
        }

        return jobChoices;
    }

    internal static List<string> GetJobChoiceLabels()
    {
        var labels = new List<string>();
        foreach (var choice in GetJobChoices())
            labels.Add(choice.Label);
        return labels;
    }

    internal static string GetJobFilterLabel()
    {
        foreach (var choice in GetJobChoices())
        {
            if (choice.Kind != MoreGearSetListPanel.JobFilterKind)
            {
                continue;
            }

            if (choice.Kind != MoreGearSetListJobFilterKind.Job ||
                choice.ClassJobID == MoreGearSetListPanel.FilterClassJobID)
            {
                return choice.Label;
            }
        }

        return "全部";
    }

    internal static void ApplyJobFilterLabel(string label)
    {
        foreach (var choice in GetJobChoices())
        {
            if (choice.Label != label)
            {
                continue;
            }

            MoreGearSetListPanel.JobFilterKind    = choice.Kind;
            MoreGearSetListPanel.FilterClassJobID = choice.ClassJobID;
            return;
        }
    }

    private static bool MatchesEntry(MoreGearSetListEntry entry) =>
        MatchesJobFilter(entry.ClassJobID) &&
        MatchesSearch(entry.Name, MoreGearSetListPanel.SearchText);

    private static bool MatchesJobFilter(uint classJobID) =>
        MoreGearSetListPanel.JobFilterKind switch
        {
            MoreGearSetListJobFilterKind.Current =>
                classJobID != 0 && classJobID == GetCurrentClassJobID(),
            MoreGearSetListJobFilterKind.Job =>
                classJobID == MoreGearSetListPanel.FilterClassJobID,
            _ => true
        };

    private static bool MatchesSearch(string name, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var keyword = search.Trim();
        foreach (var term in GetSearchTerms(keyword))
        {
            if (ContainsIgnoreCase(name, term))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetSearchTerms(string keyword)
    {
        var terms = new List<string> { keyword };
        foreach (var group in GetSearchAliasGroups())
        {
            var matched = false;
            foreach (var alias in group)
            {
                if (alias.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            foreach (var alias in group)
            {
                if (!ContainsAlias(terms, alias))
                {
                    terms.Add(alias);
                }
            }
        }

        return terms;
    }

    private static List<string[]> GetSearchAliasGroups()
    {
        if (searchAliasGroups is not null)
        {
            return searchAliasGroups;
        }

        searchAliasGroups = [];
        if (!LuminaGetter.TryGet<ClassJob>(out var sheet))
        {
            return searchAliasGroups;
        }

        foreach (var job in sheet)
        {
            if (job.RowId == 0)
            {
                continue;
            }

            var names = new List<string>();
            AddAlias(names, job.Name.ToString());
            AddAlias(names, job.Abbreviation.ToString());
            AddAlias(names, GetLocalizedJobName(job.RowId, ClientLanguage.ChineseSimplified));
            AddAlias(names, GetEnglishJobName(job.RowId));
            if (names.Count > 1)
            {
                searchAliasGroups.Add(names.ToArray());
            }
        }

        return searchAliasGroups;
    }

    private static string GetLocalizedJobName(uint classJobID, ClientLanguage language)
    {
        var sheet = DService.Instance().Data.GetExcelSheet<ClassJob>(language);
        return sheet.TryGetRow(classJobID, out var job) ? job.Name.ToString().Trim() : string.Empty;
    }

    private static string GetEnglishJobName(uint classJobID)
    {
        var name = GetLocalizedJobName(classJobID, ClientLanguage.English);
        if (HasLatinLetter(name))
        {
            return name;
        }

        return classJobID < EnglishJobNames.Length ? EnglishJobNames[classJobID] : string.Empty;
    }

    private static void AddAlias(List<string> names, string name)
    {
        var text = name.Trim();
        if (string.IsNullOrWhiteSpace(text) || ContainsAlias(names, text))
        {
            return;
        }

        names.Add(text);
    }

    private static bool ContainsAlias(List<string> names, string name)
    {
        foreach (var item in names)
        {
            if (item.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLatinLetter(string text)
    {
        foreach (var character in text)
        {
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIgnoreCase(string text, string keyword) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string GetItemName(uint itemID)
    {
        if (itemID == 0 || !LuminaGetter.TryGetRow<Item>(itemID, out var item))
        {
            return itemID.ToString(CultureInfo.InvariantCulture);
        }

        var name = item.Name.ToString().Trim();
        return string.IsNullOrWhiteSpace(name) ? itemID.ToString(CultureInfo.InvariantCulture) : name;
    }

    private static string GetSlotName(int slot) =>
        slot >= 0 && slot < SlotNames.Length ? SlotNames[slot] : slot.ToString(CultureInfo.InvariantCulture);

    private static string FormatSetItems(MoreGearSetListEntry entry)
    {
        var builder = new StringBuilder();
        foreach (var item in entry.Items)
        {
            AppendItemLine(builder, item);
        }

        if (entry.GlamourPlateID > 0)
        {
            AppendLine(builder, $"投影台：{entry.GlamourPlateID}");
        }

        if (entry.BannerIndex > 0)
        {
            AppendLine(builder, "肖像：已绑定");
        }

        return builder.Length == 0 ? "空" : builder.ToString();
    }

    private static uint GetCurrentClassJobID()
    {
        var localPlayer = DService.Instance().ObjectTable.LocalPlayer;
        return localPlayer is null ? 0 : localPlayer.ClassJob.RowId;
    }

    private static uint GetJobIconID(uint classJobID) =>
        classJobID == 0 ? 0 : JobIconBase + classJobID;

    private void UpdateGearSnapshot()
    {
        var manager = InventoryManager.Instance();
        var builder = new StringBuilder();
        if (manager == null)
        {
            if (gearSnapshotKey.Length == 0)
                return;

            gearSnapshotKey = string.Empty;
            equippedSnapshot.Clear();
            availableSnapshot.Clear();
            return;
        }

        foreach (var containerType in SearchContainers)
        {
            var container = manager->GetInventoryContainer(containerType);
            builder.Append((int)containerType).Append(':');
            if (container == null || !container->IsLoaded)
            {
                builder.Append('|');
                continue;
            }

            for (var index = 0; index < container->Size; index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot == null || slot->ItemId == 0)
                {
                    continue;
                }

                var itemID = ItemUtil.GetBaseId(slot->ItemId).ItemId;
                var hq = slot->IsHighQuality();
                builder.Append(itemID).Append(hq ? 'H' : 'N').Append(',');
            }

            builder.Append('|');
        }

        var key = builder.ToString();
        if (key == gearSnapshotKey)
        {
            return;
        }

        gearSnapshotKey = key;
        equippedSnapshot.Clear();
        availableSnapshot.Clear();
        foreach (var containerType in SearchContainers)
        {
            var container = manager->GetInventoryContainer(containerType);
            if (container == null || !container->IsLoaded)
            {
                continue;
            }

            for (var index = 0; index < container->Size; index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot == null || slot->ItemId == 0)
                {
                    continue;
                }

                var itemID = ItemUtil.GetBaseId(slot->ItemId).ItemId;
                var hq = slot->IsHighQuality();
                var item = (itemID, hq);
                availableSnapshot[item] = availableSnapshot.GetValueOrDefault(item) + 1;
                if (containerType == InventoryType.EquippedItems)
                {
                    equippedSnapshot[index] = item;
                }
            }
        }
    }

    private bool IsCurrentSet(MoreGearSetListEntry entry)
    {
        if (entry.Items.Count == 0)
        {
            return false;
        }

        foreach (var item in entry.Items)
        {
            if (item.ItemID == 0)
            {
                continue;
            }

            if (!equippedSnapshot.TryGetValue(item.Slot, out var equipped) ||
                equipped.ItemID != item.ItemID ||
                equipped.HQ != item.HQ)
            {
                return false;
            }
        }

        return true;
    }

    private bool HasMissingItems(MoreGearSetListEntry entry)
    {
        if (gearSnapshotKey.Length == 0)
        {
            return false;
        }

        Dictionary<(uint ItemID, bool HQ), int>? used = null;
        foreach (var item in entry.Items)
        {
            if (item.ItemID == 0)
            {
                continue;
            }

            if (equippedSnapshot.TryGetValue(item.Slot, out var equipped) &&
                equipped.ItemID == item.ItemID &&
                equipped.HQ == item.HQ)
            {
                continue;
            }

            used ??= [];
            var key = (item.ItemID, item.HQ);
            used.TryGetValue(key, out var claimed);
            if (!availableSnapshot.TryGetValue(key, out var available) || claimed >= available)
            {
                return true;
            }

            used[key] = claimed + 1;
        }

        return false;
    }

    private MoreGearSetListCharacterRecord? GetOrCreateRecord(
        string key,
        string name,
        string world,
        ulong contentID,
        bool create)
    {
        MoreGearSetListCharacterRecord? record = null;
        foreach (var candidate in config.Characters)
        {
            if (string.Equals(candidate.CharacterKey, key, StringComparison.Ordinal))
            {
                record = candidate;
                break;
            }
        }

        if (record is null)
        {
            if (!create)
            {
                return null;
            }

            record = new() { CharacterKey = key };
            config.Characters.Add(record);
        }

        record.CharacterName = name;
        record.HomeWorld     = world;
        record.ContentID     = contentID;
        return record;
    }

    private static bool TryGetCurrentCharacter(
        out string key,
        out string name,
        out string world,
        out ulong contentID)
    {
        key       = string.Empty;
        name      = string.Empty;
        world     = string.Empty;
        contentID = 0;

        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn ||
            services.ObjectTable.LocalPlayer is not { } localPlayer ||
            !services.PlayerState.IsLoaded ||
            services.PlayerState.HomeWorld.ValueNullable is not { } homeWorld)
        {
            return false;
        }

        name      = localPlayer.Name;
        world     = homeWorld.Name.ToString();
        contentID = services.PlayerState.ContentId;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world))
        {
            return false;
        }

        key = contentID != 0
            ? contentID.ToString(CultureInfo.InvariantCulture)
            : $"{name}@{world}";
        return true;
    }

    private static bool TryCaptureEquipped(
        out List<MoreGearSetListItem> items,
        out uint classJobID,
        out string jobName)
    {
        items      = [];
        classJobID = GetCurrentClassJobID();
        jobName    = GetJobName(classJobID);

        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            return false;
        }

        var container = manager->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null || !container->IsLoaded)
        {
            return false;
        }

        for (var slot = 0; slot < container->Size; slot++)
        {
            var equipped = container->GetInventorySlot(slot);
            if (equipped == null || equipped->ItemId == 0)
            {
                continue;
            }

            items.Add(new()
            {
                Slot   = slot,
                ItemID = ItemUtil.GetBaseId(equipped->ItemId).ItemId,
                HQ     = equipped->IsHighQuality()
            });
        }

        return items.Count > 0;
    }

    private static MoreGearSetListEntry CaptureFromNative(
        RaptureGearsetModule.GearsetEntry* gearset,
        int index)
    {
        var items = new List<MoreGearSetListItem>();
        var slot = 0;
        foreach (var item in gearset->Items)
        {
            if (item.ItemId != 0)
            {
                var parsed = ItemUtil.GetBaseId(item.ItemId);
                items.Add(new()
                {
                    Slot   = slot,
                    ItemID = parsed.ItemId,
                    HQ     = item.ItemId >= 1_000_000
                });
            }

            slot++;
        }

        var classJobID = (uint)gearset->ClassJob;
        var name = gearset->NameString;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = GetJobName(classJobID);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"套装 {index + 1}";
        }

        return new()
        {
            Name           = name.Trim(),
            ClassJobID     = classJobID,
            NativeIndex    = index,
            GlamourPlateID = gearset->GlamourSetLink,
            BannerIndex    = gearset->BannerIndex,
            Items          = items
        };
    }

    private static bool HasNativeIndex(List<MoreGearSetListEntry> sets, int nativeIndex)
    {
        foreach (var entry in sets)
        {
            if (entry.NativeIndex == nativeIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static List<MoreGearSetListItem> GetApplyItems(MoreGearSetListEntry entry)
    {
        var items = new List<MoreGearSetListItem>();
        foreach (var item in entry.Items)
        {
            if (item.ItemID == 0 || item.Slot == SoulCrystalSlot)
            {
                continue;
            }

            items.Add(item);
        }

        return items;
    }

    private static MoreGearSetListItem? GetSoulCrystal(MoreGearSetListEntry entry)
    {
        foreach (var item in entry.Items)
        {
            if (item.Slot == SoulCrystalSlot && item.ItemID != 0)
            {
                return item;
            }
        }

        return null;
    }

    private static MoreGearSetListItem? GetMainHand(List<MoreGearSetListItem> items)
    {
        foreach (var item in items)
        {
            if (item.Slot == 0)
            {
                return item;
            }
        }

        return null;
    }

    private static bool TryCollectMissing(
        InventoryManager* manager,
        List<MoreGearSetListItem> items,
        out List<MoreGearSetListItem> missing)
    {
        missing = [];
        var claimed = new HashSet<(InventoryType Type, int Slot)>();
        foreach (var item in items)
        {
            if (IsSlotEquipped(manager, item))
            {
                claimed.Add((InventoryType.EquippedItems, item.Slot));
                continue;
            }

            if (!TryFindSource(manager, item, claimed, out var type, out var slot))
            {
                missing.Add(item);
                continue;
            }

            claimed.Add((type, slot));
        }

        return missing.Count > 0;
    }

    private static bool TryEquipStep(MoreGearSetListItem item, List<MoreGearSetListItem> all)
    {
        if (DService.Instance().Condition.IsOccupiedInEvent)
        {
            return false;
        }

        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            return false;
        }

        return IsSlotEquipped(manager, item) || TryEquipOnce(manager, item, all);
    }

    private static bool TryEquipRemaining(MoreGearSetListEntry entry, List<MoreGearSetListItem> items)
    {
        if (DService.Instance().Condition.IsOccupiedInEvent)
        {
            return false;
        }

        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (IsSlotEquipped(manager, item))
            {
                continue;
            }

            if (!TryEquipOnce(manager, item, items))
            {
                return false;
            }

            return false;
        }

        foreach (var item in items)
        {
            if (!IsSlotEquipped(manager, item))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryEquipOnce(
        InventoryManager* manager,
        MoreGearSetListItem item,
        List<MoreGearSetListItem> all)
    {
        var blocked = CollectEquippedSlots(manager, all);
        return TryFindSource(manager, item, blocked, out var type, out var slot) &&
               manager->MoveItemSlot(type, (ushort)slot, InventoryType.EquippedItems, (ushort)item.Slot, true) != 0;
    }

    private static HashSet<(InventoryType Type, int Slot)> CollectEquippedSlots(
        InventoryManager* manager,
        List<MoreGearSetListItem> items)
    {
        var slots = new HashSet<(InventoryType Type, int Slot)>();
        foreach (var item in items)
        {
            if (IsSlotEquipped(manager, item))
            {
                slots.Add((InventoryType.EquippedItems, item.Slot));
            }
        }

        return slots;
    }

    private static bool IsSlotEquipped(InventoryManager* manager, MoreGearSetListItem item)
    {
        var container = manager->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null || !container->IsLoaded || item.Slot < 0 || item.Slot >= container->Size)
        {
            return false;
        }

        var equipped = container->GetInventorySlot(item.Slot);
        return equipped != null && IsMatch(equipped, item);
    }

    private static bool TryFindSource(
        InventoryManager* manager,
        MoreGearSetListItem item,
        HashSet<(InventoryType Type, int Slot)> claimed,
        out InventoryType type,
        out int slot)
    {
        type = default;
        slot = 0;
        foreach (var containerType in SearchContainers)
        {
            var container = manager->GetInventoryContainer(containerType);
            if (container == null || !container->IsLoaded)
            {
                continue;
            }

            for (var index = 0; index < container->Size; index++)
            {
                var candidate = container->GetInventorySlot(index);
                if (candidate == null || claimed.Contains((containerType, candidate->Slot)) || !IsMatch(candidate, item))
                {
                    continue;
                }

                type = containerType;
                slot = candidate->Slot;
                return true;
            }
        }

        return false;
    }

    private static bool IsMatch(InventoryItem* slot, MoreGearSetListItem item) =>
        slot->ItemId != 0 &&
        ItemUtil.GetBaseId(slot->ItemId).ItemId == item.ItemID &&
        slot->IsHighQuality() == item.HQ;

    private static string FormatMissing(List<MoreGearSetListItem> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            AppendItemLine(builder, item);
        }

        return builder.ToString();
    }

    private static void AppendItemLine(StringBuilder builder, MoreGearSetListItem item)
    {
        AppendLine(builder, $"{GetSlotName(item.Slot)}：{GetItemName(item.ItemID)}{(item.HQ ? " HQ" : string.Empty)}");
    }

    private static void AppendLine(StringBuilder builder, string text)
    {
        if (builder.Length > 0)
            builder.Append('\n');
        builder.Append(text);
    }

    #endregion
}

[Serializable]
public sealed class MoreGearSetListConfig
{
    public List<MoreGearSetListCharacterRecord> Characters { get; set; } = [];
}

[Serializable]
public sealed class MoreGearSetListCharacterRecord
{
    public string CharacterKey { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string HomeWorld { get; set; } = string.Empty;

    public ulong ContentID { get; set; }

    public List<MoreGearSetListEntry> Sets { get; set; } = [];
}

[Serializable]
public sealed class MoreGearSetListEntry
{
    public string Name { get; set; } = string.Empty;

    public uint ClassJobID { get; set; }

    public int NativeIndex { get; set; } = -1;

    public int GlamourPlateID { get; set; }

    public int BannerIndex { get; set; }

    public List<MoreGearSetListItem> Items { get; set; } = [];
}

[Serializable]
public sealed class MoreGearSetListItem
{
    public int Slot { get; set; }

    public uint ItemID { get; set; }

    public bool HQ { get; set; }
}

internal enum BorrowKind
{
    None,
    Preview,
    Items,
    Glamour,
    Portrait
}

internal enum MoreGearSetListJobFilterKind
{
    All,
    Current,
    Job
}

internal readonly record struct MoreGearSetListJobChoice(
    string Label,
    MoreGearSetListJobFilterKind Kind,
    uint ClassJobID);

internal static class MoreGearSetListPanel
{
    internal static string SearchText = string.Empty;
    internal static MoreGearSetListJobFilterKind JobFilterKind;
    internal static uint FilterClassJobID;

    public static void Draw(MoreGearSetList feature)
    {
        if (OmniControls.SmallButton("打开套装列表", false))
            feature.ToggleList();
    }
}
