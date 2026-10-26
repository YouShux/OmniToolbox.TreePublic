using static OmniToolbox.TreePublic.MitigationMonitor;

namespace OmniToolbox.TreePublic;

public sealed partial class MitigationMonitor
{
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

}