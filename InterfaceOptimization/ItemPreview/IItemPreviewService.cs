using OmniToolbox.Collections;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemPreview;

public interface IItemPreviewService
{
    bool IsEnabled { get; }

    bool CanPreview(uint? itemId);

    bool CanPreview(CollectionEntry item);

    bool Preview(uint itemId);

    bool Preview(CollectionEntry item);

    bool TryOn(uint itemId);

    void ClearPreview();
}
