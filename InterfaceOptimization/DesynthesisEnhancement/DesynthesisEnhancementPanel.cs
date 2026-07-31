using OmniToolbox.Config;
using OmniToolbox.Tooltips;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class DesynthesisEnhancementPanel
{
    private static int customItemIdInput;

    public static bool Draw(DesynthesisEnhancementConfig config)
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
            changed |= config.CustomLockItemIds.Remove(change.ItemId);
        }

        return changed;
    }

    private static bool DrawSwitches(DesynthesisEnhancementConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##desynthesisEnhancementSwitches",
            3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##desynthesisSwitch0", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##desynthesisSwitch1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##desynthesisSwitch2", ImGuiTableColumnFlags.WidthStretch, 2f);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var value = config.ShowTooltipText;
        if (DrawCheckbox(
            "Feature.DesynthesisEnhancement.TooltipText",
            "desynthesisTooltipText",
            ref value))
        {
            config.ShowTooltipText = value;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DesynthesisEnhancement.TooltipText.Help"));

        ImGui.TableNextColumn();
        value = config.LockGearsetItems;
        if (DrawCheckbox(
            "Feature.DesynthesisEnhancement.LockGearsetItems",
            "desynthesisLockGearsetItems",
            ref value))
        {
            config.LockGearsetItems = value;
            changed = true;
        }

        ImGui.TableNextColumn();
        value = config.LockCustomItems;
        if (DrawCheckbox(
            "Feature.DesynthesisEnhancement.LockCustomItems",
            "desynthesisLockCustomItems",
            ref value))
        {
            config.LockCustomItems = value;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(8f));
        using (ImRaii.Disabled(!config.LockCustomItems))
        {
            ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
            if (OmniControls.InputInt("##desynthesisCustomItemId", ref customItemIdInput) && customItemIdInput < 0)
            {
                customItemIdInput = 0;
            }

            var itemId = DesynthesisEnhancementState.NormalizeItemId((uint)Math.Max(0, customItemIdInput));
            ImGui.SameLine();
            if (OmniControls.SmallButton(OmniLoc.Get("Feature.DesynthesisEnhancement.Add"), false) &&
                itemId > 0 &&
                !config.CustomLockItemIds.Contains(itemId))
            {
                config.CustomLockItemIds.Add(itemId);
                changed = true;
            }
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DesynthesisEnhancement.LockCustomItems.Help"));
        return changed;
    }

    private static bool DrawCheckbox(string labelKey, string id, ref bool value) =>
        OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##{id}", ref value);
}
