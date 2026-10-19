using Dalamud.Interface.Textures;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MitigationMonitor : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("MitigationMonitorTitle"),
        Description = OmniLoc.Get("MitigationMonitorDescription"),
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Combat/MitigationMonitor-1.png"
    };

    public override bool HasSettings => true;

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

    public override bool DrawSettings() => Panel.Draw(this, config, hotkey);

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