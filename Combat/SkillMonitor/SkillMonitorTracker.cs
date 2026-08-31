using System.Collections.Concurrent;
using System.Globalization;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.OmenService;
using GameCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class SkillMonitorTracker(SkillMonitorDefinition[] definitions)
{
    private const uint InvalidEntityID = 0xE0000000;

    private readonly ConcurrentQueue<ActionUse> pendingActions = new();
    private readonly Dictionary<uint, int> actionIndexes = CreateActionIndexes(definitions);
    private readonly Dictionary<uint, int> statusIndexes = CreateStatusIndexes(definitions);
    private readonly SkillMonitorMember[] members = new SkillMonitorMember[8];
    private readonly SkillMonitorRuntimeState[,] states = new SkillMonitorRuntimeState[8, definitions.Length];
    private Hook<ActionEffectHandler.Delegates.Receive>? receiveHook;

    public void Register(FeatureLifetime lifetime)
    {
        receiveHook = DService.Instance().Hook.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
            ActionEffectHandler.MemberFunctionPointers.Receive,
            OnActionEffect);
        lifetime.Add(receiveHook.Dispose);
        receiveHook.Enable();

        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 125))
        {
            throw new InvalidOperationException("Skill monitor update registration failed.");
        }

        lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
        var clientState = DService.Instance().ClientState;
        clientState.Logout += OnLogout;
        lifetime.Add(() => clientState.Logout -= OnLogout);
        clientState.TerritoryChanged += OnTerritoryChanged;
        lifetime.Add(() => clientState.TerritoryChanged -= OnTerritoryChanged);
    }

    public SkillMonitorMember GetMember(int index) => members[index];

    public SkillMonitorRuntimeState GetState(int memberIndex, int definitionIndex) => states[memberIndex, definitionIndex];

    public void Clear()
    {
        Array.Clear(members);
        Array.Clear(states);
        while (pendingActions.TryDequeue(out _))
        {
        }
    }

    private void OnActionEffect(
        uint casterEntityID,
        GameCharacter* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        try
        {
            if (header != null && header->ActionType == 1 && actionIndexes.ContainsKey(header->ActionId))
            {
                pendingActions.Enqueue(new(casterEntityID, header->ActionId, Environment.TickCount64));
            }
        }
        finally
        {
            receiveHook!.Original(casterEntityID, casterPtr, targetPos, header, effects, targetEntityIds);
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            Clear();
            return;
        }

        var agentHud = AgentHUD.Instance();
        if (agentHud == null)
        {
            Clear();
            return;
        }

        UpdateMembers(agentHud);
        while (pendingActions.TryDequeue(out var action))
        {
            ApplyAction(action);
        }

        var now = Environment.TickCount64;
        UpdateLocalCooldowns(now);
        UpdateVisualStates(now);
    }

    private void UpdateMembers(AgentHUD* agentHud)
    {
        var count = Math.Min((int)agentHud->PartyMemberCount, 8);
        byte seenMask = 0;
        for (var sourceIndex = 0; sourceIndex < count; sourceIndex++)
        {
            var hudMember = agentHud->PartyMembers[sourceIndex];
            if (hudMember.Index >= members.Length || hudMember.EntityId is 0 or InvalidEntityID || hudMember.Object == null)
            {
                continue;
            }

            var memberIndex = hudMember.Index;
            seenMask |= (byte)(1 << memberIndex);
            var classJobID = (uint)((GameCharacter*)hudMember.Object)->CharacterData.ClassJob;
            if (members[memberIndex].EntityID != hudMember.EntityId || members[memberIndex].ClassJobID != classJobID)
            {
                ResetMember(memberIndex);
                members[memberIndex] = new(hudMember.EntityId, classJobID, true);
            }

            ClearStatusActivity(memberIndex);
            var statusManager = &hudMember.Object->StatusManager;
            for (var statusIndex = 0; statusIndex < statusManager->NumValidStatuses; statusIndex++)
            {
                var status = statusManager->Status[statusIndex];
                if (status.StatusId == 0 || !statusIndexes.TryGetValue(status.StatusId, out var definitionIndex) ||
                    !definitions[definitionIndex].AppliesTo(classJobID))
                {
                    continue;
                }

                var state = states[memberIndex, definitionIndex];
                state.StatusActive = true;
                state.StatusRemainingMilliseconds = status.RemainingTime > 0f
                    ? (int)Math.Ceiling(status.RemainingTime * 1_000f)
                    : 0;
                states[memberIndex, definitionIndex] = state;
            }
        }

        for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
        {
            if ((seenMask & (1 << memberIndex)) == 0)
            {
                ResetMember(memberIndex);
            }
        }
    }

    private void ApplyAction(ActionUse action)
    {
        if (!actionIndexes.TryGetValue(action.ActionID, out var definitionIndex))
        {
            return;
        }

        for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
        {
            if (!members[memberIndex].Visible || members[memberIndex].EntityID != action.CasterEntityID ||
                !definitions[definitionIndex].AppliesTo(members[memberIndex].ClassJobID))
            {
                continue;
            }

            var state = states[memberIndex, definitionIndex];
            state.CooldownKnown = true;
            state.ReadyAtTick = action.Tick + definitions[definitionIndex].CooldownMilliseconds;
            state.CooldownMilliseconds = definitions[definitionIndex].CooldownMilliseconds;
            state.ActiveUntilTick = action.Tick + definitions[definitionIndex].ActiveMilliseconds;
            states[memberIndex, definitionIndex] = state;
            return;
        }
    }

    private void UpdateVisualStates(long now)
    {
        for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
        {
            if (!members[memberIndex].Visible)
            {
                continue;
            }

            for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
            {
                var state = states[memberIndex, definitionIndex];
                var definition = definitions[definitionIndex];
                var isActive = definition.StatusID != 0
                    ? state.StatusActive
                    : state.ActiveUntilTick > now;
                if (isActive)
                {
                    state.DisplayState = SkillMonitorDisplayState.Active;
                    state.CooldownText = string.Empty;
                    state.CooldownProgress = 0f;
                    var statusSeconds = (int)Math.Ceiling(state.StatusRemainingMilliseconds / 1_000d);
                    if (statusSeconds != state.DisplaySeconds)
                    {
                        state.DisplaySeconds = statusSeconds;
                        state.StatusText = statusSeconds > 0
                            ? FormatDuration(statusSeconds)
                            : string.Empty;
                    }
                }
                else if (state.CooldownKnown && state.ReadyAtTick > now)
                {
                    state.DisplayState = SkillMonitorDisplayState.Cooldown;
                    var remainingMilliseconds = state.ReadyAtTick - now;
                    var seconds = (int)Math.Ceiling(remainingMilliseconds / 1_000d);
                    if (seconds != state.DisplaySeconds)
                    {
                        state.DisplaySeconds = seconds;
                        state.CooldownText = seconds.ToString(CultureInfo.InvariantCulture);
                    }

                    state.CooldownProgress = state.CooldownMilliseconds > 0
                        ? Math.Clamp((float)remainingMilliseconds / state.CooldownMilliseconds, 0f, 1f)
                        : 0f;
                    state.StatusText = string.Empty;
                }
                else if (state.CooldownKnown)
                {
                    state.DisplayState = SkillMonitorDisplayState.Ready;
                    state.CooldownText = string.Empty;
                    state.CooldownProgress = 0f;
                    state.StatusText = string.Empty;
                }
                else if (definition.StatusOnly)
                {
                    state.DisplayState = SkillMonitorDisplayState.Inactive;
                    state.CooldownText = string.Empty;
                    state.CooldownProgress = 0f;
                    state.StatusText = string.Empty;
                }
                else
                {
                    state.DisplayState = SkillMonitorDisplayState.Unknown;
                    state.CooldownText = string.Empty;
                    state.CooldownProgress = 0f;
                    state.StatusText = string.Empty;
                }

                states[memberIndex, definitionIndex] = state;
            }
        }
    }

    private void UpdateLocalCooldowns(long now)
    {
        var localEntityID = DService.Instance().ObjectTable.LocalPlayer?.EntityID ?? 0;
        var actionManager = ActionManager.Instance();
        if (localEntityID == 0 || actionManager == null)
        {
            return;
        }

        for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
        {
            if (!members[memberIndex].Visible || members[memberIndex].EntityID != localEntityID)
            {
                continue;
            }

            for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
            {
                var definition = definitions[definitionIndex];
                if (definition.StatusOnly || !definition.AppliesTo(members[memberIndex].ClassJobID))
                {
                    continue;
                }

                var state = states[memberIndex, definitionIndex];
                state.CooldownKnown = true;
                if (actionManager->IsRecastTimerActive(ActionType.Action, definition.ActionID))
                {
                    state.CooldownMilliseconds = (int)(Math.Max(
                        0f,
                        actionManager->GetRecastTime(ActionType.Action, definition.ActionID)) * 1_000f);
                    state.ReadyAtTick = now + (long)Math.Max(
                        0f,
                        actionManager->GetRecastTime(ActionType.Action, definition.ActionID) -
                        actionManager->GetRecastTimeElapsed(ActionType.Action, definition.ActionID)) * 1_000L;
                }
                else
                {
                    state.ReadyAtTick = now;
                    state.CooldownMilliseconds = definition.CooldownMilliseconds;
                }

                states[memberIndex, definitionIndex] = state;
            }

            return;
        }
    }

    private void ClearStatusActivity(int memberIndex)
    {
        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            var state = states[memberIndex, definitionIndex];
            state.StatusActive = false;
            state.StatusRemainingMilliseconds = 0;
            states[memberIndex, definitionIndex] = state;
        }
    }

    private void ResetMember(int memberIndex)
    {
        members[memberIndex] = default;
        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            states[memberIndex, definitionIndex] = default;
        }
    }

    private void OnLogout(int _, int unusedReason) => Clear();

    private void OnTerritoryChanged(uint _) => Clear();

    private static Dictionary<uint, int> CreateActionIndexes(SkillMonitorDefinition[] source)
    {
        var result = new Dictionary<uint, int>(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index].ActionID != 0)
            {
                result[source[index].ActionID] = index;
            }
        }

        return result;
    }

    private static Dictionary<uint, int> CreateStatusIndexes(SkillMonitorDefinition[] source)
    {
        var result = new Dictionary<uint, int>(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index].StatusID != 0)
            {
                result[source[index].StatusID] = index;
            }
        }

        return result;
    }

    private static string FormatDuration(int seconds)
    {
        return seconds >= 60
            ? string.Format(CultureInfo.InvariantCulture, OmniLoc.Get("Feature.SkillMonitor.Duration.Minutes"), seconds / 60)
            : string.Format(CultureInfo.InvariantCulture, OmniLoc.Get("Feature.SkillMonitor.Duration.Seconds"), seconds);
    }

    private readonly record struct ActionUse(uint CasterEntityID, uint ActionID, long Tick);
}

internal readonly record struct SkillMonitorMember(uint EntityID, uint ClassJobID, bool Visible);

internal struct SkillMonitorRuntimeState
{
    public bool CooldownKnown;
    public bool StatusActive;
    public long ReadyAtTick;
    public long ActiveUntilTick;
    public int CooldownMilliseconds;
    public int StatusRemainingMilliseconds;
    public int DisplaySeconds;
    public string? CooldownText;
    public string? StatusText;
    public float CooldownProgress;
    public SkillMonitorDisplayState DisplayState;
}

internal enum SkillMonitorDisplayState
{
    Unknown,
    Inactive,
    Ready,
    Cooldown,
    Active
}
