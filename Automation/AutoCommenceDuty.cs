using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using OmenTools;
using OmenTools.Extensions;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using AgentContentsFinder = FFXIVClientStructs.Expansion.FFXIV.Client.UI.Agent.AgentContentsFinder;
using ContentsFinderQueueInfo = FFXIVClientStructs.Expansion.FFXIV.Client.Game.UI.ContentsFinderQueueInfo;
using ContentsType = FFXIVClientStructs.FFXIV.Client.UI.Agent.ContentsType;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class AutoCommenceDuty : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Automation,
        Author   = "YouShu"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "Feature.AutoCommenceDuty.Title",
            ["Description"] = "Feature.AutoCommenceDuty.Description"
        });

    private Hook<ContentsFinderQueueInfo.Delegates.SetQueueState> SetQueueStateHook = null!;

    protected override void OnStart(FeatureLifetime activation)
    {
        var services = DService.Instance();
        activation.OnStopping(() => services.ClientState.ClassJobChanged -= OnClassJobChanged);
        activation.OnStopping(() => services.Condition.ConditionChange -= OnConditionChanged);

        SetQueueStateHook = services.Hook.HookFromMemberFunction(
            typeof(ContentsFinderQueueInfo.MemberFunctionPointers),
            nameof(ContentsFinderQueueInfo.MemberFunctionPointers.SetQueueState),
            (ContentsFinderQueueInfo.Delegates.SetQueueState)SetQueueStateDetour);
        activation.Add(SetQueueStateHook);
        activation.OnStopping(SetQueueStateHook.Disable);
        SetQueueStateHook.Enable();

        services.ClientState.ClassJobChanged += OnClassJobChanged;
        services.Condition.ConditionChange += OnConditionChanged;
        TryCommence();
    }

    private void SetQueueStateDetour
    (
        ContentsFinderQueueInfo* queueInfo,
        ContentsFinderQueueState newState
    )
    {
        var previousState = queueInfo->QueueState;
        SetQueueStateHook.Original(queueInfo, newState);

        if (newState == ContentsFinderQueueState.Ready && previousState != ContentsFinderQueueState.Ready)
            TryCommence();
    }

    private void OnClassJobChanged(uint classJobID) => TryCommence();

    private void OnConditionChanged
    (
        ConditionFlag flag,
        bool          value
    )
    {
        if (!value)
            TryCommence();
    }

    private void TryCommence()
    {
        if (!Activation.IsStopping)
            TryCommence(0);
    }

    internal static bool TryCommence(uint expectedDutyID)
    {
        var contentsFinder = ContentsFinder.Instance();
        if (contentsFinder is null || contentsFinder->QueueInfo.QueueState != ContentsFinderQueueState.Ready)
            return false;
        var entry = contentsFinder->QueueInfo.PoppedQueueEntry;
        if (expectedDutyID != 0 && (entry.ContentType != ContentsType.Regular || entry.Id != expectedDutyID))
            return false;

        var playerState = PlayerState.Instance();
        var conditions = Conditions.Instance();
        if (playerState is null || !playerState->IsLoaded || conditions is null || conditions->Dead ||
            playerState->CurrentClassJobId != contentsFinder->QueueInfo.QueuedClassJobId)
            return false;

        var agent = AgentContentsFinder.Instance();
        return agent is not null && agent->RespondToDuty(true);
    }
}
