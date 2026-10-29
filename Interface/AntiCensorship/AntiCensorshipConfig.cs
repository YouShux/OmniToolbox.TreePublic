namespace OmniToolbox.TreePublic;

public sealed class AntiCensorshipConfig
{
    public bool EnableColoring { get; set; }

    public bool EnableAutoHandle { get; set; } = true;

    public int HighlightColor { get; set; } = 17;

    public bool UseCustomHighlightColor { get; set; }

    public Vector3 HighlightColorValue { get; set; } = new(1f, 0f, 0f);

    public char Separator { get; set; } = '.';
}
