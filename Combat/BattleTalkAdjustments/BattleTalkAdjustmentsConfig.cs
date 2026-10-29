namespace OmniToolbox.TreePublic;

public sealed class BattleTalkAdjustmentsConfig
{
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    public float Scale { get; set; } = 1f;

    public bool UseWhitelist { get; set; }

    public HashSet<uint> BlacklistTerritoryIDs { get; set; } = [];

    public HashSet<uint> WhitelistTerritoryIDs { get; set; } = [];
}
