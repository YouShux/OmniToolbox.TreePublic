namespace OmniToolbox.TreePublic;

[Serializable]
public sealed class BetterBattlehornSettingsConfig
{
    public Dictionary<ulong, List<BattlehornPreset>> Characters { get; set; } = [];
}

[Serializable]
public sealed class BattlehornPreset
{
    public string Name { get; set; } = string.Empty;
    public byte[] Pets { get; set; } = new byte[3];
}
