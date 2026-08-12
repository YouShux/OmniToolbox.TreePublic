using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class SkillMonitor : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("SkillMonitorTitle"),
        Description = OmniLoc.Get("SkillMonitorDescription"),
        Category = ModuleCategory.Utility
    };

    private readonly SkillMonitorConfig config;
    private readonly SkillMonitorDefinition[] definitions;
    private readonly SkillMonitorTracker tracker;
    private readonly SkillMonitorOverlay overlay;
    private FeatureLifetime? runtimeLifetime;

    public SkillMonitor(SkillMonitorConfig config)
    {
        this.config = config;
        definitions = SkillMonitorDefinitions.Create();
        NormalizeConfig();
        tracker = new(definitions);
        overlay = new(config, definitions, tracker);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => SkillMonitorPanel.Draw(config, definitions);

    public override bool ResetSettings()
    {
        var defaults = new SkillMonitorConfig();
        config.Offset = defaults.Offset;
        config.IconScale = defaults.IconScale;
        config.IconSpacing = defaults.IconSpacing;
        config.ShowSelf = defaults.ShowSelf;
        config.EnabledActions = [];
        NormalizeConfig();
        return true;
    }

    protected override void OnEnable()
    {
        NormalizeConfig();
        var lifetime = new FeatureLifetime();
        try
        {
            tracker.Register(lifetime);
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
                runtimeLifetime = null;
                tracker.Clear();
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
            tracker.Clear();
        }
    }

    private void NormalizeConfig()
    {
        config.IconScale = Math.Clamp(config.IconScale <= 0f ? 1f : config.IconScale, 0.5f, 2f);
        config.IconSpacing = Math.Clamp(config.IconSpacing, 0f, 12f);
        config.EnabledActions ??= [];
        for (var index = 0; index < definitions.Length; index++)
        {
            config.EnabledActions.TryAdd(definitions[index].ConfigID, true);
        }

        foreach (var actionID in new List<uint>(config.EnabledActions.Keys))
        {
            var found = false;
            for (var index = 0; index < definitions.Length; index++)
            {
                if (definitions[index].ConfigID == actionID)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                config.EnabledActions.Remove(actionID);
            }
        }
    }
}

internal static class SkillMonitorPanel
{
    public static bool Draw(SkillMonitorConfig config, SkillMonitorDefinition[] definitions)
    {
        var changed = false;
        {
            using var settingsTable = ImRaii.Table(
                "##skillMonitorLayoutSettings",
                2,
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.SizingStretchProp);
            if (settingsTable)
            {
                ImGui.TableSetupColumn(
                    "##skillMonitorSettingLabel",
                    ImGuiTableColumnFlags.WidthFixed,
                    MathF.Max(
                        ImGui.CalcTextSize(OmniLoc.Get("Feature.SkillMonitor.Offset")).X,
                        MathF.Max(
                            ImGui.CalcTextSize(OmniLoc.Get("Feature.SkillMonitor.IconScale")).X,
                            ImGui.CalcTextSize(OmniLoc.Get("Feature.SkillMonitor.IconSpacing")).X)) +
                    ImGui.GetStyle().CellPadding.X * 2f);
                ImGui.TableSetupColumn("##skillMonitorSettingValue", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.SkillMonitor.Offset"));
                ImGui.TableNextColumn();
                var offset = config.Offset;
                if (OmniControls.DragFloat2(
                    "##skillMonitorOffset",
                    ref offset,
                    1f,
                    -500f,
                    500f,
                    "%.0f",
                    ImGui.GetContentRegionAvail().X))
                {
                    config.Offset = offset;
                }

                changed |= ImGui.IsItemDeactivatedAfterEdit();

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.SkillMonitor.IconScale"));
                ImGui.TableNextColumn();
                var iconScale = config.IconScale;
                if (OmniControls.DragFloat(
                    "##skillMonitorIconScale",
                    ref iconScale,
                    0.05f,
                    0.5f,
                    2f,
                    "%.2f",
                    ImGui.GetContentRegionAvail().X,
                    ImGuiSliderFlags.AlwaysClamp))
                {
                    config.IconScale = iconScale;
                }

                changed |= ImGui.IsItemDeactivatedAfterEdit();

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.SkillMonitor.IconSpacing"));
                ImGui.TableNextColumn();
                var iconSpacing = config.IconSpacing;
                if (OmniControls.DragFloat(
                    "##skillMonitorIconSpacing",
                    ref iconSpacing,
                    0.5f,
                    0f,
                    12f,
                    "%.1f",
                    ImGui.GetContentRegionAvail().X,
                    ImGuiSliderFlags.AlwaysClamp))
                {
                    config.IconSpacing = iconSpacing;
                }

                changed |= ImGui.IsItemDeactivatedAfterEdit();
            }
        }

        var showSelf = config.ShowSelf;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.SkillMonitor.ShowSelf"), ref showSelf))
        {
            config.ShowSelf = showSelf;
            changed = true;
        }

        using var table = ImRaii.Table(
            "##skillMonitorActions",
            2,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(ImGui.GetContentRegionAvail().X, OmniTheme.Scale(260f)));
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupColumn(OmniLoc.Get("Feature.SkillMonitor.Column.Enabled"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(72f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.SkillMonitor.Column.Action"), ImGuiTableColumnFlags.WidthStretch);
        OmniControls.ScrollableTableHeadersRow();
        SkillMonitorGroup? currentGroup = null;
        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            if (currentGroup != definition.Group)
            {
                currentGroup = definition.Group;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
                ImGui.TableNextColumn();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
                ImGui.TextUnformatted(OmniLoc.Get(definition.Group switch
                {
                    SkillMonitorGroup.Tank => "Feature.SkillMonitor.Group.Tank",
                    SkillMonitorGroup.Healer => "Feature.SkillMonitor.Group.Healer",
                    SkillMonitorGroup.Dps => "Feature.SkillMonitor.Group.Dps",
                    _ => "Feature.SkillMonitor.Group.General"
                }));
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = config.EnabledActions[definition.ConfigID];
            var iconSize = new Vector2(MathF.Max(OmniTheme.Scale(24f), ImGui.GetTextLineHeightWithSpacing()));
            var rowContentHeight = MathF.Max(iconSize.Y, ImGui.GetFrameHeight());
            OmniControls.CenterTableItem(new Vector2(ImGui.GetFrameHeight()), rowContentHeight);
            if (OmniControls.Checkbox($"##skillMonitorAction{definition.ConfigID}", ref enabled))
            {
                config.EnabledActions[definition.ConfigID] = enabled;
                changed = true;
            }

            ImGui.TableNextColumn();
            FramedGameIcon.Draw(definition.IconID, ImGui.GetCursorScreenPos(), iconSize);
            ImGui.Dummy(iconSize);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(definition.Name);
        }

        return changed;
    }
}

[Serializable]
public sealed class SkillMonitorConfig
{
    public Vector2 Offset { get; set; }
    public float IconScale { get; set; } = 1f;
    public float IconSpacing { get; set; } = 2f;
    public bool ShowSelf { get; set; } = true;
    public Dictionary<uint, bool> EnabledActions { get; set; } = [];
}
