namespace OmniToolbox.TreePublic;

public sealed record AutoLoginTarget
(
    uint   WorldID,
    string CharacterName
)
{
    public bool Enabled { get; set; } = true;
    public ulong AccountID { get; set; }
}
