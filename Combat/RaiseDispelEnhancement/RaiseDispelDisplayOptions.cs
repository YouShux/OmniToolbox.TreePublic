using Newtonsoft.Json;

namespace OmniToolbox.TreePublic;

public sealed class RaiseDispelDisplayOptions
{
    public bool ShowPartyFrame { get; set; } = true;

    public bool ShowAllianceFrame { get; set; } = true;

    public bool ShowWorldIcon { get; set; } = true;

    public bool ShowWorldText { get; set; } = true;

    public bool ShowCasterName { get; set; } = true;

    public bool ShowCastProgress { get; set; } = true;

    [JsonIgnore]
    public bool Enabled => ShowPartyFrame || ShowAllianceFrame || ShowWorldIcon || ShowWorldText;
}
