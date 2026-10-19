namespace OmniToolbox.TreePublic;

public sealed partial class ChocoboColorPreview
{
    internal readonly record struct ChocoboFeedOrder(int Index, string Name, uint IconID);

    internal readonly record struct ChocoboFruitRequirement(string Name, uint IconID, int Count);
}
