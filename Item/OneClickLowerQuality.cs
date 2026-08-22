using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
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
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;
using OmenTools.Threading.TaskHelper.Enums;

namespace OmniToolbox.TreePublic;

public sealed unsafe class OneClickLowerQuality(OneClickLowerQualityConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("OneClickLowerQualityTitle"),
        Description = OmniLoc.Get("OneClickLowerQualityDescription"),
        Category = ModuleCategory.Item,
        RequiresPrivateProvider = true
    };

    public override bool HasSettings => true;

    public override bool DrawSettings() => OneClickLowerQualityPanel.Draw(config);

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
    private bool skipCurrent;

    protected override void OnEnable()
    {
        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = 2_000
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
        skipCurrent = false;
    }

    protected override bool OnInterruptAutomation()
    {
        if (taskHelper?.IsBusy != true && pendingSlots.Count == 0)
        {
            return false;
        }

        pendingSlots.Clear();
        taskHelper?.Abort();
        skipCurrent = false;
        return true;
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
        if (taskHelper is null || taskHelper.IsBusy)
        {
            return;
        }

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

        skipCurrent = false;
        EnqueueCurrent(OpenCurrent);
        taskHelper.DelayNext(300);
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
        => skipCurrent;

    private void SkipCurrent() => skipCurrent = true;

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
        return slot != null &&
               ItemUtil.GetBaseId(slot->ItemId).ItemId == currentSlot.ItemID &&
               NeedsLowerQuality(slot);
    }

    private static bool NeedsLowerQuality(InventoryItem* slot) =>
        slot->IsHighQuality() || slot->IsCollectable();

    private readonly record struct InventorySlot(InventoryType Container, ushort Slot, uint ItemID);
}

[Serializable]
public sealed class OneClickLowerQualityConfig
{
    public HashSet<uint> ItemIDs { get; set; } = [];
}

internal static class OneClickLowerQualityPanel
{
    private static int itemIDInput;

    public static bool Draw(OneClickLowerQualityConfig config)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.OneClickLowerQuality.ItemId"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(110f));
        if (OmniControls.InputInt("##oneClickLowerQualityItemId", ref itemIDInput) && itemIDInput < 0)
        {
            itemIDInput = 0;
        }

        var itemID = ItemUtil.GetBaseId((uint)Math.Max(0, itemIDInput)).ItemId;
        ImGui.SameLine();
        var changed = OmniControls.SmallButton(
                          $"{OmniLoc.Get("Feature.OneClickLowerQuality.Add")}##oneClickLowerQualityAdd",
                          false) &&
                      itemID > 0 &&
                      config.ItemIDs.Add(itemID);

        ImGui.Dummy(new(0f, OmniTheme.Scale(6f)));
        var itemIDs = new uint[config.ItemIDs.Count];
        config.ItemIDs.CopyTo(itemIDs);
        Array.Sort(itemIDs);
        var rows = new ItemSelectionTableRow[itemIDs.Length];
        for (var index = 0; index < itemIDs.Length; index++)
        {
            rows[index] = new(itemIDs[index], true, true);
        }

        var change = ItemSelectionTable.Draw("oneClickLowerQualityItems", rows, showEnabledColumn: false);
        if (change.Action == ItemSelectionTableAction.Delete)
        {
            changed |= config.ItemIDs.Remove(change.ItemID);
        }

        return changed;
    }
}
