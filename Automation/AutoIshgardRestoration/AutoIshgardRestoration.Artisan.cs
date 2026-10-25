using System.Globalization;
using Dalamud.Plugin.Ipc;
using OmenTools.Dalamud;
using OmniToolbox.Host;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private ICallGateSubscriber<ushort, int, object>? craftItem;

    private ICallGateSubscriber<bool>? artisanIsBusy;

    private ICallGateSubscriber<bool>? getStopRequest;

    private ICallGateSubscriber<bool, object>? setStopRequest;

    private bool artisanStartedByModule;

    private int craftCycleStartingItemCount;

    private void InitializeArtisan()
    {
        craftItem = DalamudServices.PluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
        artisanIsBusy = DalamudServices.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
        getStopRequest = DalamudServices.PluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
        setStopRequest = DalamudServices.PluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetStopRequest");
    }

    private void ReleaseArtisan()
    {
        craftItem = null;
        artisanIsBusy = null;
        getStopRequest = null;
        setStopRequest = null;
    }

    private void StartCraftingCycle(RecipeOption recipe, string reason)
    {
        if (TryBeginAutoScripPurchase()) return;

        var snapshot = ReadInventory(recipe.ItemID);
        if (ShouldStop(snapshot, ReadSkybuildersScrips(), out var stopReason))
        {
            BeginSubmission(stopReason);
            return;
        }

        if (artisanIsBusy?.InvokeFunc() == true)
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ArtisanBusy"));
            return;
        }

        if (getStopRequest?.InvokeFunc() == true)
        {
            setStopRequest?.InvokeAction(false);
        }

        craftCycleStartingItemCount = snapshot.ItemCount;
        if (craftItem is null)
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ArtisanUnavailable"));
            return;
        }

        artisanStartedByModule = true;
        craftItem.InvokeAction((ushort)recipe.RecipeID, 9999);
        EnterPhase(AutomationPhase.WaitArtisanStart);
        status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ArtisanStarting"), reason, recipe.ItemName);
    }

    private void RequestArtisanStop()
    {
        if (!artisanStartedByModule)
        {
            return;
        }

        if (getStopRequest?.InvokeFunc() != true)
        {
            setStopRequest?.InvokeAction(true);
        }
    }

    private void StopOwnedArtisan()
    {
        RequestArtisanStop();
        artisanStartedByModule = false;
    }
}
