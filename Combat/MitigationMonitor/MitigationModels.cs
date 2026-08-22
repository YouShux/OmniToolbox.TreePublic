using System.Reflection;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

[Obfuscation(Exclude = true, ApplyToMembers = true, Feature = "renaming")]
internal enum MitigationRecordKind
{
    Damage,
    Defeated,
    Wipe
}

[Obfuscation(Exclude = true, ApplyToMembers = true, Feature = "renaming")]
internal enum MitigationStatusCategory
{
    Vulnerability,
    Mitigation,
    Shield
}

[Obfuscation(Exclude = true, ApplyToMembers = true, Feature = "renaming")]
internal enum DamageSourceKind
{
    Skill,
    AutoAttack,
    Dot
}

internal enum MitigationStatusSourceKind
{
    Target,
    CurrentSource,
    VisibleEnemyFallback
}

[Obfuscation(Exclude = true, ApplyToMembers = true, Feature = "renaming")]
internal enum DamageKind
{
    Physical,
    Magical,
    Special
}

internal enum ActionEffectType : byte
{
    Miss = 1,
    Damage = 3,
    BlockedDamage = 5,
    ParriedDamage = 6,
    Invulnerable = 7
}

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

internal readonly record struct StatusDisplayInfo(string Name, string Description, uint IconID);

internal readonly record struct DamageActionDisplay(string Name, DamageSourceKind SourceKind);

internal readonly record struct CachedStatus(uint StatusID, uint SourceID, ushort Param, float RemainingTime);

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

internal readonly record struct MitigationCombatHistory(
    string Key,
    MitigationRecord[] Records,
    DateTime StartUTC,
    DateTime EndUTC,
    string ElapsedLabel,
    string ZoneName);

internal readonly record struct FriendlySnapshot(
    uint EntityID,
    bool IsAlive,
    string TargetName,
    string TargetShortName,
    string JobName,
    uint JobRowID,
    uint ShieldValue,
    uint CurrentHp);

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

internal static class MitigationText
{
    public static string FormatElapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

    public static string GetDamageKindText(DamageKind kind) => kind switch
    {
        DamageKind.Physical => OmniLoc.Get("Feature.MitigationMonitor.DamageKind.Physical"),
        DamageKind.Magical => OmniLoc.Get("Feature.MitigationMonitor.DamageKind.Magical"),
        _ => OmniLoc.Get("Feature.MitigationMonitor.DamageKind.Special")
    };

    public static string BuildTargetName(string jobName, string targetName)
    {
        if (string.IsNullOrWhiteSpace(jobName))
        {
            return string.IsNullOrWhiteSpace(targetName)
                ? OmniLoc.Get("Feature.MitigationMonitor.TargetFallback")
                : targetName;
        }

        return string.IsNullOrWhiteSpace(targetName) ? jobName : $"{jobName} {targetName}";
    }
}
