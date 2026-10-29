namespace OmniToolbox.TreePublic;

public sealed partial class ChocoboColorPreview
{
    private readonly record struct ChocoboFeedOrder
    (
        int    Index,
        string Name,
        uint   IconID
    );

    private readonly record struct ChocoboFruitRequirement
    (
        string Name,
        uint   IconID,
        int    Count
    );
}
