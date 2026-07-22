namespace OmniToolbox.TreeHouse.InterfaceOperation;

public interface IArmoireRecordService
{
    bool IsStoring { get; }

    bool IsRetrieving { get; }

    int ItemCount { get; }

    int DirectStoreCount { get; }

    bool RequestCheck();
}
