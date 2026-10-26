using Dalamud.Plugin.Services;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class DisplayIDInformation
{
    internal static class DisplayIDInformationPanel
    {
        public static bool Draw(DisplayIDInformationConfig config, IconBrowser iconBrowser)
        {
            var changed = false;
            using var table = OmniControls.SettingsTable(
                "##displayIdInformationSettings",
                [OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.DisplayIdInformation.Item")),
                    OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.DisplayIdInformation.Weather")),
                    OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.DisplayIdInformation.Zone")),
                    OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.DisplayIdInformation.Icon"))],
                ["##displayIdItem", "##displayIdWeather", "##displayIdZone", "##displayIdIcon"],
                weights: [1f, 1f, 1f, 1.25f], flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 4);
            if (!table)
            {
                return false;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (DrawCheckbox("Feature.DisplayIdInformation.Item", "item", config.DisplayItemID, out var displayItemId))
            {
                config.DisplayItemID = displayItemId;
                changed = true;
            }

            ImGui.TableNextColumn();
            if (DrawCheckbox("Feature.DisplayIdInformation.Weather", "weather", config.DisplayWeatherID, out var displayWeatherId))
            {
                config.DisplayWeatherID = displayWeatherId;
                changed = true;
            }

            ImGui.TableNextColumn();
            if (DrawCheckbox("Feature.DisplayIdInformation.Zone", "zone", config.DisplayZoneInfo, out var displayZoneInfo))
            {
                config.DisplayZoneInfo = displayZoneInfo;
                changed = true;
            }

            ImGui.TableNextColumn();
            if (DrawCheckbox("Feature.DisplayIdInformation.Icon", "icon", config.DisplayIconID, out var displayIconID))
            {
                config.DisplayIconID = displayIconID;
                changed = true;
            }

            var buttonLabel = OmniLoc.Get("IconBrowser.Title");
            var buttonSize = OmniControls.CompactButtonSize(buttonLabel);
            if (ImGui.GetContentRegionAvail().X > buttonSize.X + ImGui.GetStyle().ItemSpacing.X)
            {
                ImGui.SameLine();
            }

            if (OmniControls.SmallButton(
                    buttonLabel,
                    false,
                    new Vector2(MathF.Min(buttonSize.X, ImGui.GetContentRegionAvail().X), buttonSize.Y)))
            {
                iconBrowser.Toggle();
            }

            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
            OmniControls.HelpIcon(OmniLoc.Get("Feature.DisplayIdInformation.IconBrowser.Help"));
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            changed |= DrawStatus(config);
            ImGui.TableNextColumn();
            changed |= DrawAction(config);
            ImGui.TableNextColumn();
            changed |= DrawTarget(config);
            return changed;
        }

        private static bool DrawStatus(DisplayIDInformationConfig config)
        {
            var changed = false;
            if (DrawCheckbox("Feature.DisplayIdInformation.Status", "status", config.DisplayStatusID, out var displayStatusId))
            {
                config.DisplayStatusID = displayStatusId;
                changed = true;
            }

            ImGui.Spacing();
            ImGui.Indent(OmniTheme.Scale(26f));
            using (ImRaii.Disabled(!config.DisplayStatusID))
            {
                if (DrawCheckbox("Feature.DisplayIdInformation.FlyText", "statusFlyText", config.DisplayStatusIDAppendToName, out var displayFlyText))
                {
                    config.DisplayStatusIDAppendToName = displayFlyText;
                    changed = true;
                }

                OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
                OmniControls.HelpIcon(OmniLoc.Get("Feature.DisplayIdInformation.StatusFlyText.Help"));
            }

            ImGui.Unindent(OmniTheme.Scale(26f));

            return changed;
        }

        private static bool DrawAction(DisplayIDInformationConfig config)
        {
            var changed = false;
            if (DrawCheckbox("Feature.DisplayIdInformation.Action", "action", config.DisplayActionID, out var displayActionId))
            {
                config.DisplayActionID = displayActionId;
                changed = true;
            }

            ImGui.Spacing();
            ImGui.Indent(OmniTheme.Scale(26f));
            using (ImRaii.Disabled(!config.DisplayActionID))
            {
                if (DrawCheckbox("Feature.DisplayIdInformation.Resolved", "resolved", config.DisplayActionIDResolved, out var displayResolved))
                {
                    config.DisplayActionIDResolved = displayResolved;
                    changed = true;
                }

                if (DrawCheckbox("Feature.DisplayIdInformation.Original", "original", config.DisplayActionIDOriginal, out var displayOriginal))
                {
                    config.DisplayActionIDOriginal = displayOriginal;
                    changed = true;
                }

                if (DrawCheckbox("Feature.DisplayIdInformation.FlyText", "actionFlyText", config.DisplayFlyTextDamageActionID, out var displayFlyText))
                {
                    config.DisplayFlyTextDamageActionID = displayFlyText;
                    changed = true;
                }

                OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
                OmniControls.HelpIcon(OmniLoc.Get("Feature.DisplayIdInformation.ActionFlyText.Help"));
                if (DrawCheckbox("Feature.DisplayIdInformation.CastBar", "castBar", config.DisplayCastBarActionID, out var displayCastBar))
                {
                    config.DisplayCastBarActionID = displayCastBar;
                    changed = true;
                }

                OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
                OmniControls.HelpIcon(OmniLoc.Get("Feature.DisplayIdInformation.CastBar.Help"));
            }

            ImGui.Unindent(OmniTheme.Scale(26f));

            return changed;
        }

        private static bool DrawTarget(DisplayIDInformationConfig config)
        {
            var changed = false;
            if (DrawCheckbox("Feature.DisplayIdInformation.Target", "target", config.DisplayTargetID, out var displayTargetId))
            {
                config.DisplayTargetID = displayTargetId;
                changed = true;
            }

            ImGui.Spacing();
            ImGui.Indent(OmniTheme.Scale(26f));
            using (ImRaii.Disabled(!config.DisplayTargetID))
            {
                if (DrawCheckbox("Feature.DisplayIdInformation.BattleNpc", "battleNpc", config.DisplayTargetIDBattleNPC, out var battleNpc))
                {
                    config.DisplayTargetIDBattleNPC = battleNpc;
                    changed = true;
                }

                if (DrawCheckbox("Feature.DisplayIdInformation.EventNpc", "eventNpc", config.DisplayTargetIDEventNPC, out var eventNpc))
                {
                    config.DisplayTargetIDEventNPC = eventNpc;
                    changed = true;
                }

                if (DrawCheckbox("Feature.DisplayIdInformation.Companion", "companion", config.DisplayTargetIDCompanion, out var companion))
                {
                    config.DisplayTargetIDCompanion = companion;
                    changed = true;
                }

                if (DrawCheckbox("Feature.DisplayIdInformation.Others", "others", config.DisplayTargetIDOthers, out var others))
                {
                    config.DisplayTargetIDOthers = others;
                    changed = true;
                }

                OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
                OmniControls.HelpIcon(OmniLoc.Get("Feature.DisplayIdInformation.Others.Help"));
            }

            ImGui.Unindent(OmniTheme.Scale(26f));

            return changed;
        }

        private static bool DrawCheckbox(string labelKey, string id, bool current, out bool value)
        {
            value = current;
            return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##displayId{id}", ref value);
        }
    }

}