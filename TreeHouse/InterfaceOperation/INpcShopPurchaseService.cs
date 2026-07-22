namespace OmniToolbox.TreeHouse.InterfaceOperation;

public interface INpcShopPurchaseService
{
    bool IsAvailable { get; }

    bool IsBusy { get; }

    string StatusKey { get; }

    bool RequestPurchase(uint itemId, uint quantity);

    void Cancel();
}
