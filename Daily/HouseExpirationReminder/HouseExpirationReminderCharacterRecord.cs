namespace OmniToolbox.TreePublic;

public sealed class HouseExpirationReminderCharacterRecord
{
    public string CharacterKey { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string HomeWorld { get; set; } = string.Empty;

    public ulong ContentID { get; set; }

    public DateTime PersonalLastVisitUTC { get; set; } = DateTime.MinValue;

    public DateTime FreeCompanyLastVisitUTC { get; set; } = DateTime.MinValue;

    public ulong PersonalHouseID { get; set; }

    public ulong FreeCompanyHouseID { get; set; }
}