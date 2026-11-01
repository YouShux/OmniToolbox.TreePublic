using Dalamud.Utility;
using OmenTools.ImGuiOm;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Tooltips;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class DesynthesisEnhancement
{
    internal sealed class DesynthesisEnhancementPanel
    {
        private int CustomItemIDInput;

        public bool Draw(DesynthesisEnhancementConfig config)
        {
            var changed = DrawSwitches(config);
            if (!config.LockCustomItems || config.CustomLockItemIds.Count == 0)
            {
                return changed;
            }

            ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
            var rows = new ItemSelectionTableRow[config.CustomLockItemIds.Count];
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index] = new(config.CustomLockItemIds[index], true, true);
            }

            var change = ItemSelectionTable.Draw("desynthesisCustomLockItems", rows, showEnabledColumn: false);
            if (change.Action == ItemSelectionTableAction.Delete)
            {
                changed |= config.CustomLockItemIds.Remove(change.ItemID);
            }

            return changed;
        }

        private bool DrawSwitches(DesynthesisEnhancementConfig config)
        {
            var changed = false;
            using var table = OmniControls.SettingsTable(
                "##desynthesisEnhancementSwitches",
                [OmniControls.MeasureGroup([OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.DesynthesisEnhancement.TooltipText")),
                    OmniControls.HelpIconSize()]),
                    OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.DesynthesisEnhancement.LockGearsetItems")),
                    OmniControls.MeasureGroup([OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.DesynthesisEnhancement.LockCustomItems")),
                        OmniControls.MeasureInput(CustomItemIDInput.ToString(), OmniTheme.Scale(120f)),
                        OmniControls.CompactButtonSize(OmniLoc.Get("Feature.DesynthesisEnhancement.Add")), OmniControls.HelpIconSize()])],
                ["##desynthesisSwitch0", "##desynthesisSwitch1", "##desynthesisSwitch2"], weights: [1f, 1f, 2f],
                flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 4);
            if (!table)
            {
                return false;
            }


            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var value = config.ShowTooltipText;
            if (DrawCheckbox(
                "Feature.DesynthesisEnhancement.TooltipText",
                "desynthesisTooltipText",
                ref value))
            {
                config.ShowTooltipText = value;
                changed                = true;
            }

            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
            OmniControls.HelpIcon(OmniLoc.Get("Feature.DesynthesisEnhancement.TooltipText.Help"));

            ImGui.TableNextColumn();
            value = config.LockGearsetItems;
            if (DrawCheckbox(
                "Feature.DesynthesisEnhancement.LockGearsetItems",
                "desynthesisLockGearsetItems",
                ref value))
            {
                config.LockGearsetItems = value;
                changed                 = true;
            }

            ImGui.TableNextColumn();
            value = config.LockCustomItems;
            if (DrawCheckbox(
                "Feature.DesynthesisEnhancement.LockCustomItems",
                "desynthesisLockCustomItems",
                ref value))
            {
                config.LockCustomItems = value;
                changed                = true;
            }

            ImGui.SameLine(0f, OmniTheme.Scale(8f));
            using (ImRaii.Disabled(!config.LockCustomItems))
            {
                ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
                if (OmniControls.InputInt("##desynthesisCustomItemId", ref CustomItemIDInput) && CustomItemIDInput < 0)
                {
                    CustomItemIDInput = 0;
                }

                var itemID = ItemUtil.GetBaseId((uint)Math.Max(0, CustomItemIDInput)).ItemId;
                ImGui.SameLine();
                if (OmniControls.SmallButton(OmniLoc.Get("Feature.DesynthesisEnhancement.Add"), false) &&
                    itemID > 0 &&
                    !config.CustomLockItemIds.Contains(itemID))
                {
                    config.CustomLockItemIds.Add(itemID);
                    changed = true;
                }
            }

            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
            OmniControls.HelpIcon(OmniLoc.Get("Feature.DesynthesisEnhancement.LockCustomItems.Help"));
            return changed;
        }

        private static bool DrawCheckbox
        (
            string   labelKey,
            string   id,
            ref bool value
        ) =>
            OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##{id}", ref value);
    }
}
