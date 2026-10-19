using Dalamud.Interface.Textures;
using OmenTools.Extensions;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MitigationMonitor
{
    private static class Panel
    {
        public static bool Draw(
            MitigationMonitor feature,
            MitigationMonitorConfig config,
            MitigationMonitorHotkey hotkey)
        {
            var changed = false;
            using var table = ImRaii.Table(
                "##mitigationMonitorSettings",
                1,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                new Vector2(ImGui.GetContentRegionAvail().X, 0f));
            if (!table)
            {
                return false;
            }

            ImGui.TableSetupColumn("##mitigationSettingsColumn1", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var showDotDamage = config.ShowDotDamage;
            if (OmniControls.Checkbox(
                    OmniLoc.Get("Feature.MitigationMonitor.ShowDotDamage"),
                    ref showDotDamage))
            {
                config.ShowDotDamage = showDotDamage;
                changed = true;
            }

            var targetLabel = OmniLoc.Get("Feature.MitigationMonitor.TargetDisplayMode");
            var characterNameSize = OmniControls.CompactButtonSize(
                OmniLoc.Get("Feature.MitigationMonitor.TargetMode.CharacterName"));
            var jobNameSize = OmniControls.CompactButtonSize(
                OmniLoc.Get("Feature.MitigationMonitor.TargetMode.JobName"));
            var iconSize = new Vector2(MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight()));
            var targetSize = OmniControls.MeasureGroup(
                [
                    ImGui.CalcTextSize(targetLabel),
                    characterNameSize,
                    jobNameSize,
                    iconSize,
                    iconSize,
                    iconSize
                ]);
            OmniControls.SameLineOrWrap(targetSize.X);
            using (ImRaii.Group())
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(targetLabel);
                OmniControls.SameLineOrWrap(characterNameSize.X);
                changed |= DrawTargetMode(
                    config,
                    MitigationTargetDisplayMode.CharacterName,
                    "Feature.MitigationMonitor.TargetMode.CharacterName");
                OmniControls.SameLineOrWrap(jobNameSize.X);
                changed |= DrawTargetMode(
                    config,
                    MitigationTargetDisplayMode.JobName,
                    "Feature.MitigationMonitor.TargetMode.JobName");
                OmniControls.SameLineOrWrap(iconSize.X);
                changed |= DrawTargetIconMode(
                    config,
                    MitigationTargetDisplayMode.JobIcon,
                    62028,
                    "Feature.MitigationMonitor.TargetMode.JobIcon");
                OmniControls.SameLineOrWrap(iconSize.X);
                changed |= DrawTargetIconMode(
                    config,
                    MitigationTargetDisplayMode.JobIconV2,
                    62128,
                    "Feature.MitigationMonitor.TargetMode.JobIconV2");
                OmniControls.SameLineOrWrap(iconSize.X);
                changed |= DrawTargetIconMode(
                    config,
                    MitigationTargetDisplayMode.JobIconV3,
                    62409,
                    "Feature.MitigationMonitor.TargetMode.JobIconV3");
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var scaleSize = OmniControls.MeasureFloatInput(
                config.GlobalScale,
                "%.1fx",
                OmniTheme.Scale(150f));
            using (ImRaii.Group())
            {
                var globalScale = config.GlobalScale;
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.GlobalScale"));
                OmniControls.SameLineOrWrap(scaleSize.X);
                if (OmniControls.SliderFloat(
                        "##mitigationGlobalScale",
                        ref globalScale,
                        0f,
                        3f,
                        "%.1fx",
                        scaleSize.X))
                {
                    config.GlobalScale = globalScale;
                }

                changed |= ImGui.IsItemDeactivatedAfterEdit();
            }

            var opacityLabel = OmniLoc.Get("Feature.MitigationMonitor.Opacity");
            var opacitySize = OmniControls.MeasureFloatInput(
                config.Opacity * 100f,
                "%.0f%%",
                OmniTheme.Scale(150f));
            OmniControls.SameLineOrWrap(
                OmniControls.MeasureGroup([ImGui.CalcTextSize(opacityLabel), opacitySize]).X);
            using (ImRaii.Group())
            {
                var opacity = config.Opacity * 100f;
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(opacityLabel);
                OmniControls.SameLineOrWrap(opacitySize.X);
                if (OmniControls.SliderFloat(
                        "##mitigationOpacity",
                        ref opacity,
                        0f,
                        100f,
                        "%.0f%%",
                        opacitySize.X))
                {
                    config.Opacity = opacity / 100f;
                }

                changed |= ImGui.IsItemDeactivatedAfterEdit();
            }

            OmniControls.SameLineOrWrap(hotkey.ModifierSettingSize.X);
            using (ImRaii.Group())
            {
                changed |= hotkey.DrawModifierSetting();
            }

            OmniControls.SameLineOrWrap(hotkey.KeySettingSize.X);
            using (ImRaii.Group())
            {
                changed |= hotkey.DrawKeySetting();
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var replaySaveCount = config.ReplaySaveCount;
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.ReplaySaveCount"));
            var replayWidth = OmniControls.MeasureInput("2,147,483,647", OmniTheme.Scale(96f)).X;
            OmniControls.SameLineOrWrap(replayWidth);
            if (OmniControls.InputInt(
                    "##mitigationReplaySaveCount",
                    ref replaySaveCount,
                    replayWidth,
                    groupThousands: true))
            {
                feature.SetReplaySaveCount(replaySaveCount);
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
            OmniControls.SameLineOrWrap(
                OmniControls.CompactButtonSize(OmniLoc.Get("Feature.MitigationMonitor.Clear")).X);
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
            var clicked = OmniControls.SmallButton(
                $"##MitigationTargetDisplay{(int)mode}",
                config.TargetDisplayMode == mode,
                size);
            var texture = DalamudServices.TextureProvider
                .GetFromGameIcon(new GameIconLookup(iconID))
                .GetWrapOrDefault();
            if (texture is not null)
            {
                var iconSize = height - OmniTheme.Scale(6f);
                var min = ImGui.GetItemRectMin() + (size - new Vector2(iconSize)) * 0.5f;
                ImGui.GetWindowDrawList().AddImage(texture.Handle, min, min + new Vector2(iconSize));
            }

            if (ImGui.IsItemHovered())
            {
                OmniControls.HelpTooltip(OmniLoc.Get(tooltipKey));
            }

            if (clicked)
            {
                config.TargetDisplayMode = mode;
            }

            return clicked;
        }
    }
}
