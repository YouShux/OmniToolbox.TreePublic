using Dalamud.Interface.Textures;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Extensions;

namespace OmniToolbox.TreeHouse.General;

internal static class MitigationMonitorPanel
{
    public static bool Draw(
        MitigationMonitorFeature feature,
        MitigationMonitorConfig config,
        MitigationMonitorHotkey hotkey)
    {
        var changed = false;
        changed |= hotkey.DrawSettings();

        var opacity = config.Opacity * 100f;
        if (OmniControls.SliderFloat(
                $"{OmniLoc.Get("Feature.MitigationMonitor.Opacity")}##mitigationOpacity",
                ref opacity,
                0f,
                100f,
                "%.0f%%",
                OmniTheme.Scale(150f)))
        {
            config.Opacity = opacity / 100f;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine();
        var replaySaveCount = config.ReplaySaveCount;
        if (OmniControls.InputInt(
                $"{OmniLoc.Get("Feature.MitigationMonitor.ReplaySaveCount")}##mitigationReplaySaveCount",
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
        ImGui.SameLine();
        var showDotDamage = config.ShowDotDamage;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.MitigationMonitor.ShowDotDamage"), ref showDotDamage))
        {
            config.ShowDotDamage = showDotDamage;
            changed = true;
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
        uint iconId,
        string tooltipKey)
    {
        var height = MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight());
        var size = new Vector2(height);
        var clicked = OmniControls.SmallButton($"##MitigationTargetDisplay{(int)mode}", config.TargetDisplayMode == mode, size);
        var texture = DalamudServices.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
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
