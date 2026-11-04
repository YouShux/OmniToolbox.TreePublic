using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using LuminaAction = Lumina.Excel.Sheets.Action;
using GameControl = FFXIVClientStructs.FFXIV.Client.Game.Control.Control;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class AutoMount
{
    private static readonly CompSig IsActionUsableSig = new("48 89 5C 24 ?? 55 56 57 48 83 EC ?? 32 DB 41 8B F8 48 83 3D ?? ?? ?? ?? ?? 8B F2 48 8B E9");

    private delegate byte IsActionUsableDelegate
    (
        ActionManager* manager,
        ActionType     type,
        uint           actionID
    );

    private IsActionUsableDelegate IsActionUsable = null!;

    private void OnUseAction
    (
        bool                        result,
        ActionType                  actionType,
        uint                        actionID,
        ulong                       targetID,
        uint                        extraParam,
        ActionManager.UseActionMode queueState,
        uint                        comboRouteID
    )
    {
        if (Activation.IsStopping || issuingAutomaticAction)
            return;

        mountTasks?.Abort();
        if (result)
        {
            dismountTasks?.Abort();
            return;
        }
        if (!config.AutoDismount || dismountTasks is null ||
            !TryPrepareDismount(actionType, actionID, ref targetID))
            return;

        var services = DService.Instance();
        var territoryID = services.ClientState.TerritoryType;
        var contentID = DalamudServices.PlayerState.ContentId;
        var classJob = LocalPlayerState.ClassJob;
        var replayTargetID = targetID;
        dismountTasks.Abort();
        MountCommand.Dismount();
        dismountTasks.Enqueue(() =>
        {
            if (Activation.IsStopping || !config.AutoDismount || !services.ClientState.IsLoggedIn ||
                contentID != DalamudServices.PlayerState.ContentId ||
                territoryID != services.ClientState.TerritoryType || classJob != LocalPlayerState.ClassJob ||
                services.Condition.IsBetweenAreas || services.ObjectTable.LocalPlayer is null)
                return true;
            if (services.Condition.IsOnMount)
                return false;
            if (replayTargetID is not (0 or 0xE0000000) &&
                services.ObjectTable.SearchByID(replayTargetID) is null)
                return true;

            return UseActionManager.Instance().UseAction(actionType, actionID, replayTargetID,
                extraParam, queueState, comboRouteID);
        }, "Use action after dismounting");
    }

    private bool TryPrepareDismount(ActionType type, uint actionID, ref ulong targetID)
    {
        var services = DService.Instance();
        var condition = services.Condition;
        var manager = ActionManager.Instance();
        var player = GameControl.GetLocalPlayer();
        if (!services.ClientState.IsLoggedIn || services.ClientState.IsPvP || player == null || manager == null ||
            !condition[ConditionFlag.Mounted] || condition[ConditionFlag.RidingPillion] ||
            condition[ConditionFlag.InFlight] || condition[ConditionFlag.Diving] ||
            condition[ConditionFlag.Jumping] || condition.IsOccupiedInEvent || condition.IsCasting ||
            type is not (ActionType.Action or ActionType.GeneralAction))
            return false;

        uint adjustedActionID;
        if (type == ActionType.Action)
            adjustedActionID = manager->GetAdjustedActionId(actionID);
        else if (LuminaGetter.TryGetRow<GeneralAction>(actionID, out var generalAction))
            adjustedActionID = generalAction.Action.RowId;
        else
            return false;

        var checkedActionID = type == ActionType.Action ? adjustedActionID : actionID;
        if (!LuminaGetter.TryGetRow<LuminaAction>(adjustedActionID, out var action) ||
            action.CanUseWhileMounted || manager->GetActionStatus(type, checkedActionID, targetID, false, false) == 0 ||
            !manager->IsActionOffCooldown(type, checkedActionID) ||
            manager->CheckActionResources(ActionType.Action, adjustedActionID) != 0 ||
            IsActionUsable(manager, ActionType.Action, adjustedActionID) == 0)
            return false;

        if (action.TargetArea || action.Range == 0)
            return true;

        var target = targetID is 0 or 0xE0000000
            ? TargetSystem.Instance()->Target
            : GameObjectManager.Instance()->Objects.GetObjectByGameObjectId(targetID);
        if (action.CanTargetSelf && (target == null || !ActionManager.CanUseActionOnTarget(adjustedActionID, target)))
            target = (GameObject*)player;
        if (target == null || !ActionManager.CanUseActionOnTarget(adjustedActionID, target) ||
            ActionManager.GetActionInRangeOrLoS(adjustedActionID, (GameObject*)player, target) != 0)
            return false;

        targetID = target->GetGameObjectId();
        return true;
    }
}
