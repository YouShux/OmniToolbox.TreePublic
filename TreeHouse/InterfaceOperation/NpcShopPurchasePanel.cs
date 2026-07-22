using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class NpcShopPurchasePanel
{
    private static readonly AutoPathAbortModifier[] AbortModifiers =
        [AutoPathAbortModifier.Shift, AutoPathAbortModifier.Alt, AutoPathAbortModifier.Control];

    public static bool Draw(INpcShopPurchaseService service, AutoPathConfig config, bool enabled)
    {
        var changed = false;
        var autoPath = config.Enabled;
        if (ImGui.Checkbox(
                $"{OmniLoc.Get("Feature.NpcShopPurchase.AutoPath")}##npcShopAutoPath",
                ref autoPath))
        {
            config.Enabled = autoPath;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.NpcShopPurchase.AutoPath.Help"));
        if (config.Enabled)
        {
            ImGui.SetNextItemWidth(MathF.Min(OmniTheme.Scale(160f), ImGui.GetContentRegionAvail().X));
            if (ImGui.BeginCombo(
                    $"{OmniLoc.Get("Feature.NpcShopPurchase.AutoPath.AbortModifier")}##npcShopAutoPathAbort",
                    GetAbortModifierLabel(config.AbortModifier)))
            {
                foreach (var modifier in AbortModifiers)
                {
                    var selected = modifier == config.AbortModifier;
                    if (ImGui.Selectable(
                            $"{GetAbortModifierLabel(modifier)}##npcShopAutoPathAbort{modifier}",
                            selected))
                    {
                        config.AbortModifier = modifier;
                        changed = true;
                    }

                    if (selected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled(OmniLoc.Get(!enabled
            ? "Feature.NpcShopPurchase.Status.Unavailable"
            : string.IsNullOrEmpty(service.StatusKey)
            ? "Feature.NpcShopPurchase.Status.Ready"
            : service.StatusKey));

        if (service.IsBusy)
        {
            using var disabled = ImRaii.Disabled(!enabled);
            if (ImGui.Button($"{OmniLoc.Get("Feature.NpcShopPurchase.Cancel")}##npcShopPurchase"))
            {
                service.Cancel();
            }
        }

        return changed;
    }

    private static string GetAbortModifierLabel(AutoPathAbortModifier modifier) => OmniLoc.Get(modifier switch
    {
        AutoPathAbortModifier.Alt => "Feature.NpcShopPurchase.AutoPath.AbortModifier.Alt",
        AutoPathAbortModifier.Control => "Feature.NpcShopPurchase.AutoPath.AbortModifier.Control",
        _ => "Feature.NpcShopPurchase.AutoPath.AbortModifier.Shift"
    });
}
