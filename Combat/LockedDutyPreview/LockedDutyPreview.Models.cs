namespace OmniToolbox.TreePublic;

public sealed partial class LockedDutyPreview
{
    private readonly record struct LockedDutyPreviewDefinition
    (
        uint   ContentFinderConditionID,
        uint   InstanceContentID,
        string Name
    );

    private readonly record struct LockedDutyPreviewDuty
    (
        uint   ContentFinderConditionID,
        string Name,
        bool   IsUnlocked,
        bool   IsCompleted
    );

    private readonly record struct LockedDutyPreviewRow
    (
        uint   ContentFinderConditionID,
        string Name,
        bool   IsExcluded
    );

    private enum LockedDutyPreviewView
    {
        Locked,
        Incomplete,
        Excluded
    }
}
