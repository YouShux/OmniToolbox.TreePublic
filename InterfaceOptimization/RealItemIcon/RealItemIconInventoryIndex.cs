using FFXIVClientStructs.FFXIV.Client.Game;
using OmniToolbox.Host;
using OmniToolbox.Items;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed class RealItemIconInventoryIndex(PlayerInventoryService inventoryService)
{
    private const int SlotsPerInventoryPage = 35;

    private readonly Dictionary<InventorySlotKey, uint> itemIdsBySlot = new(1024);
    private long indexedRevision = -1;
    private ulong indexedContentId;

    public void Refresh()
    {
        var revision = inventoryService.GetSnapshotRevision();
        var contentId = DalamudServices.PlayerState.ContentId;
        if (revision == indexedRevision && contentId == indexedContentId)
        {
            return;
        }

        indexedRevision = revision;
        indexedContentId = contentId;
        itemIdsBySlot.Clear();
        var items = inventoryService.GetItemsSnapshot();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var holderId = item.HolderId == 0 ? contentId : item.HolderId;
            if (holderId != 0 && item.ItemId != 0)
            {
                itemIdsBySlot[new(holderId, item.ContainerType, item.SlotIndex)] = item.ItemId;
            }
        }
    }

    public bool TryGetItemId(
        ulong holderId,
        InventoryType containerType,
        ushort slotIndex,
        out uint itemId) =>
        itemIdsBySlot.TryGetValue(new(holderId, containerType, slotIndex), out itemId);

    public static int GetSortedIndex(string addonName, int parentTab, int visibleSlot)
    {
        if (visibleSlot < 0 || !IsVisibleSlotValid(addonName, visibleSlot))
        {
            return -1;
        }

        return addonName switch
        {
            "InventoryBuddy" or "InventoryBuddy2" => visibleSlot,
            "RetainerGrid" => parentTab < 0 ? -1 : parentTab * SlotsPerInventoryPage + visibleSlot,
            _ when TryGetRetainerGridIndex(addonName, out var gridIndex) =>
                gridIndex * SlotsPerInventoryPage + visibleSlot,
            _ => GetInventoryPage(addonName, parentTab) is var page && page >= 0
                ? page * SlotsPerInventoryPage + visibleSlot
                : -1,
        };
    }

    private static int GetInventoryPage(string addonName, int parentTab) =>
        addonName switch
        {
            "InventoryGrid0E" => 0,
            "InventoryGrid1E" => 1,
            "InventoryGrid2E" => 2,
            "InventoryGrid3E" => 3,
            "InventoryGrid0" => parentTab switch
            {
                0 => 0,
                1 => 2,
                _ => -1,
            },
            "InventoryGrid1" => parentTab switch
            {
                0 => 1,
                1 => 3,
                _ => -1,
            },
            "InventoryGrid" => parentTab is >= 0 and <= 3 ? parentTab : -1,
            _ => -1,
        };

    private static bool IsVisibleSlotValid(string addonName, int visibleSlot) =>
        addonName switch
        {
            "InventoryBuddy" or "InventoryBuddy2" => visibleSlot < SlotsPerInventoryPage * 2,
            "RetainerGrid" => visibleSlot < SlotsPerInventoryPage,
            _ when TryGetRetainerGridIndex(addonName, out _) => visibleSlot < SlotsPerInventoryPage,
            "InventoryGrid" or "InventoryGrid0" or "InventoryGrid1" or
                "InventoryGrid0E" or "InventoryGrid1E" or "InventoryGrid2E" or "InventoryGrid3E" =>
                visibleSlot < SlotsPerInventoryPage,
            _ => false,
        };

    private static bool TryGetRetainerGridIndex(string addonName, out int gridIndex)
    {
        gridIndex = addonName switch
        {
            "RetainerGrid0" => 0,
            "RetainerGrid1" => 1,
            "RetainerGrid2" => 2,
            "RetainerGrid3" => 3,
            "RetainerGrid4" => 4,
            _ => -1,
        };
        return gridIndex >= 0;
    }

    private readonly record struct InventorySlotKey(
        ulong HolderId,
        InventoryType ContainerType,
        ushort SlotIndex);
}
