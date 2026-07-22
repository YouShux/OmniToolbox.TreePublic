namespace OmniToolbox.TreeHouse.InterfaceOperation;

public interface IInnEntryService
{
    bool IsRunning { get; }

    bool IsEnteredInnRoom { get; }

    string StatusKey { get; }

    bool RequestEnterNearestInn();

    bool RequestEnterNearestInnForCacheCollection();

    void Cancel();
}
