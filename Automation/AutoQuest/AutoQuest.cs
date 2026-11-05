using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools;
using OmenTools.Dalamud;
using OmenTools.Extensions;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Operations;
using OmniToolbox.Quests;
using TreeTools.Threading;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class AutoQuest(IQuestTravelService travel, IQuestCombatService combat) : OmniModuleBase, IQuestService
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Automation,
        Author = "YouShu",
        ShowsAutoPathStatus = true,
        Commands =
        [
            new("CommandOpen", OmniCommandCatalog.FormatModule(nameof(AutoQuest), "open")),
            new("CommandStart", OmniCommandCatalog.FormatModule(nameof(AutoQuest), "start <任务ID>")),
            new("CommandAll", OmniCommandCatalog.FormatModule(nameof(AutoQuest), "all")),
            new("CommandPause", OmniCommandCatalog.FormatModule(nameof(AutoQuest), "pause")),
            new("CommandResume", OmniCommandCatalog.FormatModule(nameof(AutoQuest), "resume")),
            new("CommandStop", OmniCommandCatalog.FormatModule(nameof(AutoQuest), "stop"))
        ]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        TextKeys.ToDictionary(static key => key, static key => $"Feature.AutoQuest.{key}"));

    private readonly QuestData data = new();
    private readonly OperationTracker operations = new("Quest");
    private readonly Dictionary<string, QuestOperationResult> results = [];
    private TaskHelper? tasks;
    private Session? session;
    private bool starting;
    private bool restoring;
    private bool windowOpen;
    private bool panelCollapsed;
    private bool panelRestore = true;
    private Vector2 panelSize;
    private float panelScale;
    private Vector2 panelViewport;
    private string search = string.Empty;
    private QuestTargetType? filter;
    private QuestCatalogPage? page;
    private readonly HashSet<QuestTarget> selected = [];
    private int pageOffset;
    private long nextCatalogRefresh;
    private string? panelError;

    public Func<CancellationToken, Task<bool>>? EnableForRequestAsync { get; set; }
    public Func<Task>? RestoreAfterRequestAsync { get; set; }
    public OperationSnapshot? LatestOperation => operations.Latest;

    public event Action<OperationSnapshot>? OperationChanged
    {
        add => operations.Changed += value;
        remove => operations.Changed -= value;
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        data.Bind(InstanceLifetime);
        tasks = new() { TimeoutMS = 0, RetryIntervalMS = 100 };
        activation.Add(tasks);
        activation.AddAsync(FinalizeAbandonedSessionAsync);
        activation.OnStopping(() =>
        {
            if (session is { } current)
            {
                current.Cancelled = true;
                current.Error = "module_disabled";
            }
            DService.Instance().PI.UiBuilder.Draw -= DrawWindow;
            DService.Instance().ClientState.Logout -= OnLogout;
            windowOpen = false;
        });
        DService.Instance().PI.UiBuilder.Draw += DrawWindow;
        DService.Instance().ClientState.Logout += OnLogout;
    }

    protected override void OnStop() => tasks = null;

    protected override bool OnInterruptAutomation() => operations.Current is { } current && CancelOperation(current.OperationID);

    private void OnLogout(int type, int code) => FailCurrent("character_changed_or_disconnected");

    public QuestCatalogPage GetCatalog(QuestCatalogQuery query)
    {
        data.Bind(InstanceLifetime);
        return data.GetCatalog(query);
    }

    public QuestEvaluation Evaluate(QuestRequest request)
    {
        if (request is null || !request.IsValid())
            return new(false, false, [], "request_invalid");
        data.Bind(InstanceLifetime);
        return data.Evaluate(request);
    }
    public OperationSnapshot? GetOperation(string operationID) => operations.Get(operationID);

    public QuestOperationResult? GetQuestOperation(string operationID)
    {
        if (operationID is not { Length: 32 })
            return null;
        if (session is { } current && current.Operation.OperationID == operationID)
            return new(operations.Get(operationID) ?? current.Operation, current.Request.Caller, current.Request.Relation, [.. current.Results.Values]);
        return results.GetValueOrDefault(operationID);
    }

    public QuestServiceStatus GetStatus()
    {
        if (session is not { } current)
            return new(null, starting ? QuestExecutionPhase.Starting : QuestExecutionPhase.Idle, null, 0, null, null, 0, 0, 0);
        var completed = current.Results.Values.Count(static result => result.Availability == QuestAvailability.Satisfied);
        var blocked = current.Results.Values.Count(static result => result.Availability is QuestAvailability.Blocked or QuestAvailability.Unsupported or QuestAvailability.Waiting);
        return new(current.Operation.OperationID, current.Phase, current.Current,
            DalamudServices.PlayerState.IsLoaded && current.Operation.CharacterContentID == DalamudServices.PlayerState.ContentId &&
            current.Current is { } target ? data.GetSequence(target) : (byte)0,
            current.Step?.Kind.ToString(), current.Error, completed, current.Goals.Length - current.Results.Count, blocked);
    }

    public Task<QuestStartResult> StartAsync(QuestRequest request, CancellationToken cancellationToken = default)
    {
        var lifetime = InstanceLifetime;
        var pending = StartCoreAsync(request, lifetime, cancellationToken);
        lifetime.Track(pending);
        return pending;
    }

    private async Task<QuestStartResult> StartCoreAsync(QuestRequest request, FeatureLifetime lifetime, CancellationToken cancellationToken)
    {
        if (request is null || !request.IsValid())
            return new(false, null, "request_invalid");
        if (starting || restoring || session is not null || operations.Current is not null)
            return new(false, null, "quest_busy");
        request = request with { Targets = request.Targets.Select(static target => target.Normalize()).Distinct().ToArray() };
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.CancellationToken);
        cancellationToken = linkedCancellation.Token;
        starting = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEnabled && (EnableForRequestAsync is null || !await EnableForRequestAsync(cancellationToken)))
                return new(false, null, "module_unavailable");
            if (request.Mode == QuestRequestMode.Catalog)
            {
                var loaded = false;
                var available = true;
                while (!loaded && available)
                {
                    await DalamudServices.Framework.RunOnFrameworkThread(() =>
                    {
                        available = IsEnabled && !Activation.IsStopping;
                        if (!available)
                            return;
                        data.ExpandTargets(request);
                        loaded = data.CatalogReady || data.CatalogError is not null;
                    });
                    if (!loaded && available)
                        await Task.Delay(100, cancellationToken);
                }
                if (!available)
                    return new(false, null, "module_unavailable");
            }
            return await DalamudServices.Framework.RunOnFrameworkThread(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return new QuestStartResult(false, null, "cancelled");
                if (!IsEnabled || Activation.IsStopping || tasks is null)
                    return new QuestStartResult(false, null, "module_unavailable");
                if (!DalamudServices.PlayerState.IsLoaded || !DService.Instance().ClientState.IsLoggedIn)
                    return new QuestStartResult(false, null, "not_logged_in");
                var goals = data.ExpandTargets(request);
                if (goals.Length == 0)
                    return new QuestStartResult(false, null, data.CatalogError ?? (data.CatalogReady ? "catalog_empty" : "resource_loading"));
                var operation = operations.Begin(request.Mode.ToString(), DalamudServices.PlayerState.ContentId);
                data.BeginRound(operation.OperationID);
                session = new(operation, request, goals);
                LogTransition(session, "operation-accepted", $"caller={request.Caller} mode={request.Mode} goals={goals.Length}");
                tasks.EnqueueAsync(RunAsync, "AutoQuest", exceptionAction: () => FailCurrent("execution_exception"));
                return new QuestStartResult(true, operation.OperationID);
            });
        }
        catch (OperationCanceledException)
        {
            return new(false, null, "cancelled");
        }
        finally
        {
            starting = false;
            if (session is null && !lifetime.IsStopping && RestoreAfterRequestAsync is not null)
                ScheduleRestore();
        }
    }

    public bool CancelOperation(string operationID)
    {
        if (!Owns(operationID) || session is not { } current)
            return false;
        current.Cancelled = true;
        current.Phase = QuestExecutionPhase.Stopping;
        return true;
    }

    public bool Pause(string operationID)
    {
        if (!Owns(operationID) || session is not { Cancelled: false } current)
            return false;
        current.Paused = true;
        current.Phase = QuestExecutionPhase.Paused;
        StopOwned(current);
        return true;
    }

    public bool Resume(string operationID)
    {
        if (!Owns(operationID) || session is not { Cancelled: false, Paused: true } current)
            return false;
        current.Paused = false;
        current.Phase = QuestExecutionPhase.Planning;
        if (current.Step is { } step && (!current.ActionSent ||
            !current.AcceptSent && !current.TurnInSent && !current.HandOverSent &&
            step.Kind is QuestStepKind.Interact or QuestStepKind.AcceptQuest or QuestStepKind.CompleteQuest or
                QuestStepKind.WalkTo or QuestStepKind.Craft or QuestStepKind.Gather or QuestStepKind.Combat or QuestStepKind.Duty))
            current.Step = null;
        current.StepStartedAt = Environment.TickCount64;
        return true;
    }

    private bool Owns(string operationID) => operations.IsCurrent(operationID, DalamudServices.PlayerState.ContentId);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var current = session;
        if (current is null)
            return;
        try
        {
            while (!await DalamudServices.Framework.RunOnFrameworkThread(() => Tick(current)))
                await Task.Delay(100, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            current.Cancelled = true;
            current.Error ??= "cancelled";
        }
        catch (Exception exception)
        {
            current.Error = "execution_exception";
            DalamudServices.PluginLog.Error(exception, "AutoQuest execution failed.");
        }
        finally
        {
            await DrainAsync(current);
            await DalamudServices.Framework.RunOnFrameworkThread(() => Finish(current));
        }
    }

    private bool Tick(Session current)
    {
        if (current.Cancelled)
            return true;

        var services = DService.Instance();
        var contentID = DalamudServices.PlayerState.ContentId;
        var transientCharacterState = services.Condition.IsBetweenAreas || !UIModule.IsScreenReady() || !DalamudServices.PlayerState.IsLoaded ||
            contentID == 0 || services.ObjectTable.LocalPlayer is null;
        if (!services.ClientState.IsLoggedIn)
        {
            current.Cancelled = true;
            current.Error = "character_changed_or_disconnected";
            return true;
        }
        if (contentID != 0 && current.Operation.CharacterContentID != contentID)
        {
            current.Cancelled = true;
            current.Error = "character_changed_or_disconnected";
            return true;
        }
        if (transientCharacterState)
        {
            current.Phase = QuestExecutionPhase.Waiting;
            current.Error = "character_not_ready";
            var unavailableSince = current.CharacterUnavailableSince ??= Environment.TickCount64;
            if (Environment.TickCount64 - unavailableSince < 180_000)
                return false;
            current.Cancelled = true;
            current.Error = "character_not_ready_timeout";
            return true;
        }
        current.CharacterUnavailableSince = null;

        if (current.CleanupRequested && !DrainOwned(current))
            return false;
        if (current.Paused)
        {
            current.Phase = QuestExecutionPhase.Paused;
            return false;
        }

        if (current.GoalIndex >= current.Goals.Length)
            return true;
        var goal = current.Goals[current.GoalIndex];
        if (current.Current is { } activeTarget && current.Step is { } activeStep && IsOwnedQuestEvent(current))
            return ExecuteCurrentStep(current, goal, activeTarget, activeStep);
        if (data.IsSatisfied(goal))
        {
            LogTransition(current, "target-complete", $"goal={goal.Type}:{goal.ID}");
            current.Results[goal] = new(goal, QuestAvailability.Satisfied, []);
            current.GoalIndex++;
            StopOwned(current);
            current.Current = null;
            current.Step = null;
            current.Error = null;
            return current.Request.Relation == QuestTargetRelation.Any && current.Request.Mode == QuestRequestMode.Targets;
        }
        var next = data.Next(goal, current.BlockedPrerequisites, out var blockers);
        if (next is null)
            return SetBlocked(current, goal, blockers);
        if (current.Current != next)
        {
            StopOwned(current);
            current.Current = next;
            current.Step = null;
            current.StepStartedAt = Environment.TickCount64;
            LogTransition(current, "target-start", $"goal={goal.Type}:{goal.ID} dependency={next.Type}:{next.ID}");
            return false;
        }
        if (current.Step is { } previous && data.IsStepComplete(next, previous))
        {
            LogTransition(current, "step-complete", previous.Kind.ToString());
            StopOwned(current);
            current.Step = null;
            current.StepStartedAt = Environment.TickCount64;
            return false;
        }
        if (current.Step is { } pendingStep && current.ActionSent &&
            !IsOwnedQuestEvent(current) && data.ReadStateKey(next) != pendingStep.StateKey)
        {
            StopOwned(current);
            current.Step = null;
            current.StepStartedAt = Environment.TickCount64;
            return false;
        }
        if (current.Step is null)
        {
            if (!data.TryGetStep(next, out var step, out blockers) || step is null)
                return SetBlocked(current, goal, blockers);
            current.Step = step;
            current.StepStartedAt = Environment.TickCount64;
            current.ActionSent = false;
            current.AcceptSent = false;
            current.TurnInSent = false;
            current.HandOverSent = false;
            current.HandOver = null;
            current.GatheringProgressAt = 0;
            current.GatheringActionAt = 0;
            LogTransition(current, "step-start", $"kind={step.Kind} sequence={step.Sequence} territory={step.TerritoryID} object={step.ObjectID} argument={step.Argument} job={step.ClassJobID}");
        }
        return ExecuteCurrentStep(current, goal, next, current.Step);
    }

    private bool ExecuteCurrentStep(Session current, QuestTarget goal, QuestTarget target, QuestStep step)
    {
        current.RelatedID = null;
        current.Error = null;
        var failure = ExecuteStep(current, target, step);
        if (failure is null)
            return false;
        LogTransition(current, "step-failure", $"error={failure} related={current.RelatedID}");
        if (failure is "navigation_busy" or "combat_busy" or "plugin_busy" or "resource_loading" or
            "player_busy" or "character_busy" or "character_not_ready" or "external_interaction_busy" or
            "external_gathering_busy" or "npc_entry_unavailable")
        {
            current.Error = failure;
            current.Phase = QuestExecutionPhase.Waiting;
            if (Environment.TickCount64 - current.StepStartedAt < 30_000)
                return false;
        }
        var canRetry = !current.ActionSent || failure == "interaction_timeout" &&
            !current.AcceptSent && !current.TurnInSent && !current.HandOverSent && !IsOwnedQuestEvent(current) &&
            step.Kind is QuestStepKind.Interact or QuestStepKind.AcceptQuest or QuestStepKind.CompleteQuest;
        if (canRetry && (failure is "navigation_failed" or "navigation_timeout" or "target_not_found" or
            "interaction_timeout" or "teleport_interrupted" or "teleport_unconfirmed" or "teleport_timeout" or
            "teleport_arrival_unconfirmed" or "boundary_transition_timeout" or "navigation_ipc_failed") && current.RecoveryAttempts < 3)
        {
            current.RecoveryAttempts++;
            LogTransition(current, "step-retry", $"attempt={current.RecoveryAttempts} error={failure}");
            StopOwned(current);
            current.Step = null;
            current.StepStartedAt = Environment.TickCount64;
            current.NextActionAt = Environment.TickCount64 + 1_000;
            return false;
        }
        return SetBlocked(current, goal, [new(failure, current.RelatedID)]);
    }

    private bool SetBlocked(Session current, QuestTarget goal, QuestBlocker[] blockers)
    {
        if (blockers.Length == 0)
            blockers = [new("mechanism_unresolved")];
        if (blockers.All(static blocker => blocker.Code is "resource_loading" or "script_loading") &&
            Environment.TickCount64 - current.StepStartedAt < 30_000)
        {
            current.Phase = QuestExecutionPhase.Waiting;
            current.Error = blockers[0].Code;
            return false;
        }
        var availability = blockers.Any(static blocker => blocker.Code.Contains("unsupported", StringComparison.Ordinal) ||
            blocker.Code.Contains("unresolved", StringComparison.Ordinal) || blocker.Code.Contains("unimplemented", StringComparison.Ordinal) ||
            blocker.Code.Contains("unverified", StringComparison.Ordinal) ||
            blocker.Code.StartsWith("script_", StringComparison.Ordinal) && blocker.Code != "script_loading")
            ? QuestAvailability.Unsupported : blockers.Any(static blocker => blocker.RetryAt is not null ||
                blocker.Code is "resource_loading" or "script_loading") ? QuestAvailability.Waiting : QuestAvailability.Blocked;
        current.Results[goal] = new(goal, availability, blockers);
        var blockerText = string.Join(",", blockers.Select(static blocker => blocker.Code));
        LogTransition(current, "target-blocked", $"goal={goal.Type}:{goal.ID} target={current.Current?.ID} blockers={blockerText}");
        if (current.Current is { Type: QuestTargetType.Quest } failedTarget)
            current.BlockedPrerequisites.Add(failedTarget.ID);
        if (goal.Type == QuestTargetType.Quest)
            current.BlockedPrerequisites.Add(goal.ID);
        StopOwned(current);
        current.GoalIndex++;
        current.Current = null;
        current.Step = null;
        current.StepStartedAt = Environment.TickCount64;
        current.Error = blockers[0].Code;
        return false;
    }

    private void FailCurrent(string error)
    {
        if (session is { } current)
        {
            current.Error = error;
            current.Cancelled = true;
        }
    }

    private void Finish(Session current)
    {
        if (!ReferenceEquals(session, current) || current.Phase == QuestExecutionPhase.Finished)
            return;
        var canReadState = DService.Instance().ClientState.IsLoggedIn && DalamudServices.PlayerState.IsLoaded &&
            current.Operation.CharacterContentID == DalamudServices.PlayerState.ContentId;
        var completedGoals = current.Goals.Where(goal => canReadState ? data.IsSatisfied(goal) :
            current.Results.TryGetValue(goal, out var result) && result.Availability == QuestAvailability.Satisfied).ToHashSet();
        var success = current.Request.Relation == QuestTargetRelation.Any && current.Request.Mode == QuestRequestMode.Targets
            ? completedGoals.Count != 0 : completedGoals.Count == current.Goals.Length;
        var state = current.Cancelled ? OperationState.Cancelled : success ? OperationState.Succeeded : OperationState.Failed;
        foreach (var goal in current.Goals)
        {
            if (completedGoals.Contains(goal))
                current.Results[goal] = new(goal, QuestAvailability.Satisfied, []);
            else if (!current.Results.ContainsKey(goal))
                current.Results[goal] = new(goal, QuestAvailability.Blocked, [new(current.Error ?? "request_incomplete")]);
        }
        operations.Complete(state, state == OperationState.Succeeded ? string.Empty : current.Error ?? "request_incomplete");
        LogTransition(current, "operation-finished", $"state={state} completed={completedGoals.Count}/{current.Goals.Length} reason={current.Error}");
        if (operations.Latest is { } completed)
            results[completed.OperationID] = new(completed, current.Request.Caller, current.Request.Relation, [.. current.Results.Values]);
        foreach (var id in results.Keys.Where(id => operations.Get(id) is null).ToArray())
            results.Remove(id);
        current.Phase = QuestExecutionPhase.Finished;
        data.EndRound(current.Operation.OperationID);
        session = null;
        nextCatalogRefresh = 0;
        if (!Activation.IsStopping)
            ScheduleRestore();
    }

    private async ValueTask FinalizeAbandonedSessionAsync()
    {
        if (session is not { } current)
            return;
        current.Cancelled = true;
        current.Error ??= "module_disabled";
        await DrainAsync(current);
        await DalamudServices.Framework.RunOnFrameworkThread(() => Finish(current));
    }

    private void ScheduleRestore()
    {
        if (restoring || RestoreAfterRequestAsync is null)
            return;
        restoring = true;
        InstanceLifetime.Track(RestoreAsync());
    }

    private async Task RestoreAsync()
    {
        try
        {
            await Task.Yield();
            if (RestoreAfterRequestAsync is { } restore)
                await restore();
        }
        finally
        {
            restoring = false;
        }
    }

    private sealed class Session(OperationSnapshot operation, QuestRequest request, QuestTarget[] goals)
    {
        public OperationSnapshot Operation { get; } = operation;
        public QuestRequest Request { get; } = request;
        public QuestTarget[] Goals { get; } = goals;
        public Dictionary<QuestTarget, QuestTargetResult> Results { get; } = [];
        public HashSet<uint> BlockedPrerequisites { get; } = [];
        public QuestExecutionPhase Phase { get; set; } = QuestExecutionPhase.Planning;
        public QuestTarget? Current { get; set; }
        public QuestStep? Step { get; set; }
        public int GoalIndex { get; set; }
        public int RecoveryAttempts { get; set; }
        public long StepStartedAt { get; set; } = Environment.TickCount64;
        public long? CharacterUnavailableSince { get; set; }
        public bool Cancelled { get; set; }
        public bool Paused { get; set; }
        public bool ActionSent { get; set; }
        public bool AcceptSent { get; set; }
        public bool TurnInSent { get; set; }
        public bool HandOverSent { get; set; }
        public HandOverProgress? HandOver { get; set; }
        public bool OwnsTravel { get; set; }
        public bool OwnsCombat { get; set; }
        public bool CleanupRequested { get; set; }
        public bool CleanupErrorReported { get; set; }
        public uint OwnedEventID { get; set; }
        public ulong OwnedActorID { get; set; }
        public string? Error { get; set; }
        public uint? RelatedID { get; set; }
        public long NextActionAt { get; set; }
        public ulong GatheringObjectID { get; set; }
        public int GatheringCount { get; set; }
        public byte GatheringIntegrity { get; set; }
        public bool GatheringPending { get; set; }
        public long GatheringProgressAt { get; set; }
        public long GatheringActionAt { get; set; }
        public string? LastLogKey { get; set; }
    }

    private static void LogTransition(Session current, string state, string? details = null)
    {
        var key = $"{state}|{current.GoalIndex}|{current.Current?.ID}|{current.Step?.Kind}|{details}";
        if (current.LastLogKey == key) return;
        current.LastLogKey = key;
        DalamudServices.PluginLog.Information(
            "AutoQuest operation={OperationID} state={State} goal={GoalID} target={TargetID} step={Step} phase={Phase} details={Details}",
            current.Operation.OperationID, state, current.GoalIndex < current.Goals.Length ? current.Goals[current.GoalIndex].ID : 0u,
            current.Current?.ID ?? 0u, current.Step?.Kind.ToString() ?? string.Empty, current.Phase.ToString(), details ?? string.Empty);
    }
}
