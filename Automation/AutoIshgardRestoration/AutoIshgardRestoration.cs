using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using Action = System.Action;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AutoIshgardRestorationTitle"),
        Description = OmniLoc.Get("AutoIshgardRestorationDescription"),
        Category = ModuleCategory.Automation,
        Author = "Angelways，YouShu",
        SupportUrls = ["https://github.com/Angelways"],
        Commands =
        [
            new("Feature.AutoIshgardRestoration.OpenCommand", OmniCommandCatalog.Format("ishgardrestoration"))
        ]
    };

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

    protected override void OnEnable()
    {
        if (!GameState.IsCN)
            throw new InvalidOperationException(OmniLoc.Get("Feature.AutoIshgardRestoration.CNOnly"));

        status = OmniLoc.Get("Feature.AutoIshgardRestoration.Idle");
        DalamudServices.Framework.Update += OnFrameworkUpdate;
        DalamudServices.PluginInterface.UiBuilder.Draw += DrawConfigurationWindow;
    }

    protected override void OnDisable()
    {
        DalamudServices.Framework.Update -= OnFrameworkUpdate;
        DalamudServices.PluginInterface.UiBuilder.Draw -= DrawConfigurationWindow;
        startPending = false;
        var previousStatus = status;
        StopProduction(OmniLoc.Get("Feature.AutoIshgardRestoration.Disabled"));
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
        StopProduction(OmniLoc.Get("Feature.AutoIshgardRestoration.Interrupted"));
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
            Fail(OmniLoc.Get("Feature.AutoIshgardRestoration.EnableFirst"));
            return;
        }

        if (running || startPending)
        {
            startPending = false;
            StopProduction(OmniLoc.Get("Feature.AutoIshgardRestoration.Stopped"));
            return;
        }

        lastError = string.Empty;
        status = OmniLoc.Get("Feature.AutoIshgardRestoration.StartPending");
        startPending = true;
    }
}
