using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using Action = System.Action;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class AutoIshgardRestoration : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Automation,
        Author = "Angelways，YouShu",
        SupportUrls = ["https://github.com/Angelways"],
        Commands =
        [
            new("Feature.AutoIshgardRestoration.OpenCommand", OmniCommandCatalog.Format("ishgardrestoration"))
        ]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "AutoIshgardRestorationTitle",
            ["Description"] = "AutoIshgardRestorationDescription",
            ["Feature.AutoIshgardRestoration.AllItemsSubmitted"] = "Feature.AutoIshgardRestoration.AllItemsSubmitted",
            ["Feature.AutoIshgardRestoration.AlreadyInFirmament"] = "Feature.AutoIshgardRestoration.AlreadyInFirmament",
            ["Feature.AutoIshgardRestoration.AppraiserNPC"] = "Feature.AutoIshgardRestoration.AppraiserNPC",
            ["Feature.AutoIshgardRestoration.ArtisanBusy"] = "Feature.AutoIshgardRestoration.ArtisanBusy",
            ["Feature.AutoIshgardRestoration.ArtisanCrafting"] = "Feature.AutoIshgardRestoration.ArtisanCrafting",
            ["Feature.AutoIshgardRestoration.ArtisanCycleCompleted"] = "Feature.AutoIshgardRestoration.ArtisanCycleCompleted",
            ["Feature.AutoIshgardRestoration.ArtisanCycleEnded"] = "Feature.AutoIshgardRestoration.ArtisanCycleEnded",
            ["Feature.AutoIshgardRestoration.ArtisanStopTimedOut"] = "Feature.AutoIshgardRestoration.ArtisanStopTimedOut",
            ["Feature.AutoIshgardRestoration.ArtisanStopped"] = "Feature.AutoIshgardRestoration.ArtisanStopped",
            ["Feature.AutoIshgardRestoration.AutomationStopped"] = "Feature.AutoIshgardRestoration.AutomationStopped",
            ["Feature.AutoIshgardRestoration.CNOnly"] = "Feature.AutoIshgardRestoration.CNOnly",
            ["Feature.AutoIshgardRestoration.CannotContinueCrafting"] = "Feature.AutoIshgardRestoration.CannotContinueCrafting",
            ["Feature.AutoIshgardRestoration.CannotStart"] = "Feature.AutoIshgardRestoration.CannotStart",
            ["Feature.AutoIshgardRestoration.ClosingCompletedRequest"] = "Feature.AutoIshgardRestoration.ClosingCompletedRequest",
            ["Feature.AutoIshgardRestoration.ClosingSupplyForCrafting"] = "Feature.AutoIshgardRestoration.ClosingSupplyForCrafting",
            ["Feature.AutoIshgardRestoration.ContinuingSubmission"] = "Feature.AutoIshgardRestoration.ContinuingSubmission",
            ["Feature.AutoIshgardRestoration.CraftingJobRequired"] = "Feature.AutoIshgardRestoration.CraftingJobRequired",
            ["Feature.AutoIshgardRestoration.CraftingPointTravelTimedOut"] = "Feature.AutoIshgardRestoration.CraftingPointTravelTimedOut",
            ["Feature.AutoIshgardRestoration.Disabled"] = "Feature.AutoIshgardRestoration.Disabled",
            ["Feature.AutoIshgardRestoration.EnableFirst"] = "Feature.AutoIshgardRestoration.EnableFirst",
            ["Feature.AutoIshgardRestoration.EnteredFirmament"] = "Feature.AutoIshgardRestoration.EnteredFirmament",
            ["Feature.AutoIshgardRestoration.FillingRequestTimedOut"] = "Feature.AutoIshgardRestoration.FillingRequestTimedOut",
            ["Feature.AutoIshgardRestoration.FirmamentTeleportTimedOut"] = "Feature.AutoIshgardRestoration.FirmamentTeleportTimedOut",
            ["Feature.AutoIshgardRestoration.Idle"] = "Feature.AutoIshgardRestoration.Idle",
            ["Feature.AutoIshgardRestoration.Interrupted"] = "Feature.AutoIshgardRestoration.Interrupted",
            ["Feature.AutoIshgardRestoration.InvalidRunData"] = "Feature.AutoIshgardRestoration.InvalidRunData",
            ["Feature.AutoIshgardRestoration.JobLevelRequired"] = "Feature.AutoIshgardRestoration.JobLevelRequired",
            ["Feature.AutoIshgardRestoration.LeftFirmamentDuringTravel"] = "Feature.AutoIshgardRestoration.LeftFirmamentDuringTravel",
            ["Feature.AutoIshgardRestoration.LotteryNPC"] = "Feature.AutoIshgardRestoration.LotteryNPC",
            ["Feature.AutoIshgardRestoration.MovingToAppraiser"] = "Feature.AutoIshgardRestoration.MovingToAppraiser",
            ["Feature.AutoIshgardRestoration.MovingToCraftingPoint"] = "Feature.AutoIshgardRestoration.MovingToCraftingPoint",
            ["Feature.AutoIshgardRestoration.NPCEventFailed"] = "Feature.AutoIshgardRestoration.NPCEventFailed",
            ["Feature.AutoIshgardRestoration.NPCNotFound"] = "Feature.AutoIshgardRestoration.NPCNotFound",
            ["Feature.AutoIshgardRestoration.NavigationNotReady"] = "Feature.AutoIshgardRestoration.NavigationNotReady",
            ["Feature.AutoIshgardRestoration.NavigationUnavailable"] = "Feature.AutoIshgardRestoration.NavigationUnavailable",
            ["Feature.AutoIshgardRestoration.NoItemsToSubmit"] = "Feature.AutoIshgardRestoration.NoItemsToSubmit",
            ["Feature.AutoIshgardRestoration.OpenCommand"] = "Feature.AutoIshgardRestoration.OpenCommand",
            ["Feature.AutoIshgardRestoration.OpeningNPCEvent"] = "Feature.AutoIshgardRestoration.OpeningNPCEvent",
            ["Feature.AutoIshgardRestoration.PlayerNotLoggedIn"] = "Feature.AutoIshgardRestoration.PlayerNotLoggedIn",
            ["Feature.AutoIshgardRestoration.PlayerOccupiedBeforeCrafting"] = "Feature.AutoIshgardRestoration.PlayerOccupiedBeforeCrafting",
            ["Feature.AutoIshgardRestoration.PreparingNextCraftingCycle"] = "Feature.AutoIshgardRestoration.PreparingNextCraftingCycle",
            ["Feature.AutoIshgardRestoration.ReachedCraftingPoint"] = "Feature.AutoIshgardRestoration.ReachedCraftingPoint",
            ["Feature.AutoIshgardRestoration.RecipeSelectionRequired"] = "Feature.AutoIshgardRestoration.RecipeSelectionRequired",
            ["Feature.AutoIshgardRestoration.ReopeningSupply"] = "Feature.AutoIshgardRestoration.ReopeningSupply",
            ["Feature.AutoIshgardRestoration.RequestNotOpened"] = "Feature.AutoIshgardRestoration.RequestNotOpened",
            ["Feature.AutoIshgardRestoration.StartPending"] = "Feature.AutoIshgardRestoration.StartPending",
            ["Feature.AutoIshgardRestoration.StartingNextCycle"] = "Feature.AutoIshgardRestoration.StartingNextCycle",
            ["Feature.AutoIshgardRestoration.Stopped"] = "Feature.AutoIshgardRestoration.Stopped",
            ["Feature.AutoIshgardRestoration.StoppedForFreeSlots"] = "Feature.AutoIshgardRestoration.StoppedForFreeSlots",
            ["Feature.AutoIshgardRestoration.StoppedForScrips"] = "Feature.AutoIshgardRestoration.StoppedForScrips",
            ["Feature.AutoIshgardRestoration.SubmissionCompletedWithTickets"] = "Feature.AutoIshgardRestoration.SubmissionCompletedWithTickets",
            ["Feature.AutoIshgardRestoration.SubmissionRequiresFirmament"] = "Feature.AutoIshgardRestoration.SubmissionRequiresFirmament",
            ["Feature.AutoIshgardRestoration.SubmittingItem"] = "Feature.AutoIshgardRestoration.SubmittingItem",
            ["Feature.AutoIshgardRestoration.SupplyItemNotFound"] = "Feature.AutoIshgardRestoration.SupplyItemNotFound",
            ["Feature.AutoIshgardRestoration.SupplyJobSelected"] = "Feature.AutoIshgardRestoration.SupplyJobSelected",
            ["Feature.AutoIshgardRestoration.SupplyOpenFailed"] = "Feature.AutoIshgardRestoration.SupplyOpenFailed",
            ["Feature.AutoIshgardRestoration.SupplyOpened"] = "Feature.AutoIshgardRestoration.SupplyOpened",
            ["Feature.AutoIshgardRestoration.SupplyRefreshFailed"] = "Feature.AutoIshgardRestoration.SupplyRefreshFailed",
            ["Feature.AutoIshgardRestoration.SupplyUnexpectedlyClosed"] = "Feature.AutoIshgardRestoration.SupplyUnexpectedlyClosed",
            ["Feature.AutoIshgardRestoration.TeleportCommandFailed"] = "Feature.AutoIshgardRestoration.TeleportCommandFailed",
            ["Feature.AutoIshgardRestoration.TeleportingToFirmament"] = "Feature.AutoIshgardRestoration.TeleportingToFirmament",
            ["Feature.AutoIshgardRestoration.TicketThresholdReached"] = "Feature.AutoIshgardRestoration.TicketThresholdReached",
            ["Feature.AutoIshgardRestoration.TurnInConfirmedByInventory"] = "Feature.AutoIshgardRestoration.TurnInConfirmedByInventory",
            ["Feature.AutoIshgardRestoration.TurnInConfirmedByScrips"] = "Feature.AutoIshgardRestoration.TurnInConfirmedByScrips",
            ["Feature.AutoIshgardRestoration.TurnInNotCompleted"] = "Feature.AutoIshgardRestoration.TurnInNotCompleted",
            ["Feature.AutoIshgardRestoration.UI.ArtisanBusy"] = "Feature.AutoIshgardRestoration.UI.ArtisanBusy",
            ["Feature.AutoIshgardRestoration.UI.ArtisanStarting"] = "Feature.AutoIshgardRestoration.UI.ArtisanStarting",
            ["Feature.AutoIshgardRestoration.UI.ArtisanUnavailable"] = "Feature.AutoIshgardRestoration.UI.ArtisanUnavailable",
            ["Feature.AutoIshgardRestoration.UI.AutoBuyScrips"] = "Feature.AutoIshgardRestoration.UI.AutoBuyScrips",
            ["Feature.AutoIshgardRestoration.UI.AutoStopScrips"] = "Feature.AutoIshgardRestoration.UI.AutoStopScrips",
            ["Feature.AutoIshgardRestoration.UI.Collapse"] = "Feature.AutoIshgardRestoration.UI.Collapse",
            ["Feature.AutoIshgardRestoration.UI.CurrentJob"] = "Feature.AutoIshgardRestoration.UI.CurrentJob",
            ["Feature.AutoIshgardRestoration.UI.Error"] = "Feature.AutoIshgardRestoration.UI.Error",
            ["Feature.AutoIshgardRestoration.UI.Expand"] = "Feature.AutoIshgardRestoration.UI.Expand",
            ["Feature.AutoIshgardRestoration.UI.ExpertRecipe"] = "Feature.AutoIshgardRestoration.UI.ExpertRecipe",
            ["Feature.AutoIshgardRestoration.UI.LotteryClosedEarly"] = "Feature.AutoIshgardRestoration.UI.LotteryClosedEarly",
            ["Feature.AutoIshgardRestoration.UI.LotteryContinuing"] = "Feature.AutoIshgardRestoration.UI.LotteryContinuing",
            ["Feature.AutoIshgardRestoration.UI.LotteryInitializing"] = "Feature.AutoIshgardRestoration.UI.LotteryInitializing",
            ["Feature.AutoIshgardRestoration.UI.LotteryNPCMissing"] = "Feature.AutoIshgardRestoration.UI.LotteryNPCMissing",
            ["Feature.AutoIshgardRestoration.UI.LotteryNPCOpenFailed"] = "Feature.AutoIshgardRestoration.UI.LotteryNPCOpenFailed",
            ["Feature.AutoIshgardRestoration.UI.LotteryNotInitialized"] = "Feature.AutoIshgardRestoration.UI.LotteryNotInitialized",
            ["Feature.AutoIshgardRestoration.UI.LotteryOpenFailed"] = "Feature.AutoIshgardRestoration.UI.LotteryOpenFailed",
            ["Feature.AutoIshgardRestoration.UI.LotteryPreparing"] = "Feature.AutoIshgardRestoration.UI.LotteryPreparing",
            ["Feature.AutoIshgardRestoration.UI.LotteryRemaining"] = "Feature.AutoIshgardRestoration.UI.LotteryRemaining",
            ["Feature.AutoIshgardRestoration.UI.LotteryResultClosing"] = "Feature.AutoIshgardRestoration.UI.LotteryResultClosing",
            ["Feature.AutoIshgardRestoration.UI.LotteryReturnToSubmission"] = "Feature.AutoIshgardRestoration.UI.LotteryReturnToSubmission",
            ["Feature.AutoIshgardRestoration.UI.LotteryRevealTimedOut"] = "Feature.AutoIshgardRestoration.UI.LotteryRevealTimedOut",
            ["Feature.AutoIshgardRestoration.UI.LotteryRevealing"] = "Feature.AutoIshgardRestoration.UI.LotteryRevealing",
            ["Feature.AutoIshgardRestoration.UI.LotteryScratchAttempt"] = "Feature.AutoIshgardRestoration.UI.LotteryScratchAttempt",
            ["Feature.AutoIshgardRestoration.UI.LotteryScratchPreparing"] = "Feature.AutoIshgardRestoration.UI.LotteryScratchPreparing",
            ["Feature.AutoIshgardRestoration.UI.LotteryTicketComplete"] = "Feature.AutoIshgardRestoration.UI.LotteryTicketComplete",
            ["Feature.AutoIshgardRestoration.UI.NextLotteryInitializing"] = "Feature.AutoIshgardRestoration.UI.NextLotteryInitializing",
            ["Feature.AutoIshgardRestoration.UI.NoMatchingItems"] = "Feature.AutoIshgardRestoration.UI.NoMatchingItems",
            ["Feature.AutoIshgardRestoration.UI.NotCraftingJob"] = "Feature.AutoIshgardRestoration.UI.NotCraftingJob",
            ["Feature.AutoIshgardRestoration.UI.ProductPrice"] = "Feature.AutoIshgardRestoration.UI.ProductPrice",
            ["Feature.AutoIshgardRestoration.UI.ProductionConfigChanged"] = "Feature.AutoIshgardRestoration.UI.ProductionConfigChanged",
            ["Feature.AutoIshgardRestoration.UI.PurchaseComplete"] = "Feature.AutoIshgardRestoration.UI.PurchaseComplete",
            ["Feature.AutoIshgardRestoration.UI.PurchaseConditionsChanged"] = "Feature.AutoIshgardRestoration.UI.PurchaseConditionsChanged",
            ["Feature.AutoIshgardRestoration.UI.PurchaseContinueLottery"] = "Feature.AutoIshgardRestoration.UI.PurchaseContinueLottery",
            ["Feature.AutoIshgardRestoration.UI.PurchaseContinueSubmission"] = "Feature.AutoIshgardRestoration.UI.PurchaseContinueSubmission",
            ["Feature.AutoIshgardRestoration.UI.PurchaseCost"] = "Feature.AutoIshgardRestoration.UI.PurchaseCost",
            ["Feature.AutoIshgardRestoration.UI.PurchaseItem"] = "Feature.AutoIshgardRestoration.UI.PurchaseItem",
            ["Feature.AutoIshgardRestoration.UI.PurchaseItemRequired"] = "Feature.AutoIshgardRestoration.UI.PurchaseItemRequired",
            ["Feature.AutoIshgardRestoration.UI.PurchaseLimit"] = "Feature.AutoIshgardRestoration.UI.PurchaseLimit",
            ["Feature.AutoIshgardRestoration.UI.PurchasePreparing"] = "Feature.AutoIshgardRestoration.UI.PurchasePreparing",
            ["Feature.AutoIshgardRestoration.UI.PurchaseQuantity"] = "Feature.AutoIshgardRestoration.UI.PurchaseQuantity",
            ["Feature.AutoIshgardRestoration.UI.PurchaseResultUnknown"] = "Feature.AutoIshgardRestoration.UI.PurchaseResultUnknown",
            ["Feature.AutoIshgardRestoration.UI.PurchaseShopOpening"] = "Feature.AutoIshgardRestoration.UI.PurchaseShopOpening",
            ["Feature.AutoIshgardRestoration.UI.PurchaseStateInvalid"] = "Feature.AutoIshgardRestoration.UI.PurchaseStateInvalid",
            ["Feature.AutoIshgardRestoration.UI.PurchaseTimedOut"] = "Feature.AutoIshgardRestoration.UI.PurchaseTimedOut",
            ["Feature.AutoIshgardRestoration.UI.PurchaseUnavailable"] = "Feature.AutoIshgardRestoration.UI.PurchaseUnavailable",
            ["Feature.AutoIshgardRestoration.UI.Purchased"] = "Feature.AutoIshgardRestoration.UI.Purchased",
            ["Feature.AutoIshgardRestoration.UI.Purchasing"] = "Feature.AutoIshgardRestoration.UI.Purchasing",
            ["Feature.AutoIshgardRestoration.UI.Recipe"] = "Feature.AutoIshgardRestoration.UI.Recipe",
            ["Feature.AutoIshgardRestoration.UI.RecipeFormat"] = "Feature.AutoIshgardRestoration.UI.RecipeFormat",
            ["Feature.AutoIshgardRestoration.UI.RecipeInvalid"] = "Feature.AutoIshgardRestoration.UI.RecipeInvalid",
            ["Feature.AutoIshgardRestoration.UI.RecipeUnavailable"] = "Feature.AutoIshgardRestoration.UI.RecipeUnavailable",
            ["Feature.AutoIshgardRestoration.UI.RecipeVoucher"] = "Feature.AutoIshgardRestoration.UI.RecipeVoucher",
            ["Feature.AutoIshgardRestoration.UI.ScripBalance"] = "Feature.AutoIshgardRestoration.UI.ScripBalance",
            ["Feature.AutoIshgardRestoration.UI.ScripCatalogUnavailable"] = "Feature.AutoIshgardRestoration.UI.ScripCatalogUnavailable",
            ["Feature.AutoIshgardRestoration.UI.ScripStopReached"] = "Feature.AutoIshgardRestoration.UI.ScripStopReached",
            ["Feature.AutoIshgardRestoration.UI.ScripThreshold"] = "Feature.AutoIshgardRestoration.UI.ScripThreshold",
            ["Feature.AutoIshgardRestoration.UI.ScripThresholdHelp"] = "Feature.AutoIshgardRestoration.UI.ScripThresholdHelp",
            ["Feature.AutoIshgardRestoration.UI.SearchItem"] = "Feature.AutoIshgardRestoration.UI.SearchItem",
            ["Feature.AutoIshgardRestoration.UI.SelectPurchaseItem"] = "Feature.AutoIshgardRestoration.UI.SelectPurchaseItem",
            ["Feature.AutoIshgardRestoration.UI.SelectRecipe"] = "Feature.AutoIshgardRestoration.UI.SelectRecipe",
            ["Feature.AutoIshgardRestoration.UI.Start"] = "Feature.AutoIshgardRestoration.UI.Start",
            ["Feature.AutoIshgardRestoration.UI.Status"] = "Feature.AutoIshgardRestoration.UI.Status",
            ["Feature.AutoIshgardRestoration.UI.Stop"] = "Feature.AutoIshgardRestoration.UI.Stop",
            ["Feature.AutoIshgardRestoration.UI.StopThreshold"] = "Feature.AutoIshgardRestoration.UI.StopThreshold",
            ["Feature.AutoIshgardRestoration.UI.StopThresholdHelp"] = "Feature.AutoIshgardRestoration.UI.StopThresholdHelp",
            ["Feature.AutoIshgardRestoration.UI.SubmissionLotteryComplete"] = "Feature.AutoIshgardRestoration.UI.SubmissionLotteryComplete",
            ["Feature.AutoIshgardRestoration.UI.SwitchCraftingJob"] = "Feature.AutoIshgardRestoration.UI.SwitchCraftingJob",
            ["Feature.AutoIshgardRestoration.UI.TicketThreshold"] = "Feature.AutoIshgardRestoration.UI.TicketThreshold",
            ["Feature.AutoIshgardRestoration.UI.UnknownJob"] = "Feature.AutoIshgardRestoration.UI.UnknownJob",
            ["Feature.AutoIshgardRestoration.UI.VoucherCount"] = "Feature.AutoIshgardRestoration.UI.VoucherCount",
            ["Feature.AutoIshgardRestoration.UI.VoucherUnknown"] = "Feature.AutoIshgardRestoration.UI.VoucherUnknown",
            ["Feature.AutoIshgardRestoration.WaitingForArtisanStop"] = "Feature.AutoIshgardRestoration.WaitingForArtisanStop",
            ["Feature.AutoIshgardRestoration.WaitingForContinuousMovability"] = "Feature.AutoIshgardRestoration.WaitingForContinuousMovability",
            ["Feature.AutoIshgardRestoration.WaitingForFirmament"] = "Feature.AutoIshgardRestoration.WaitingForFirmament",
            ["Feature.AutoIshgardRestoration.WaitingForInteractionBeforeCrafting"] = "Feature.AutoIshgardRestoration.WaitingForInteractionBeforeCrafting",
            ["Feature.AutoIshgardRestoration.WaitingForNPCInteraction"] = "Feature.AutoIshgardRestoration.WaitingForNPCInteraction",
            ["Feature.AutoIshgardRestoration.WaitingForNavigationReady"] = "Feature.AutoIshgardRestoration.WaitingForNavigationReady",
            ["Feature.AutoIshgardRestoration.WaitingForPlayerMovable"] = "Feature.AutoIshgardRestoration.WaitingForPlayerMovable",
            ["Feature.AutoIshgardRestoration.WaitingForPlayerPosition"] = "Feature.AutoIshgardRestoration.WaitingForPlayerPosition",
            ["Feature.AutoIshgardRestoration.WaitingForRequestClose"] = "Feature.AutoIshgardRestoration.WaitingForRequestClose",
            ["Feature.AutoIshgardRestoration.WaitingForStableTerritory"] = "Feature.AutoIshgardRestoration.WaitingForStableTerritory",
            ["Feature.AutoIshgardRestoration.WaitingForTurnIn"] = "Feature.AutoIshgardRestoration.WaitingForTurnIn"
        });

    public override bool HasSettings => true;

    private readonly Config config;
    private readonly Action saveConfig;
    private readonly Func<string, bool> tryTeleport;
    private readonly Func<bool> isTeleportBusy;
    private readonly Action cancelTeleport;
    private bool startPending;

    public AutoIshgardRestoration(Config config, Action saveConfig, Func<string, bool> tryTeleport,
        Func<bool> isTeleportBusy, Action cancelTeleport)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.tryTeleport = tryTeleport;
        this.isTeleportBusy = isTeleportBusy;
        this.cancelTeleport = cancelTeleport;
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        if (!GameState.IsCN)
            throw new InvalidOperationException(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.CNOnly"));

        status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.Idle");
        Activation.OnStopping(() => DalamudServices.Framework.Update -= OnFrameworkUpdate);
        DalamudServices.Framework.Update += OnFrameworkUpdate;
        Activation.OnStopping(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawConfigurationWindow);
        DalamudServices.PluginInterface.UiBuilder.Draw += DrawConfigurationWindow;
    }

    protected override void OnStop()
    {
        startPending = false;
        var previousStatus = status;
        StopProduction(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.Disabled"));
        if (!string.IsNullOrEmpty(lastError))
            status = previousStatus;
        ReleaseArtisan();
        windowOpen = false;
    }

    protected override bool OnInterruptAutomation()
    {
        if (!IsEnabled || !firmamentTravelStartedByModule || !isTeleportBusy())
            return false;
        startPending = false;
        StopProduction(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.Interrupted"));
        return true;
    }

    public override bool TryHandleCommand(string arguments) => HandleCommandArguments(arguments);

    public override bool TryHandleCommand(string command, string arguments)
    {
        return string.Equals(command, "ishgardrestoration", StringComparison.OrdinalIgnoreCase) &&
            HandleCommandArguments(arguments);
    }

    public override bool ResetSettings()
    {
        var defaults = new Config();
        config.SelectedRecipeID = defaults.SelectedRecipeID;
        config.TicketThreshold = defaults.TicketThreshold;
        config.AutoBuyScrips = defaults.AutoBuyScrips;
        config.ScripStopThreshold = defaults.ScripStopThreshold;
        config.ScripThreshold = defaults.ScripThreshold;
        config.ScripShopID = defaults.ScripShopID;
        config.ScripItemID = defaults.ScripItemID;
        config.ScripQuantity = defaults.ScripQuantity;
        config.ExpandedWindowSize = defaults.ExpandedWindowSize;
        windowSizePending = true;
        saveConfig();
        return true;
    }

    private bool HandleCommandArguments(string arguments)
    {
        var command = arguments.Trim();
        if (command.Length == 0 ||
            string.Equals(command, "open", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "窗口", StringComparison.OrdinalIgnoreCase))
        {
            OpenConfigurationWindow();
            return true;
        }

        return false;
    }

    private void ToggleProduction()
    {
        if (!IsEnabled)
        {
            Fail(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.EnableFirst"));
            return;
        }

        if (running || startPending)
        {
            startPending = false;
            StopProduction(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.Stopped"));
            return;
        }

        lastError = string.Empty;
        status = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.StartPending");
        startPending = true;
    }
}
