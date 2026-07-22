using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class MarketPurchasePanel
{
    public static bool Draw(IMarketPurchaseService service, bool enabled)
    {
        ImGui.TextDisabled(OmniLoc.Get(!enabled
            ? "Feature.MarketPurchase.Status.Unavailable"
            : string.IsNullOrEmpty(service.StatusKey)
            ? "Feature.MarketPurchase.Status.Ready"
            : service.StatusKey));

        using (ImRaii.Disabled(!service.IsAvailable))
        {
            if (ImGui.Button($"{OmniLoc.Get("Feature.MarketPurchase.Browser.Open")}##marketBrowser"))
            {
                service.OpenBrowser();
            }
        }

        if (service.IsBusy)
        {
            ImGui.SameLine();
            using var disabled = ImRaii.Disabled(!enabled);
            if (ImGui.Button($"{OmniLoc.Get("Feature.MarketPurchase.Cancel")}##marketPurchase"))
            {
                service.Cancel();
            }
        }

        return false;
    }
}
