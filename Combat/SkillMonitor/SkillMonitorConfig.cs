using FFXIVClientStructs.FFXIV.Component.GUI;

namespace OmniToolbox.TreePublic;

public sealed class SkillMonitorConfig
{
    public Vector2 Offset { get; set; } = new(17f, 0f);

    public float IconScale { get; set; } = DEFAULT_ICON_SCALE;

    public float IconSpacing { get; set; } = 3f;

    public int IconsPerRow { get; set; } = 12;

    public FontType Font { get; set; } = FontType.TrumpGothic;

    public SkillMonitorAlignment Alignment { get; set; }

    public bool ShowActive { get; set; } = true;

    public bool ShowOnCooldown { get; set; } = true;

    public bool ShowOffCooldown { get; set; } = true;

    public bool HideOutOfCombat { get; set; }

    public bool HideWeaponSheathed { get; set; } = true;

    public bool ShowGeneralSkillsFirst { get; set; } = true;

    public List<CustomAction> CustomActions { get; set; } = [];

    public Dictionary<uint, List<uint>> JobActionOrder { get; set; } = [];

    public Dictionary<uint, List<uint>> JobDisabledActions { get; set; } = new()
    {
        [0] = [3],
        [19] = [7531, 7548],
        [21] = [7531, 7548],
        [32] = [7531, 7548],
        [37] = [7531, 7548]
    };

    #region 常量

    internal const float DEFAULT_ICON_SCALE = 0.9f;

    #endregion

    public sealed record CustomAction(uint ActionID);
}
