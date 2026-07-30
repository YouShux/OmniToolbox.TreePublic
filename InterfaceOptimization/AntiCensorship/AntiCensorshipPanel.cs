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
    public static bool Draw(AntiCensorshipConfig config)
    {
        var changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.AntiCensorship.Mode"));
        ImGui.SameLine();
        var autoHandle = config.EnableAutoHandle;
        if (OmniControls.Checkbox($"{OmniLoc.Get("Feature.AntiCensorship.AutoHandle")}##antiCensorshipAutoHandle", ref autoHandle))
        {
            config.EnableAutoHandle = autoHandle;
            changed = true;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(60f));
        var separator = config.Separator.ToString();
        if (OmniControls.InputText(
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
        ImGui.SameLine(0f, OmniTheme.ContentGap());
        var coloring = config.EnableColoring;
        if (OmniControls.Checkbox($"{OmniLoc.Get("Feature.AntiCensorship.Coloring")}##antiCensorshipColoring", ref coloring))
        {
            config.EnableColoring = coloring;
            changed = true;
        }

        ImGui.SameLine();
        var colorId = config.HighlightColor;
        if (UIColorPicker.Draw("antiCensorship", ref colorId, KnownColor.Red.ToVector4()))
        {
            config.HighlightColor = colorId;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.AntiCensorship.Coloring.Help"));
        return changed;
    }
}
