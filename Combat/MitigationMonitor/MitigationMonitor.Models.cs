namespace OmniToolbox.TreePublic;

public sealed partial class MitigationMonitor
{
    internal readonly record struct MitigationDefinition(uint StatusID, int Physical, int Magical, int Special)
    {
        public int PercentValueFor(DamageKind kind) => kind switch
        {
            DamageKind.Physical => Physical,
            DamageKind.Magical => Magical,
            _ => Math.Max(Physical, Magical)
        };

        public int DisplayValueFor(DamageKind kind)
        {
            var value = PercentValueFor(kind);
            return value > 0 ? value : Special;
        }

        public bool HasPercentMitigation => Physical > 0 || Magical > 0;
    }

    internal readonly record struct ActionDisplayInfo(string Name);

    internal readonly record struct ActiveMitigation(
        uint StatusID,
        string Name,
        uint IconID,
        float RemainingSeconds,
        int Value,
        int StackCount,
        MitigationStatusCategory Category,
        bool Useful,
        bool AffectsPercent);

    internal readonly record struct DamageActionDisplay(string Name, DamageSourceKind SourceKind);

    internal enum DamageKind
    {
        Physical,
        Magical,
        Special
    }

    internal enum DamageSourceKind
    {
        Skill,
        AutoAttack,
        Dot
    }

    internal readonly record struct FriendlySnapshot(
        uint EntityID,
        bool IsAlive,
        string TargetName,
        string TargetShortName,
        string JobName,
        uint JobRowID,
        uint ShieldValue,
        uint CurrentHp);

    internal readonly record struct MitigationCombatHistory(
        string Key,
        MitigationRecord[] Records,
        DateTime StartUTC,
        DateTime EndUTC,
        string ElapsedLabel,
        string ZoneName);

    internal enum MitigationRecordKind
    {
        Damage,
        Defeated,
        Wipe
    }

    internal readonly record struct MitigationRecord(
        MitigationRecordKind Kind,
        DateTime TimestampUTC,
        TimeSpan Elapsed,
        string ActionName,
        string SourceName,
        uint TargetEntityID,
        string TargetName,
        string TargetShortName,
        string TargetJobName,
        uint TargetJobRowID,
        DamageSourceKind SourceKind,
        uint Damage,
        DamageKind DamageKind,
        bool Blocked,
        bool Parried,
        bool Missed,
        bool Invulnerable,
        float MitigationPercent,
        ActiveMitigation[] Statuses,
        uint ShieldValue,
        uint CurrentHp);

    internal enum MitigationStatusCategory
    {
        Vulnerability,
        Mitigation,
        Shield
    }

    internal enum MitigationStatusSourceKind
    {
        Target,
        CurrentSource
    }

    internal readonly record struct MitigationTableLayout(
        float Time,
        float Action,
        float Target,
        float Damage,
        float Mitigation,
        float Status);

    internal readonly record struct StatusDisplayInfo(string Name, uint IconID);

    internal struct TargetDamageResult
    {
        public bool Found;
        public uint Damage;
        public DamageKind Kind;
        public bool Blocked;
        public bool Parried;
        public bool Missed;
        public bool Invulnerable;
    }
}
