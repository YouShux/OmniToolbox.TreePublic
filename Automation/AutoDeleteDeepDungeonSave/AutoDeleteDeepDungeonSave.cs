using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using TreeToolKit;
using TreeTools.Threading;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class AutoDeleteDeepDungeonSave : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Automation
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "AutoDeleteDeepDungeonSaveTitle",
            ["Description"] = "AutoDeleteDeepDungeonSaveDescription",
            ["FloorRange"] = "Feature.AutoDeleteDeepDungeonSave.FloorRange"
        });

    private Settings    config;
    private TaskHelper? tasks;
    private ushort      handledAddonID;
    private bool        checkboxSent;
    private bool        deletionConfirmed;
    private OmniToolbox.Game.DeepDungeonSaveLease? saveLease;

    public AutoDeleteDeepDungeonSave(Settings config) => this.config = config;

    protected override void OnStart(FeatureLifetime activation)
    {
        tasks = new() { RetryIntervalMS = 100, TimeoutMS = 10_000 };
        activation.Add(tasks);
        tasks.LeaveBusyState += () =>
        {
            deletionConfirmed = false;
            saveLease?.Dispose();
            saveLease = null;
        };
        var events = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        activation.OnStopping(events.Dispose);
        activation.OnStopping(StopDeletion);
        events.Register(AddonEvent.PostSetup, SAVE_ADDON_NAME, OnSaveDataEvent);
        events.Register(AddonEvent.PostRefresh, SAVE_ADDON_NAME, OnSaveDataEvent);
        events.Register(AddonEvent.PostUpdate, SAVE_ADDON_NAME, OnSaveDataEvent);
        events.Register(AddonEvent.PreFinalize, SAVE_ADDON_NAME, OnSaveDataEvent);
        events.Register(AddonEvent.PreFinalize, "SelectYesno", OnConfirmationClosed);
        QueueDeletion();
    }

    protected override void OnStop()
    {
        tasks = null;
        handledAddonID = 0;
        checkboxSent = false;
        deletionConfirmed = false;
    }

    protected override bool OnInterruptAutomation()
    {
        if (tasks is not { IsBusy: true })
            return false;

        StopDeletion();
        return true;
    }

    private void OnSaveDataEvent(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            if (!deletionConfirmed)
                StopDeletion();
            handledAddonID = 0;
            return;
        }

        QueueDeletion();
    }

    private void OnConfirmationClosed(AddonEvent eventType, AddonArgs args)
    {
        if (!deletionConfirmed)
            StopDeletion();
    }

    private void QueueDeletion()
    {
        if (OmniToolbox.Game.DeepDungeonSaveLease.IsHeld)
            return;

        if (tasks is null || tasks.IsBusy || handledAddonID != 0 || config.AllowedFloorSets.Count == 0 ||
            ((AtkUnitBase*)AddonHelper.GetByName<AddonSelectYesno>("SelectYesno"))->IsAddonAndNodesReady() ||
            !TryGetSaveData(out var addon, out var dungeonID) ||
            addon->GetComponentListById(SAVE_LIST_NODE_ID)->IsUpdatePending)
            return;

        handledAddonID = addon->Id;
        var ownerContentID = DalamudServices.PlayerState.ContentId;
        if (ownerContentID == 0)
            return;

        saveLease = OmniToolbox.Game.DeepDungeonSaveLease.TryAcquire();
        if (saveLease is null)
            return;

        var context = new DeletionContext(ownerContentID, dungeonID, addon->Id);
        for (var index = 0; index < SAVE_SLOT_COUNT; index++)
        {
            if (!TryReadSlot(addon, dungeonID, index, out var slot) || !IsAllowed(dungeonID, slot.Floor))
                continue;

            tasks.Enqueue(() => SelectSave(context, slot), name: "Select deep dungeon save");
            tasks.Enqueue(() => ConfirmDeletion(context, slot), name: "Confirm deep dungeon save deletion");
            tasks.Enqueue(() => WaitForDeletion(context, slot), name: "Wait for deep dungeon save deletion");
        }
        if (!tasks.IsBusy)
        {
            saveLease.Dispose();
            saveLease = null;
        }
    }

    private bool SelectSave(DeletionContext context, SaveSlot slot)
    {
        checkboxSent = false;
        deletionConfirmed = false;
        if (!TryGetCurrentSave(context, slot, out var addon) ||
            ((AtkUnitBase*)AddonHelper.GetByName<AddonSelectYesno>("SelectYesno"))->IsAddonAndNodesReady())
        {
            StopDeletion();
            return true;
        }

        var list = addon->GetComponentListById(SAVE_LIST_NODE_ID);
        if (list == null || list->IsUpdatePending)
            return false;
        if (list->GetItemDisabledState(slot.Index))
        {
            StopDeletion();
            return true;
        }

        list->SelectItem(slot.Index);
        addon->Callback(slot.Index);
        return true;
    }

    private bool ConfirmDeletion(DeletionContext context, SaveSlot slot)
    {
        if (!TryGetCurrentSave(context, slot, out var addon))
        {
            StopDeletion();
            return true;
        }

        var confirmation = AddonHelper.GetByName<AddonSelectYesno>("SelectYesno");
        if (!((AtkUnitBase*)confirmation)->IsAddonAndNodesReady())
            return false;

        var list = addon->GetComponentListById(SAVE_LIST_NODE_ID);
        if (list != null && list->IsUpdatePending)
            return false;
        if (list == null || list->SelectedItemIndex != slot.Index || !IsDeleteConfirmation(confirmation, addon))
        {
            StopDeletion();
            return true;
        }

        deletionConfirmed = confirmation->ConfirmCheckBox->IsChecked && confirmation->YesButton->IsEnabled;
        return SelectYesnoConfirmation.TryConfirmWithCheckbox(confirmation, ref checkboxSent);
    }

    private bool WaitForDeletion(DeletionContext context, SaveSlot slot)
    {
        if (DalamudServices.PlayerState.ContentId != context.OwnerContentID)
        {
            StopDeletion();
            return true;
        }

        if (!TryGetSaveData(out var addon, out var dungeonID))
        {
            if (context.Reopening || !IsDeepDungeonMenuReady(context.DungeonID))
                return false;

            if (tasks is not { CurrentTasksCount: > 1 })
            {
                deletionConfirmed = false;
                return true;
            }

            AgentId.DeepDungeonMenu.SendEvent(0, 1);
            context.Reopening = true;
            return false;
        }
        if (context.Reopening)
        {
            context.AddonID = addon->Id;
            context.Reopening = false;
            handledAddonID = addon->Id;
        }
        if (addon->Id != context.AddonID || dungeonID != context.DungeonID)
        {
            StopDeletion();
            return true;
        }
        if (addon->GetComponentListById(SAVE_LIST_NODE_ID)->IsUpdatePending)
            return false;
        if (IsEmptySlot(addon, slot.Index))
        {
            deletionConfirmed = false;
            return true;
        }
        if (!TryReadSlot(addon, dungeonID, slot.Index, out var current) || current != slot)
        {
            StopDeletion();
            return true;
        }

        return false;
    }

    private bool TryGetCurrentSave(DeletionContext context, SaveSlot slot, out AtkUnitBase* addon) =>
        TryGetSaveData(out addon, out var dungeonID) && dungeonID == context.DungeonID &&
        addon->Id == context.AddonID && DalamudServices.PlayerState.ContentId == context.OwnerContentID &&
        IsAllowed(dungeonID, slot.Floor) && TryReadSlot(addon, dungeonID, slot.Index, out var current) && current == slot;

    private bool IsAllowed(uint dungeonID, int floor)
    {
        if (!config.AllowedFloorSets.TryGetValue(dungeonID, out var selectedFloorSets))
            return false;

        foreach (var range in GetFloorRanges(dungeonID))
            if (range.Contains(floor))
                return range.IsSelected(selectedFloorSets);
        return false;
    }

    private void StopDeletion()
    {
        tasks?.Abort();
        saveLease?.Dispose();
        saveLease = null;
        checkboxSent = false;
        deletionConfirmed = false;
    }

    private sealed class DeletionContext(ulong ownerContentID, uint dungeonID, ushort addonID)
    {
        public ulong OwnerContentID { get; } = ownerContentID;
        public uint DungeonID { get; } = dungeonID;
        public ushort AddonID = addonID;
        public bool Reopening;
    }

    private readonly record struct SaveSlot(int Index, int Floor, uint State, string Identity);

    #region 常量

    private const string SAVE_ADDON_NAME = "DeepDungeonSaveData";
    private const uint SAVE_LIST_NODE_ID = 6;
    private const int SAVE_SLOT_COUNT = 2;
    private const int SAVE_VALUES_OFFSET = 12;
    private const int SAVE_VALUES_STRIDE = 30;
    private const uint FLOOR_TEXT_ROW_ID = 10405;
    private const uint RESET_TITLE_ROW_ID = 10464;

    #endregion
}
