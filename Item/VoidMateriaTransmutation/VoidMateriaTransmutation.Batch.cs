using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using TreeTools.Threading;
using EventFramework = FFXIVClientStructs.FFXIV.Client.Game.Event.EventFramework;
using Item = Lumina.Excel.Sheets.Item;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class VoidMateriaTransmutation
{
    private TaskHelper? batchTasks;
    private HashSet<uint> materiaIDs = [];
    private readonly InventoryItemRef[] batchMaterials = new InventoryItemRef[5];
    private readonly int[] quantitiesBeforeSubmission = new int[5];

    private void InitializeBatch()
    {
        materiaIDs = LuminaGetter.Get<Item>().Where(item => item.FilterGroup == 13)
            .Select(item => item.RowId).ToHashSet();
        batchTasks = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = 15_000
        };
        Activation.Add(batchTasks);
        batchTasks.TaskTimeout += _ => StopBatch("Feature.VoidMateriaTransmutation.BatchTimedOut");
        batchTasks.TaskGetException += _ => StopBatch("Feature.VoidMateriaTransmutation.BatchFailed");
    }

    private void DisposeBatch()
    {
        batchTasks = null;
        ReleaseBatchButton();
    }

    private void ToggleBatch()
    {
        if (batchTasks?.IsBusy == true)
        {
            batchTasks.Abort();
            return;
        }

        if (CanContinueBatch() && GetSelectionAgent() != null)
            EnqueueBatch();
    }

    private void EnqueueBatch()
    {
        if (batchTasks is not { } tasks || !CanContinueBatch())
            return;

        tasks.Enqueue(SelectBatchMaterials);
        tasks.Enqueue(SubmitBatch);
        tasks.Enqueue(WaitForBatchResult);
        tasks.Enqueue(EnqueueBatch);
    }

    private bool CanContinueBatch()
    {
        var services = DService.Instance();
        if (IsEnabled && canUseFeature() && hasSceneOverrides && services.ClientState.IsLoggedIn &&
            services.ClientState.TerritoryType == destinationTerritoryID && !services.Condition.IsBetweenAreas &&
            !services.Condition[ConditionFlag.InCombat])
            return true;

        batchTasks?.Abort();
        return false;
    }

    private AgentTradeMultiple* GetSelectionAgent()
    {
        if (!ownsWindow || !AddonHelper.TryGetByName("TradeMultiple", out var addon) ||
            !addon->IsAddonAndNodesReady() || !addon->IsVisible)
            return null;

        var agent = AgentTradeMultiple.Instance();
        var events = EventFramework.Instance();
        var handler = events == null ? null : events->GetEventHandlerById(TRANSMUTATION_EVENT_ID);
        if (agent == null || handler == null || !agent->IsAgentActive() ||
            agent->Handler == null || agent->MaxQuantity != 5 ||
            agent->PendingAdd != 0 || agent->InputNumericAddonId != 0 || agent->SelectYesnoAddonId != 0)
            return null;

        return agent;
    }

    private bool SelectBatchMaterials()
    {
        if (!CanContinueBatch())
            return false;

        var agent = GetSelectionAgent();
        var inventory = InventoryManager.Instance();
        if (agent == null || inventory == null)
            return false;

        foreach (var type in InventoryContainers)
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                return false;
        }

        Array.Clear(batchMaterials);
        var remaining = 5;
        var selectedCount = 0;
        foreach (var type in InventoryContainers)
        {
            var container = inventory->GetInventoryContainer(type);
            for (var index = 0; index < container->Size && remaining > 0; index++)
            {
                var item = container->GetInventorySlot(index);
                if (item == null || item->Quantity <= 0 || !materiaIDs.Contains(item->ItemId))
                    continue;

                var quantity = Math.Min(item->Quantity, remaining);
                batchMaterials[selectedCount++] = new()
                {
                    Container = type,
                    Slot = (short)index,
                    ItemId = item->ItemId,
                    Quantity = quantity
                };
                remaining -= quantity;
            }
        }

        if (remaining != 0)
        {
            StopBatch("Feature.VoidMateriaTransmutation.BatchFinished");
            return false;
        }

        for (var index = agent->Slots.Length - 1; index >= 0; index--)
        {
            if (agent->Slots[index].Container != InventoryType.Invalid)
                agent->RemoveSlot((uint)index);
        }

        for (var index = 0; index < selectedCount; index++)
        {
            var selected = batchMaterials[index];
            var item = inventory->GetInventorySlot(selected.Container, selected.Slot);
            agent->AddItemQuantity(item, (uint)selected.Quantity);
        }

        return true;
    }

    private void SubmitBatch()
    {
        if (!CanContinueBatch())
            return;

        var agent = GetSelectionAgent();
        var inventory = InventoryManager.Instance();
        if (agent == null || inventory == null)
        {
            StopBatch("Feature.VoidMateriaTransmutation.BatchFailed");
            return;
        }

        for (var index = 0; index < batchMaterials.Length; index++)
        {
            var expected = batchMaterials[index];
            var selected = agent->Slots[index];
            if (expected.Quantity == 0)
            {
                if (selected.Container == InventoryType.Invalid)
                    continue;
            }
            else if (selected.Container == expected.Container && selected.Slot == expected.Slot &&
                selected.ItemId == expected.ItemId && selected.Quantity == expected.Quantity)
            {
                var item = inventory->GetInventorySlot(selected.Container, selected.Slot);
                if (item != null && item->ItemId == selected.ItemId && item->Quantity >= selected.Quantity)
                {
                    quantitiesBeforeSubmission[index] = item->Quantity;
                    continue;
                }
            }

            StopBatch("Feature.VoidMateriaTransmutation.BatchFailed");
            return;
        }

        if (!agent->Confirm())
            StopBatch("Feature.VoidMateriaTransmutation.BatchFailed");
    }

    private bool WaitForBatchResult()
    {
        if (!CanContinueBatch())
            return false;

        var agent = GetSelectionAgent();
        if (agent == null)
            return false;

        foreach (ref readonly var selected in agent->Slots)
        {
            if (selected.Quantity > 0)
                return false;
        }

        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return false;

        for (var index = 0; index < batchMaterials.Length; index++)
        {
            var material = batchMaterials[index];
            if (material.Quantity == 0)
                continue;

            var item = inventory->GetInventorySlot(material.Container, material.Slot);
            if (item == null)
                return false;

            var expectedQuantity = quantitiesBeforeSubmission[index] - material.Quantity;
            if (item->ItemId == material.ItemId ? item->Quantity > expectedQuantity : expectedQuantity != 0)
                return false;
        }

        return true;
    }

    private void StopBatch(string messageKey)
    {
        batchTasks?.Abort();
        NotifyHelper.Toast(OmniLoc.Get(messageKey));
    }

    private static readonly InventoryType[] InventoryContainers =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4
    ];
}
