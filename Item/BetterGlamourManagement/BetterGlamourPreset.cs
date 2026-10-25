namespace OmniToolbox.TreePublic;

public sealed class BetterGlamourPreset
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public int GearsetIndex { get; set; } = -1;

    public Dictionary<ulong, List<int>> GearsetBindings { get; set; } = [];

    public ushort GlassesID { get; set; }

    public uint HairstyleID { get; set; }

    public byte? HairColor { get; set; }

    public byte? HighlightColor { get; set; }

    public bool? HighlightsEnabled { get; set; }

    public List<BetterGlamourItem> Items { get; set; } = [];

    internal IReadOnlyList<int> GetGearsetIndices(ulong contentID) =>
        contentID != 0 && GearsetBindings.TryGetValue(contentID, out var indices) ? indices : Array.Empty<int>();
}
