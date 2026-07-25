using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class BetterCharacterPanelPanel
{
    public static bool Draw(BetterCharacterPanelConfig config)
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##betterCharacterPanelOptions",
                   4,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
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
                    changed = true;
                }
                ImGuiOm.HelpMarker(OmniLoc.Get("Feature.BetterCharacterPanel.ShowUsefulStats.Help"));

                ImGui.TableNextColumn();
                if (DrawCheckbox(
                        "Feature.BetterCharacterPanel.ReverseCharacterPanel",
                        "reverse",
                        config.ReverseCharacterPanel,
                        out var reverseCharacterPanel))
                {
                    config.ReverseCharacterPanel = reverseCharacterPanel;
                    changed = true;
                }

                ImGui.TableNextColumn();
                if (DrawCheckbox(
                        "Feature.BetterCharacterPanel.ShowGearSetReorderButtons",
                        "gearSetReorder",
                        config.ShowGearSetReorderButtons,
                        out var showGearSetReorderButtons))
                {
                    config.ShowGearSetReorderButtons = showGearSetReorderButtons;
                    changed = true;
                }

                ImGui.TableNextColumn();
                if (DrawCheckbox(
                        "Feature.BetterCharacterPanel.AdjustEquipmentPositions",
                        "equipmentPositions",
                        config.AdjustEquipmentPositions,
                        out var adjustEquipmentPositions))
                {
                    config.AdjustEquipmentPositions = adjustEquipmentPositions;
                    changed = true;
                }
            }
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        ImGui.Indent(OmniTheme.Scale(18f));
        using (ImRaii.Disabled(!config.AdjustEquipmentPositions))
        {
            if (DrawCheckbox(
                    "Feature.BetterCharacterPanel.SoulstoneAboveOffhand",
                    "soulstoneAboveOffhand",
                    config.SoulstoneAboveOffhand,
                    out var soulstoneAboveOffhand))
            {
                config.SoulstoneAboveOffhand = soulstoneAboveOffhand;
                changed = true;
            }
        }

        ImGui.Unindent(OmniTheme.Scale(18f));
        return changed;
    }

    private static bool DrawCheckbox(
        string labelKey,
        string id,
        bool value,
        out bool updatedValue)
    {
        updatedValue = value;
        return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##betterCharacterPanel{id}", ref updatedValue);
    }
}
