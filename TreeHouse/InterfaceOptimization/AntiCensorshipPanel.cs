using System.Drawing;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class AntiCensorshipPanel
{
    private static bool colorPickerOpen;

    public static bool Draw(AntiCensorshipConfig config)
    {
        var changed = false;
        var coloring = config.EnableColoring;
        if (ImGui.Checkbox($"{OmniLoc.Get("Feature.AntiCensorship.Coloring")}##antiCensorshipColoring", ref coloring))
        {
            config.EnableColoring = coloring;
            changed = true;
        }

        ImGui.SameLine();
        var colorId = config.HighlightColor;
        if (UIColorPicker.Draw(
                "antiCensorship",
                OmniLoc.Get("Feature.AntiCensorship.ColorPicker.Title"),
                ref colorPickerOpen,
                ref colorId,
                KnownColor.Red.ToVector4()))
        {
            config.HighlightColor = colorId;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.AntiCensorship.Coloring.Help"));

        var autoHandle = config.EnableAutoHandle;
        if (ImGui.Checkbox($"{OmniLoc.Get("Feature.AntiCensorship.AutoHandle")}##antiCensorshipAutoHandle", ref autoHandle))
        {
            config.EnableAutoHandle = autoHandle;
            changed = true;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(60f));
        var separator = config.Separator.ToString();
        if (ImGui.InputText(
                $"{OmniLoc.Get("Feature.AntiCensorship.Separator")}##antiCensorshipSeparator",
                ref separator,
                8))
        {
            separator = separator.Trim();
            config.Separator = string.IsNullOrWhiteSpace(separator) || separator == "*"
                ? '.'
                : separator[0];
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.AntiCensorship.AutoHandle.Help"));
        return changed;
    }
}
