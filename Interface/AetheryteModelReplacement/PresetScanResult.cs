using Newtonsoft.Json;

namespace OmniToolbox.TreePublic;

public sealed record PresetScanResult
(
    IReadOnlyList<AetherytePreset> Presets,
    int                            AetheryteRows,
    int                            Territories,
    int                            BgFiles,
    int                            EventFiles,
    int                            AetheryteInstances,
    int                            SharedGroups,
    int                            PathMatches,
    int                            BoundMatches,
    int                            ScannedTerritories
)
{
    [JsonIgnore]
    public bool IsComplete => ScannedTerritories >= Territories;
}
