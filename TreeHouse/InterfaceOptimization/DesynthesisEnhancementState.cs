using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal enum DesynthesisStatus
{
    Green,
    Yellow,
    Red
}

internal readonly record struct DesynthesisLevelInfo(
    int Current,
    int Recommended,
    DesynthesisStatus Status);

internal static class DesynthesisEnhancementState
{
    private const int GreenThreshold = 50;

    public static DesynthesisLevelInfo Calculate(float currentLevel, uint recommendedLevel)
    {
        var current = (int)MathF.Floor(currentLevel);
        var recommended = (int)recommendedLevel;
        return new(
            current,
            recommended,
            current >= recommended + GreenThreshold
                ? DesynthesisStatus.Green
                : current >= recommended
                    ? DesynthesisStatus.Yellow
                    : DesynthesisStatus.Red);
    }

    public static uint NormalizeItemId(uint itemId) =>
        itemId < 2_000_000 ? itemId % 500_000 : itemId;

    public static ushort GetColorKey(DesynthesisStatus status) => status switch
    {
        DesynthesisStatus.Green => 43,
        DesynthesisStatus.Yellow => 506,
        DesynthesisStatus.Red => 14,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static string FormatTooltipLine(DesynthesisLevelInfo level, string jobName) =>
        OmniLoc.Get("Feature.DesynthesisEnhancement.TooltipPrefix") + FormatTooltipValue(level, jobName);

    public static string FormatTooltipValue(DesynthesisLevelInfo level, string jobName) => string.Format(
        OmniLoc.Get(string.IsNullOrWhiteSpace(jobName)
            ? "Feature.DesynthesisEnhancement.TooltipValueWithoutJob"
            : "Feature.DesynthesisEnhancement.TooltipValue"),
        level.Recommended,
        level.Current,
        jobName);
}
