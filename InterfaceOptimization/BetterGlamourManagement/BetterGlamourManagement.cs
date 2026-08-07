using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
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
        Category = ModuleCategory.InterfaceOptimization,
        RequiresPrivateProvider = true
    };

    private const string CharacterAddonName = "Character";
    private const string CharacterInspectAddonName = "CharacterInspect";
    private const string TryOnAddonName = "Tryon";
    private const float ScreenPadding = 8f;

    private static readonly int[] InspectSlots = [0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12];

    private AddonEventRegistry? addonEvents;
    private BetterGlamourActionsNativeUI? actionsNativeUI;
    private TextButtonNode? characterButton;
    private TaskHelper? taskHelper;
    private bool managerOpen;
    private int selectedPresetIndex = -1;
    private int lastGearsetIndex = int.MinValue;

    protected override void OnEnable()
    {
        try
        {
            taskHelper = new() { TimeoutMS = 10_000 };
            actionsNativeUI = new(TryOnInspect, SaveInspect, ExportInspect, ClearTryOn);
            var events = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            events.Register(AddonEvent.PostDraw, CharacterAddonName, OnCharacterAddon);
            events.Register(AddonEvent.PreFinalize, CharacterAddonName, OnCharacterAddon);
            addonEvents = events;

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
        addonEvents?.Dispose();
        addonEvents = null;
        characterButton?.Dispose();
        characterButton = null;
        taskHelper?.Abort();
        taskHelper?.Dispose();
        taskHelper = null;
        DisposeActionsNativeUI();
        managerOpen = false;
        selectedPresetIndex = -1;
        lastGearsetIndex = int.MinValue;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var gearsetModule = RaptureGearsetModule.Instance();
        if (!DService.Instance().ClientState.IsLoggedIn || gearsetModule == null)
        {
            lastGearsetIndex = int.MinValue;
            return;
        }

        var currentGearsetIndex = gearsetModule->CurrentGearsetIndex;
        if (lastGearsetIndex == int.MinValue)
        {
            lastGearsetIndex = currentGearsetIndex;
            return;
        }

        if (lastGearsetIndex == currentGearsetIndex)
        {
            return;
        }

        lastGearsetIndex = currentGearsetIndex;
        var preset = config.Presets.Find(item => item.GearsetIndex == currentGearsetIndex);
        if (preset is not null)
        {
            StartTryOn(preset);
        }
    }

    private void OnCharacterAddon(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            characterButton?.Dispose();
            characterButton = null;
            return;
        }

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || addon->RootNode == null || !addon->IsAddonAndNodesReady())
        {
            return;
        }

        if (characterButton is null)
        {
            characterButton = new()
            {
                String = OmniLoc.Get("Feature.BetterGlamourManagement.OpenSaved"),
                Size = new(92f, 26f),
                OnClick = () => managerOpen = true
            };
            characterButton.AttachNode(addon->RootNode);
        }

        characterButton.Position = new(18f, MathF.Max(8f, addon->RootNode->Height - 40f));
    }

    private void Draw()
    {
        DrawInspectActions();
        DrawManagerWindow();
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
        }

        return preset.Items.Count == 0 ? null : preset;
    }

    private void StartTryOn(BetterGlamourPreset preset)
    {
        if (taskHelper is null || taskHelper.IsBusy || AgentTryon.Instance() == null)
        {
            return;
        }

        CloseTryOnAddon();
        var items = preset.Items
            .Where(static item => item.ItemID != 0)
            .OrderBy(static item => item.Slot)
            .ToArray();
        taskHelper.Enqueue(() =>
        {
            var agent = AgentTryon.Instance();
            if (agent == null)
            {
                return false;
            }

            agent->SaveDeleteOutfit = true;
            return true;
        });

        foreach (var item in items)
        {
            taskHelper.Enqueue(() => AgentTryon.TryOn(0, item.ItemID, item.Stain0, item.Stain1));
            taskHelper.DelayNext(50);
        }

        taskHelper.Enqueue(() =>
        {
            var agent = AgentTryon.Instance();
            if (agent == null)
            {
                return false;
            }

            var character = agent->CharaView.GetCharacter();
            if (character == null)
            {
                return false;
            }

            character->DrawData.SetGlasses(0, preset.GlassesID);
            return true;
        });
    }

    private void ClearTryOn()
    {
        taskHelper?.Abort();
        CloseTryOnAddon();
        itemPreviewService.ClearPreviewResidue();
    }

    private static void CloseTryOnAddon()
    {
        if (AddonHelper.TryGetByName(TryOnAddonName, out AtkUnitBase* addon) && addon != null)
        {
            addon->Close(true);
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
