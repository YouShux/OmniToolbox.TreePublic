using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed class MitigationMonitorConfig
{
    public bool Visible { get; set; } = true;

    public bool Locked { get; set; }

    public bool Collapsed { get; set; }

    public Vector2 Position { get; set; } = new(520f, 320f);

    public Vector2 CollapsedPosition { get; set; }

    public Vector2 Size { get; set; } = new(540f, 270f);

    public int ReplaySaveCount { get; set; } = 20;

    public float GlobalScale { get; set; } = 1f;

    public float Opacity { get; set; } = 0.7f;

    public bool ShowDotDamage { get; set; } = true;

    public int HideHotkey { get; set; }

    public MitigationHotkeyModifier HideHotkeyModifier { get; set; }

    public MitigationTargetDisplayMode TargetDisplayMode { get; set; } = MitigationTargetDisplayMode.JobIcon;

    public float TimeColumnWidth { get; set; } = 54f;

    public float ActionColumnWidth { get; set; } = 80f;

    public float TargetColumnWidth { get; set; } = 64f;

    public float DamageColumnWidth { get; set; } = 88f;

    public float MitigationColumnWidth { get; set; } = 64f;

    internal float EffectiveScale => MathF.Max(0.01f, GlobalScale);

    internal float ScaleValue => OmniTheme.ScaleValue * EffectiveScale;

    internal float Scale(float value) => value * ScaleValue;

    internal Vector2 Scale(Vector2 value) => value * ScaleValue;

    internal Vector2 Unscale(Vector2 value) => value / ScaleValue;
}
