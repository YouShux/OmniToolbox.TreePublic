using OmniToolbox.Config;
using OmniToolbox.UI;
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
        using (ImRaii.Disabled(!config.ShowBar))
        {
            var color = config.Color;
            if (ImGui.ColorEdit3(
                    $"{OmniLoc.Get("Feature.ShieldOnHp.Color")}##shieldOnHpColor",
                    ref color,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.PickerHueBar))
            {
                config.Color = color;
            }

            shouldSave |= ImGui.IsItemDeactivatedAfterEdit();
        }

        return shouldSave;
    }
}
