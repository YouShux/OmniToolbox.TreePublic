using System.Linq;
using Dalamud.Game.Gui.Dtr;
using Lumina.Text.ReadOnly;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmenTools;

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
        using var combo = ImRaii.Combo("##singleDtrEntry", widget.DtrTitle);
        if (!combo)
        {
            return false;
        }

        foreach (var entry in DService.Instance().DTRBar.Entries.OrderBy(entry => entry.Title))
        {
            if (ImGui.Selectable(entry.Title, entry.Title == widget.DtrTitle))
            {
                widget.DtrTitle = entry.Title;
                return true;
            }
        }
        return false;
    }

    private void DrawDtrEntry(IReadOnlyDtrBarEntry entry, int index)
    {
        var size = new Vector2(MeasureDtrText(entry).X + ScaleToolbar(16f), toolbarButtonHeight);
        ImGui.InvisibleButton($"##multiToolbarDtrVisible{index}_{entry.Title}", size);
        DrawWidgetVisual(string.Empty, size);
        DrawDtrText(entry, ImGui.GetItemRectMin() + new Vector2(ScaleToolbar(8f), 0f), size.X - ScaleToolbar(16f));
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
