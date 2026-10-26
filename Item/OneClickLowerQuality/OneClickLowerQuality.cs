using TreeToolKit;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.OmenService;
using TreeTools.Threading;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Items;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class OneClickLowerQuality(OneClickLowerQuality.Settings config) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        RequiresPrivateProvider = true
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "OneClickLowerQualityTitle",
            ["Description"] = "OneClickLowerQualityDescription",
            ["Feature.OneClickLowerQuality.Add"] = "Feature.OneClickLowerQuality.Add",
            ["Feature.OneClickLowerQuality.ItemId"] = "Feature.OneClickLowerQuality.ItemId",
            ["Feature.OneClickLowerQuality.Menu"] = "Feature.OneClickLowerQuality.Menu"
        });

    public override bool HasSettings => true;

    private readonly Panel panel = new();
    private static readonly InventoryType[] InventoryContainers =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4
    ];

    private static readonly InventoryType[] EquipmentContainers =
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
        InventoryType.ArmorySoulCrystal
    ];

    private static readonly InventoryType[] RetainerContainers =
    [
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7
    ];

    private readonly Queue<InventorySlot> pendingSlots = new();
    private TaskHelper? taskHelper;
    private AddonEventRegistry? addonEvents;
    private LowerQualityMenuEntry? menuEntry;
    private InventorySlot currentSlot;
    private uint ownerAddonID;
    private ushort confirmationAddonID;
    private nint confirmationAddonAddress;
    private bool awaitingWindow;
    private bool checkboxSent;
    private bool confirmationClosed;

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => addonEvents?.Dispose());

        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = 15_000
        };
        activation.Add(taskHelper);
        activation.OnStopping(taskHelper.Abort);
        taskHelper.TaskTimeout += _ => StopPending();
        taskHelper.TaskGetException += _ => StopPending();
        addonEvents = new(DalamudServices.AddonLifecycle);
        addonEvents.Register(AddonEvent.PostSetup, "SelectYesno", OnConfirmation);
        addonEvents.Register(AddonEvent.PreFinalize, "SelectYesno", OnConfirmation);
        menuEntry = new(this);
        ContextMenuManager.Instance().Reg(menuEntry);
    }

    protected override void OnStop()
    {
        if (menuEntry != null)
            ContextMenuManager.Instance().Unreg(menuEntry);
        menuEntry = null;
        addonEvents = null;
        awaitingWindow = false;
        pendingSlots.Clear();
        taskHelper?.Abort();
        taskHelper = null;
    }

    protected override bool OnInterruptAutomation()
    {
        if (taskHelper?.IsBusy != true && pendingSlots.Count == 0)
        {
            return false;
        }

        pendingSlots.Clear();
        taskHelper?.Abort();
        awaitingWindow = false;
        return true;
    }

    public override bool DrawSettings() => panel.Draw(config);

    private void Begin(uint itemID, InventoryType sourceContainer)
    {
        if (taskHelper is null || taskHelper.IsBusy)
        {
            return;
        }

        var context = AgentInventoryContext.Instance();
        if (context == null || context->OwnerAddonId == 0)
        {
            return;
        }

        ownerAddonID = context->OwnerAddonId;
        pendingSlots.Clear();
        CollectSlots(sourceContainer, itemID);
        foreach (var configuredItemID in config.ItemIDs)
        {
            if (configuredItemID != itemID || Array.IndexOf(InventoryContainers, sourceContainer) < 0)
            {
                CollectSlots(InventoryType.Inventory1, configuredItemID);
            }
        }

        if (pendingSlots.Count == 0)
        {
            return;
        }

        EnqueueNext();
    }

    private void EnqueueNext()
    {
        if (taskHelper is null)
        {
            return;
        }

        if (!pendingSlots.TryDequeue(out currentSlot))
        {
            return;
        }

        confirmationAddonID = 0;
        confirmationAddonAddress = 0;
        awaitingWindow = false;
        checkboxSent = false;
        confirmationClosed = false;
        taskHelper.Enqueue(OpenCurrent);
        taskHelper.Enqueue(ConfirmCurrent);
        taskHelper.Enqueue(WaitCurrentApplied);
        taskHelper.Enqueue(EnqueueNext);
    }

    private bool OpenCurrent()
    {
        if (!CheckFrameworkThread())
        {
            return true;
        }

        if (!TryGetCurrentSlot(out var slot))
        {
            return true;
        }

        var context = AgentInventoryContext.Instance();
        var manager = RaptureAtkUnitManager.Instance();
        var owner = manager == null ? null : manager->GetAddonById((ushort)ownerAddonID);
        var confirmation = Addons.SelectYesno;
        if (context == null || !owner->IsAddonAndNodesReady() ||
            confirmation != null && confirmation->IsVisible)
        {
            return false;
        }

        awaitingWindow = true;
        context->LowerItemQuality(slot, currentSlot.Container, currentSlot.Slot, ownerAddonID);
        return true;
    }

    private void OnConfirmation(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (type == AddonEvent.PreFinalize)
        {
            if ((nint)addon == confirmationAddonAddress && addon->Id == confirmationAddonID)
            {
                confirmationClosed = true;
            }

            return;
        }

        if (!awaitingWindow || taskHelper?.IsBusy != true || !TryGetCurrentSlot(out _))
        {
            return;
        }

        confirmationAddonID = addon->Id;
        confirmationAddonAddress = (nint)addon;
        awaitingWindow = false;
    }

    private bool ConfirmCurrent()
    {
        if (!CheckFrameworkThread())
        {
            return true;
        }

        if (confirmationClosed)
        {
            StopPending();
            taskHelper?.Abort();
            return true;
        }

        if (!TryGetCurrentSlot(out _))
        {
            return true;
        }

        var addon = (AddonSelectYesno*)Addons.SelectYesno;
        // 只确认本次原生调用打开的窗口，等创建调用返回后再操作控件。
        if (addon == null || !addon->AtkUnitBase.IsAddonAndNodesReady() ||
            confirmationAddonID == 0 || addon->Id != confirmationAddonID ||
            (nint)addon != confirmationAddonAddress)
        {
            return false;
        }

        return SelectYesnoConfirmation.TryConfirmWithCheckbox(addon, ref checkboxSent);
    }

    private bool WaitCurrentApplied()
    {
        if (!CheckFrameworkThread())
        {
            return true;
        }

        var confirmation = Addons.SelectYesno;
        return (confirmation == null || !confirmation->IsVisible) && !TryGetCurrentSlot(out _);
    }

    private void StopPending()
    {
        awaitingWindow = false;
        pendingSlots.Clear();
    }

    private bool CheckFrameworkThread()
    {
        if (DalamudServices.Framework.IsInFrameworkUpdateThread)
        {
            return true;
        }

        StopPending();
        taskHelper?.Abort();
        return false;
    }

    private void CollectSlots(InventoryType sourceContainer, uint itemID)
    {
        var containers = Array.IndexOf(InventoryContainers, sourceContainer) >= 0
            ? InventoryContainers
            : Array.IndexOf(EquipmentContainers, sourceContainer) >= 0
                ? EquipmentContainers
                : Array.IndexOf(RetainerContainers, sourceContainer) >= 0
                    ? RetainerContainers
                    : null;

        if (containers is null)
        {
            CollectSlotsFromContainer(sourceContainer, itemID);
            return;
        }

        foreach (var container in containers)
        {
            CollectSlotsFromContainer(container, itemID);
        }
    }

    private void CollectSlotsFromContainer(InventoryType inventoryType, uint itemID)
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            return;
        }

        var container = manager->GetInventoryContainer(inventoryType);
        if (container == null || !container->IsLoaded)
        {
            return;
        }

        for (var index = 0; index < container->Size; index++)
        {
            var slot = container->GetInventorySlot(index);
            if (slot != null && ItemUtil.GetBaseId(slot->ItemId).ItemId == itemID && NeedsLowerQuality(slot))
            {
                pendingSlots.Enqueue(new(inventoryType, (ushort)index, itemID));
            }
        }
    }

    private bool TryGetCurrentSlot(out InventoryItem* slot) =>
        InventorySlotAccess.TryGet(currentSlot.Container, currentSlot.Slot, out slot) &&
        ItemUtil.GetBaseId(slot->ItemId).ItemId == currentSlot.ItemID &&
        NeedsLowerQuality(slot);

    private static bool NeedsLowerQuality(InventoryItem* slot) =>
        slot->IsHighQuality() || slot->IsCollectable();

    private sealed class LowerQualityMenuEntry(OneClickLowerQuality module) : ContextMenuEntry
    {
        public override string Identifier => nameof(OneClickLowerQuality);

        public override ContextMenuItem? Create(ContextMenuOpenedArgs args)
        {
            if (args.InventoryAgentContext == null ||
                args.TargetInventoryItem is not { } targetItem ||
                args.TargetInventoryType is not { } container ||
                !targetItem.IsHighQuality() && !targetItem.IsCollectable())
                return null;

            var itemID = args.TargetItemID;
            if (itemID == 0)
                return null;

            return new()
            {
                Name = OmniLoc.Get("Feature.OneClickLowerQuality.Menu"),
                Prefix = OmniContextMenu.Prefix,
                OnClicked = _ => module.Begin(itemID, container)
            };
        }
    }

    private readonly record struct InventorySlot(InventoryType Container, ushort Slot, uint ItemID);

    public sealed class Settings
    {
        public HashSet<uint> ItemIDs { get; set; } = [];
    }
}
