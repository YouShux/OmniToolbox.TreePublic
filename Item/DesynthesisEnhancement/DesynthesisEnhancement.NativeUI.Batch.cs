using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using TreeTools.Threading;

namespace OmniToolbox.TreePublic;

public sealed partial class DesynthesisEnhancement
{
    internal sealed unsafe partial class DesynthesisEnhancementNativeUI
    {
        private readonly TaskHelper                   batchTasks;
        private readonly DesynthesisEnhancementWindow operationWindow;
        private ulong                                 batchContentID;
        private AgentSalvage.SalvageItemCategory      batchCategory;
        private bool                                  dialogConfirmed;

        private void UpdateBatchWindow(AtkUnitBase* selector)
        {
            if (!selector->IsVisible)
                return;

            if (!operationWindow.IsOpen)
                operationWindow.Open();
            operationWindow.SyncPosition(selector);
        }

        public bool CancelBatch()
        {
            if (!batchTasks.IsBusy)
                return false;
            batchTasks.Abort();
            if (AddonHelper.TryGetByName("SalvageDialog", out var dialog) && dialog->IsVisible)
                dialog->Close(true);
            return true;
        }

        private void ToggleBatch()
        {
            if (activation.IsStopping)
                return;

            if (batchTasks.IsBusy)
            {
                batchTasks.Abort();
                return;
            }

            var agent = AgentSalvage.Instance();
            if (agent == null || !DService.Instance().ClientState.IsLoggedIn ||
                !AddonHelper.TryGetByName(ADDON_NAME, out var selector) ||
                !selector->IsAddonAndNodesReady() || !selector->IsVisible ||
                AddonHelper.TryGetByName("SalvageDialog", out var dialog) && dialog->IsVisible)
                return;

            RefreshCaches();
            batchContentID = DalamudServices.PlayerState.ContentId;
            batchCategory  = agent->SelectedCategory;
            batchTasks.Enqueue(SelectNextDesynthesisItem, "Select desynthesis item", timeoutMS: 15_000);
        }

        private bool CanContinueBatch()
        {
            if (activation.IsStopping)
                return false;

            var agent = AgentSalvage.Instance();
            if (DService.Instance().ClientState.IsLoggedIn &&
                batchContentID == DalamudServices.PlayerState.ContentId &&
                agent != null && agent->SelectedCategory == batchCategory &&
                AddonHelper.TryGetByName(ADDON_NAME, out var selector) && selector->IsAddonAndNodesReady())
                return true;

            batchTasks.Abort();
            return false;
        }

        private bool SelectNextDesynthesisItem()
        {
            if (!CanContinueBatch())
                return true;
            if (DService.Instance().Condition.IsOccupiedInEvent)
                return false;
            if (Inventories.Player.IsFull(3))
            {
                RaptureLogModule.Instance()->ShowLogMessage(3974);
                return true;
            }

            var agent = AgentSalvage.Instance();
            var inventory = InventoryManager.Instance();
            if (agent == null || inventory == null || agent->ItemList == null)
                return true;

            for (var index = 0; index < agent->ItemCount; index++)
            {
                var entry = agent->ItemList[index];
                var slot = inventory->GetInventorySlot(entry.InventoryType, (int)entry.InventorySlot);
                if (slot == null || slot->ItemId == 0 ||
                    config.SkipHQ && slot->IsHighQuality() || IsItemLocked(slot))
                    continue;

                var itemID = slot->ItemId;
                var quantity = slot->Quantity;
                var container = entry.InventoryType;
                var inventorySlot = entry.InventorySlot;
                AgentId.Salvage.SendEvent(0, 12, index);
                batchTasks.Enqueue(() => WaitForDesynthesis(container, inventorySlot, itemID, quantity),
                    "Wait for desynthesis", timeoutMS: 15_000);
                batchTasks.Enqueue(SelectNextDesynthesisItem, "Select desynthesis item", timeoutMS: 15_000);
                return true;
            }

            NotifyHelper.Instance().Chat(OmniLoc.Get("Feature.DesynthesisEnhancement.NoCandidates"));
            return true;
        }

        private bool WaitForDesynthesis
        (
            InventoryType container,
            uint          inventorySlot,
            uint          itemID,
            int           quantity
        )
        {
            if (!CanContinueBatch())
                return true;

            var inventory = InventoryManager.Instance();
            var slot = inventory == null ? null : inventory->GetInventorySlot(container, (int)inventorySlot);
            if (slot == null || slot->ItemId == itemID && slot->Quantity >= quantity)
                return false;

            if (AddonHelper.TryGetByName("SalvageResult", out var result) && result->IsVisible)
                result->Close(true);
            return !DService.Instance().Condition.IsOccupiedInEvent;
        }

        private void OnSalvageDialog
        (
            AddonEvent type,
            AddonArgs  args
        )
        {
            if (type == AddonEvent.PostSetup)
                dialogConfirmed = false;

            var dialog = (AddonSalvageDialog*)args.Addon.Address;
            if (!dialog->AtkUnitBase.IsAddonAndNodesReady() || !dialog->IsVisible)
                return;

            var automatic = batchTasks.IsBusy || config.ConfirmWhenManual;
            foreach (ref var slot in dialog->Checkboxes)
            {
                if (slot.Checkbox == null)
                    continue;
                var desired = slot.Type switch
                {
                    SalvageDialogCheckboxType.GuaranteeNQ => config.GuaranteeNQ,
                    SalvageDialogCheckboxType.BulkDesynth when batchTasks.IsBusy => false,
                    SalvageDialogCheckboxType.Warning when automatic => true,
                    _ => slot.Checkbox->IsChecked
                };
                if (slot.Checkbox->IsChecked != desired)
                    slot.Checkbox->SetChecked(desired);
                slot.IsCheckedCached = desired;
            }

            if (dialogConfirmed || !batchTasks.IsBusy && !config.ConfirmWhenManual ||
                dialog->DesynthesizeButton == null || !dialog->DesynthesizeButton->IsEnabled)
                return;

            dialogConfirmed = true;
            dialog->AtkUnitBase.Callback(0, 0);
        }
    }
}
