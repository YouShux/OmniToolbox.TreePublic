using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmenTools.Info.Game.Data;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal sealed unsafe class AutoStoreDriver(PlayerInventoryService inventoryService)
{
    private const uint HighQualityOffset = 1_000_000;

    private static readonly InventoryType[] SaddlebagContainers =
    [
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2
    ];

    private static readonly InventoryType[] FreeCompanyChestContainers =
    [
        InventoryType.FreeCompanyPage1,
        InventoryType.FreeCompanyPage2,
        InventoryType.FreeCompanyPage3,
        InventoryType.FreeCompanyPage4,
        InventoryType.FreeCompanyPage5
    ];

    public bool Retrieve(uint itemId, int maxQuantity = int.MaxValue)
    {
        var manager = InventoryManager.Instance();
        if (itemId == 0 || maxQuantity <= 0)
        {
            return true;
        }

        if (manager is null)
        {
            return false;
        }

        var result = TryMoveFrom(manager, SaddlebagContainers, Inventories.Player, itemId, maxQuantity);
        if (result != MoveResult.NotFound)
        {
            return false;
        }

        if (AutoStoreNativeUi.IsFreeCompanyChestReady())
        {
            result = TryMoveFrom(manager, FreeCompanyChestContainers, Inventories.Player, itemId, maxQuantity);
            if (result != MoveResult.NotFound)
            {
                return false;
            }
        }

        if (AutoStoreNativeUi.IsRetainerReady(-1))
        {
            result = TryMoveFrom(manager, Inventories.Retainer, Inventories.Player, itemId, maxQuantity);
            if (result != MoveResult.NotFound)
            {
                return false;
            }
        }

        result = TryRestoreFromGlamourDresser(itemId);
        if (result != MoveResult.NotFound)
        {
            return false;
        }

        return TryRestoreFromArmoire(itemId) == MoveResult.NotFound;
    }

    public bool Store(uint itemId, AutoStoreDestination destination, int maxQuantity = int.MaxValue)
    {
        var manager = InventoryManager.Instance();
        if (itemId == 0 || maxQuantity <= 0)
        {
            return true;
        }

        if (manager is null)
        {
            return false;
        }

        return destination.Kind switch
        {
            AutoStoreDestinationKind.Saddlebag =>
                TryMoveFrom(manager, Inventories.Player, SaddlebagContainers, itemId, maxQuantity) ==
                    MoveResult.NotFound,
            AutoStoreDestinationKind.FreeCompanyChest when AutoStoreNativeUi.IsFreeCompanyChestReady() =>
                MoveFromPlayerAndSaddlebag(manager, FreeCompanyChestContainers, itemId, maxQuantity),
            AutoStoreDestinationKind.Retainer when AutoStoreNativeUi.IsRetainerReady(destination.RetainerIndex) =>
                MoveFromPlayerAndSaddlebag(manager, Inventories.Retainer, itemId, maxQuantity),
            AutoStoreDestinationKind.GlamourDresser => StoreInGlamourDresser(manager, itemId),
            AutoStoreDestinationKind.Armoire => StoreInArmoire(manager, itemId),
            _ => true
        };
    }

    private static bool MoveFromPlayerAndSaddlebag(
        InventoryManager* manager,
        IEnumerable<InventoryType> targets,
        uint itemId,
        int maxQuantity)
    {
        var result = TryMoveFrom(manager, Inventories.Player, targets, itemId, maxQuantity);
        if (result == MoveResult.NotFound)
        {
            result = TryMoveFrom(manager, SaddlebagContainers, targets, itemId, maxQuantity);
        }

        return result == MoveResult.NotFound;
    }

    private static MoveResult TryMoveFrom(
        InventoryManager* manager,
        IEnumerable<InventoryType> sources,
        IEnumerable<InventoryType> targets,
        uint itemId,
        int maxQuantity)
    {
        if (maxQuantity <= 0)
        {
            return MoveResult.NotFound;
        }

        var sourceFound = false;
        foreach (var sourceType in sources)
        {
            var source = manager->GetInventoryContainer(sourceType);
            if (source is null || !source->IsLoaded)
            {
                continue;
            }

            var sourceBlocked = false;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var index = 0; index < source->Size; index++)
                {
                    var sourceSlot = source->GetInventorySlot(index);
                    if (sourceSlot is null ||
                        sourceSlot->ItemId == 0 ||
                        sourceSlot->GetBaseItemId() != itemId)
                    {
                        continue;
                    }

                    var sourceQuantity = (int)Math.Min((long)sourceSlot->Quantity, int.MaxValue);
                    var requestedQuantity = maxQuantity == int.MaxValue
                        ? sourceQuantity
                        : Math.Min(maxQuantity, sourceQuantity);
                    if (requestedQuantity <= 0)
                    {
                        continue;
                    }

                    sourceFound = true;
                    if (!TryFindTargetSlot(
                            manager,
                            targets,
                            sourceSlot,
                            sourceType,
                            (ushort)sourceSlot->Slot,
                            out var targetType,
                            out var targetSlot,
                            out var targetCapacity))
                    {
                        continue;
                    }

                    var needsSplit = maxQuantity != int.MaxValue &&
                                     sourceQuantity > requestedQuantity &&
                                     requestedQuantity < targetCapacity;
                    if ((pass == 0 && needsSplit) || (pass == 1 && !needsSplit))
                    {
                        continue;
                    }

                    if (needsSplit)
                    {
                        if (manager->SplitItem(
                                sourceType,
                                (ushort)sourceSlot->Slot,
                                requestedQuantity) == 0)
                        {
                            return MoveResult.Pending;
                        }

                        sourceBlocked = true;
                        break;
                    }

                    if (IsFreeCompanyChestContainer(sourceType) || IsFreeCompanyChestContainer(targetType))
                    {
                        var agent = AgentFreeCompanyChest.Instance();
                        if (agent is null || !agent->IsAgentActive())
                        {
                            sourceBlocked = true;
                            break;
                        }

                        agent->MoveItemInChest(
                            sourceType,
                            (uint)sourceSlot->Slot,
                            targetType,
                            targetSlot);
                        return MoveResult.Moved;
                    }

                    if (manager->MoveItemSlot(
                            sourceType,
                            (ushort)sourceSlot->Slot,
                            targetType,
                            (ushort)targetSlot,
                            true) == 0)
                    {
                        return MoveResult.Moved;
                    }

                    sourceBlocked = true;
                    break;
                }

                if (sourceBlocked)
                {
                    break;
                }
            }
        }

        return sourceFound ? MoveResult.Blocked : MoveResult.NotFound;
    }

    private static bool TryFindTargetSlot(
        InventoryManager* manager,
        IEnumerable<InventoryType> targets,
        InventoryItem* source,
        InventoryType sourceType,
        ushort sourceSlot,
        out InventoryType targetType,
        out uint targetSlot,
        out int targetCapacity)
    {
        targetType = InventoryType.Invalid;
        targetSlot = 0;
        targetCapacity = 0;
        var itemId = source->GetBaseItemId();
        if (!LuminaGetter.TryGetRow<Item>(itemId, out var itemData) ||
            itemData.IsUnique && ContainsItem(manager, targets, itemId))
        {
            return false;
        }

        foreach (var type in targets)
        {
            var container = manager->GetInventoryContainer(type);
            if (container is null || !container->IsLoaded)
            {
                continue;
            }

            for (var index = 0; index < container->Size; index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot is null ||
                    (type == sourceType && slot->Slot == sourceSlot) ||
                    !IsSameItem(slot, source) ||
                    slot->Quantity >= itemData.StackSize)
                {
                    continue;
                }

                targetType = type;
                targetSlot = (uint)slot->Slot;
                targetCapacity = (int)Math.Min(
                    (long)itemData.StackSize - slot->Quantity,
                    int.MaxValue);
                return true;
            }
        }

        foreach (var type in targets)
        {
            var container = manager->GetInventoryContainer(type);
            if (container is null || !container->IsLoaded)
            {
                continue;
            }

            for (var index = 0; index < container->Size; index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot is not null &&
                    !(type == sourceType && slot->Slot == sourceSlot) &&
                    slot->ItemId == 0)
                {
                    targetType = type;
                    targetSlot = (uint)slot->Slot;
                    targetCapacity = (int)Math.Clamp((long)itemData.StackSize, 1, int.MaxValue);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSameItem(InventoryItem* left, InventoryItem* right) =>
        left->GetBaseItemId() == right->GetBaseItemId() &&
        left->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) ==
        right->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) &&
        left->Flags.HasFlag(InventoryItem.ItemFlags.Collectable) ==
        right->Flags.HasFlag(InventoryItem.ItemFlags.Collectable);

    private static bool ContainsItem(
        InventoryManager* manager,
        IEnumerable<InventoryType> containers,
        uint itemId)
    {
        foreach (var type in containers)
        {
            var container = manager->GetInventoryContainer(type);
            if (container is null || !container->IsLoaded)
            {
                continue;
            }

            for (var index = 0; index < container->Size; index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot is not null && slot->ItemId != 0 && slot->GetBaseItemId() == itemId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static MoveResult TryRestoreFromGlamourDresser(uint itemId)
    {
        var manager = MirageManager.Instance();
        if (manager is null || !manager->PrismBoxLoaded)
        {
            return MoveResult.Pending;
        }

        for (var index = 0; index < manager->PrismBoxItemIds.Length; index++)
        {
            if (manager->PrismBoxItemIds[index] % HighQualityOffset != itemId)
            {
                continue;
            }

            return TryExecute(
                () => PrsimBoxCommand.Restore((uint)index),
                "restore from glamour dresser",
                itemId)
                ? MoveResult.Moved
                : MoveResult.Blocked;
        }

        return MoveResult.NotFound;
    }

    private MoveResult TryRestoreFromArmoire(uint itemId)
    {
        var uiState = UIState.Instance();
        if (uiState is null || !uiState->Cabinet.IsCabinetLoaded())
        {
            return MoveResult.Pending;
        }

        if (!inventoryService.ArmoireRows.TryGetValue(itemId, out var cabinetRowId) ||
            !uiState->Cabinet.IsItemInCabinet(cabinetRowId))
        {
            return MoveResult.NotFound;
        }

        return TryExecute(
            () => CabinetCommand.Restore(cabinetRowId),
            "restore from armoire",
            itemId)
            ? MoveResult.Moved
            : MoveResult.Blocked;
    }

    private static bool StoreInGlamourDresser(InventoryManager* manager, uint itemId)
    {
        var mirageManager = MirageManager.Instance();
        if (mirageManager is null || !mirageManager->PrismBoxLoaded)
        {
            return false;
        }

        if (!TryFindSourceSlot(manager, itemId, out var sourceType, out var sourceSlot))
        {
            return true;
        }

        return TryExecute(
            () => ExecuteCommandManager.Instance().ExecuteCommand(
                ExecuteCommandFlag.MiragePrismCommand439,
                (uint)sourceType,
                sourceSlot),
            "store in glamour dresser",
            itemId);
    }

    private bool StoreInArmoire(InventoryManager* manager, uint itemId)
    {
        var uiState = UIState.Instance();
        if (uiState is null || !uiState->Cabinet.IsCabinetLoaded())
        {
            return false;
        }

        if (!inventoryService.ArmoireRows.TryGetValue(itemId, out var cabinetRowId) ||
            uiState->Cabinet.IsItemInCabinet(cabinetRowId) ||
            !TryFindSourceSlot(manager, itemId, out _, out _))
        {
            return true;
        }

        return TryExecute(
            () => CabinetCommand.Store(cabinetRowId),
            "store in armoire",
            itemId);
    }

    private static bool TryFindSourceSlot(
        InventoryManager* manager,
        uint itemId,
        out InventoryType sourceType,
        out uint sourceSlot)
    {
        if (TryFindSourceSlot(manager, Inventories.Player, itemId, out sourceType, out sourceSlot))
        {
            return true;
        }

        return TryFindSourceSlot(manager, SaddlebagContainers, itemId, out sourceType, out sourceSlot);
    }

    private static bool TryFindSourceSlot(
        InventoryManager* manager,
        IEnumerable<InventoryType> containers,
        uint itemId,
        out InventoryType sourceType,
        out uint sourceSlot)
    {
        foreach (var type in containers)
        {
            var container = manager->GetInventoryContainer(type);
            if (container is null || !container->IsLoaded)
            {
                continue;
            }

            for (var index = 0; index < container->Size; index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot is not null && slot->ItemId != 0 && slot->GetBaseItemId() == itemId)
                {
                    sourceType = type;
                    sourceSlot = (uint)slot->Slot;
                    return true;
                }
            }
        }

        sourceType = InventoryType.Invalid;
        sourceSlot = 0;
        return false;
    }

    private static bool IsFreeCompanyChestContainer(InventoryType type) =>
        type is InventoryType.FreeCompanyPage1 or
            InventoryType.FreeCompanyPage2 or
            InventoryType.FreeCompanyPage3 or
            InventoryType.FreeCompanyPage4 or
            InventoryType.FreeCompanyPage5;

    private static bool TryExecute(System.Action action, string operation, uint itemId)
    {
        try
        {
            action();
            ExecuteCommandManager.Instance().ExecuteCommand(ExecuteCommandFlag.RefreshInventory);
            return true;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Auto store failed to {Operation} for item {ItemId}.", operation, itemId);
            return false;
        }
    }

    private enum MoveResult
    {
        NotFound,
        Moved,
        Pending,
        Blocked
    }
}
