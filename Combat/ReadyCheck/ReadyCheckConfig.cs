namespace OmniToolbox.TreePublic;

public sealed class ReadyCheckConfig
{
    public bool ClearOnTerritoryChanged { get; set; } = true;

    public bool ClearOnCombat { get; set; }

    public bool ClearOnInstancedCombat { get; set; } = true;

    public bool ClearAfterTime { get; set; }

    public int ClearAfterSeconds { get; set; } = 60;

    public Vector2 PartyIconOffset { get; set; }

    public Vector2 AllianceIconOffset { get; set; }

    public Vector2 CrossWorldAllianceIconOffset { get; set; }

    public float PartyIconScale { get; set; } = 1f;

    public float AllianceIconScale { get; set; } = 1f;

    public float CrossWorldAllianceIconScale { get; set; } = 1f;
}
