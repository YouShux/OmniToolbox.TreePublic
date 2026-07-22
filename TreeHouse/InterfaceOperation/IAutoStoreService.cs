using OmniToolbox.Config;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public readonly record struct AutoStoreItemRequest(uint ItemId, int Quantity = int.MaxValue);

public enum AutoStoreCacheCollection
{
    Retainer,
    GlamourDresserAndArmoire,
    FreeCompanyChest
}

public interface IAutoStoreService
{
    bool IsAvailable { get; }

    bool IsBusy { get; }

    bool RequestRetrieve(uint itemId);

    bool RequestRetrieve(IReadOnlyList<uint> itemIds);

    bool RequestRetrieve(IReadOnlyList<AutoStoreItemRequest> items);

    bool RequestStore(uint itemId, string destination);

    bool RequestStore(IReadOnlyList<AutoStoreItemRequest> items, AutoStoreListTarget destination);

    bool RequestCacheCollection(AutoStoreCacheCollection target);

    bool OpenList();

    bool ToggleList();

    void Cancel();
}
