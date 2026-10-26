using System.Collections.Frozen;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Dalamud.Services.Game;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Notifications;
using OmniToolbox.UI.Theme;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class DuplicateStatusPrevention : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Combat
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "DuplicateStatusPreventionTitle",
            ["Description"] = "DuplicateStatusPreventionDescription",
            ["Feature.DuplicateStatusPrevention.ActionCount"] = "Feature.DuplicateStatusPrevention.ActionCount",
            ["Feature.DuplicateStatusPrevention.ChatNotify"] = "Feature.DuplicateStatusPrevention.ChatNotify",
            ["Feature.DuplicateStatusPrevention.Column.Action"] = "Feature.DuplicateStatusPrevention.Column.Action",
            ["Feature.DuplicateStatusPrevention.Column.Status"] = "Feature.DuplicateStatusPrevention.Column.Status",
            ["Feature.DuplicateStatusPrevention.OverlapThreshold"] = "Feature.DuplicateStatusPrevention.OverlapThreshold",
            ["Feature.DuplicateStatusPrevention.OverlapThreshold.Help"] = "Feature.DuplicateStatusPrevention.OverlapThreshold.Help",
            ["Feature.DuplicateStatusPrevention.PopupNotify"] = "Feature.DuplicateStatusPrevention.PopupNotify",
            ["Feature.DuplicateStatusPrevention.Prevented"] = "Feature.DuplicateStatusPrevention.Prevented",
            ["Feature.DuplicateStatusPrevention.StatusTooltip"] = "Feature.DuplicateStatusPrevention.StatusTooltip",
            ["Feature.DuplicateStatusPrevention.Target.Member"] = "Feature.DuplicateStatusPrevention.Target.Member",
            ["Feature.DuplicateStatusPrevention.Target.Self"] = "Feature.DuplicateStatusPrevention.Target.Self",
            ["Feature.DuplicateStatusPrevention.Target.Target"] = "Feature.DuplicateStatusPrevention.Target.Target"
        });

    public override bool HasSettings => true;

    private readonly Settings config;
    private long lastNotificationTick;

    public DuplicateStatusPrevention(Settings config)
    {
        this.config = config;
        NormalizeConfig(config);
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => UseActionManager.Instance().Unreg(OnPreUseAction));

        lastNotificationTick = 0;
        if (!UseActionManager.Instance().RegPreUseAction(OnPreUseAction))
        {
            throw new InvalidOperationException("Duplicate status prevention registration failed.");
        }
    }

    public override bool DrawSettings() => Panel.Draw(config);

    internal static void NormalizeConfig(Settings config)
    {
        config.EnabledActions ??= [];
        foreach (var action in Rules.OrderedActions)
        {
            config.EnabledActions.TryAdd(action.Key, true);
        }

        foreach (var actionID in new List<uint>(config.EnabledActions.Keys))
        {
            if (!Rules.Actions.ContainsKey(actionID))
            {
                config.EnabledActions.Remove(actionID);
            }
        }
    }

    private void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionID,
        ref ulong targetID,
        ref uint extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint comboRouteID)
    {
        if (actionType is not (ActionType.Action or ActionType.GeneralAction))
        {
            return;
        }

        var adjustedActionID = actionType == ActionType.Action
            ? ActionManager.Instance()->GetAdjustedActionId(actionID)
            : actionID;
        if (!Rules.Actions.TryGetValue(adjustedActionID, out var rule) ||
            !config.EnabledActions.TryGetValue(adjustedActionID, out var enabled) ||
            !enabled ||
            ActionManager.Instance()->GetActionStatus(actionType, adjustedActionID) != 0 ||
            !LuminaGetter.TryGetRow<LuminaAction>(adjustedActionID, out var action))
        {
            return;
        }

        var canTargetSelf = action.CanTargetSelf && adjustedActionID != 7535;
        var target = DService.Instance().ObjectTable.SearchByID(targetID);
        var detectionTarget = targetID is 0 or 0xE0000000 || targetID == LocalPlayerState.EntityID
            ? DService.Instance().ObjectTable.LocalPlayer
            : target as IBattleChara;
        if (canTargetSelf && (target is null || !ActionManager.CanUseActionOnTarget(adjustedActionID, target.ToStruct())))
        {
            detectionTarget = DService.Instance().ObjectTable.LocalPlayer;
        }

        if (!ShouldPrevent(rule, detectionTarget))
        {
            return;
        }

        isPrevented = true;
        if (!config.ChatNotify && !config.PopupNotify)
        {
            return;
        }

        var currentTick = Environment.TickCount64;
        if (lastNotificationTick != 0 && currentTick - lastNotificationTick < 5000)
        {
            return;
        }

        lastNotificationTick = currentTick;
        var details = string.Format(
            OmniLoc.Get(this, "Feature.DuplicateStatusPrevention.Prevented"),
            action.Name);
        if (config.ChatNotify)
        {
            OmniNotifier.Chat($"{Title} {details}");
        }

        if (config.PopupNotify)
        {
            OmniNotifier.Popup(Title, details);
        }
    }

    private bool ShouldPrevent(ActionRule rule, IBattleChara? target)
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

    private bool HasStatus(StatusRule rule, IBattleChara? target) => rule.DetectionTarget switch
    {
        StatusDetectionTarget.Self => HasStatus(DService.Instance().ObjectTable.LocalPlayer?.StatusList, rule.StatusID),
        StatusDetectionTarget.Member => PartyHasStatus(rule.StatusID),
        StatusDetectionTarget.Target => HasStatus(target?.StatusList, rule.StatusID),
        _ => false
    };

    private static IBattleChara? GetCurrentTarget() =>
        DService.Instance().ObjectTable.CreateObjectReference((nint)TargetSystem.Instance()->Target) as IBattleChara;

    private bool PartyHasStatus(uint statusID)
    {
        foreach (var partyMember in AgentHUD.Instance()->PartyMembers)
        {
            if (partyMember.Object is null || partyMember.EntityId == LocalPlayerState.EntityID)
            {
                continue;
            }

            if (DService.Instance().ObjectTable.CreateObjectReference((nint)partyMember.Object) is IBattleChara member &&
                HasStatus(member.StatusList, statusID))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasStatus(StatusList? statuses, uint statusID)
    {
        if (statuses is null || !statuses.TryGetStatus(statusID, out var status, out _) || status is null)
        {
            return false;
        }

        return status.GameData.Value.IsPermanent || status.RemainingTime > config.OverlapThreshold;
    }

    public sealed class Settings
    {
        public float OverlapThreshold { get; set; } = 5f;

        public bool ChatNotify { get; set; } = true;

        public bool PopupNotify { get; set; }

        public Dictionary<uint, bool> EnabledActions { get; set; } = [];
    }
}
