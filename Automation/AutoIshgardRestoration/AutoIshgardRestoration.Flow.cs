using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Plugin.Services;
using OmenTools;
using OmenTools.Extensions;
using OmniToolbox.Host;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private DateTime nextCheckAt;

    private DateTime phaseStartedAt;

    private DateTime nextActionAt;

    private bool running;

    private uint activeRecipeID;

    private uint activeItemID;

    private AutomationPhase phase;

    private string status = OmniLoc.Get("Feature.AutoIshgardRestoration.Idle");

    private string lastError = string.Empty;

    private Dictionary<AutomationPhase, Action<RecipeOption>> automationHandlers = [];

    private void StartProduction()
    {
        if (running)
        {
            return;
        }

        lastError = string.Empty;
        var playerState = DalamudServices.PlayerState;
        if (!playerState.IsLoaded)
        {
            Fail(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.PlayerNotLoggedIn"));
            return;
        }

        if (TryStopAtScripThreshold()) return;

        var currentJobID = playerState.ClassJob.RowId;
        if (currentJobID is < 8 or > 15)
        {
            Fail(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.CraftingJobRequired"));
            return;
        }

        var recipe = FindRecipe(config.SelectedRecipeID);
        if (recipe is null || recipe.Value.JobID != currentJobID)
        {
            Fail(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.RecipeSelectionRequired"));
            return;
        }

        if (playerState.Level < recipe.Value.Level)
        {
            Fail(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.JobLevelRequired"), recipe.Value.Level));
            return;
        }

        if (!DalamudServices.PluginInterface.IsPluginEnabled("Artisan"))
        {
            Fail(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ArtisanUnavailable"));
            return;
        }

        if (artisanIsBusy?.InvokeFunc() == true)
        {
            Fail(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ArtisanBusy"));
            return;
        }

        running = true;
        turnInGeneration = 0;
        purchasedGeneration = -1;
        activeRecipeID = recipe.Value.RecipeID;
        activeItemID = recipe.Value.ItemID;
        nextCheckAt = DateTime.UtcNow;

        if (DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            firmamentTravelStartedByModule = tryTeleport(FIRMAMENT_TELEPORT_DESTINATION);
            if (!firmamentTravelStartedByModule)
            {
                FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.TeleportCommandFailed"));
                return;
            }

            EnterPhase(AutomationPhase.WaitForFirmament);
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.TeleportingToFirmament");
            return;
        }

        EnterPhase(AutomationPhase.WaitForPlayerMovable);
        status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.AlreadyInFirmament");
    }

    private void StopProduction(string reason)
    {
        StopOwnedTeleport();
        CleanupScripPurchase();
        StopOwnedArtisan();
        StopOwnedVnavPath();

        running = false;
        phase = AutomationPhase.Idle;
        activeRecipeID = 0;
        activeItemID = 0;
        ticketsToPlay = 0;
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        craftCycleStartingItemCount = 0;
        movableSince = null;
        initialDestination = default;
        artisanStartedByModule = false;
        vnavPathStartedByModule = false;
        status = reason;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (startPending)
        {
            startPending = false;
            if (!IsEnabled)
            {
                return;
            }

            InitializeArtisan();
            StartProduction();
            return;
        }
        if (!running || DateTime.UtcNow < nextCheckAt)
        {
            return;
        }

        nextCheckAt = DateTime.UtcNow.AddMilliseconds(100);
        if (TryStopAtScripThreshold()) return;

        UpdateVoucherCount(DateTime.UtcNow);
        if (IsScripPurchasePhase())
        {
            DriveScripPurchase();
            return;
        }

        var activeRecipe = FindRecipe(activeRecipeID);
        if (activeRecipe is null || activeRecipe.Value.ItemID != activeItemID)
        {
            StopProduction(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.InvalidRunData"));
            return;
        }

        DriveAutomation(activeRecipe.Value);
    }

    private void EnsureAutomationHandlers()
    {
        if (automationHandlers.Count != 0) return;
        automationHandlers = new()
        {
            [AutomationPhase.WaitForFirmament] = _ => DriveWaitForFirmament(),
            [AutomationPhase.WaitForPlayerMovable] = _ => DriveWaitForPlayerMovable(),
            [AutomationPhase.MoveToInitialPoint] = DriveMoveToInitialPoint,
            [AutomationPhase.WaitArtisanStart] = DriveWaitArtisanStart,
            [AutomationPhase.Crafting] = DriveCrafting,
            [AutomationPhase.WaitArtisanStop] = _ => DriveWaitArtisanStop(),
            [AutomationPhase.MoveToAppraiser] = _ => DriveStartNPCEvent(
                [APPRAISER_NPC_ID_1, APPRAISER_NPC_ID_2], AutomationPhase.OpenAppraiser, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.AppraiserNPC")),
            [AutomationPhase.OpenAppraiser] = _ => DriveOpenAppraiser(),
            [AutomationPhase.SelectJob] = DriveSelectJob,
            [AutomationPhase.SelectItem] = DriveSelectItem,
            [AutomationPhase.FillRequest] = _ => DriveFillRequest(),
            [AutomationPhase.WaitTurnIn] = DriveWaitTurnIn,
            [AutomationPhase.WaitSupplyRefresh] = DriveWaitSupplyRefresh,
            [AutomationPhase.MoveToLottery] = _ => DriveStartNPCEvent(
                [LOTTERY_NPC_ID], AutomationPhase.OpenLottery, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.LotteryNPC")),
            [AutomationPhase.OpenLottery] = _ => DriveOpenLottery(),
            [AutomationPhase.PlayLottery] = _ => DrivePlayLottery(),
            [AutomationPhase.PostLottery] = DrivePostLottery,
            [AutomationPhase.PrepareNextCycle] = DrivePrepareNextCycle
        };
    }

    private void DriveAutomation(RecipeOption recipe)
    {
        EnsureAutomationHandlers();
        if (automationHandlers.TryGetValue(phase, out var handler))
        {
            handler(recipe);
        }
    }

    private void DriveWaitArtisanStart(RecipeOption recipe)
    {
        var snapshot = ReadInventory(activeItemID);
        if (ShouldStop(snapshot, ReadSkybuildersScrips(), out var reason))
        {
            RequestArtisanStop();
            EnterPhase(AutomationPhase.WaitArtisanStop);
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForArtisanStop"), reason);
        }
        else if (artisanIsBusy?.InvokeFunc() == true)
        {
            EnterPhase(AutomationPhase.Crafting);
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ArtisanCrafting"), recipe.ItemName);
        }
        else if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            artisanStartedByModule = false;
            if (snapshot.ItemCount > craftCycleStartingItemCount)
                BeginSubmission(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ArtisanCycleCompleted"));
            else
                CompleteAutomation(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.CannotContinueCrafting"), recipe.ItemName));
        }
    }

    private void DriveCrafting(RecipeOption recipe)
    {
        var snapshot = ReadInventory(activeItemID);
        if (ShouldStop(snapshot, ReadSkybuildersScrips(), out var reason))
        {
            RequestArtisanStop();
            EnterPhase(AutomationPhase.WaitArtisanStop);
            status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForArtisanStop"), reason);
        }
        else if (artisanIsBusy?.InvokeFunc() == false)
        {
            artisanStartedByModule = false;
            if (snapshot.ItemCount > craftCycleStartingItemCount)
                BeginSubmission(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ArtisanCycleEnded"));
            else
                CompleteAutomation(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.CannotContinueCrafting"), recipe.ItemName));
        }
    }

    private void DriveWaitArtisanStop()
    {
        if (artisanIsBusy?.InvokeFunc() == false)
        {
            artisanStartedByModule = false;
            BeginSubmission(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ArtisanStopped"));
        }
        else if (PhaseTimedOut(TimeSpan.FromSeconds(30)))
        {
            FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ArtisanStopTimedOut"));
        }
    }

    private unsafe void BeginNextCraftingCycle(string reason)
    {
        CloseAddon("Request");
        CloseAddon("HWDSupply");
        EnterPhase(AutomationPhase.PrepareNextCycle);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        status = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.PreparingNextCraftingCycle"), reason);
    }

    private unsafe void DrivePrepareNextCycle(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (GetAddon("Request") != null || GetAddon("HWDSupply") != null)
        {
            CloseAddon("Request");
            CloseAddon("HWDSupply");
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.ClosingSupplyForCrafting");
            return;
        }

        if (IsOccupied())
        {
            status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.WaitingForInteractionBeforeCrafting");
            if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
            {
                FailAutomation(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.PlayerOccupiedBeforeCrafting"));
            }
            return;
        }

        StartCraftingCycle(recipe, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.StartingNextCycle"));
    }

    private void EnterPhase(AutomationPhase next)
    {
        phase = next;
        phaseStartedAt = DateTime.UtcNow;
    }

    private bool PhaseTimedOut(TimeSpan timeout) => DateTime.UtcNow - phaseStartedAt > timeout;

    private void CompleteAutomation(string message)
    {
        running = false;
        phase = AutomationPhase.Idle;
        activeRecipeID = 0;
        activeItemID = 0;
        ticketsToPlay = 0;
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        craftCycleStartingItemCount = 0;
        movableSince = null;
        initialDestination = default;
        status = message;
    }

    private void FailAutomation(string message)
    {
        StopOwnedTeleport();
        CleanupScripPurchase();
        StopOwnedArtisan();
        StopOwnedVnavPath();

        running = false;
        phase = AutomationPhase.Idle;
        lastError = message;
        status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.AutomationStopped");
    }

    private bool ShouldStop(InventorySnapshot snapshot, uint balance, out string reason)
    {
        if (snapshot.FreeSlots <= 5)
        {
            reason = string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.StoppedForFreeSlots"), snapshot.FreeSlots);
            return true;
        }

        var threshold = Math.Clamp(config.AutoBuyScrips ? config.ScripThreshold : config.ScripStopThreshold, 1, 10000);
        if (snapshot.PendingScrips > 0 && (long)balance + snapshot.PendingScrips >= threshold)
        {
            reason = string.Format(CultureInfo.InvariantCulture,
                OmniLoc.Get(this, "Feature.AutoIshgardRestoration.StoppedForScrips"), balance, snapshot.PendingScrips, threshold);
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private bool TryStopAtScripThreshold()
    {
        if (config.AutoBuyScrips) return false;

        var balance = ReadSkybuildersScrips();
        var threshold = Math.Clamp(config.ScripStopThreshold, 1, 10000);
        if (balance < threshold) return false;

        StopProduction(string.Format(CultureInfo.InvariantCulture,
            OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ScripStopReached"), balance, threshold));
        return true;
    }

    private void Fail(string message)
    {
        lastError = message;
        status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.CannotStart");
    }

    private enum AutomationPhase
    {
        Idle,
        WaitForFirmament,
        WaitForPlayerMovable,
        MoveToInitialPoint,
        WaitArtisanStart,
        Crafting,
        WaitArtisanStop,
        MoveToAppraiser,
        OpenAppraiser,
        SelectJob,
        SelectItem,
        FillRequest,
        WaitTurnIn,
        WaitSupplyRefresh,
        MoveToLottery,
        OpenLottery,
        PlayLottery,
        PostLottery,
        PrepareNextCycle,
        PrepareScripPurchase,
        OpenScripShop,
        BuyScripItem,
        VerifyScripPurchase,
        CloseScripShop
    }
}
