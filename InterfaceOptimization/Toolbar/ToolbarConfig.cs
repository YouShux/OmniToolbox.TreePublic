namespace OmniToolbox.TreePublic;

public enum ToolbarEdge
{
    Top,
    Bottom
}

public enum ToolbarSection
{
    Left,
    Center,
    Right
}

public enum ToolbarComponentType
{
    Player,
    Health,
    Magic,
    Gil,
    Location,
    LocalTime,
    EorzeaTime,
    Fps,
    Volume,
    Dtr,
    Omni,
    Separator
}

[Serializable]
public sealed class ToolbarConfig
{
    public ToolbarEdge Edge { get; set; } = ToolbarEdge.Top;

    public bool Stretch { get; set; } = true;

    public bool HideNativeDtr { get; set; } = true;

    public bool Locked { get; set; } = true;

    public float Scale { get; set; } = 1f;

    public float Opacity { get; set; } = 0.9f;

    public float Spacing { get; set; } = 10f;

    public float Height { get; set; } = 34f;

    public Dictionary<ToolbarComponentType, uint> Icons { get; set; } = [];

    public List<ToolbarComponentType> Left { get; set; } =
    [
        ToolbarComponentType.Player,
        ToolbarComponentType.Health,
        ToolbarComponentType.Magic
    ];

    public List<ToolbarComponentType> Center { get; set; } =
    [
        ToolbarComponentType.Location,
        ToolbarComponentType.Dtr
    ];

    public List<ToolbarComponentType> Right { get; set; } =
    [
        ToolbarComponentType.Gil,
        ToolbarComponentType.LocalTime,
        ToolbarComponentType.EorzeaTime,
        ToolbarComponentType.Fps,
        ToolbarComponentType.Volume,
        ToolbarComponentType.Omni
    ];

    public List<ToolbarComponentType> GetSection(ToolbarSection section) => section switch
    {
        ToolbarSection.Left => Left,
        ToolbarSection.Center => Center,
        ToolbarSection.Right => Right,
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
    };

    public uint GetIconID(ToolbarComponentType component) => Icons.GetValueOrDefault(component);

    public bool Normalize()
    {
        var changed = false;
        var scale = Math.Clamp(Scale, 0.75f, 2f);
        var opacity = Math.Clamp(Opacity, 0.15f, 1f);
        var spacing = Math.Clamp(Spacing, 0f, 32f);
        var height = Math.Clamp(Height, 24f, 64f);
        changed |= Scale != scale || Opacity != opacity || Spacing != spacing || Height != height;
        Scale = scale;
        Opacity = opacity;
        Spacing = spacing;
        Height = height;

        var seen = new HashSet<ToolbarComponentType>();
        changed |= NormalizeSection(Left, seen);
        changed |= NormalizeSection(Center, seen);
        changed |= NormalizeSection(Right, seen);
        return changed;
    }

    private static bool NormalizeSection(
        List<ToolbarComponentType> components,
        HashSet<ToolbarComponentType> seen)
    {
        var changed = false;
        for (var i = 0; i < components.Count;)
        {
            if (!Enum.IsDefined(components[i]) ||
                components[i] != ToolbarComponentType.Separator && !seen.Add(components[i]))
            {
                components.RemoveAt(i);
                changed = true;
                continue;
            }

            i++;
        }

        return changed;
    }
}
