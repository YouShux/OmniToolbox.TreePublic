using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

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
        using (var table = ImRaii.Table(
                   "##mitigationMonitorSettings",
                   2,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(120f));
                ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.Opacity"));
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                var opacity = config.Opacity * 100f;
                if (OmniControls.SliderFloat("##mitigationOpacity", ref opacity, 0f, 100f, "%.0f%%", ImGui.GetContentRegionAvail().X))
                {
                    config.Opacity = opacity / 100f;
                }

                changed |= ImGui.IsItemDeactivatedAfterEdit();
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.ReplaySaveCount"));
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                var replaySaveCount = config.ReplaySaveCount;
                if (OmniControls.InputInt("##mitigationReplaySaveCount", ref replaySaveCount, 1, 10))
                {
                    feature.SetReplaySaveCount(replaySaveCount);
                }

                changed |= ImGui.IsItemDeactivatedAfterEdit();
            }
        }

        var showDotDamage = config.ShowDotDamage;
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.MitigationMonitor.ShowDotDamage"),
                ref showDotDamage))
        {
            config.ShowDotDamage = showDotDamage;
            changed = true;
        }

        ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.TargetDisplayMode"));
        using (var table = ImRaii.Table(
                   "##mitigationTargetDisplayMode",
                   5,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            if (table)
            {
                changed |= DrawTargetMode(
                    config,
                    MitigationTargetDisplayMode.CharacterName,
                    "Feature.MitigationMonitor.TargetMode.CharacterName");
                changed |= DrawTargetMode(
                    config,
                    MitigationTargetDisplayMode.JobName,
                    "Feature.MitigationMonitor.TargetMode.JobName");
                changed |= DrawTargetMode(
                    config,
                    MitigationTargetDisplayMode.JobIcon,
                    "Feature.MitigationMonitor.TargetMode.JobIcon");
                changed |= DrawTargetMode(
                    config,
                    MitigationTargetDisplayMode.JobIconV2,
                    "Feature.MitigationMonitor.TargetMode.JobIconV2");
                changed |= DrawTargetMode(
                    config,
                    MitigationTargetDisplayMode.JobIconV3,
                    "Feature.MitigationMonitor.TargetMode.JobIconV3");
            }
        }

        if (OmniControls.SmallButton(OmniLoc.Get("Feature.MitigationMonitor.Clear"), false))
        {
            feature.ClearRecords();
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.MitigationMonitor.ResetLayout"), false))
        {
            feature.ResetLayout();
            changed = true;
        }

        return changed;
    }

    private static bool DrawTargetMode(
        MitigationMonitorConfig config,
        MitigationTargetDisplayMode mode,
        string labelKey)
    {
        ImGui.TableNextColumn();
        if (!ImGui.RadioButton(OmniLoc.Get(labelKey), config.TargetDisplayMode == mode))
        {
            return false;
        }

        config.TargetDisplayMode = mode;
        return true;
    }
}
