using Dalamud.Interface.Textures;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class MitigationMonitor : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("MitigationMonitorTitle"),
        Description = OmniLoc.Get("MitigationMonitorDescription"),
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Combat/MitigationMonitor-1.png"
    };

    private readonly MitigationMonitorConfig config;
    private readonly MitigationCombatLog combatLog;
    private readonly MitigationRecorder recorder;
    private readonly MitigationMonitorHotkey hotkey;
    private readonly MitigationMonitorOverlay overlay;
    private FeatureLifetime? runtimeLifetime;

    public MitigationMonitor(MitigationMonitorConfig config, Action saveConfig)
    {
        this.config = config;
        NormalizeConfig();
        combatLog = new(config.ReplaySaveCount);
        recorder = new(combatLog);
        hotkey = new(config, saveConfig);
        overlay = new(config, combatLog, new(), saveConfig);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => MitigationMonitorPanel.Draw(this, config, hotkey);

    public override bool ResetSettings()
    {
        var defaults = new MitigationMonitorConfig();
        config.Visible = defaults.Visible;
        config.Locked = defaults.Locked;
        config.Collapsed = defaults.Collapsed;
        config.Position = defaults.Position;
        config.CollapsedPosition = defaults.CollapsedPosition;
        config.Size = defaults.Size;
        config.ReplaySaveCount = defaults.ReplaySaveCount;
        config.GlobalScale = defaults.GlobalScale;
        config.Opacity = defaults.Opacity;
        config.ShowDotDamage = defaults.ShowDotDamage;
        config.HideHotkey = defaults.HideHotkey;
        config.HideHotkeyModifier = defaults.HideHotkeyModifier;
        config.TargetDisplayMode = defaults.TargetDisplayMode;
        config.TimeColumnWidth = defaults.TimeColumnWidth;
        config.ActionColumnWidth = defaults.ActionColumnWidth;
        config.TargetColumnWidth = defaults.TargetColumnWidth;
        config.DamageColumnWidth = defaults.DamageColumnWidth;
        config.MitigationColumnWidth = defaults.MitigationColumnWidth;
        NormalizeConfig();
        overlay.RequestLayoutRestore();
        return true;
    }

    public void SetOpenSettingsAction(Action action) => overlay.SetOpenSettingsAction(action);

    internal void ClearRecords()
    {
        combatLog.ClearAll();
        overlay.ClearSelection();
    }

    internal void SetReplaySaveCount(int value)
    {
        config.ReplaySaveCount = Math.Clamp(value, 1, 300);
        combatLog.SetReplaySaveCount(config.ReplaySaveCount);
    }

    protected override void OnEnable()
    {
        NormalizeConfig();
        combatLog.SetReplaySaveCount(config.ReplaySaveCount);
        var lifetime = new FeatureLifetime();
        try
        {
            hotkey.Register(lifetime);
            recorder.Register(lifetime);
            var windowManager = WindowManager.Instance();
            _ = windowManager.WindowSystem;
            windowManager.PostDraw += overlay.Draw;
            lifetime.Add(() => windowManager.PostDraw -= overlay.Draw);
            runtimeLifetime = lifetime;
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                hotkey.Reset();
                recorder.Reset();
                runtimeLifetime = null;
            }

            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            hotkey.Reset();
            recorder.Reset();
            combatLog.ClearAll();
            overlay.ResetRuntime();
        }
    }

    private void NormalizeConfig()
    {
        config.Size = new(
            MathF.Max(config.Size.X <= 0f ? 540f : config.Size.X, MitigationMonitorOverlay.MinimumWidth),
            MathF.Max(config.Size.Y <= 0f ? 270f : config.Size.Y, 160f));
        if (config.CollapsedPosition == Vector2.Zero)
        {
            config.CollapsedPosition = config.Position + new Vector2(
                MathF.Max(0f, config.Size.X - 108f),
                6f);
        }

        config.ReplaySaveCount = Math.Clamp(config.ReplaySaveCount, 1, 300);
        config.GlobalScale = Math.Clamp(config.GlobalScale, 0f, 3f);
        config.Opacity = Math.Clamp(config.Opacity, 0f, 1f);
        if (!MitigationMonitorHotkey.IsBindable(config.HideHotkey))
        {
            config.HideHotkey = 0;
        }

        if (!Enum.IsDefined(config.HideHotkeyModifier))
        {
            config.HideHotkeyModifier = MitigationHotkeyModifier.None;
        }

        if (!Enum.IsDefined(config.TargetDisplayMode))
        {
            config.TargetDisplayMode = MitigationTargetDisplayMode.JobIcon;
        }

        config.TimeColumnWidth = Math.Clamp(config.TimeColumnWidth <= 0f ? 54f : config.TimeColumnWidth, 44f, 220f);
        config.ActionColumnWidth = Math.Clamp(config.ActionColumnWidth <= 0f ? 80f : config.ActionColumnWidth, 70f, 420f);
        config.TargetColumnWidth = Math.Clamp(config.TargetColumnWidth <= 0f ? 64f : config.TargetColumnWidth, 60f, 240f);
        config.DamageColumnWidth = Math.Clamp(config.DamageColumnWidth <= 0f ? 88f : config.DamageColumnWidth, 42f, 260f);
        config.MitigationColumnWidth = Math.Clamp(config.MitigationColumnWidth <= 0f ? 64f : config.MitigationColumnWidth, 34f, 180f);
    }
}

internal static class MitigationMonitorPanel
{
    public static bool Draw(
        MitigationMonitor feature,
        MitigationMonitorConfig config,
        MitigationMonitorHotkey hotkey)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##mitigationMonitorSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.NoClip,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##mitigationSettingsColumn1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##mitigationSettingsColumn2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##mitigationSettingsColumn3", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##mitigationSettingsColumn4", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var showDotDamage = config.ShowDotDamage;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.MitigationMonitor.ShowDotDamage"), ref showDotDamage))
        {
            config.ShowDotDamage = showDotDamage;
            changed = true;
        }

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.TargetDisplayMode"));
        ImGui.SameLine();
        changed |= DrawTargetMode(
            config,
            MitigationTargetDisplayMode.CharacterName,
            "Feature.MitigationMonitor.TargetMode.CharacterName");
        ImGui.SameLine();
        changed |= DrawTargetMode(
            config,
            MitigationTargetDisplayMode.JobName,
            "Feature.MitigationMonitor.TargetMode.JobName");
        ImGui.SameLine();
        changed |= DrawTargetIconMode(config, MitigationTargetDisplayMode.JobIcon, 62028, "Feature.MitigationMonitor.TargetMode.JobIcon");
        ImGui.SameLine();
        changed |= DrawTargetIconMode(config, MitigationTargetDisplayMode.JobIconV2, 62128, "Feature.MitigationMonitor.TargetMode.JobIconV2");
        ImGui.SameLine();
        changed |= DrawTargetIconMode(config, MitigationTargetDisplayMode.JobIconV3, 62409, "Feature.MitigationMonitor.TargetMode.JobIconV3");

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var globalScale = config.GlobalScale;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.GlobalScale"));
        ImGui.SameLine();
        if (OmniControls.SliderFloat(
                "##mitigationGlobalScale",
                ref globalScale,
                0f,
                3f,
                "%.1fx",
                OmniTheme.Scale(150f)))
        {
            config.GlobalScale = globalScale;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.TableNextColumn();
        var opacity = config.Opacity * 100f;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.Opacity"));
        ImGui.SameLine();
        if (OmniControls.SliderFloat(
                "##mitigationOpacity",
                ref opacity,
                0f,
                100f,
                "%.0f%%",
                OmniTheme.Scale(150f)))
        {
            config.Opacity = opacity / 100f;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.TableNextColumn();
        changed |= hotkey.DrawModifierSetting();
        ImGui.TableNextColumn();
        changed |= hotkey.DrawKeySetting();

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var replaySaveCount = config.ReplaySaveCount;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.ReplaySaveCount"));
        ImGui.SameLine();
        if (OmniControls.InputInt(
                "##mitigationReplaySaveCount",
                ref replaySaveCount,
                OmniTheme.Scale(96f)))
        {
            feature.SetReplaySaveCount(replaySaveCount);
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.MitigationMonitor.Clear"), false))
        {
            feature.ClearRecords();
        }

        return changed;
    }

    private static bool DrawTargetMode(
        MitigationMonitorConfig config,
        MitigationTargetDisplayMode mode,
        string labelKey)
    {
        if (!OmniControls.SmallButton(OmniLoc.Get(labelKey), config.TargetDisplayMode == mode))
        {
            return false;
        }

        config.TargetDisplayMode = mode;
        return true;
    }

    private static bool DrawTargetIconMode(
        MitigationMonitorConfig config,
        MitigationTargetDisplayMode mode,
        uint iconID,
        string tooltipKey)
    {
        var height = MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight());
        var size = new Vector2(height);
        var clicked = OmniControls.SmallButton($"##MitigationTargetDisplay{(int)mode}", config.TargetDisplayMode == mode, size);
        var texture = DalamudServices.TextureProvider.GetFromGameIcon(new GameIconLookup(iconID)).GetWrapOrDefault();
        if (texture is not null)
        {
            var iconSize = height - OmniTheme.Scale(6f);
            var min = ImGui.GetItemRectMin() + (size - new Vector2(iconSize)) * 0.5f;
            ImGui.GetWindowDrawList().AddImage(texture.Handle, min, min + new Vector2(iconSize));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(OmniLoc.Get(tooltipKey));
        }

        if (clicked)
        {
            config.TargetDisplayMode = mode;
        }

        return clicked;
    }
}

[Serializable]
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

public enum MitigationHotkeyModifier
{
    None,
    Control,
    Shift,
    Alt
}

public enum MitigationTargetDisplayMode
{
    JobName = 1,
    JobIcon = 2,
    JobIconV2 = 3,
    JobIconV3 = 4,
    CharacterName = 5
}
