using FFXIVClientStructs.FFXIV.Client.Game;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal enum AutoGcSealRunMode
{
    Automatic,
    OpenSupply,
    OpenExchange
}

internal readonly record struct AutoGcSealWindowState(
    uint CurrentSeals,
    uint Threshold,
    uint ReservedSeals,
    uint ExchangeItemId,
    string ExchangeItemName,
    int CustomExcludeCount,
    bool IsRunning);

internal readonly record struct AutoGcSealDeliveryItem(
    uint ItemId,
    InventoryType Container,
    ushort Slot,
    uint SealReward,
    int ListIndex);

internal readonly record struct AutoGcSealExchangeItem(
    uint ItemId,
    uint Cost,
    uint Tier,
    uint Category);
