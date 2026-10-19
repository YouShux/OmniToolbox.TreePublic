namespace OmniToolbox.TreePublic;

public sealed partial class SkillMonitor
{
    internal readonly record struct SkillMonitorDefinition(
        uint ActionID,
        uint StatusID,
        int CooldownMilliseconds,
        int ActiveMilliseconds,
        ulong ClassJobs,
        uint IconID,
        string Name,
        SkillMonitorGroup Group,
        bool IsCustom = false,
        uint UnlockLevel = 0)
    {
        public uint ConfigID => ActionID == 0 ? 0x80000000u | StatusID : ActionID;

        public bool StatusOnly => ActionID == 0;

        public bool IsFood => ActionID == 0 && StatusID == 48;

        public bool MatchesGroup(SkillMonitorGroup group) =>
            Group == group || ActionID == 7561 && group == SkillMonitorGroup.Dps;

        public bool AppliesTo(uint classJobID) =>
            classJobID < 64 && (ClassJobs & (1UL << (int)classJobID)) != 0;
    }

    public enum SkillMonitorGroup
    {
        Tank,
        Healer,
        Dps,
        General
    }

    internal enum SkillMonitorDisplayState
    {
        Unknown,
        Inactive,
        Ready,
        Cooldown,
        Active
    }

    internal readonly record struct SkillMonitorMember(uint EntityID, uint ClassJobID, byte Level, bool Visible);

    internal struct SkillMonitorRuntimeState
    {
        public bool CooldownKnown;
        public bool StatusActive;
        public long ReadyAtTick;
        public long ActiveUntilTick;
        public int CooldownMilliseconds;
        public int StatusRemainingMilliseconds;
        public int DisplaySeconds;
        public string? CooldownText;
        public string? StatusText;
        public float CooldownProgress;
        public SkillMonitorDisplayState DisplayState;
    }
}
