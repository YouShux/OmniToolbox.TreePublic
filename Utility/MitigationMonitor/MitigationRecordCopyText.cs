using System.Globalization;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

internal static class MitigationRecordCopyText
{
    public static string Build(MitigationRecord record) => record.Kind switch
    {
        MitigationRecordKind.Defeated => BuildDefeated(record),
        MitigationRecordKind.Wipe => string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get("Feature.MitigationMonitor.Copy.Wipe"),
            MitigationText.FormatElapsed(record.Elapsed),
            record.ActionName),
        _ => BuildDamage(record)
    };

    private static string BuildDamage(MitigationRecord record)
    {
        var source = string.IsNullOrWhiteSpace(record.SourceName)
            ? OmniLoc.Get("Feature.MitigationMonitor.SourceFallback")
            : record.SourceName;
        var target = string.IsNullOrWhiteSpace(record.TargetName)
            ? MitigationText.BuildTargetName(record.TargetJobName, record.TargetShortName)
            : record.TargetName;
        if (record.Missed)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.MitigationMonitor.Copy.DamageMiss"),
                MitigationText.FormatElapsed(record.Elapsed),
                source,
                record.ActionName,
                target,
                OmniNumberFormatter.Format(record.Damage));
        }

        if (record.Invulnerable)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.MitigationMonitor.Copy.DamageInvulnerable"),
                MitigationText.FormatElapsed(record.Elapsed),
                source,
                record.ActionName,
                target,
                OmniNumberFormatter.Format(record.Damage),
                MitigationText.GetDamageKindText(record.DamageKind));
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get("Feature.MitigationMonitor.Copy.Damage"),
            MitigationText.FormatElapsed(record.Elapsed),
            source,
            record.ActionName,
            target,
            OmniNumberFormatter.Format(record.Damage),
            MitigationText.GetDamageKindText(record.DamageKind),
            record.MitigationPercent,
            OmniNumberFormatter.Format(record.ShieldValue),
            BuildStatusSuffix(record));
    }

    private static string BuildDefeated(MitigationRecord record)
    {
        var key = (record.CurrentHp > 0, record.ShieldValue > 0, record.MitigationPercent > 0f) switch
        {
            (true, true, true) => "Feature.MitigationMonitor.Copy.Defeated.HpShieldMitigation",
            (true, true, false) => "Feature.MitigationMonitor.Copy.Defeated.HpShield",
            (true, false, true) => "Feature.MitigationMonitor.Copy.Defeated.HpMitigation",
            (false, true, true) => "Feature.MitigationMonitor.Copy.Defeated.ShieldMitigation",
            (true, false, false) => "Feature.MitigationMonitor.Copy.Defeated.Hp",
            (false, true, false) => "Feature.MitigationMonitor.Copy.Defeated.Shield",
            (false, false, true) => "Feature.MitigationMonitor.Copy.Defeated.Mitigation",
            _ => "Feature.MitigationMonitor.Copy.Defeated.Base"
        };
        return string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get(key),
            MitigationText.FormatElapsed(record.Elapsed),
            record.ActionName,
            OmniNumberFormatter.Format(record.CurrentHp),
            OmniNumberFormatter.Format(record.ShieldValue),
            record.MitigationPercent,
            BuildStatusSuffix(record));
    }

    private static string BuildStatusSuffix(MitigationRecord record)
    {
        var statuses = BuildStatusText(record);
        return string.IsNullOrWhiteSpace(statuses)
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.MitigationMonitor.Copy.StatusSuffix"),
                statuses);
    }

    private static string BuildStatusText(MitigationRecord record)
    {
        var parts = new List<string>(record.Statuses.Length + 3);
        foreach (var status in record.Statuses)
        {
            if (!string.IsNullOrWhiteSpace(status.Name))
            {
                parts.Add(status.Name);
            }
        }

        if (record.Blocked)
        {
            parts.Add(OmniLoc.Get("Feature.MitigationMonitor.Status.Blocked"));
        }

        if (record.Missed)
        {
            parts.Add(OmniLoc.Get("Feature.MitigationMonitor.Damage.Miss"));
        }

        if (record.Invulnerable)
        {
            parts.Add(OmniLoc.Get("Feature.MitigationMonitor.Damage.Invulnerable"));
        }

        return string.Join(' ', parts);
    }
}
