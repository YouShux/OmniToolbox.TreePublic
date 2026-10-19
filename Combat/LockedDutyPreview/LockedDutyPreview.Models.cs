namespace OmniToolbox.TreePublic;

public sealed partial class LockedDutyPreview
{
    internal readonly record struct LockedDutyPreviewDefinition(
        uint ContentFinderConditionID,
        uint InstanceContentID,
        string Name);

    internal readonly record struct LockedDutyPreviewDuty(
        uint ContentFinderConditionID,
        string Name,
        bool IsUnlocked,
        bool IsCompleted);

    internal readonly record struct LockedDutyPreviewRow(
        uint ContentFinderConditionID,
        string Name,
        bool IsExcluded);

    internal enum LockedDutyPreviewView
    {
        Locked,
        Incomplete,
        Excluded
    }
}
