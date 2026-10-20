using System.Linq;
using System.Threading;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;
using OmniToolbox.Notifications;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterGlamourManagement(
    BetterGlamourManagement.Settings config,
    Action saveConfig,
    ItemPreviewService itemPreviewService) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BetterGlamourManagementTitle"),
        Description = OmniLoc.Get("BetterGlamourManagementDescription"),
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Item/BetterGlamourManagement-1.png?v=a2e7f88",
        RequiresPrivateProvider = true,
        Commands =
        [
            new ModuleCommand(
                "Feature.BetterGlamourManagement.CommandDescription",
                OmniCommandCatalog.Format("glamourmanager"))
        ]
    };

    private static readonly int[] InspectSlots = [0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12];

    private BetterGlamourActionsNativeUI? actionsNativeUI;
    private BetterGlamourManagerNativeUI? managerNativeUI;
    private BetterGlamourPortraitPreview? portraitPreview;
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
            taskHelper = new()
            {
                TimeoutMS = 10_000
            };
            actionsNativeUI = new(TryOnInspect, SaveInspect, ExportInspect, OpenManager, ClearTryOn);
            portraitPreview = new();
            var services = DService.Instance();
            portraitPreview.Initialize(new()
            {
                PluginInterface = DalamudServices.PluginInterface,
                AddonLifecycle = services.AddonLifecycle,
                TextureReadback = services.TextureReadback,
                Framework = services.Framework,
                ClientState = services.ClientState,
                ObjectTable = services.ObjectTable,
                Condition = services.Condition,
                GameGUI = services.GameGUI,
                DataManager = services.Data
            }, () => config.Presets);
            portraitPreview.Enable();
            EnableRemote();
            managerNativeUI = new(
                SelectPreset,
                PresetChanged,
                ChangePresetGearset,
                ToggleTryOn,
                IsTryingOnPreset,
                CopyPreset,
                DeletePreset,
                ImportClipboard,
                portraitPreview);
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
        DisableRemote();
        DalamudServices.PluginInterface.UiBuilder.Draw -= Draw;
        FrameworkManager.Instance().Unreg(OnFrameworkUpdate);
        managerNativeUI?.ClearPreviewTexture();
        portraitPreview?.Dispose();
        portraitPreview = null;
        ClearTryOn();
        taskHelper?.Dispose();
        taskHelper = null;
        DisposeActionsNativeUI();
        DisposeManagerNativeUI();
        boundTryOnPreset = null;
        selectedPresetIndex = -1;
        lastGearsetIndex = int.MinValue;
    }

    private void Draw() => DrawInspectActions();

    private void DrawInspectActions()
    {
        if (!TryGetReadyAddon(CHARACTER_INSPECT_ADDON_NAME, out var inspectAddon) || actionsNativeUI is null)
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
        var inspectScale = inspectAddon->WindowNode->GetScale().X;
        if (MathF.Abs(actionAddon->WindowNode->GetScale().X - inspectScale) > 0.001f)
        {
            actionAddon->SetScale(inspectScale / AtkUnitBase.GetGlobalUIScale(), true);
        }
        var actionState = actionAddon->WindowNode->GetNodeState();
        var display = new Vector2(stage->ScreenSize.Width, stage->ScreenSize.Height);
        var x = inspectState.TopLeft.X + inspectState.Width + 2f;
        if (x + actionState.Width > display.X - SCREEN_PADDING)
        {
            x = MathF.Max(SCREEN_PADDING, inspectState.TopLeft.X - actionState.Width - 2f);
        }

        actionsNativeUI.SetWindowPosition(new(
            Math.Clamp(x, SCREEN_PADDING, MathF.Max(SCREEN_PADDING, display.X - actionState.Width - SCREEN_PADDING)),
            Math.Clamp(
                inspectState.TopLeft.Y,
                SCREEN_PADDING,
                MathF.Max(SCREEN_PADDING, display.Y - actionState.Height - SCREEN_PADDING))));
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        RemoteTick();
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            activeTryOnPreset = null;
            boundTryOnPreset = null;
            managerNativeUI?.UpdateTryOnButton();
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
                SynchronizeBoundTryOn(currentGearsetIndex);
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

    private static bool TryGetReadyAddon(string name, out AtkUnitBase* addon)
    {
        addon = null;
        return AddonHelper.TryGetByName(name, out addon) &&
               addon->IsAddonAndNodesReady() &&
               addon->WindowNode != null;
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
        if (config.Presets.Count >= MAX_PRESET_COUNT)
        {
            OmniNotifier.Popup(
                Info.Title,
                string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.PresetLimitReached"), MAX_PRESET_COUNT),
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
        if (!TryGetReadyAddon(CHARACTER_INSPECT_ADDON_NAME, out _) ||
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
        if (!ReferenceEquals(boundTryOnPreset, preset))
            boundTryOnPreset = null;
        activeTryOnPreset = preset;
        managerNativeUI?.UpdateTryOnButton();
        previewPlayerAddress = DService.Instance().ObjectTable.LocalPlayer?.Address ?? nint.Zero;
        pendingTryOnRestore = true;
        TryApplyActiveTryOn();
    }

    private void ToggleTryOn(BetterGlamourPreset preset)
    {
        if (IsTryingOnPreset(preset))
            ClearTryOn();
        else
            StartTryOn(preset);
    }

    internal bool IsTryingOnPreset(BetterGlamourPreset preset) => ReferenceEquals(activeTryOnPreset, preset);

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
            DService.Instance().Condition.IsBetweenAreas)
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
        boundTryOnPreset = null;
        previewPlayerAddress = nint.Zero;
        pendingTryOnRestore = false;
        managerNativeUI?.UpdateTryOnButton();
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
        if (selectedPresetIndex >= 0 && selectedPresetIndex < config.Presets.Count)
            QueuePortraitPreview(config.Presets[selectedPresetIndex]);
        else
        {
            managerNativeUI?.ClearPreviewTexture();
            portraitPreview?.CancelPreview();
        }
    }

    private void SelectPreset(BetterGlamourPreset preset)
    {
        selectedPresetIndex = config.Presets.IndexOf(preset);
        QueuePortraitPreview(preset);
    }

    private void PresetChanged(BetterGlamourPreset preset)
    {
        saveConfig();
        QueuePortraitPreview(preset);
    }

    private void QueuePortraitPreview(BetterGlamourPreset preset)
    {
        Volatile.Write(ref phoneSelectedID, string.Empty);
        managerNativeUI?.ClearPreviewTexture();
        portraitPreview?.QueuePreset(preset);
        Volatile.Write(ref phoneSelectedID, selectedPresetIndex >= 0 && selectedPresetIndex < config.Presets.Count
            ? config.Presets[selectedPresetIndex].ID
            : string.Empty);
    }

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
                SynchronizeBoundTryOn(currentGearsetIndex);
            }
        }
    }

    private void SynchronizeBoundTryOn(int gearsetIndex)
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

        if (activeTryOnPreset is not null)
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
        if (selectedPresetIndex >= 0)
            QueuePortraitPreview(config.Presets[selectedPresetIndex]);
        else
        {
            managerNativeUI?.ClearPreviewTexture();
            portraitPreview?.CancelPreview();
        }
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

    #region 常量

    private const string CHARACTER_INSPECT_ADDON_NAME = "CharacterInspect";

    private const float SCREEN_PADDING = 8f;

    internal const int MAX_PRESET_COUNT = 100;

    #endregion

    public sealed class Settings
    {
        public List<BetterGlamourPreset> Presets { get; set; } = [];
    }
}
