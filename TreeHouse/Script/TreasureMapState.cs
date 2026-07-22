namespace OmniToolbox.TreeHouse.Script;

internal enum TreasureMapState
{
    Idle,
    Advance,
    WaitingPurchase,
    WaitingStore,
    UseDecipher,
    SelectMap,
    ConfirmDecipher,
    WaitingDecipher
}

internal readonly record struct TreasureMapDefinition(uint ItemId, uint RankId, string Name);

internal readonly record struct TreasureMapLocation(uint TerritoryId, uint MapId, Vector3 Position);

internal readonly record struct TreasureMapCounts(int Inventory, int Saddlebag, bool Deciphered)
{
    public int Total => Inventory + Saddlebag + (Deciphered ? 1 : 0);
}
