using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Dalamud.Services.ObjectTable.Abstractions.ObjectKinds;
using OmenTools.Dalamud.Services.StatusList.Implementations;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Config;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace OmniToolbox.TreeHouse.Actions;

public sealed unsafe class DuplicateStatusPreventionFeature : TreeHouseFeatureBase
{
    private readonly DuplicateStatusPreventionConfig config;

    public DuplicateStatusPreventionFeature(DuplicateStatusPreventionConfig config) : base(new(
        "duplicate-status-prevention",
        "Feature.DuplicateStatusPrevention.Title",
        "Feature.DuplicateStatusPrevention.Description",
        TreeHouseCategory.Action))
    {
        this.config = config;
        NormalizeConfig(config);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => DuplicateStatusPreventionPanel.Draw(config);

    internal static void NormalizeConfig(DuplicateStatusPreventionConfig config)
    {
        config.EnabledActions ??= [];
        foreach (var action in DuplicateStatusPreventionRules.OrderedActions)
        {
            config.EnabledActions.TryAdd(action.Key, true);
        }

        foreach (var actionId in new List<uint>(config.EnabledActions.Keys))
        {
            if (!DuplicateStatusPreventionRules.Actions.ContainsKey(actionId))
            {
                config.EnabledActions.Remove(actionId);
            }
        }
    }

    protected override void OnEnable()
    {
        if (!UseActionManager.Instance().RegPreUseAction(OnPreUseAction))
        {
            throw new InvalidOperationException("Duplicate status prevention registration failed.");
        }
    }

    protected override void OnDisable() => UseActionManager.Instance().Unreg(OnPreUseAction);

    private void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionId,
        ref ulong targetId,
        ref uint extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint comboRouteId)
    {
        if (actionType is not (ActionType.Action or ActionType.GeneralAction))
        {
            return;
        }

        var adjustedActionId = actionType == ActionType.Action
            ? ActionManager.Instance()->GetAdjustedActionId(actionId)
            : actionId;
        if (!DuplicateStatusPreventionRules.Actions.TryGetValue(adjustedActionId, out var rule) ||
            !config.EnabledActions.TryGetValue(adjustedActionId, out var enabled) ||
            !enabled ||
            ActionManager.Instance()->GetActionStatus(actionType, adjustedActionId) != 0 ||
            !LuminaGetter.TryGetRow<LuminaAction>(adjustedActionId, out var action))
        {
            return;
        }

        var canTargetSelf = action.CanTargetSelf && adjustedActionId != 7535;
        var target = DService.Instance().ObjectTable.SearchByID(targetId);
        var detectionTarget = targetId is 0 or 0xE0000000 || targetId == LocalPlayerState.EntityID
            ? DService.Instance().ObjectTable.LocalPlayer
            : target as IBattleChara;
        if (canTargetSelf && (target is null || !ActionManager.CanUseActionOnTarget(adjustedActionId, target.ToStruct())))
        {
            detectionTarget = DService.Instance().ObjectTable.LocalPlayer;
        }

        if (ShouldPrevent(rule, detectionTarget))
        {
            isPrevented = true;
        }
    }

    private bool ShouldPrevent(DuplicateActionRule rule, IBattleChara? target)
    {
        if (rule.SecondStatuses is not null)
        {
            foreach (var status in rule.SecondStatuses)
            {
                if (status.Blocks(HasStatus(status, GetCurrentTarget())))
                {
                    return false;
                }
            }
        }

        foreach (var status in rule.Statuses)
        {
            if (status.Blocks(HasStatus(status, target)))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasStatus(DuplicateStatusRule rule, IBattleChara? target) => rule.DetectionTarget switch
    {
        StatusDetectionTarget.Self => HasStatus(DService.Instance().ObjectTable.LocalPlayer?.StatusList, rule.StatusId),
        StatusDetectionTarget.Member => PartyHasStatus(rule.StatusId),
        StatusDetectionTarget.Target => HasStatus(target?.StatusList, rule.StatusId),
        _ => false
    };

    private static IBattleChara? GetCurrentTarget() =>
        DService.Instance().ObjectTable.CreateObjectReference((nint)TargetSystem.Instance()->Target) as IBattleChara;

    private bool PartyHasStatus(uint statusId)
    {
        foreach (var partyMember in AgentHUD.Instance()->PartyMembers)
        {
            if (partyMember.Object is null || partyMember.EntityId == LocalPlayerState.EntityID)
            {
                continue;
            }

            if (DService.Instance().ObjectTable.CreateObjectReference((nint)partyMember.Object) is IBattleChara member &&
                HasStatus(member.StatusList, statusId))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasStatus(StatusList? statuses, uint statusId)
    {
        if (statuses is null || !statuses.TryGetStatus(statusId, out var status, out _) || status is null)
        {
            return false;
        }

        return status.GameData.Value.IsPermanent || status.RemainingTime > config.OverlapThreshold;
    }
}
