using OmniToolbox.Market;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public interface IMarketPurchaseService
{
    bool IsAvailable { get; }

    bool CanPurchaseInCurrentArea { get; }

    bool IsBusy { get; }

    uint SearchItemId { get; }

    IReadOnlyList<MarketListing> Listings { get; }

    IReadOnlyList<MarketHistoryEntry> History { get; }

    string StatusKey { get; }

    bool OpenBrowser(uint itemId = 0);

    bool RequestSearch(uint itemId);

    bool RequestPurchase(uint itemId, bool isHq, uint quantity, int maxUnitPrice);

    bool RequestPurchaseListings(uint itemId, IReadOnlyList<MarketListing> listings);

    void Cancel();
}
