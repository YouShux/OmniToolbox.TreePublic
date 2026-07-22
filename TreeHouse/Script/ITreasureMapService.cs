namespace OmniToolbox.TreeHouse.Script;

public interface ITreasureMapService
{
    bool IsAvailable { get; }

    bool IsBusy { get; }

    string StatusKey { get; }

    bool RequestStart(uint itemId, int maxUnitPrice);

    void Cancel();
}
