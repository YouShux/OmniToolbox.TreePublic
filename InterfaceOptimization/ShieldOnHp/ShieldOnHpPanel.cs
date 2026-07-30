using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class ShieldOnHpPanel
{
    public static bool Draw(ShieldOnHpConfig config)
    {
        var showBar = config.ShowBar;
        var shouldSave = OmniControls.Checkbox(
            $"{OmniLoc.Get("Feature.ShieldOnHp.ShowBar")}##shieldOnHpShowBar",
            ref showBar);
        if (shouldSave)
        {
            config.ShowBar = showBar;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.ShieldOnHp.ShowBar.Help"));
        ImGui.SameLine(0f, OmniTheme.ContentGap());
        using (ImRaii.Disabled(!config.ShowBar))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.ShieldOnHp.Color"));
            ImGui.SameLine();
            var color = config.Color;
            if (OmniControls.ColorEdit("##shieldOnHpColor", ref color))
            {
                config.Color = color;
            }

            shouldSave |= ImGui.IsItemDeactivatedAfterEdit();
        }

        return shouldSave;
    }
}
