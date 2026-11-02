namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private sealed record LootParticipant(uint EntityID, string PlayerName, uint HomeWorldID,
                                          LootChoice Choice, int? Value);

    private sealed record LootObservation(DateTimeOffset Time, LootParticipant Player, bool IsAward);

    private sealed record LootRecord(Guid ID, DateTimeOffset Time, uint ChestID, uint ChestItemIndex,
                                    uint ItemID, uint Count, LootStatus Status, LootParticipant[] Participants,
                                    LootParticipant? Winner, LootObservation[] Unassigned)
    {
        public uint TerritoryID { get; init; }
        public uint DutyID { get; init; }
        public Guid SessionID { get; init; }
    }

    private enum LootChoice
    {
        Pending,
        Selected,
        Need,
        Greed,
        Pass
    }

    private enum LootStatus
    {
        Pending,
        Awarded,
        Ended,
        Interrupted
    }

    #region 常量

    private const int MAX_LOOT_PARTICIPANTS = 48;
    private const int MAX_UNASSIGNED_EVENTS = 64;
    private const int MAX_LOOT_SESSIONS = 200;

    #endregion
}
