using OmniToolbox.Market;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemPriceQuery;

public interface IItemPriceQueryService
{
    bool IsEnabled { get; }

    MarketPrice? Get(uint itemId);

    void Request(uint itemId);
}
