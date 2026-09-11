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
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

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
    private InventorySlot currentSlot;
    private uint ownerAddonID;
    private ushort confirmationAddonID;
    private nint confirmationAddonAddress;
    private bool awaitingWindow;
    private bool checkboxSent;
    private bool confirmationClosed;

    public override bool HasSettings => true;

    public override bool DrawSettings() => OneClickLowerQualityPanel.Draw(config);

    protected override void OnEnable()
    {
        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = 15_000,
            TimeoutAction = StopPending,
            ExceptionAction = StopPending
        };
        addonEvents = new(DalamudServices.AddonLifecycle);
        addonEvents.Register(AddonEvent.PostSetup, "SelectYesno", OnConfirmation);
        addonEvents.Register(AddonEvent.PreFinalize, "SelectYesno", OnConfirmation);
        DService.Instance().ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    protected override void OnDisable()
    {
        DService.Instance().ContextMenu.OnMenuOpened -= OnMenuOpened;
        addonEvents?.Dispose();
        addonEvents = null;
        awaitingWindow = false;
        pendingSlots.Clear();
        taskHelper?.Abort();
        taskHelper?.Dispose();
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

        var itemID = ItemUtil.GetBaseId(targetItem.ItemId).ItemId;
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
        if (context == null || owner == null || !owner->IsAddonAndNodesReady() ||
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

        var confirm = addon->ConfirmCheckBox;
        if (confirm != null && confirm->AtkResNode != null &&
            confirm->AtkResNode->IsVisible() && !confirm->IsChecked)
        {
            if (checkboxSent)
            {
                return false;
            }

            if (confirm->OwnerNode == null)
            {
                return false;
            }

            checkboxSent = true;
            // 窗口点击通知不改变控件状态，先通过原生接口勾选，再通知窗口。
            confirm->SetChecked(true);
            addon->AtkUnitBase.ClickComponent(confirm->OwnerNode, 3, AtkEventType.ButtonClick);
            return false;
        }

        if (addon->YesButton == null || !addon->YesButton->IsEnabled)
        {
            return false;
        }

        return AddonSelectYesnoEvent.ClickYes();
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
        DalamudServices.PluginLog.Warning(
            "[OneClickLowerQuality] 批次中止: ItemID={ItemID}, Container={Container}, Slot={Slot}",
            currentSlot.ItemID, currentSlot.Container, currentSlot.Slot);
    }

    private bool CheckFrameworkThread()
    {
        if (DalamudServices.Framework.IsInFrameworkUpdateThread)
        {
            return true;
        }

        DalamudServices.PluginLog.Error("[OneClickLowerQuality] 非 Framework 线程，停止原生操作");
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
