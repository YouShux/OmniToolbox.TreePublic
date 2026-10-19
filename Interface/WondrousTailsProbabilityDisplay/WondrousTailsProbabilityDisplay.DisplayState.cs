namespace OmniToolbox.TreePublic;

public sealed partial class WondrousTailsProbabilityDisplay
{
    internal readonly record struct DisplayState(
        nint NodeAddress, int Mask, ushort Height, ushort Flags, byte LineSpacing)
    {
        internal bool Matches(DisplayState applied, ReadOnlySpan<byte> currentText, ReadOnlySpan<byte> appliedText) =>
            NodeAddress != nint.Zero && this == applied && currentText.SequenceEqual(appliedText);
    }

}
