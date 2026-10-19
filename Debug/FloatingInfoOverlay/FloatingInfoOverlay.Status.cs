namespace OmniToolbox.TreePublic;

internal sealed partial class FloatingInfoOverlay
{
    internal readonly record struct FloatingInfoStatus(
        uint StatusID,
        string Name,
        float RemainingTime,
        ushort Param);

}