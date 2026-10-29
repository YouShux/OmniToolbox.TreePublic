using System.Globalization;
using OmenTools;
using OmenTools.Dalamud;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private bool vnavPathStartedByModule;
    private bool firmamentTravelStartedByModule;

    private DateTime? movableSince;

    private Vector3 initialDestination;

    private void DriveWaitForFirmament()
    {
        if (DService.Instance().ClientState.TerritoryType == FIRMAMENT_TERRITORY_ID)
        {
            StopOwnedTeleport();
            movableSince = null;
            EnterPhase(AutomationPhase.WaitForPlayerMovable);
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.EnteredFirmament");
            return;
        }

        if (!firmamentTravelStartedByModule || !isTeleportBusy())
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.TeleportCommandFailed"));
            return;
        }

        status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForFirmament");
        if (PhaseTimedOut(TimeSpan.FromMinutes(3)))
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.FirmamentTeleportTimedOut"));
        }
    }

    private void DriveWaitForPlayerMovable()
    {
        if (DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            movableSince = null;
            status       = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForStableTerritory");
            return;
        }

        if (!IsPlayerMovable())
        {
            movableSince = null;
            status       = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForPlayerMovable");
            return;
        }

        movableSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - movableSince.Value < TimeSpan.FromSeconds(1))
        {
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForContinuousMovability");
            return;
        }

        if (!vnavmeshIPC.IsPluginEnabled())
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.NavigationUnavailable"));
            return;
        }

        if (!vnavmeshIPC.GetIsNavReady())
        {
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForNavigationReady");
            if (PhaseTimedOut(TimeSpan.FromSeconds(90)))
            {
                FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.NavigationNotReady"));
            }
            return;
        }

        initialDestination = new Vector3(
            38f + (Random.Shared.NextSingle() * 9f),
            -16f,
            162f + (Random.Shared.NextSingle() * 14f));
        vnavmeshIPC.PathfindAndMoveTo(initialDestination, false);
        vnavPathStartedByModule = true;
        EnterPhase(AutomationPhase.MoveToInitialPoint);
        nextActionAt = DateTime.UtcNow.AddSeconds(5);
        status       = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.MovingToCraftingPoint"), FormatPosition(initialDestination));
    }

    private void DriveMoveToInitialPoint(RecipeOption recipe)
    {
        if (DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.LeftFirmamentDuringTravel"));
            return;
        }

        var player = DService.Instance().ObjectTable.LocalPlayer;
        if (player is null)
        {
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForPlayerPosition");
            return;
        }

        if (Vector3.Distance(player.Position, initialDestination) <= 2f)
        {
            StopOwnedVnavPath();
            StartCraftingCycle(recipe, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ReachedCraftingPoint"));
            return;
        }

        if (!vnavmeshIPC.GetIsPathfindRunning() && DateTime.UtcNow >= nextActionAt)
        {
            vnavmeshIPC.PathfindAndMoveTo(initialDestination, false);
            vnavPathStartedByModule = true;
            nextActionAt            = DateTime.UtcNow.AddSeconds(5);
        }

        status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.MovingToCraftingPoint"), FormatPosition(initialDestination));
        if (PhaseTimedOut(TimeSpan.FromSeconds(90)))
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.CraftingPointTravelTimedOut"));
        }
    }

    private void StopOwnedVnavPath()
    {
        if (!vnavPathStartedByModule)
        {
            return;
        }

        vnavmeshIPC.StopPathfind();
        vnavPathStartedByModule = false;
    }

    private void StopOwnedTeleport()
    {
        if (!firmamentTravelStartedByModule)
            return;

        firmamentTravelStartedByModule = false;
        cancelTeleport();
    }

    #region 常量

    private const uint FIRMAMENT_TERRITORY_ID = 886;

    private const string FIRMAMENT_TELEPORT_DESTINATION = "无名众人广场";

    #endregion
}
