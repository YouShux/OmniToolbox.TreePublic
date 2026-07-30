namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal sealed record ArmoireRecordItem(
    uint ItemId,
    uint CabinetRowId,
    string Name,
    uint IconId,
    int Quantity,
    string LocationsText,
    byte Stain0,
    byte Stain1,
    bool CanDirectStore,
    bool CanRetrieve)
{
    public bool IsDyed => Stain0 != 0 || Stain1 != 0;

    public static int Compare(ArmoireRecordItem left, ArmoireRecordItem right)
    {
        var directCompare = right.CanDirectStore.CompareTo(left.CanDirectStore);
        if (directCompare != 0)
        {
            return directCompare;
        }

        var itemIdCompare = right.ItemId.CompareTo(left.ItemId);
        return itemIdCompare != 0
            ? itemIdCompare
            : string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
    }
}

internal readonly record struct ArmoireRecordScanKey(uint ItemId, byte Stain0, byte Stain1);
