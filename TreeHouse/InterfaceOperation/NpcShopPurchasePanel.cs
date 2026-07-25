using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class NpcShopPurchasePanel
{
    public static bool Draw(INpcShopPurchaseService service, bool enabled)
    {
        ImGui.TextDisabled(OmniLoc.Get(!enabled
            ? "Feature.NpcShopPurchase.Status.Unavailable"
            : string.IsNullOrEmpty(service.StatusKey)
            ? "Feature.NpcShopPurchase.Status.Ready"
            : service.StatusKey));

        if (service.IsBusy)
        {
            using var disabled = ImRaii.Disabled(!enabled);
            if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.NpcShopPurchase.Cancel")}##npcShopPurchase", false))
            {
                service.Cancel();
            }
        }

        return false;
    }
}
