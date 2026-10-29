namespace OmniToolbox.TreePublic;

public sealed class HouseExpirationReminderConfig
{
    public bool ChatNotify { get; set; } = true;

    public bool PopupNotify { get; set; } = true;

    public bool PersonalNotify { get; set; } = true;

    public bool FreeCompanyNotify { get; set; }

    public int WarnDays { get; set; } = 10;

    public HashSet<string> ReminderKeys { get; set; } = [];

    public List<HouseExpirationReminderCharacterRecord> CharacterRecords { get; set; } = [];
}
