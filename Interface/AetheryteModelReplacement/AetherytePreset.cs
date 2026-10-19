using Newtonsoft.Json;

namespace OmniToolbox.TreePublic;

public sealed record AetherytePreset(
    uint RowID,
    uint TerritoryID,
    string Name,
    string TerritoryName,
    string SharedGroupPath)
{
    [JsonIgnore]
    public string DisplayName => $"{Name} ({TerritoryName})";
}
