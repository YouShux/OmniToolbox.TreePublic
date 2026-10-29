using System.Threading;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.Collections;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Items;
using OmniToolbox.Notifications;
using TreeTools.Threading;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class BetterGlamourManagement
(
    BetterGlamourManagement.Settings config,
    Action                           saveConfig,
    ItemPreviewService               itemPreviewService,
    ItemPatchMap                     itemPatchMap,
    CollectionSourceService          collectionSourceService,
    Func<uint, bool>                 isInGlamourDresser,
    Func<uint, bool>                 isInArmoire,
    Func<long>                       getInventoryRevision
) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Item/BetterGlamourManagement-1.png?v=18138f9d",
        RequiresPrivateProvider = true,
        Commands =
        [
            new ModuleCommand(
                "Feature.BetterGlamourManagement.CommandDescription",
                OmniCommandCatalog.Format("glamourmanager")),
            new ModuleCommand(
                "Feature.BetterGlamourManagement.ClearTryOnCommandDescription",
                OmniCommandCatalog.Format("cleartryon"))
        ]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "BetterGlamourManagementTitle",
            ["Description"] = "BetterGlamourManagementDescription",
            ["Feature.BetterGlamourManagement.Apply"] = "Feature.BetterGlamourManagement.Apply",
            ["Feature.BetterGlamourManagement.BindGearset"] = "Feature.BetterGlamourManagement.BindGearset",
            ["Feature.BetterGlamourManagement.BindGearsetHint"] = "Feature.BetterGlamourManagement.BindGearsetHint",
            ["Feature.BetterGlamourManagement.BoundGearsets"] = "Feature.BetterGlamourManagement.BoundGearsets",
            ["Feature.BetterGlamourManagement.Clear"] = "Feature.BetterGlamourManagement.Clear",
            ["Feature.BetterGlamourManagement.ClearGearsetBindings"] = "Feature.BetterGlamourManagement.ClearGearsetBindings",
            ["Feature.BetterGlamourManagement.ClearTryOn"] = "Feature.BetterGlamourManagement.ClearTryOn",
            ["Feature.BetterGlamourManagement.ClearTryOnCommandDescription"] = "Command.cleartryon.Description",
            ["Feature.BetterGlamourManagement.Column.Stain0"] = "Feature.BetterGlamourManagement.Column.Stain0",
            ["Feature.BetterGlamourManagement.Column.Stain1"] = "Feature.BetterGlamourManagement.Column.Stain1",
            ["Feature.BetterGlamourManagement.CommandDescription"] = "Feature.BetterGlamourManagement.CommandDescription",
            ["Feature.BetterGlamourManagement.Copied"] = "Feature.BetterGlamourManagement.Copied",
            ["Feature.BetterGlamourManagement.CreateFailed"] = "Feature.BetterGlamourManagement.CreateFailed",
            ["Feature.BetterGlamourManagement.DefaultName"] = "Feature.BetterGlamourManagement.DefaultName",
            ["Feature.BetterGlamourManagement.Delete"] = "Feature.BetterGlamourManagement.Delete",
            ["Feature.BetterGlamourManagement.Dye"] = "Feature.BetterGlamourManagement.Dye",
            ["Feature.BetterGlamourManagement.DyeCategory.Blue"] = "Feature.BetterGlamourManagement.DyeCategory.Blue",
            ["Feature.BetterGlamourManagement.DyeCategory.Brown"] = "Feature.BetterGlamourManagement.DyeCategory.Brown",
            ["Feature.BetterGlamourManagement.DyeCategory.Green"] = "Feature.BetterGlamourManagement.DyeCategory.Green",
            ["Feature.BetterGlamourManagement.DyeCategory.Neutral"] = "Feature.BetterGlamourManagement.DyeCategory.Neutral",
            ["Feature.BetterGlamourManagement.DyeCategory.Purple"] = "Feature.BetterGlamourManagement.DyeCategory.Purple",
            ["Feature.BetterGlamourManagement.DyeCategory.Red"] = "Feature.BetterGlamourManagement.DyeCategory.Red",
            ["Feature.BetterGlamourManagement.DyeCategory.Special"] = "Feature.BetterGlamourManagement.DyeCategory.Special",
            ["Feature.BetterGlamourManagement.DyeCategory.Yellow"] = "Feature.BetterGlamourManagement.DyeCategory.Yellow",
            ["Feature.BetterGlamourManagement.DyeClickHint"] = "Feature.BetterGlamourManagement.DyeClickHint",
            ["Feature.BetterGlamourManagement.DyePicker.Back"] = "Feature.BetterGlamourManagement.DyePicker.Back",
            ["Feature.BetterGlamourManagement.DyePicker.MissingDye"] = "Feature.BetterGlamourManagement.DyePicker.MissingDye",
            ["Feature.BetterGlamourManagement.DyePicker.Quantity"] = "Feature.BetterGlamourManagement.DyePicker.Quantity",
            ["Feature.BetterGlamourManagement.DyePicker.Redo"] = "Feature.BetterGlamourManagement.DyePicker.Redo",
            ["Feature.BetterGlamourManagement.DyePicker.Source"] = "Feature.BetterGlamourManagement.DyePicker.Source",
            ["Feature.BetterGlamourManagement.DyePicker.Undo"] = "Feature.BetterGlamourManagement.DyePicker.Undo",
            ["Feature.BetterGlamourManagement.Empty"] = "Feature.BetterGlamourManagement.Empty",
            ["Feature.BetterGlamourManagement.Export"] = "Feature.BetterGlamourManagement.Export",
            ["Feature.BetterGlamourManagement.HairColor"] = "Feature.BetterGlamourManagement.HairColor",
            ["Feature.BetterGlamourManagement.HairColors.Current"] = "Feature.BetterGlamourManagement.HairColors.Current",
            ["Feature.BetterGlamourManagement.HairColors.Original"] = "Feature.BetterGlamourManagement.HairColors.Original",
            ["Feature.BetterGlamourManagement.HairColors.Title"] = "Feature.BetterGlamourManagement.HairColors.Title",
            ["Feature.BetterGlamourManagement.HairPaletteUnavailable"] = "Feature.BetterGlamourManagement.HairPaletteUnavailable",
            ["Feature.BetterGlamourManagement.Hairstyle"] = "Feature.BetterGlamourManagement.Hairstyle",
            ["Feature.BetterGlamourManagement.HighlightColor"] = "Feature.BetterGlamourManagement.HighlightColor",
            ["Feature.BetterGlamourManagement.Highlights"] = "Feature.BetterGlamourManagement.Highlights",
            ["Feature.BetterGlamourManagement.ImportClipboard"] = "Feature.BetterGlamourManagement.ImportClipboard",
            ["Feature.BetterGlamourManagement.ImportFailed"] = "Feature.BetterGlamourManagement.ImportFailed",
            ["Feature.BetterGlamourManagement.Imported"] = "Feature.BetterGlamourManagement.Imported",
            ["Feature.BetterGlamourManagement.ItemClickHint"] = "Feature.BetterGlamourManagement.ItemClickHint",
            ["Feature.BetterGlamourManagement.ItemPicker.Ascending"] = "Feature.BetterGlamourManagement.ItemPicker.Ascending",
            ["Feature.BetterGlamourManagement.ItemPicker.Descending"] = "Feature.BetterGlamourManagement.ItemPicker.Descending",
            ["Feature.BetterGlamourManagement.ItemPicker.Level"] = "Feature.BetterGlamourManagement.ItemPicker.Level",
            ["Feature.BetterGlamourManagement.ItemPicker.NoResults"] = "Feature.BetterGlamourManagement.ItemPicker.NoResults",
            ["Feature.BetterGlamourManagement.ItemPicker.NoStoredResults"] = "Feature.BetterGlamourManagement.ItemPicker.NoStoredResults",
            ["Feature.BetterGlamourManagement.ItemPicker.Search"] = "Feature.BetterGlamourManagement.ItemPicker.Search",
            ["Feature.BetterGlamourManagement.ItemPicker.SortID"] = "Feature.BetterGlamourManagement.ItemPicker.SortID",
            ["Feature.BetterGlamourManagement.ItemPicker.SortLevel"] = "Feature.BetterGlamourManagement.ItemPicker.SortLevel",
            ["Feature.BetterGlamourManagement.ItemPicker.SortName"] = "Feature.BetterGlamourManagement.ItemPicker.SortName",
            ["Feature.BetterGlamourManagement.ItemPicker.Title"] = "Feature.BetterGlamourManagement.ItemPicker.Title",
            ["Feature.BetterGlamourManagement.LegacyGearsetHint"] = "Feature.BetterGlamourManagement.LegacyGearsetHint",
            ["Feature.BetterGlamourManagement.MainHand"] = "Feature.BetterGlamourManagement.MainHand",
            ["Feature.BetterGlamourManagement.NewPreset"] = "Feature.BetterGlamourManagement.NewPreset",
            ["Feature.BetterGlamourManagement.NoDye"] = "Feature.BetterGlamourManagement.NoDye",
            ["Feature.BetterGlamourManagement.NoInspectData"] = "Feature.BetterGlamourManagement.NoInspectData",
            ["Feature.BetterGlamourManagement.None"] = "Feature.BetterGlamourManagement.None",
            ["Feature.BetterGlamourManagement.OffHand"] = "Feature.BetterGlamourManagement.OffHand",
            ["Feature.BetterGlamourManagement.OpenSaved"] = "Feature.BetterGlamourManagement.OpenSaved",
            ["Feature.BetterGlamourManagement.Part.Body"] = "Feature.BetterGlamourManagement.Part.Body",
            ["Feature.BetterGlamourManagement.Part.Ears"] = "Feature.BetterGlamourManagement.Part.Ears",
            ["Feature.BetterGlamourManagement.Part.Feet"] = "Feature.BetterGlamourManagement.Part.Feet",
            ["Feature.BetterGlamourManagement.Part.Glasses"] = "Feature.BetterGlamourManagement.Part.Glasses",
            ["Feature.BetterGlamourManagement.Part.Hands"] = "Feature.BetterGlamourManagement.Part.Hands",
            ["Feature.BetterGlamourManagement.Part.Head"] = "Feature.BetterGlamourManagement.Part.Head",
            ["Feature.BetterGlamourManagement.Part.Legs"] = "Feature.BetterGlamourManagement.Part.Legs",
            ["Feature.BetterGlamourManagement.Part.Neck"] = "Feature.BetterGlamourManagement.Part.Neck",
            ["Feature.BetterGlamourManagement.Part.RingLeft"] = "Feature.BetterGlamourManagement.Part.RingLeft",
            ["Feature.BetterGlamourManagement.Part.RingRight"] = "Feature.BetterGlamourManagement.Part.RingRight",
            ["Feature.BetterGlamourManagement.Part.Wrists"] = "Feature.BetterGlamourManagement.Part.Wrists",
            ["Feature.BetterGlamourManagement.PlateExport"] = "Feature.BetterGlamourManagement.PlateExport",
            ["Feature.BetterGlamourManagement.PlateExport.Action"] = "Feature.BetterGlamourManagement.PlateExport.Action",
            ["Feature.BetterGlamourManagement.PlateExport.DefaultName"] = "Feature.BetterGlamourManagement.PlateExport.DefaultName",
            ["Feature.BetterGlamourManagement.PlateExport.Empty"] = "Feature.BetterGlamourManagement.PlateExport.Empty",
            ["Feature.BetterGlamourManagement.PlateExport.Failed"] = "Feature.BetterGlamourManagement.PlateExport.Failed",
            ["Feature.BetterGlamourManagement.PlateExport.Help"] = "Feature.BetterGlamourManagement.PlateExport.Help",
            ["Feature.BetterGlamourManagement.PlateExport.OpenManager"] = "Feature.BetterGlamourManagement.PlateExport.OpenManager",
            ["Feature.BetterGlamourManagement.PlateExport.Saved"] = "Feature.BetterGlamourManagement.PlateExport.Saved",
            ["Feature.BetterGlamourManagement.PlateImport.Action"] = "Feature.BetterGlamourManagement.PlateImport.Action",
            ["Feature.BetterGlamourManagement.PlateImport.Busy"] = "Feature.BetterGlamourManagement.PlateImport.Busy",
            ["Feature.BetterGlamourManagement.PlateImport.Completed"] = "Feature.BetterGlamourManagement.PlateImport.Completed",
            ["Feature.BetterGlamourManagement.PlateImport.Failed"] = "Feature.BetterGlamourManagement.PlateImport.Failed",
            ["Feature.BetterGlamourManagement.PlateImport.Help"] = "Feature.BetterGlamourManagement.PlateImport.Help",
            ["Feature.BetterGlamourManagement.PlateImport.Interrupted"] = "Feature.BetterGlamourManagement.PlateImport.Interrupted",
            ["Feature.BetterGlamourManagement.PlateImport.MissingDyes"] = "Feature.BetterGlamourManagement.PlateImport.MissingDyes",
            ["Feature.BetterGlamourManagement.PlateImport.NoPresets"] = "Feature.BetterGlamourManagement.PlateImport.NoPresets",
            ["Feature.BetterGlamourManagement.PlateImport.Preset"] = "Feature.BetterGlamourManagement.PlateImport.Preset",
            ["Feature.BetterGlamourManagement.PlateImport.SourceChanged"] = "Feature.BetterGlamourManagement.PlateImport.SourceChanged",
            ["Feature.BetterGlamourManagement.PlateImport.Timeout"] = "Feature.BetterGlamourManagement.PlateImport.Timeout",
            ["Feature.BetterGlamourManagement.PlateImport.Unavailable"] = "Feature.BetterGlamourManagement.PlateImport.Unavailable",
            ["Feature.BetterGlamourManagement.PresetLimitReached"] = "Feature.BetterGlamourManagement.PresetLimitReached",
            ["Feature.BetterGlamourManagement.PresetList.Bound"] = "Feature.BetterGlamourManagement.PresetList.Bound",
            ["Feature.BetterGlamourManagement.PresetList.Unbound"] = "Feature.BetterGlamourManagement.PresetList.Unbound",
            ["Feature.BetterGlamourManagement.PresetName"] = "Feature.BetterGlamourManagement.PresetName",
            ["Feature.BetterGlamourManagement.Preview.ButtonUnavailable"] = "Feature.BetterGlamourManagement.Preview.ButtonUnavailable",
            ["Feature.BetterGlamourManagement.Preview.CustomizationMismatch"] = "Feature.BetterGlamourManagement.Preview.CustomizationMismatch",
            ["Feature.BetterGlamourManagement.Preview.EnvironmentChanged"] = "Feature.BetterGlamourManagement.Preview.EnvironmentChanged",
            ["Feature.BetterGlamourManagement.Preview.EquipmentUnavailable"] = "Feature.BetterGlamourManagement.Preview.EquipmentUnavailable",
            ["Feature.BetterGlamourManagement.Preview.HairAppearanceUnavailable"] = "Feature.BetterGlamourManagement.Preview.HairAppearanceUnavailable",
            ["Feature.BetterGlamourManagement.Preview.Hint"] = "Feature.BetterGlamourManagement.Preview.Hint",
            ["Feature.BetterGlamourManagement.Preview.Loaded"] = "Feature.BetterGlamourManagement.Preview.Loaded",
            ["Feature.BetterGlamourManagement.Preview.Loading"] = "Feature.BetterGlamourManagement.Preview.Loading",
            ["Feature.BetterGlamourManagement.Preview.NativeClosed"] = "Feature.BetterGlamourManagement.Preview.NativeClosed",
            ["Feature.BetterGlamourManagement.Preview.OutfitMismatch"] = "Feature.BetterGlamourManagement.Preview.OutfitMismatch",
            ["Feature.BetterGlamourManagement.Preview.Ready"] = "Feature.BetterGlamourManagement.Preview.Ready",
            ["Feature.BetterGlamourManagement.Preview.SelectPreset"] = "Feature.BetterGlamourManagement.Preview.SelectPreset",
            ["Feature.BetterGlamourManagement.Save"] = "Feature.BetterGlamourManagement.Save",
            ["Feature.BetterGlamourManagement.Saved"] = "Feature.BetterGlamourManagement.Saved",
            ["Feature.BetterGlamourManagement.SharePreset"] = "Feature.BetterGlamourManagement.SharePreset",
            ["Feature.BetterGlamourManagement.TryOnAll"] = "Feature.BetterGlamourManagement.TryOnAll",
            ["Feature.BetterGlamourManagement.Unbound"] = "Feature.BetterGlamourManagement.Unbound",
            ["Feature.BetterGlamourManagement.UnknownGlasses"] = "Feature.BetterGlamourManagement.UnknownGlasses",
            ["Feature.BetterGlamourManagement.UnknownHairstyle"] = "Feature.BetterGlamourManagement.UnknownHairstyle",
            ["Feature.BetterGlamourManagement.UnknownItem"] = "Feature.BetterGlamourManagement.UnknownItem",
            ["Feature.BetterGlamourManagement.WindowTitle"] = "Feature.BetterGlamourManagement.WindowTitle"
        });

    private static readonly int[] InspectSlots = [0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12];

    private BetterGlamourActionsNativeUI? actionsNativeUI;
    private BetterGlamourManagerNativeUI? managerNativeUI;
    private BetterGlamourPortraitPreview? portraitPreview;
    private TaskHelper?                   taskHelper;
    private BetterGlamourPreset?          activeTryOnPreset;
    private BetterGlamourPreset?          boundTryOnPreset;
    private nint                          previewPlayerAddress;
    private bool                          pendingTryOnRestore;
    private int                           selectedPresetIndex    = -1;
    private int                           lastGearsetIndex       = int.MinValue;
    private ulong                         lastCharacterContentID;

    protected override void OnStart(FeatureLifetime activation)
    {
        Activation.Add(DisposePlateExport);
        EnablePlateExport();
        taskHelper = new()
        {
            TimeoutMS = 10_000
        };
        activation.Add(taskHelper);
        var tasks = taskHelper;
        Activation.OnStopping(tasks.Abort);
        Activation.Add(() =>
        {
            taskHelper = null;
        });
        actionsNativeUI = new(TryOnInspect, SaveInspect, ExportInspect, OpenManager, ClearTryOn);
        Activation.Add(DisposeActionsNativeUI);
        portraitPreview = new(Activation);
        var preview = portraitPreview;
        Activation.Add(() => portraitPreview = null);
        Activation.AddAsync(preview.DisposeAsync);
        Activation.OnStopping(preview.Disable);
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
        }, () => config.Presets, () => itemPreviewService.TryGetOriginalHairAppearance(out var customize) ? customize : null);
        portraitPreview.Enable();
        Activation.OnStopping(DisableRemote);
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
            CreatePreset,
            portraitPreview,
            itemPatchMap,
            collectionSourceService,
            isInGlamourDresser,
            isInArmoire,
            getInventoryRevision);
        Activation.Add(DisposeManagerNativeUI);
        Activation.OnStopping(() => managerNativeUI?.ClearPreviewTexture());
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 250))
        {
            throw new InvalidOperationException("Better glamour management update registration failed.");
        }

        Activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
        var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        Activation.OnStopping(addonEvents.Dispose);
        foreach (var addonName in new[] { CHARACTER_INSPECT_ADDON_NAME, GLAMOUR_PLATE_ADDON_NAME })
        {
            addonEvents.Register(AddonEvent.PostSetup, addonName, OnAttachedAddonEvent);
            addonEvents.Register(AddonEvent.PostShow, addonName, OnAttachedAddonEvent);
            addonEvents.Register(AddonEvent.PostRefresh, addonName, OnAttachedAddonEvent);
            addonEvents.Register(AddonEvent.PostRequestedUpdate, addonName, OnAttachedAddonEvent);
            addonEvents.Register(AddonEvent.PostUpdate, addonName, OnAttachedAddonEvent);
            addonEvents.Register(AddonEvent.PreHide, addonName, OnAttachedAddonEvent);
            addonEvents.Register(AddonEvent.PreFinalize, addonName, OnAttachedAddonEvent);
        }
        if (TryGetReadyAddon(CHARACTER_INSPECT_ADDON_NAME, out var inspectAddon))
            RefreshInspectActions(inspectAddon);
        if (TryGetReadyAddon(GLAMOUR_PLATE_ADDON_NAME, out var plateAddon))
            RefreshPlateExport(plateAddon, true);
    }

    protected override void OnStop()
    {
        ClearTryOn();
        boundTryOnPreset       = null;
        selectedPresetIndex    = -1;
        lastGearsetIndex       = int.MinValue;
        lastCharacterContentID = 0;
    }

    private void OnAttachedAddonEvent
    (
        AddonEvent type,
        AddonArgs  args
    )
    {
        var closing = type is AddonEvent.PreHide or AddonEvent.PreFinalize;
        if (args.AddonName == CHARACTER_INSPECT_ADDON_NAME)
        {
            if (closing)
                actionsNativeUI?.Close();
            else
                RefreshInspectActions((AtkUnitBase*)args.Addon.Address);
        }
        else if (closing)
        {
            AbortPlateImport();
            plateExportNativeUI?.Close();
        }
        else
        {
            RefreshPlateExport((AtkUnitBase*)args.Addon.Address, type != AddonEvent.PostUpdate);
        }
    }

    private void RefreshInspectActions(AtkUnitBase* inspectAddon)
    {
        if (inspectAddon == null || !inspectAddon->IsVisible || !inspectAddon->IsAddonAndNodesReady() ||
            inspectAddon->WindowNode == null || actionsNativeUI is null)
        {
            actionsNativeUI?.Close();
            return;
        }

        PositionAttachedNativeUI(inspectAddon, actionsNativeUI, 2f);
    }

    private static void PositionAttachedNativeUI
    (
        AtkUnitBase* sourceAddon,
        NativeAddon  nativeUI,
        float        gap
    )
    {
        if (!nativeUI.IsOpen)
        {
            nativeUI.Open();
        }

        AtkUnitBase* actionAddon = nativeUI;
        var stage = AtkStage.Instance();
        if (sourceAddon->WindowNode == null || actionAddon == null || actionAddon->WindowNode == null || stage == null)
        {
            return;
        }

        OmniNativeWindowAttachment.SyncBeside(nativeUI, actionAddon, sourceAddon, gap, 0f, preferRight: true);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        RemoteTick();
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            taskHelper?.Abort();
            activeTryOnPreset = null;
            boundTryOnPreset  = null;
            managerNativeUI?.UpdateTryOnButton();
            previewPlayerAddress   = nint.Zero;
            pendingTryOnRestore    = false;
            lastGearsetIndex       = int.MinValue;
            lastCharacterContentID = 0;
            return;
        }

        var contentID = DService.Instance().PlayerState.ContentId;
        if (contentID != lastCharacterContentID)
        {
            ClearTryOn();
            lastCharacterContentID = contentID;
            lastGearsetIndex       = int.MinValue;
            managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
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
            pendingTryOnRestore  = true;
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

    private static bool TryGetReadyAddon
    (
        string           name,
        out AtkUnitBase* addon
    )
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
                Title,
                string.Format(OmniLoc.Get(this, "Feature.BetterGlamourManagement.PresetLimitReached"), MAX_PRESET_COUNT),
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
            OmniLoc.Get(this, "Feature.BetterGlamourManagement.DefaultName"),
            DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        config.Presets.Add(preset);
        selectedPresetIndex = config.Presets.Count - 1;
        saveConfig();
        managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
        OmniNotifier.Popup(
            Title,
            string.Format(OmniLoc.Get(this, "Feature.BetterGlamourManagement.Saved"), preset.Name),
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
            preset.GlassesID         = uiState->Inspect.GlassesIds[0];
            preset.HairstyleID       = ResolveInspectHairstyleID(uiState->Inspect.CustomizeData);
            preset.HairColor         = uiState->Inspect.CustomizeData.HairColor;
            preset.HighlightColor    = uiState->Inspect.CustomizeData.HighlightsColor;
            preset.HighlightsEnabled = uiState->Inspect.CustomizeData.Highlights;
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
        pendingTryOnRestore  = true;
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
               itemPreviewService.IsHairAppearancePreviewApplied(preset.HairstyleID, preset.HairColor, preset.HighlightColor, preset.HighlightsEnabled);
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
            itemPreviewService.PreviewHairAppearance(preset.HairstyleID, preset.HairColor, preset.HighlightColor, preset.HighlightsEnabled);

            return true;
        });
    }

    internal void ClearTryOn()
    {
        taskHelper?.Abort();
        activeTryOnPreset    = null;
        boundTryOnPreset     = null;
        previewPlayerAddress = nint.Zero;
        pendingTryOnRestore  = false;
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
        plateExportNativeUI?.RefreshState();
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

    private void ChangePresetGearset
    (
        BetterGlamourPreset preset,
        int                 gearsetIndex
    )
    {
        var gearsetIndices = preset.GetGearsetIndices(DService.Instance().PlayerState.ContentId).ToList();
        if (gearsetIndex < 0)
            gearsetIndices.Clear();
        else if (!gearsetIndices.Remove(gearsetIndex))
            gearsetIndices.Add(gearsetIndex);
        ChangePresetGearsets(preset, gearsetIndices);
    }

    private void ChangePresetGearsets
    (
        BetterGlamourPreset preset,
        List<int>           gearsetIndices
    )
    {
        var contentID = DService.Instance().PlayerState.ContentId;
        if (!DService.Instance().ClientState.IsLoggedIn || contentID == 0)
            throw new InvalidOperationException("请登录角色后设置职业套装绑定。");
        if (gearsetIndices.Any(index => index is < 0 or >= 100 || !TryGetGearsetName(index, out _)))
            throw new InvalidOperationException("职业套装无效。");

        var previousBindings = new Dictionary<BetterGlamourPreset, List<int>?>();
        var previousGearsetIndex = preset.GearsetIndex;
        foreach (var other in config.Presets)
        {
            other.GearsetBindings.TryGetValue(contentID, out var previousIndices);
            if (ReferenceEquals(other, preset))
            {
                previousBindings.Add(other, previousIndices);
                if (gearsetIndices.Count == 0)
                    other.GearsetBindings.Remove(contentID);
                else
                    other.GearsetBindings[contentID] = gearsetIndices;
            }
            else if (previousIndices is not null && previousIndices.Any(gearsetIndices.Contains))
            {
                previousBindings.Add(other, previousIndices);
                var remainingIndices = previousIndices.Except(gearsetIndices).ToList();
                if (remainingIndices.Count == 0)
                    other.GearsetBindings.Remove(contentID);
                else
                    other.GearsetBindings[contentID] = remainingIndices;
            }
        }

        preset.GearsetIndex = -1;
        try
        {
            saveConfig();
        }
        catch
        {
            preset.GearsetIndex = previousGearsetIndex;
            foreach (var (other, previousIndices) in previousBindings)
            {
                if (previousIndices is null)
                    other.GearsetBindings.Remove(contentID);
                else
                    other.GearsetBindings[contentID] = previousIndices;
            }
            throw;
        }

        var gearsetModule = RaptureGearsetModule.Instance();
        if (gearsetModule != null)
        {
            var currentGearsetIndex = gearsetModule->CurrentGearsetIndex;
            if (previousBindings[preset]?.Contains(currentGearsetIndex) == true || gearsetIndices.Contains(currentGearsetIndex))
            {
                lastGearsetIndex = currentGearsetIndex;
                SynchronizeBoundTryOn(currentGearsetIndex);
            }
        }
    }

    private void SynchronizeBoundTryOn(int gearsetIndex)
    {
        var contentID = DService.Instance().PlayerState.ContentId;
        boundTryOnPreset = contentID == 0 ? null : config.Presets.Find(item => item.GetGearsetIndices(contentID).Contains(gearsetIndex));
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
        Title,
        OmniLoc.Get(this, "Feature.BetterGlamourManagement.NoInspectData"),
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

        public bool EnableGlamourPlateExport { get; set; } = true;
    }
}
