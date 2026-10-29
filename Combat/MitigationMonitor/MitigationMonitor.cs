using Dalamud.Interface.Textures;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class MitigationMonitor : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Combat/MitigationMonitor-1.png"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "MitigationMonitorTitle",
            ["Description"] = "MitigationMonitorDescription",
            ["Feature.MitigationMonitor.Action.AutoAttack"] = "Feature.MitigationMonitor.Action.AutoAttack",
            ["Feature.MitigationMonitor.Clear"] = "Feature.MitigationMonitor.Clear",
            ["Feature.MitigationMonitor.Collapse"] = "Feature.MitigationMonitor.Collapse",
            ["Feature.MitigationMonitor.Column.Action"] = "Feature.MitigationMonitor.Column.Action",
            ["Feature.MitigationMonitor.Column.Damage"] = "Feature.MitigationMonitor.Column.Damage",
            ["Feature.MitigationMonitor.Column.Mitigation"] = "Feature.MitigationMonitor.Column.Mitigation",
            ["Feature.MitigationMonitor.Column.Status"] = "Feature.MitigationMonitor.Column.Status",
            ["Feature.MitigationMonitor.Column.Target"] = "Feature.MitigationMonitor.Column.Target",
            ["Feature.MitigationMonitor.Column.Time"] = "Feature.MitigationMonitor.Column.Time",
            ["Feature.MitigationMonitor.Copy.Damage"] = "Feature.MitigationMonitor.Copy.Damage",
            ["Feature.MitigationMonitor.Copy.DamageInvulnerable"] = "Feature.MitigationMonitor.Copy.DamageInvulnerable",
            ["Feature.MitigationMonitor.Copy.DamageMiss"] = "Feature.MitigationMonitor.Copy.DamageMiss",
            ["Feature.MitigationMonitor.Copy.Defeated.Base"] = "Feature.MitigationMonitor.Copy.Defeated.Base",
            ["Feature.MitigationMonitor.Copy.Defeated.Hp"] = "Feature.MitigationMonitor.Copy.Defeated.Hp",
            ["Feature.MitigationMonitor.Copy.Defeated.HpMitigation"] = "Feature.MitigationMonitor.Copy.Defeated.HpMitigation",
            ["Feature.MitigationMonitor.Copy.Defeated.HpShield"] = "Feature.MitigationMonitor.Copy.Defeated.HpShield",
            ["Feature.MitigationMonitor.Copy.Defeated.HpShieldMitigation"] = "Feature.MitigationMonitor.Copy.Defeated.HpShieldMitigation",
            ["Feature.MitigationMonitor.Copy.Defeated.Mitigation"] = "Feature.MitigationMonitor.Copy.Defeated.Mitigation",
            ["Feature.MitigationMonitor.Copy.Defeated.Shield"] = "Feature.MitigationMonitor.Copy.Defeated.Shield",
            ["Feature.MitigationMonitor.Copy.Defeated.ShieldMitigation"] = "Feature.MitigationMonitor.Copy.Defeated.ShieldMitigation",
            ["Feature.MitigationMonitor.Copy.StatusSuffix"] = "Feature.MitigationMonitor.Copy.StatusSuffix",
            ["Feature.MitigationMonitor.Copy.Success"] = "Feature.MitigationMonitor.Copy.Success",
            ["Feature.MitigationMonitor.Copy.Wipe"] = "Feature.MitigationMonitor.Copy.Wipe",
            ["Feature.MitigationMonitor.Damage.Invulnerable"] = "Feature.MitigationMonitor.Damage.Invulnerable",
            ["Feature.MitigationMonitor.Damage.Miss"] = "Feature.MitigationMonitor.Damage.Miss",
            ["Feature.MitigationMonitor.DamageKind.Magical"] = "Feature.MitigationMonitor.DamageKind.Magical",
            ["Feature.MitigationMonitor.DamageKind.Physical"] = "Feature.MitigationMonitor.DamageKind.Physical",
            ["Feature.MitigationMonitor.DamageKind.Special"] = "Feature.MitigationMonitor.DamageKind.Special",
            ["Feature.MitigationMonitor.Expand"] = "Feature.MitigationMonitor.Expand",
            ["Feature.MitigationMonitor.GlobalScale"] = "Feature.MitigationMonitor.GlobalScale",
            ["Feature.MitigationMonitor.History.Empty"] = "Feature.MitigationMonitor.History.Empty",
            ["Feature.MitigationMonitor.History.Export"] = "Feature.MitigationMonitor.History.Export",
            ["Feature.MitigationMonitor.History.Import"] = "Feature.MitigationMonitor.History.Import",
            ["Feature.MitigationMonitor.History.Imported"] = "Feature.MitigationMonitor.History.Imported",
            ["Feature.MitigationMonitor.History.Item"] = "Feature.MitigationMonitor.History.Item",
            ["Feature.MitigationMonitor.History.NoImportFiles"] = "Feature.MitigationMonitor.History.NoImportFiles",
            ["Feature.MitigationMonitor.History.Realtime"] = "Feature.MitigationMonitor.History.Realtime",
            ["Feature.MitigationMonitor.History.Title"] = "Feature.MitigationMonitor.History.Title",
            ["Feature.MitigationMonitor.History.UnknownZone"] = "Feature.MitigationMonitor.History.UnknownZone",
            ["Feature.MitigationMonitor.History.Unnamed"] = "Feature.MitigationMonitor.History.Unnamed",
            ["Feature.MitigationMonitor.Hotkey.Capturing"] = "Feature.MitigationMonitor.Hotkey.Capturing",
            ["Feature.MitigationMonitor.Hotkey.Clear"] = "Feature.MitigationMonitor.Hotkey.Clear",
            ["Feature.MitigationMonitor.Hotkey.Key"] = "Feature.MitigationMonitor.Hotkey.Key",
            ["Feature.MitigationMonitor.Hotkey.Modifier"] = "Feature.MitigationMonitor.Hotkey.Modifier",
            ["Feature.MitigationMonitor.Hotkey.Modifier.Alt"] = "Feature.MitigationMonitor.Hotkey.Modifier.Alt",
            ["Feature.MitigationMonitor.Hotkey.Modifier.Control"] = "Feature.MitigationMonitor.Hotkey.Modifier.Control",
            ["Feature.MitigationMonitor.Hotkey.Modifier.None"] = "Feature.MitigationMonitor.Hotkey.Modifier.None",
            ["Feature.MitigationMonitor.Hotkey.Modifier.Shift"] = "Feature.MitigationMonitor.Hotkey.Modifier.Shift",
            ["Feature.MitigationMonitor.Hotkey.Unbound"] = "Feature.MitigationMonitor.Hotkey.Unbound",
            ["Feature.MitigationMonitor.Lock"] = "Feature.MitigationMonitor.Lock",
            ["Feature.MitigationMonitor.NoData"] = "Feature.MitigationMonitor.NoData",
            ["Feature.MitigationMonitor.Opacity"] = "Feature.MitigationMonitor.Opacity",
            ["Feature.MitigationMonitor.Record.DefeatedBy"] = "Feature.MitigationMonitor.Record.DefeatedBy",
            ["Feature.MitigationMonitor.Record.DefeatedGeneric"] = "Feature.MitigationMonitor.Record.DefeatedGeneric",
            ["Feature.MitigationMonitor.Record.Wipe"] = "Feature.MitigationMonitor.Record.Wipe",
            ["Feature.MitigationMonitor.ReplaySaveCount"] = "Feature.MitigationMonitor.ReplaySaveCount",
            ["Feature.MitigationMonitor.Settings"] = "Feature.MitigationMonitor.Settings",
            ["Feature.MitigationMonitor.ShowDotDamage"] = "Feature.MitigationMonitor.ShowDotDamage",
            ["Feature.MitigationMonitor.SourceFallback"] = "Feature.MitigationMonitor.SourceFallback",
            ["Feature.MitigationMonitor.Status.Blocked"] = "Feature.MitigationMonitor.Status.Blocked",
            ["Feature.MitigationMonitor.Status.Mitigation"] = "Feature.MitigationMonitor.Status.Mitigation",
            ["Feature.MitigationMonitor.Status.Shield"] = "Feature.MitigationMonitor.Status.Shield",
            ["Feature.MitigationMonitor.Status.Stack"] = "Feature.MitigationMonitor.Status.Stack",
            ["Feature.MitigationMonitor.Status.Vulnerability"] = "Feature.MitigationMonitor.Status.Vulnerability",
            ["Feature.MitigationMonitor.TargetDisplayMode"] = "Feature.MitigationMonitor.TargetDisplayMode",
            ["Feature.MitigationMonitor.TargetFallback"] = "Feature.MitigationMonitor.TargetFallback",
            ["Feature.MitigationMonitor.TargetFilter.Active"] = "Feature.MitigationMonitor.TargetFilter.Active",
            ["Feature.MitigationMonitor.TargetFilter.All"] = "Feature.MitigationMonitor.TargetFilter.All",
            ["Feature.MitigationMonitor.TargetFilter.Empty"] = "Feature.MitigationMonitor.TargetFilter.Empty",
            ["Feature.MitigationMonitor.TargetFilter.Help"] = "Feature.MitigationMonitor.TargetFilter.Help",
            ["Feature.MitigationMonitor.TargetMode.CharacterName"] = "Feature.MitigationMonitor.TargetMode.CharacterName",
            ["Feature.MitigationMonitor.TargetMode.JobIcon"] = "Feature.MitigationMonitor.TargetMode.JobIcon",
            ["Feature.MitigationMonitor.TargetMode.JobIconV2"] = "Feature.MitigationMonitor.TargetMode.JobIconV2",
            ["Feature.MitigationMonitor.TargetMode.JobIconV3"] = "Feature.MitigationMonitor.TargetMode.JobIconV3",
            ["Feature.MitigationMonitor.TargetMode.JobName"] = "Feature.MitigationMonitor.TargetMode.JobName",
            ["Feature.MitigationMonitor.Title"] = "Feature.MitigationMonitor.Title",
            ["Feature.MitigationMonitor.Unlock"] = "Feature.MitigationMonitor.Unlock"
        });

    private readonly MitigationMonitorConfig  config;
    private readonly MitigationCombatLog      combatLog;
    private readonly MitigationRecorder       recorder;
    private readonly MitigationMonitorHotkey  hotkey;
    private readonly MitigationMonitorOverlay overlay;

    public MitigationMonitor
    (
        MitigationMonitorConfig config,
        Action                  saveConfig
    )
    {
        this.config = config;
        NormalizeConfig();
        combatLog = new(config.ReplaySaveCount);
        recorder  = new(combatLog);
        hotkey    = new(config, saveConfig);
        overlay   = new(config, combatLog, new(), saveConfig);
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        NormalizeConfig();
        combatLog.SetReplaySaveCount(config.ReplaySaveCount);
        var lifetime = Activation;
        lifetime.Add(() =>
        {
            hotkey.Reset();
            recorder.Reset();
            combatLog.ClearAll();
            overlay.ResetRuntime();
        });
        hotkey.Register(lifetime);
        recorder.Register(lifetime);
        var windowManager = WindowManager.Instance();
        _ = windowManager.WindowSystem;
        lifetime.OnStopping(() => windowManager.PostDraw -= overlay.Draw);
        windowManager.PostDraw += overlay.Draw;
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => Panel.Draw(this, config, hotkey);

    public override bool ResetSettings()
    {
        var defaults = new MitigationMonitorConfig();
        config.Visible               = defaults.Visible;
        config.Locked                = defaults.Locked;
        config.Collapsed             = defaults.Collapsed;
        config.Position              = defaults.Position;
        config.CollapsedPosition     = defaults.CollapsedPosition;
        config.Size                  = defaults.Size;
        config.ReplaySaveCount       = defaults.ReplaySaveCount;
        config.GlobalScale           = defaults.GlobalScale;
        config.Opacity               = defaults.Opacity;
        config.ShowDotDamage         = defaults.ShowDotDamage;
        config.HideHotkey            = defaults.HideHotkey;
        config.HideHotkeyModifier    = defaults.HideHotkeyModifier;
        config.TargetDisplayMode     = defaults.TargetDisplayMode;
        config.TimeColumnWidth       = defaults.TimeColumnWidth;
        config.ActionColumnWidth     = defaults.ActionColumnWidth;
        config.TargetColumnWidth     = defaults.TargetColumnWidth;
        config.DamageColumnWidth     = defaults.DamageColumnWidth;
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
        config.GlobalScale     = Math.Clamp(config.GlobalScale, 0f, 3f);
        config.Opacity         = Math.Clamp(config.Opacity, 0f, 1f);
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

        config.TimeColumnWidth       = Math.Clamp(config.TimeColumnWidth <= 0f ? 54f : config.TimeColumnWidth, 44f, 220f);
        config.ActionColumnWidth     = Math.Clamp(config.ActionColumnWidth <= 0f ? 80f : config.ActionColumnWidth, 70f, 420f);
        config.TargetColumnWidth     = Math.Clamp(config.TargetColumnWidth <= 0f ? 64f : config.TargetColumnWidth, 60f, 240f);
        config.DamageColumnWidth     = Math.Clamp(config.DamageColumnWidth <= 0f ? 88f : config.DamageColumnWidth, 42f, 260f);
        config.MitigationColumnWidth = Math.Clamp(config.MitigationColumnWidth <= 0f ? 64f : config.MitigationColumnWidth, 34f, 180f);
    }
}
