using Lumina.Excel.Sheets;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class DesynthesisEnhancementPanel
{
    private static int customItemIdInput;

    public static bool Draw(DesynthesisEnhancementConfig config)
    {
        var value = config.ShowTooltipText;
        var changed = DrawCheckbox(
            "Feature.DesynthesisEnhancement.TooltipText",
            "desynthesisTooltipText",
            ref value);
        if (changed)
        {
            config.ShowTooltipText = value;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DesynthesisEnhancement.TooltipText.Help"));

        value = config.LockGearsetItems;
        if (DrawCheckbox(
            "Feature.DesynthesisEnhancement.LockGearsetItems",
            "desynthesisLockGearsetItems",
            ref value))
        {
            config.LockGearsetItems = value;
            changed = true;
        }

        value = config.LockCustomItems;
        if (DrawCheckbox(
            "Feature.DesynthesisEnhancement.LockCustomItems",
            "desynthesisLockCustomItems",
            ref value))
        {
            config.LockCustomItems = value;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DesynthesisEnhancement.LockCustomItems.Help"));

        if (!config.LockCustomItems)
        {
            return changed;
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.DesynthesisEnhancement.CustomItemId"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        if (OmniControls.InputInt("##desynthesisCustomItemId", ref customItemIdInput) && customItemIdInput < 0)
        {
            customItemIdInput = 0;
        }

        var itemId = DesynthesisEnhancementState.NormalizeItemId((uint)Math.Max(0, customItemIdInput));
        Item item = default;
        var hasItem = itemId > 0 && LuminaGetter.TryGetRow(itemId, out item);
        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.DesynthesisEnhancement.Add"), false) &&
            customItemIdInput > 0 &&
            !config.CustomLockItemIds.Contains(itemId))
        {
            config.CustomLockItemIds.Add(itemId);
            changed = true;
        }

        if (itemId > 0)
        {
            ImGui.SameLine(0f, OmniTheme.Scale(10f));
            if (hasItem)
            {
                ImGui.TextColored(OmniTheme.Tokens.Accent, $"{item.Name} ({itemId})");
            }
            else
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.DesynthesisEnhancement.InvalidItemId"));
            }
        }

        if (config.CustomLockItemIds.Count == 0)
        {
            return changed;
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        for (var index = 0; index < config.CustomLockItemIds.Count; index++)
        {
            if (!LuminaGetter.TryGetRow<Item>(config.CustomLockItemIds[index], out var lockedItem))
            {
                continue;
            }

            ImGui.PushID(index);
            ImGui.BeginGroup();
            var iconSize = new Vector2(OmniTheme.Scale(44f));
            if (ImageHelper.GetGameIcon(lockedItem.Icon) is { } icon)
            {
                ImGui.Image(icon.Handle, iconSize);
            }
            else
            {
                ImGui.Dummy(iconSize);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(lockedItem.Name.ToString());
            }

            if (OmniControls.SmallButton(OmniLoc.Get("Feature.DesynthesisEnhancement.Delete"), false))
            {
                config.CustomLockItemIds.RemoveAt(index--);
                changed = true;
            }

            ImGui.EndGroup();
            ImGui.PopID();
            if (index < config.CustomLockItemIds.Count - 1 &&
                ImGui.GetContentRegionAvail().X >= iconSize.X + ImGui.GetStyle().ItemSpacing.X)
            {
                ImGui.SameLine(0f, OmniTheme.Scale(10f));
            }
        }

        return changed;
    }

    private static bool DrawCheckbox(string labelKey, string id, ref bool value) =>
        OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##{id}", ref value);
}
