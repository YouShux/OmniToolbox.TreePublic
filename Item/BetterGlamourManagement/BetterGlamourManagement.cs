using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterGlamourManagement(
    BetterGlamourManagementConfig config,
    Action saveConfig,
    ItemPreviewService itemPreviewService) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BetterGlamourManagementTitle"),
        Description = OmniLoc.Get("BetterGlamourManagementDescription"),
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Item/BetterGlamourManagement-1.png?v=a2e7f88",
        RequiresPrivateProvider = true,
        Commands =
        [
            new ModuleCommand(
                "Feature.BetterGlamourManagement.CommandDescription",
                "/omni 幻化管理")
        ]
    };

    private const string CharacterInspectAddonName = "CharacterInspect";
    private const float ScreenPadding = 8f;
    internal const int MaxPresetCount = 100;

    private static readonly int[] InspectSlots = [0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12];

    private BetterGlamourActionsNativeUI? actionsNativeUI;
    private BetterGlamourManagerNativeUI? managerNativeUI;
    private TaskHelper? taskHelper;
    private BetterGlamourPreset? activeTryOnPreset;
    private BetterGlamourPreset? boundTryOnPreset;
    private nint previewPlayerAddress;
    private bool pendingTryOnRestore;
    private int selectedPresetIndex = -1;
    private int lastGearsetIndex = int.MinValue;

    protected override void OnEnable()
    {
        try
        {
            taskHelper = new() { TimeoutMS = 10_000 };
            actionsNativeUI = new(TryOnInspect, SaveInspect, ExportInspect, OpenManager, ClearTryOn);
            managerNativeUI = new(
                SelectPreset,
                _ => saveConfig(),
                ChangePresetGearset,
                StartTryOn,
                CopyPreset,
                DeletePreset);
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 250))
            {
                throw new InvalidOperationException("Better glamour management update registration failed.");
            }

            DalamudServices.PluginInterface.UiBuilder.Draw += Draw;
        }
        catch
        {
            OnDisable();
            throw;
        }
    }

    protected override void OnDisable()
    {
        DalamudServices.PluginInterface.UiBuilder.Draw -= Draw;
        FrameworkManager.Instance().Unreg(OnFrameworkUpdate);
        ClearTryOn();
        taskHelper?.Dispose();
        taskHelper = null;
        DisposeActionsNativeUI();
        DisposeManagerNativeUI();
        boundTryOnPreset = null;
        selectedPresetIndex = -1;
        lastGearsetIndex = int.MinValue;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            activeTryOnPreset = null;
            boundTryOnPreset = null;
            previewPlayerAddress = nint.Zero;
            pendingTryOnRestore = false;
            lastGearsetIndex = int.MinValue;
            return;
        }

        var gearsetModule = RaptureGearsetModule.Instance();
        if (gearsetModule == null)
        {
            lastGearsetIndex = int.MinValue;
        }
        else
        {
            var currentGearsetIndex = gearsetModule->CurrentGearsetIndex;
            if (lastGearsetIndex != currentGearsetIndex)
            {
                lastGearsetIndex = currentGearsetIndex;
                SynchronizeBoundTryOn(currentGearsetIndex, true);
            }
            else if (boundTryOnPreset is not null &&
                     !ReferenceEquals(activeTryOnPreset, boundTryOnPreset))
            {
                StartTryOn(boundTryOnPreset);
            }
        }

        var playerAddress = DService.Instance().ObjectTable.LocalPlayer?.Address ?? nint.Zero;
        if (activeTryOnPreset is not null &&
            playerAddress != nint.Zero &&
            playerAddress != previewPlayerAddress)
        {
            previewPlayerAddress = playerAddress;
            pendingTryOnRestore = true;
        }

        if (!pendingTryOnRestore &&
            activeTryOnPreset is not null &&
            taskHelper is { IsBusy: false } &&
            !IsActiveTryOnApplied())
        {
            pendingTryOnRestore = true;
        }

        TryApplyActiveTryOn();
    }

    private void Draw()
    {
        DrawInspectActions();
    }

    private void DrawInspectActions()
    {
        if (!TryGetReadyAddon(CharacterInspectAddonName, out var inspectAddon) || actionsNativeUI is null)
        {
            actionsNativeUI?.Close();
            return;
        }

        if (!actionsNativeUI.IsOpen)
        {
            actionsNativeUI.Open();
        }

        AtkUnitBase* actionAddon = actionsNativeUI;
        var stage = AtkStage.Instance();
        if (actionAddon == null || actionAddon->WindowNode == null || stage == null)
        {
            return;
        }

        var inspectState = inspectAddon->WindowNode->GetNodeState();
        var actionState = actionAddon->WindowNode->GetNodeState();
        var display = new Vector2(stage->ScreenSize.Width, stage->ScreenSize.Height);
        var x = inspectState.TopLeft.X + inspectState.Width + 2f;
        if (x + actionState.Width > display.X - ScreenPadding)
        {
            x = MathF.Max(ScreenPadding, inspectState.TopLeft.X - actionState.Width - 2f);
        }

        actionsNativeUI.SetWindowPosition(new(
            Math.Clamp(x, ScreenPadding, MathF.Max(ScreenPadding, display.X - actionState.Width - ScreenPadding)),
            Math.Clamp(
                inspectState.TopLeft.Y,
                ScreenPadding,
                MathF.Max(ScreenPadding, display.Y - actionState.Height - ScreenPadding))));
    }

    private static bool TryGetReadyAddon(string name, out AtkUnitBase* addon)
    {
        addon = null;
        return AddonHelper.TryGetByName(name, out addon) &&
               addon != null &&
               addon->IsVisible &&
               addon->WindowNode != null &&
               addon->IsAddonAndNodesReady();
    }

    private void TryOnInspect()
    {
        if (CaptureInspectPreset() is { } preset)
        {
            StartTryOn(preset);
        }
        else
        {
            NotifyNoInspectData();
        }
    }

    private void SaveInspect()
    {
        if (config.Presets.Count >= MaxPresetCount)
        {
            OmniNotifier.Popup(
                Info.Title,
                string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.PresetLimitReached"), MaxPresetCount),
                NotificationType.Warning);
            return;
        }

        var preset = CaptureInspectPreset();
        if (preset is null)
        {
            NotifyNoInspectData();
            return;
        }

        preset.Name = string.Format(
            OmniLoc.Get("Feature.BetterGlamourManagement.DefaultName"),
            DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        config.Presets.Add(preset);
        selectedPresetIndex = config.Presets.Count - 1;
        saveConfig();
        managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
        OmniNotifier.Popup(
            Info.Title,
            string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.Saved"), preset.Name),
            NotificationType.Success);
    }

    private void ExportInspect()
    {
        var preset = CaptureInspectPreset();
        if (preset is null)
        {
            NotifyNoInspectData();
            return;
        }

        CopyPreset(preset);
    }

    private BetterGlamourPreset? CaptureInspectPreset()
    {
        if (!TryGetReadyAddon(CharacterInspectAddonName, out _) ||
            !InventoryType.Examine.TryGetItems(
                item => item.ItemId != 0 && Array.IndexOf(InspectSlots, item.Slot) >= 0,
                out var inspectItems))
        {
            return null;
        }

        var preset = new BetterGlamourPreset();
        foreach (var item in inspectItems)
        {
            preset.Items.Add(new()
            {
                Slot = item.Slot,
                ItemID = item.GlamourId != 0 ? item.GlamourId : item.ItemId,
                Stain0 = item.Stains[0],
                Stain1 = item.Stains[1]
            });
        }

        var uiState = UIState.Instance();
        if (uiState != null)
        {
            preset.GlassesID = uiState->Inspect.GlassesIds[0];
            preset.HairstyleID = ResolveInspectHairstyleID(uiState->Inspect.CustomizeData);
        }

        return preset.Items.Count == 0 ? null : preset;
    }

    private void StartTryOn(BetterGlamourPreset preset)
    {
        activeTryOnPreset = preset;
        previewPlayerAddress = DService.Instance().ObjectTable.LocalPlayer?.Address ?? nint.Zero;
        pendingTryOnRestore = true;
        TryApplyActiveTryOn();
    }

    private bool IsActiveTryOnApplied()
    {
        if (activeTryOnPreset is not { } preset)
        {
            return true;
        }

        foreach (var item in preset.Items)
        {
            if (item.ItemID != 0 &&
                !itemPreviewService.IsEquipmentPreviewApplied(item.ItemID, item.Slot, item.Stain0, item.Stain1))
            {
                return false;
            }
        }

        return itemPreviewService.IsGlassesPreviewApplied(preset.GlassesID) &&
               (preset.HairstyleID == 0 || itemPreviewService.IsHairstylePreviewApplied(preset.HairstyleID));
    }

    private void TryApplyActiveTryOn()
    {
        if (!pendingTryOnRestore ||
            activeTryOnPreset is null ||
            taskHelper is null ||
            taskHelper.IsBusy ||
            previewPlayerAddress == nint.Zero ||
            DService.Instance().Condition[ConditionFlag.BetweenAreas] ||
            DService.Instance().Condition[ConditionFlag.BetweenAreas51])
        {
            return;
        }

        pendingTryOnRestore = false;
        var preset = activeTryOnPreset;
        var items = preset.Items
            .Where(static item => item.ItemID != 0)
            .OrderBy(static item => item.Slot)
            .ToArray();
        foreach (var item in items)
        {
            taskHelper.Enqueue(() =>
            {
                itemPreviewService.PreviewEquipment(item.ItemID, item.Slot, item.Stain0, item.Stain1);
                return true;
            });
            taskHelper.DelayNext(50);
        }

        taskHelper.Enqueue(() =>
        {
            itemPreviewService.PreviewGlasses(preset.GlassesID);
            return true;
        });

        taskHelper.DelayNext(100);
        taskHelper.Enqueue(() =>
        {
            if (preset.HairstyleID != 0)
            {
                itemPreviewService.PreviewHairstyle(preset.HairstyleID);
            }
            else
            {
                itemPreviewService.RestoreHairstylePreview();
            }

            return true;
        });
    }

    private void ClearTryOn()
    {
        taskHelper?.Abort();
        activeTryOnPreset = null;
        previewPlayerAddress = nint.Zero;
        pendingTryOnRestore = false;
        itemPreviewService.ClearPreviewResidue();
    }

    public void OpenManager()
    {
        NormalizeSelection();
        managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
        if (managerNativeUI is { IsOpen: false })
        {
            managerNativeUI.Open();
        }
    }

    private void SelectPreset(BetterGlamourPreset preset) =>
        selectedPresetIndex = config.Presets.IndexOf(preset);

    private void ChangePresetGearset(BetterGlamourPreset preset, int gearsetIndex)
    {
        var previousGearsetIndex = preset.GearsetIndex;
        if (gearsetIndex >= 0)
        {
            foreach (var other in config.Presets)
            {
                if (!ReferenceEquals(other, preset) && other.GearsetIndex == gearsetIndex)
                {
                    other.GearsetIndex = -1;
                }
            }
        }

        preset.GearsetIndex = gearsetIndex;
        saveConfig();

        var gearsetModule = RaptureGearsetModule.Instance();
        if (gearsetModule != null)
        {
            var currentGearsetIndex = gearsetModule->CurrentGearsetIndex;
            if (currentGearsetIndex == previousGearsetIndex || currentGearsetIndex == gearsetIndex)
            {
                lastGearsetIndex = currentGearsetIndex;
                SynchronizeBoundTryOn(currentGearsetIndex, true);
            }
        }
    }

    private void SynchronizeBoundTryOn(int gearsetIndex, bool clearIfUnbound)
    {
        boundTryOnPreset = config.Presets.Find(item => item.GearsetIndex == gearsetIndex);
        if (boundTryOnPreset is not null)
        {
            if (!ReferenceEquals(activeTryOnPreset, boundTryOnPreset))
            {
                StartTryOn(boundTryOnPreset);
            }

            return;
        }

        if (clearIfUnbound && activeTryOnPreset is not null)
        {
            ClearTryOn();
        }
    }

    private void DeletePreset(BetterGlamourPreset preset)
    {
        var index = config.Presets.IndexOf(preset);
        if (index < 0)
        {
            return;
        }

        config.Presets.RemoveAt(index);
        if (ReferenceEquals(activeTryOnPreset, preset))
        {
            ClearTryOn();
        }

        if (ReferenceEquals(boundTryOnPreset, preset))
        {
            boundTryOnPreset = null;
        }

        selectedPresetIndex = Math.Min(index, config.Presets.Count - 1);
        saveConfig();
        managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
    }

    private void NotifyNoInspectData() => OmniNotifier.Popup(
        Info.Title,
        OmniLoc.Get("Feature.BetterGlamourManagement.NoInspectData"),
        NotificationType.Warning);

    private void DisposeActionsNativeUI()
    {
        var ui = actionsNativeUI;
        actionsNativeUI = null;
        if (ui is null)
        {
            return;
        }

        ui.Close();
        ui.Dispose();
    }

    private void DisposeManagerNativeUI()
    {
        var ui = managerNativeUI;
        managerNativeUI = null;
        if (ui is null)
        {
            return;
        }

        ui.Close();
        ui.Dispose();
    }
}

[Serializable]
public sealed class BetterGlamourManagementConfig
{
    public List<BetterGlamourPreset> Presets { get; set; } = [];
}

[Serializable]
public sealed class BetterGlamourPreset
{
    public string Name { get; set; } = string.Empty;

    public int GearsetIndex { get; set; } = -1;

    public ushort GlassesID { get; set; }

    public uint HairstyleID { get; set; }

    public List<BetterGlamourItem> Items { get; set; } = [];
}

[Serializable]
public sealed class BetterGlamourItem
{
    public int Slot { get; set; }

    public uint ItemID { get; set; }

    public byte Stain0 { get; set; }

    public byte Stain1 { get; set; }
}
