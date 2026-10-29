namespace OmniToolbox.TreePublic;

public sealed class FoodReminderConfig
{
    public int TargetNameMode { get; set; } = 1;

    public string PartyMessage { get; set; } = "吃一下食物！";

    public int ThresholdSeconds { get; set; } = 480;

    public bool UseWhitelist { get; set; }

    public HashSet<uint> BlacklistTerritoryIds { get; set; } = [];

    public HashSet<uint> WhitelistTerritoryIds { get; set; } = [];

    public HashSet<uint> AllowedTerritoryIds { get; set; } = [];

}
