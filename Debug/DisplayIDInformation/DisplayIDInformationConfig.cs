namespace OmniToolbox.TreePublic;

public sealed class DisplayIDInformationConfig
{
    public bool DisplayItemID { get; set; } = true;

    public bool DisplayActionID { get; set; } = true;

    public bool DisplayActionIDResolved { get; set; } = true;

    public bool DisplayActionIDOriginal { get; set; } = true;

    public bool DisplayCastBarActionID { get; set; } = true;

    public bool DisplayFlyTextDamageActionID { get; set; } = true;

    public bool DisplayTargetID { get; set; } = true;

    public bool DisplayTargetIDBattleNPC { get; set; } = true;

    public bool DisplayTargetIDEventNPC { get; set; } = true;

    public bool DisplayTargetIDCompanion { get; set; } = true;

    public bool DisplayTargetIDOthers { get; set; } = true;

    public bool DisplayStatusID { get; set; } = true;

    public bool DisplayStatusIDAppendToName { get; set; } = true;

    public bool DisplayWeatherID { get; set; } = true;

    public bool DisplayZoneInfo { get; set; } = true;

    public bool DisplayIconID { get; set; }
}
