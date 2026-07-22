using Dalamud.Game.ClientState.Conditions;
using OmniToolbox.Verification;
using OmenTools;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Threading.TaskHelper;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed class InnEntryFeature(FeatureGate featureGate) : TreeHouseFeatureBase(new(
    "inn-entry",
    "Feature.InnEntry.Title",
    "Feature.InnEntry.Description",
    TreeHouseCategory.InterfaceOperation,
    true)), IInnEntryService
{
    private const int OverallTimeoutMs = 180_000;
    private const int EventAvailabilityTimeoutMs = 5_000;
    private const int EventRetryIntervalMs = 8_000;

    private TaskHelper? taskHelper;
    private string statusKey = string.Empty;
    private long startedTick;
    private long entranceReadyTick;
    private long eventWaitStartedTick;
    private long nextTeleportAttemptTick;
    private long nextEventAttemptTick;
    private long nextInteractionTick;
    private bool teleportRequested;
    private bool eventStarted;

    public bool IsRunning => taskHelper?.IsBusy == true;

    public bool IsEnteredInnRoom => InnEntryInterop.IsInnRoomLikeArea();

    public string StatusKey => statusKey;

    public override bool HasSettings => true;

    public override bool DrawSettings() => InnEntryPanel.Draw(this, IsEnabled);

    public bool RequestEnterNearestInn() => RequestEnterNearestInn(false);

    public bool RequestEnterNearestInnForCacheCollection() => RequestEnterNearestInn(true);

    public void Cancel()
    {
        taskHelper?.Abort();
        statusKey = string.Empty;
        if (!IsEnabled)
        {
            taskHelper?.Dispose();
            taskHelper = null;
        }
    }

    protected override void OnEnable()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        taskHelper ??= CreateTaskHelper();
    }

    protected override void OnDisable()
    {
        taskHelper?.Dispose();
        taskHelper = null;
        ResetStatus();
    }

    protected override void OnDispose()
    {
        taskHelper?.Dispose();
        taskHelper = null;
    }

    private bool RequestEnterNearestInn(bool cacheCollection)
    {
        var services = DService.Instance();
        if ((!cacheCollection && !IsEnabled) ||
            !featureGate.CanUsePrivateFeature(out _) ||
            !services.ClientState.IsLoggedIn ||
            services.ObjectTable.LocalPlayer is null)
        {
            return false;
        }

        taskHelper ??= CreateTaskHelper();
        if (taskHelper.IsBusy)
        {
            return true;
        }

        startedTick = Environment.TickCount64;
        entranceReadyTick = 0;
        eventWaitStartedTick = 0;
        nextTeleportAttemptTick = 0;
        nextEventAttemptTick = 0;
        nextInteractionTick = 0;
        teleportRequested = false;
        eventStarted = false;
        statusKey = "Feature.InnEntry.Status.SelectTarget";

        taskHelper.Enqueue(WaitForGridania, timeoutMS: OverallTimeoutMs);
        taskHelper.Enqueue(SendToEntrance, timeoutMS: 15_000);
        taskHelper.DelayNext(1_400);
        taskHelper.Enqueue(EnterRoom, timeoutMS: OverallTimeoutMs);
        return true;
    }

    private TaskHelper CreateTaskHelper() => new()
    {
        RetryIntervalMS = 100,
        TimeoutMS = OverallTimeoutMs,
        TimeoutAction = ResetStatus,
        ExceptionAction = ResetStatus
    };

    private bool WaitForGridania()
    {
        if (!CanContinue())
        {
            return false;
        }

        var now = Environment.TickCount64;
        var isBetweenAreas = IsBetweenAreas();
        if (DService.Instance().ClientState.TerritoryType == InnEntryInterop.GridaniaTerritoryId &&
            !isBetweenAreas)
        {
            entranceReadyTick = now + (teleportRequested ? 800 : 0);
            statusKey = "Feature.InnEntry.Status.MoveToEntrance";
            return true;
        }

        if (isBetweenAreas)
        {
            statusKey = "Feature.InnEntry.Status.WaitLanding";
            return false;
        }

        statusKey = "Feature.InnEntry.Status.Teleport";
        if (now < nextTeleportAttemptTick)
        {
            return false;
        }

        if (!InnEntryInterop.TryTeleportToGridania())
        {
            Cancel();
            return false;
        }

        teleportRequested = true;
        nextTeleportAttemptTick = now + 15_000;
        return false;
    }

    private bool SendToEntrance()
    {
        if (!CanContinue() ||
            IsBetweenAreas() ||
            DService.Instance().ClientState.TerritoryType != InnEntryInterop.GridaniaTerritoryId ||
            Environment.TickCount64 < entranceReadyTick)
        {
            return false;
        }

        statusKey = "Feature.InnEntry.Status.MoveToEntrance";
        if (InnEntryInterop.SendToEntrance())
        {
            return true;
        }

        Cancel();
        return false;
    }

    private bool EnterRoom()
    {
        if (!CanContinue())
        {
            return false;
        }

        if (InnEntryInterop.IsInnRoomLikeArea())
        {
            statusKey = "Feature.InnEntry.Status.Completed";
            return true;
        }

        var now = Environment.TickCount64;
        if (!eventStarted)
        {
            statusKey = "Feature.InnEntry.Status.StartEvent";
            if (AddonSelectStringEvent.Select(0))
            {
                eventStarted = true;
                nextEventAttemptTick = now + EventRetryIntervalMs;
                nextInteractionTick = now + 600;
                statusKey = "Feature.InnEntry.Status.SelectRoom";
                return false;
            }

            if (eventWaitStartedTick == 0)
            {
                eventWaitStartedTick = now;
            }

            if (!InnEntryInterop.IsInnEventNearby() &&
                now - eventWaitStartedTick < EventAvailabilityTimeoutMs)
            {
                return false;
            }

            AdvanceDialog();
            if (!InnEntryInterop.TryStartInnEvent())
            {
                Cancel();
                return false;
            }

            eventStarted = true;
            nextEventAttemptTick = now + EventRetryIntervalMs;
            nextInteractionTick = now + 500;
            statusKey = "Feature.InnEntry.Status.SelectRoom";
            return false;
        }

        statusKey = "Feature.InnEntry.Status.SelectRoom";
        if (now < nextInteractionTick)
        {
            return false;
        }

        AdvanceDialog();
        AddonSelectStringEvent.Select(0);
        nextInteractionTick = now + 350;
        if (now >= nextEventAttemptTick)
        {
            eventStarted = false;
            eventWaitStartedTick = now;
        }

        return false;
    }

    private bool CanContinue()
    {
        var services = DService.Instance();
        if (Environment.TickCount64 - startedTick <= OverallTimeoutMs &&
            services.ClientState.IsLoggedIn &&
            services.ObjectTable.LocalPlayer is not null)
        {
            return true;
        }

        Cancel();
        return false;
    }

    private static void AdvanceDialog()
    {
        AddonTalkEvent.ClickNext();
        AddonSelectYesnoEvent.ClickYes();
    }

    private static bool IsBetweenAreas() =>
        DService.Instance().Condition[ConditionFlag.BetweenAreas] ||
        DService.Instance().Condition[ConditionFlag.BetweenAreas51];

    private void ResetStatus() => statusKey = string.Empty;
}
