using FFXIVClientStructs.FFXIV.Client.Game;
using OmniToolbox.Host;
using OmniToolbox.Items;

namespace OmniToolbox.TreePublic;

internal sealed class RealItemIconInventoryIndex(PlayerInventoryService inventoryService)
{
    private const int SlotsPerInventoryPage = 35;

    private readonly Dictionary<InventorySlotKey, uint> itemIdsBySlot = new(1024);
    private long indexedRevision = -1;
    private ulong indexedContentID;

    public void Refresh()
    {
        var revision = inventoryService.GetSnapshotRevision();
        var contentID = DalamudServices.PlayerState.ContentId;
        if (revision == indexedRevision && contentID == indexedContentID)
        {
            return;
        }

        indexedRevision = revision;
        indexedContentID = contentID;
        itemIdsBySlot.Clear();
        var items = inventoryService.GetItemsSnapshot();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var holderID = item.HolderID == 0 ? contentID : item.HolderID;
            if (holderID != 0 && item.ItemID != 0)
            {
                itemIdsBySlot[new(holderID, item.ContainerType, item.SlotIndex)] = item.ItemID;
            }
        }
    }

    public bool TryGetItemID(
        ulong holderID,
        InventoryType containerType,
        ushort slotIndex,
        out uint itemID) =>
        itemIdsBySlot.TryGetValue(new(holderID, containerType, slotIndex), out itemID);

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
        ulong HolderID,
        InventoryType ContainerType,
        ushort SlotIndex);
}
