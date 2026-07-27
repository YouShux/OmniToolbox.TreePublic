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
        var style = ImGui.GetStyle();
        using var cellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y));
        using var itemSpacing = ImRaii.PushStyle(
            ImGuiStyleVar.ItemSpacing,
            new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
        using (var table = ImRaii.Table(
                   "##betterCharacterPanelOptions",
                   4,
                   ImGuiTableFlags.SizingStretchProp,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##c0", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##c1", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##c2", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##c3", ImGuiTableColumnFlags.WidthStretch, 1.25f);
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

                ImGui.Spacing();
                ImGui.Indent(OmniTheme.Scale(26f));
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

                ImGui.Unindent(OmniTheme.Scale(26f));
            }
        }
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
