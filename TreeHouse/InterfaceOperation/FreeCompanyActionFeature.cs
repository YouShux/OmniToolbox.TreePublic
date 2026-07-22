using System.Diagnostics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.PublicBridge;
using OmniToolbox.UI;
using OmniToolbox.Verification;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed class FreeCompanyActionFeature : TreeHouseFeatureBase
{
    private const int ConfirmationWindowMs = 3000;
    private static readonly long ConfirmationWindowTicks = Stopwatch.Frequency * ConfirmationWindowMs / 1000;
    private static readonly (int Id, string LabelKey)[] Actions =
    [
        (33, "Feature.FreeCompanyAction.Action.TeleportDiscountII"),
        (30, "Feature.FreeCompanyAction.Action.FoodDurationII"),
        (31, "Feature.FreeCompanyAction.Action.GearWearReductionII"),
        (17, "Feature.FreeCompanyAction.Action.BattleExperienceII"),
        (23, "Feature.FreeCompanyAction.Action.MgpBonusII")
    ];

    private readonly IAgentEventService agentEventService;
    private readonly FeatureGate featureGate;
    private AddonEventRegistry? addonEvents;
    private long confirmationDeadline;
    private bool confirmationUpdateRegistered;

    public FreeCompanyActionFeature(
        IAgentEventService agentEventService,
        FeatureGate featureGate) : base(new(
        "free-company-action",
        "Feature.FreeCompanyAction.Title",
        "Feature.FreeCompanyAction.Description",
        TreeHouseCategory.Utility,
        true,
        true))
    {
        this.agentEventService = agentEventService;
        this.featureGate = featureGate;
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        foreach (var action in Actions)
        {
            if (ImGui.Button(
                    $"{OmniLoc.Get(action.LabelKey)}##freeCompanyAction{action.Id}",
                    new(ImGui.GetContentRegionAvail().X, 0f)))
            {
                TryPurchaseAction(action.Id, out _);
            }
        }

        return false;
    }

    public bool TryPurchaseAction(int actionId, out string failureReasonKey)
    {
        failureReasonKey = string.Empty;
        if (!IsEnabled)
        {
            return Fail(out failureReasonKey, "Feature.FreeCompanyAction.Disabled");
        }

        if (!featureGate.CanUseTestFeature(out failureReasonKey))
        {
            return false;
        }

        var supported = false;
        foreach (var action in Actions)
        {
            if (action.Id != actionId)
            {
                continue;
            }

            supported = true;
            break;
        }

        if (!supported)
        {
            return Fail(out failureReasonKey, "Feature.FreeCompanyAction.InvalidAction");
        }

        if (!BeginConfirmationWindow())
        {
            return Fail(out failureReasonKey, "Feature.FreeCompanyAction.ConfirmationUnavailable");
        }

        if (agentEventService.TrySend(
                "FreeCompanyExchange",
                "0",
                ["2", actionId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                out failureReasonKey))
        {
            return true;
        }

        ClearConfirmationWindow();
        return false;
    }

    protected override void OnEnable()
    {
        if (!featureGate.CanUseTestFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var events = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        try
        {
            events.Register(AddonEvent.PostSetup, "SelectYesno", OnSelectYesno);
            addonEvents = events;
        }
        catch
        {
            events.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            addonEvents?.Dispose();
        }
        finally
        {
            addonEvents = null;
            ClearConfirmationWindow();
        }
    }

    private bool BeginConfirmationWindow()
    {
        confirmationDeadline = Stopwatch.GetTimestamp() + ConfirmationWindowTicks;
        if (confirmationUpdateRegistered)
        {
            return true;
        }

        if (!FrameworkManager.Instance().Reg(OnUpdate, 100))
        {
            confirmationDeadline = 0;
            return false;
        }

        confirmationUpdateRegistered = true;
        return true;
    }

    private void OnSelectYesno(AddonEvent _, AddonArgs __)
    {
        if (!IsEnabled || confirmationDeadline == 0)
        {
            return;
        }

        if (Stopwatch.GetTimestamp() > confirmationDeadline)
        {
            ClearConfirmationWindow();
            return;
        }

        ClearConfirmationWindow();
        AddonSelectYesnoEvent.ClickYes();
    }

    private void OnUpdate(IFramework _)
    {
        if (confirmationDeadline != 0 && Stopwatch.GetTimestamp() <= confirmationDeadline)
        {
            return;
        }

        ClearConfirmationWindow();
    }

    private void ClearConfirmationWindow()
    {
        confirmationDeadline = 0;
        if (!confirmationUpdateRegistered)
        {
            return;
        }

        confirmationUpdateRegistered = false;
        FrameworkManager.Instance().Unreg(OnUpdate);
    }

    private static bool Fail(out string failureReasonKey, string reasonKey)
    {
        failureReasonKey = reasonKey;
        return false;
    }
}
