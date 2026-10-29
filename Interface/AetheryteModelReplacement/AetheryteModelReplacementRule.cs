namespace OmniToolbox.TreePublic;

public sealed class AetheryteModelReplacementRule
{
    public AetheryteModelReplacementRule()
    {
    }

    public AetheryteModelReplacementRule
    (
        string sourceSharedGroupPath,
        string targetSharedGroupPath
    )
    {
        SourceSharedGroupPath = sourceSharedGroupPath;
        TargetSharedGroupPath = targetSharedGroupPath;
    }

    public uint SourceTerritoryID { get; set; }

    public uint SourceAetheryteRowID { get; set; }

    public bool Enabled { get; set; } = true;

    public string SourceSharedGroupPath { get; set; } = string.Empty;

    public string TargetSharedGroupPath { get; set; } = string.Empty;
}
