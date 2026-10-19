namespace OmniToolbox.TreePublic;

public sealed class RaiseDispelEnhancementConfig
{
    public RaiseDispelDisplayRange DisplayRange { get; set; } = RaiseDispelDisplayRange.Partial;

    public RaiseDispelDisplayOptions Raise { get; set; } = new();

    public RaiseDispelDisplayOptions Dispel { get; set; } = new();

    public RaiseDispelDisplayOptions GetRaiseOptions() => Raise;

    public RaiseDispelDisplayOptions GetDispelOptions() => Dispel;

    public bool ShowRaiseOnList { get; set; } = true;

    public bool ShowDispelOnList { get; set; } = true;

    public float IconScale { get; set; } = 1f;

    public float WorldTextScale { get; set; } = 1f;

    public uint RaiseListColor { get; set; } = 0x60FF0000;

    public uint DispelListColor { get; set; } = 0x600000FF;

    public uint RaiseWorldColor { get; set; } = 0xC8143C0A;

    public uint SelfRaiseBackgroundColor { get; set; } = 0xF53B2126;

    public uint OtherRaiseBackgroundColor { get; set; } = 0xC8FF0000;

    public uint DispelWorldColor { get; set; } = 0xC8140A3C;
}