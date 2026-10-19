namespace OmniToolbox.TreePublic;

public sealed class BetterGlamourPreset
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public int GearsetIndex { get; set; } = -1;

    public ushort GlassesID { get; set; }

    public uint HairstyleID { get; set; }

    public List<BetterGlamourItem> Items { get; set; } = [];
}
