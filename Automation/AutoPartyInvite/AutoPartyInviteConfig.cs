namespace OmniToolbox.TreePublic;

internal sealed class AutoPartyInviteConfig
{
    public bool UseWhitelist { get; set; } = true;

    public bool ChannelsInitialized { get; set; }

    public HashSet<ushort> EnabledChannels { get; set; } =
        new(Array.ConvertAll(AutoPartyInvite.ChannelOptions, static option => (ushort)option.Type));

    public HashSet<int> EnabledDigits { get; set; } = [1];

    public List<AutoPartyInvite.Trigger> CustomTriggers { get; set; } = [];

    public HashSet<uint> BlacklistTerritoryIds { get; set; } = [];

    public HashSet<uint> WhitelistTerritoryIds { get; set; } = [];

    public HashSet<uint> AllowedTerritoryIds { get; set; } = [];
}
