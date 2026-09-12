using System.Linq;
using Dalamud.Game.Gui.Dtr;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private static IReadOnlyDtrBarEntry? GetSingleDtrEntry(MultiToolbarWidgetConfig widget) =>
        DService.Instance().DTRBar.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Title, widget.DtrTitle, StringComparison.Ordinal) &&
            entry.Shown && !entry.UserHidden);

    private static bool DrawSingleDtrSettings(MultiToolbarWidgetConfig widget)
    {
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.SingleDtrEntry"));
        ImGui.SetNextItemWidth(-1f);
        if (!OmniControls.BeginCombo("##singleDtrEntry", widget.DtrTitle, ImGui.GetContentRegionAvail().X))
        {
            return false;
        }

        var changed = false;
        foreach (var entry in DService.Instance().DTRBar.Entries.OrderBy(entry => entry.Title))
        {
            if (ImGui.Selectable(entry.Title, entry.Title == widget.DtrTitle))
            {
                widget.DtrTitle = entry.Title;
                changed = true;
            }
        }
        ImGui.EndCombo();
        return changed;
    }

    private void DrawDtrEntry(IReadOnlyDtrBarEntry entry, int index)
    {
        var padding = ScaleToolbar(8f) + ToolbarButtonPadding;
        var size = new Vector2(MeasureDtrText(entry).X + padding * 2f, toolbarButtonHeight);
        ImGui.InvisibleButton($"##multiToolbarDtrVisible{index}_{entry.Title}", size);
        DrawWidgetVisual(string.Empty, size);
        DrawDtrText(entry, ImGui.GetItemRectMin() + new Vector2(padding, 0f), size.X - padding * 2f);
        if (entry.HasClickAction && (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right)))
        {
            entry.OnClick?.Invoke(new DtrInteractionEvent
            {
                ClickType = ImGui.IsItemClicked(ImGuiMouseButton.Right) ? MouseClickType.Right : MouseClickType.Left,
                ModifierKeys = GetDtrModifierKeys(),
                Position = ImGui.GetMousePos(),
            });
        }

        if (ImGui.IsItemHovered() && entry.Tooltip is { } tooltip)
        {
            OmniControls.HelpTooltip(new ReadOnlySeString(tooltip.Encode()));
        }
    }
}
