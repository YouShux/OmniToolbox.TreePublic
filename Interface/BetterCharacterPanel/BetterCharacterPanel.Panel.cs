using OmenTools.ImGuiOm;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterCharacterPanel
{
    internal static class BetterCharacterPanelPanel
    {
        public static bool Draw(BetterCharacterPanelConfig config)
        {
            var changed = false;
            var style = ImGui.GetStyle();
            using var cellPadding = ImRaii.PushStyle(
                ImGuiStyleVar.CellPadding,
                new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y));
            using var itemSpacing = ImRaii.PushStyle(
                ImGuiStyleVar.ItemSpacing,
                new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
            using (var table = OmniControls.SettingsTable(
                       "##betterCharacterPanelOptions",
                       [Vector2.Max(
                            OmniControls.MeasureGroup(
                                [OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterCharacterPanel.ShowUsefulStats")),
                                 OmniControls.HelpIconSize()]),
                            OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterCharacterPanel.AdjustEquipmentPositions"))),
                        Vector2.Max(
                            OmniControls.MeasureGroup(
                                [OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterCharacterPanel.ShowTargetItemLevel")),
                                 OmniControls.HelpIconSize()]) +
                            new Vector2(OmniTheme.SmallButtonSize().X + OmniTheme.Scale(5f), 0f),
                            OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterCharacterPanel.SoulstoneAboveOffhand"))),
                        OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterCharacterPanel.ShowGearSetReorderButtons")),
                        OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterCharacterPanel.ReverseCharacterPanel"))],
                       ["##c0", "##c1", "##c2", "##c3"], weights: [1f, 1.25f, 1f, 1f], columnsPerRow: 4))
            {
                if (table)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (DrawCheckbox(
                            "Feature.BetterCharacterPanel.ShowUsefulStats",
                            "usefulStats",
                            config.ShowUsefulStats,
                            out var showUsefulStats))
                    {
                        config.ShowUsefulStats = showUsefulStats;
                        changed                = true;
                    }
                    OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
                    OmniControls.HelpIcon(OmniLoc.Get("Feature.BetterCharacterPanel.ShowUsefulStats.Help"));

                    ImGui.TableNextColumn();
                    if (DrawCheckbox(
                            "Feature.BetterCharacterPanel.ShowTargetItemLevel",
                            "targetItemLevel",
                            config.ShowTargetItemLevel,
                            out var showTargetItemLevel))
                    {
                        config.ShowTargetItemLevel = showTargetItemLevel;
                        changed                    = true;
                    }
                    OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
                    OmniControls.HelpIcon(OmniLoc.Get("Feature.BetterCharacterPanel.ShowTargetItemLevel.Help"));
                    OmniControls.PreviewImageIcon(
                        "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Interface/BetterCharacterPanel-2.png");

                    ImGui.TableNextColumn();
                    if (DrawCheckbox(
                            "Feature.BetterCharacterPanel.ShowGearSetReorderButtons",
                            "gearSetReorder",
                            config.ShowGearSetReorderButtons,
                            out var showGearSetReorderButtons))
                    {
                        config.ShowGearSetReorderButtons = showGearSetReorderButtons;
                        changed                          = true;
                    }

                    ImGui.TableNextColumn();
                    if (DrawCheckbox(
                            "Feature.BetterCharacterPanel.ReverseCharacterPanel",
                            "reverse",
                            config.ReverseCharacterPanel,
                            out var reverseCharacterPanel))
                    {
                        config.ReverseCharacterPanel = reverseCharacterPanel;
                        changed                      = true;
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (DrawCheckbox(
                            "Feature.BetterCharacterPanel.AdjustEquipmentPositions",
                            "equipmentPositions",
                            config.AdjustEquipmentPositions,
                            out var adjustEquipmentPositions))
                    {
                        config.AdjustEquipmentPositions = adjustEquipmentPositions;
                        changed                         = true;
                    }

                    ImGui.TableNextColumn();
                    if (DrawCheckbox(
                            "Feature.BetterCharacterPanel.SoulstoneAboveOffhand",
                            "soulstoneAboveOffhand",
                            config.SoulstoneAboveOffhand,
                            out var soulstoneAboveOffhand))
                    {
                        config.SoulstoneAboveOffhand = soulstoneAboveOffhand;
                        changed                      = true;
                    }
                }
            }
            return changed;
        }

        private static bool DrawCheckbox
        (
            string   labelKey,
            string   id,
            bool     value,
            out bool updatedValue
        )
        {
            updatedValue = value;
            return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##betterCharacterPanel{id}", ref updatedValue);
        }
    }

}
