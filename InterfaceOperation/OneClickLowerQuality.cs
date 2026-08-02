using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;
using OmenTools.Threading.TaskHelper.Enums;

namespace OmniToolbox.TreePublic;

public sealed unsafe class OneClickLowerQuality : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("OneClickLowerQualityTitle"),
        Description = OmniLoc.Get("OneClickLowerQualityDescription"),
        Category = ModuleCategory.InterfaceOperation
    };

    private const int OperationTimeoutMs = 2_000;
    private const int MenuDelayMs = 100;
    private const int AppliedDelayMs = 300;
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
    private AddonEventRegistry? addonEvents;
    private TaskHelper? taskHelper;
    private InventorySlot currentSlot;
    private uint targetItemID;
    private bool skipCurrent;

    protected override void OnEnable()
    {
        taskHelper = new()
        {
            RetryIntervalMS = MenuDelayMs,
            TimeoutMS = OperationTimeoutMs
        };
        var events = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        events.Register(AddonEvent.PostSetup, "SelectYesno", OnSelectYesnoSetup);
        addonEvents = events;
        DService.Instance().ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    protected override void OnDisable()
    {
        DService.Instance().ContextMenu.OnMenuOpened -= OnMenuOpened;
        addonEvents?.Dispose();
        addonEvents = null;
        pendingSlots.Clear();
        taskHelper?.Dispose();
        taskHelper = null;
        targetItemID = 0;
        skipCurrent = false;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetInventory targetInventory ||
            !targetInventory.TargetItem.HasValue)
        {
            return;
        }

        var targetItem = targetInventory.TargetItem.Value;
        if (!targetItem.IsHq && !targetItem.IsCollectable)
        {
            return;
        }

        var itemID = ItemUtil.GetBaseId(ContextMenuItemManager.Instance().CurrentItemID).ItemId;
        if (itemID == 0)
        {
            return;
        }

        args.AddMenuItem(new MenuItem
        {
            Name = new SeStringBuilder()
                .AddUiForeground(10)
                .Append(((char)SeIconChar.BoxedLetterT).ToString())
                .AddUiForegroundOff()
                .Append($" {OmniLoc.Get("Feature.OneClickLowerQuality.Menu")}")
                .Build(),
            OnClicked = _ => Begin(itemID, (InventoryType)targetItem.ContainerType),
            PrefixChar = 'O',
            PrefixColor = 10
        });
    }

    private void Begin(uint itemID, InventoryType sourceContainer)
    {
        if (taskHelper is null || taskHelper.IsBusy || IsAbortKeyDown())
        {
            return;
        }

        targetItemID = itemID;
        pendingSlots.Clear();
        CollectSlots(sourceContainer);
        if (pendingSlots.Count == 0)
        {
            targetItemID = 0;
            return;
        }

        EnqueueNext();
    }

    private void EnqueueNext()
    {
        if (taskHelper is null || ShouldCancel())
        {
            return;
        }

        if (!pendingSlots.TryDequeue(out currentSlot))
        {
            targetItemID = 0;
            return;
        }

        skipCurrent = false;
        EnqueueCurrent(OpenCurrent);
        taskHelper.DelayNext(AppliedDelayMs);
        EnqueueCurrent(WaitCurrentApplied);
        taskHelper.Enqueue(EnqueueNext);
    }

    private void EnqueueCurrent(Func<bool> action) => taskHelper!.Enqueue(
        action,
        timeoutBehaviour: TaskAbortBehaviour.AbortCurrent,
        exceptionBehaviour: TaskAbortBehaviour.AbortCurrent,
        timeoutAction: SkipCurrent,
        exceptionAction: SkipCurrent);

    private bool OpenCurrent()
    {
        if (ShouldSkipCurrent())
        {
            return true;
        }

        if (!TryGetCurrentSlot(out var slot))
        {
            skipCurrent = true;
            return true;
        }

        var context = AgentInventoryContext.Instance();
        var inventory = AgentInventory.Instance();
        if (context == null || inventory == null)
        {
            return false;
        }

        context->LowerItemQuality(slot, currentSlot.Container, currentSlot.Slot, inventory->GetActiveAddonID());
        return true;
    }

    private void OnSelectYesnoSetup(AddonEvent _, AddonArgs args)
    {
        if (taskHelper is null ||
            !taskHelper.IsBusy ||
            ShouldSkipCurrent() ||
            !TryGetCurrentSlot(out var _))
        {
            return;
        }

        var addon = (AddonSelectYesno*)args.Addon.Address;
        var confirm = addon->ConfirmCheckBox;
        if (confirm == null ||
            confirm->AtkResNode == null ||
            !confirm->AtkResNode->NodeFlags.HasFlag(NodeFlags.Visible))
        {
            return;
        }

        if (!confirm->IsChecked)
        {
            confirm->Click(3);
        }

        var yesButton = addon->YesButton;
        if (yesButton != null && !yesButton->IsEnabled && yesButton->AtkComponentBase.OwnerNode != null)
        {
            var flags = (ushort*)&yesButton->AtkComponentBase.OwnerNode->AtkResNode.NodeFlags;
            *flags |= 1 << 5;
        }

        addon->AtkUnitBase.FireCallbackInt(0);
    }

    private bool WaitCurrentApplied()
    {
        if (ShouldSkipCurrent())
        {
            return true;
        }

        return !TryGetCurrentSlot(out _);
    }

    private bool ShouldSkipCurrent()
    {
        if (skipCurrent)
        {
            return true;
        }

        return ShouldCancel();
    }

    private bool ShouldCancel()
    {
        if (!IsAbortKeyDown())
        {
            return false;
        }

        pendingSlots.Clear();
        targetItemID = 0;
        taskHelper?.Abort();
        return true;
    }

    private static bool IsAbortKeyDown() => DService.Instance().KeyState[VirtualKey.SHIFT];

    private void SkipCurrent() => skipCurrent = true;

    private void CollectSlots(InventoryType sourceContainer)
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
            CollectSlotsFromContainer(sourceContainer);
            return;
        }

        foreach (var container in containers)
        {
            CollectSlotsFromContainer(container);
        }
    }

    private void CollectSlotsFromContainer(InventoryType inventoryType)
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
            if (slot != null && ItemUtil.GetBaseId(slot->ItemId).ItemId == targetItemID && NeedsLowerQuality(slot))
            {
                pendingSlots.Enqueue(new(inventoryType, (ushort)index));
            }
        }
    }

    private bool TryGetCurrentSlot(out InventoryItem* slot)
    {
        slot = null;
        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            return false;
        }

        var container = manager->GetInventoryContainer(currentSlot.Container);
        if (container == null || !container->IsLoaded || currentSlot.Slot >= container->Size)
        {
            return false;
        }

        slot = container->GetInventorySlot(currentSlot.Slot);
        return slot != null && ItemUtil.GetBaseId(slot->ItemId).ItemId == targetItemID && NeedsLowerQuality(slot);
    }

    private static bool NeedsLowerQuality(InventoryItem* slot) =>
        slot->IsHighQuality() || slot->IsCollectable();

    private readonly record struct InventorySlot(InventoryType Container, ushort Slot);
}
