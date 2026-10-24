using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using OmenTools.Extensions;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterGlamourManagement
{
    private BetterGlamourPlateExportNativeUI? plateExportNativeUI;

    public override bool DrawSettings()
    {
        var enabled = config.EnableGlamourPlateExport;
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.BetterGlamourManagement.PlateExport"),
                ref enabled))
        {
            config.EnableGlamourPlateExport = enabled;
            saveConfig();
        }

        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X, OmniTheme.Scale(4f));
        OmniControls.HelpIcon(OmniLoc.Get("Feature.BetterGlamourManagement.PlateExport.Help"));
        return false;
    }

    private void EnablePlateExport() =>
        plateExportNativeUI = new(ExportCurrentGlamourPlate, OpenManager, CanExportCurrentGlamourPlate);

    private void DisposePlateExport()
    {
        var ui = plateExportNativeUI;
        plateExportNativeUI = null;
        if (ui is null)
        {
            return;
        }

        ui.Close();
        ui.Dispose();
    }

    private void DrawPlateExport()
    {
        if (!config.EnableGlamourPlateExport ||
            plateExportNativeUI is null ||
            !TryGetReadyAddon(GLAMOUR_PLATE_ADDON_NAME, out var plateAddon) ||
            !plateAddon->IsVisible)
        {
            plateExportNativeUI?.Close();
            return;
        }

        PositionPlateExportNativeUI(plateAddon, plateExportNativeUI);
    }

    private static void PositionPlateExportNativeUI(
        AtkUnitBase* plateAddon,
        BetterGlamourPlateExportNativeUI nativeUI)
    {
        var plateWindow = plateAddon->WindowNode;
        var stage = AtkStage.Instance();
        if (plateWindow == null || stage == null)
        {
            nativeUI.Close();
            return;
        }

        if (!nativeUI.IsOpen)
        {
            nativeUI.Open();
        }

        AtkUnitBase* nativeAddon = nativeUI;
        if (nativeAddon == null || nativeAddon->WindowNode == null)
        {
            return;
        }

        var plateState = plateWindow->GetNodeState();
        var plateScale = plateWindow->GetScale().X;
        if (MathF.Abs(nativeAddon->WindowNode->GetScale().X - plateScale) > 0.001f)
        {
            nativeAddon->SetScale(plateScale / AtkUnitBase.GetGlobalUIScale(), true);
        }

        var nativeState = nativeAddon->WindowNode->GetNodeState();
        var display = new Vector2(stage->ScreenSize.Width, stage->ScreenSize.Height);
        var x = plateState.TopLeft.X + plateState.Width + SCREEN_PADDING;
        if (x + nativeState.Width > display.X - SCREEN_PADDING)
        {
            x = plateState.TopLeft.X - nativeState.Width - SCREEN_PADDING;
        }

        nativeUI.SetWindowPosition(new(
            Math.Clamp(x, SCREEN_PADDING, MathF.Max(SCREEN_PADDING, display.X - nativeState.Width - SCREEN_PADDING)),
            Math.Clamp(
                plateState.TopLeft.Y,
                SCREEN_PADDING,
                MathF.Max(SCREEN_PADDING, display.Y - nativeState.Height - SCREEN_PADDING))));
    }

    private static bool CanExportCurrentGlamourPlate()
    {
        var agent = AgentMiragePrismMiragePlate.Instance();
        if (agent == null ||
            !agent->IsAgentActive() ||
            agent->Data == null ||
            agent->Data->SelectedMiragePlateIndex >= MIRAGE_PLATE_COUNT)
        {
            return false;
        }

        var plate = agent->Data->GlamourPlates[(int)agent->Data->SelectedMiragePlateIndex];
        for (var index = 0; index < plate.Items.Length; index++)
        {
            if (plate.Items[index].ItemId != 0)
            {
                return true;
            }
        }

        return false;
    }

    private void ExportCurrentGlamourPlate()
    {
        if (config.Presets.Count >= MAX_PRESET_COUNT)
        {
            OmniNotifier.Popup(
                Info.Title,
                string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.PresetLimitReached"), MAX_PRESET_COUNT),
                NotificationType.Warning);
            return;
        }

        if (!TryCaptureCurrentGlamourPlate(out var preset))
        {
            OmniNotifier.Popup(
                Info.Title,
                OmniLoc.Get("Feature.BetterGlamourManagement.PlateExport.Empty"),
                NotificationType.Warning);
            return;
        }

        var previousSelection = selectedPresetIndex;
        config.Presets.Add(preset);
        selectedPresetIndex = config.Presets.Count - 1;
        try
        {
            saveConfig();
        }
        catch (Exception error)
        {
            config.Presets.Remove(preset);
            selectedPresetIndex = previousSelection;
            OmniNotifier.Popup(
                Info.Title,
                string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.PlateExport.Failed"), error.Message),
                NotificationType.Error);
            return;
        }

        managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
        QueuePortraitPreview(preset);
        OmniNotifier.Popup(
            Info.Title,
            string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.PlateExport.Saved"), preset.Name),
            NotificationType.Success);
    }

    private static bool TryCaptureCurrentGlamourPlate(out BetterGlamourPreset preset)
    {
        preset = null!;
        var agent = AgentMiragePrismMiragePlate.Instance();
        if (agent == null ||
            !agent->IsAgentActive() ||
            agent->Data == null ||
            agent->Data->SelectedMiragePlateIndex >= MIRAGE_PLATE_COUNT)
        {
            return false;
        }

        var plateIndex = agent->Data->SelectedMiragePlateIndex;
        var plate = agent->Data->GlamourPlates[(int)plateIndex];
        preset = new()
        {
            Name = string.Format(
                OmniLoc.Get("Feature.BetterGlamourManagement.PlateExport.DefaultName"),
                plateIndex + 1)
        };
        for (var index = 0; index < plate.Items.Length; index++)
        {
            var sourceItem = plate.Items[index];
            var itemID = sourceItem.ItemId;
            if (itemID == 0)
            {
                continue;
            }

            preset.Items.Add(new()
            {
                Slot = InspectSlots[index],
                ItemID = itemID,
                Stain0 = sourceItem.StainIds[0],
                Stain1 = sourceItem.StainIds[1]
            });
        }

        return preset.Items.Count > 0;
    }

    internal sealed class BetterGlamourPlateExportNativeUI : NativeAddon
    {
        private readonly Action onExport;
        private readonly Action onOpenManager;
        private readonly Func<bool> canExport;
        private TextButtonNode? exportButton;
        private TextButtonNode? openManagerButton;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public BetterGlamourPlateExportNativeUI(
            Action onExport,
            Action onOpenManager,
            Func<bool> canExport)
        {
            InternalName = "OmniBetterGlamourPlateExport";
            Title = OmniLoc.Get("Feature.BetterGlamourManagement.PlateExport");
            Subtitle = string.Empty;
            Size = new(210f, 132f);
            ContentPadding = new(8f, 8f);
            RememberClosePosition = false;
            CreateWindowNode = static () => new WindowNode { ShowCloseButton = false };
            EnableContextMenu = false;
            RespectCloseAll = false;
            DisableClose = true;
            this.onExport = onExport;
            this.onOpenManager = onOpenManager;
            this.canExport = canExport;
        }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
        {
            exportButton = new()
            {
                String = OmniLoc.Get("Feature.BetterGlamourManagement.PlateExport.Action"),
                OnClick = onExport
            };
            exportButton.AttachNode(this);
            openManagerButton = new()
            {
                String = OmniLoc.Get("Feature.BetterGlamourManagement.PlateExport.OpenManager"),
                OnClick = onOpenManager
            };
            openManagerButton.AttachNode(this);
            ResizeContent();
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            if (exportButton is not null)
            {
                exportButton.IsEnabled = canExport();
            }

            ResizeContent();
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            exportButton = null;
            openManagerButton = null;
        }

        private void ResizeContent()
        {
            if (exportButton is null || openManagerButton is null)
            {
                return;
            }

            const float BUTTON_HEIGHT = 30f;
            const float BUTTON_GAP = 6f;
            exportButton.Position = ContentStartPosition;
            exportButton.Size = new(ContentSize.X, BUTTON_HEIGHT);
            openManagerButton.Position = new(
                ContentStartPosition.X,
                ContentStartPosition.Y + BUTTON_HEIGHT + BUTTON_GAP);
            openManagerButton.Size = new(ContentSize.X, BUTTON_HEIGHT);
        }
    }

    #region 常量

    private const string GLAMOUR_PLATE_ADDON_NAME = "MiragePrismMiragePlate";

    private const uint MIRAGE_PLATE_COUNT = 20;

    #endregion
}
