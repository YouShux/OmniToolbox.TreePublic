using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class InnEntryPanel
{
    public static bool Draw(IInnEntryService service, bool enabled)
    {
        if (service.IsRunning)
        {
            ImGui.TextDisabled(OmniLoc.Get(service.StatusKey));
        }
        else
        {
            using var disabled = ImRaii.Disabled(!enabled);
            if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.InnEntry.Enter")}##innEntry", false))
            {
                service.RequestEnterNearestInn();
            }
        }

        return false;
    }
}
