using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.Extensions;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.OmenService;
using OmniToolbox.Config;
using OmniToolbox.Verification;
using Control = FFXIVClientStructs.FFXIV.Client.Game.Control.Control;

namespace OmniToolbox.TreeHouse.Actions;

public sealed unsafe class InfiniteSprintFeature : TreeHouseFeatureBase
{
    internal const int MaxIntervalMs = 15_000;

    private readonly InfiniteSprintConfig config;
    private readonly FeatureGate featureGate;
    private DateTime lastSprintTime = DateTime.MinValue;

    public InfiniteSprintFeature(InfiniteSprintConfig config, FeatureGate featureGate) : base(new(
        "infinite-sprint",
        "Feature.InfiniteSprint.Title",
        "Feature.InfiniteSprint.Description",
        TreeHouseCategory.Action,
        true,
        true))
    {
        this.config = config;
        this.featureGate = featureGate;
        config.IntervalMs = Math.Clamp(config.IntervalMs, 0, MaxIntervalMs);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => InfiniteSprintPanel.Draw(config);

    protected override void OnEnable()
    {
        if (!featureGate.CanUseTestFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        if (!UseActionManager.Instance().RegPreUseAction(OnPreUseAction))
        {
            throw new InvalidOperationException("Infinite sprint registration failed.");
        }
    }

    protected override void OnDisable()
    {
        UseActionManager.Instance().Unreg(OnPreUseAction);
        lastSprintTime = DateTime.MinValue;
    }

    private void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionId,
        ref ulong targetId,
        ref uint extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint comboRouteId)
    {
        if ((actionType != ActionType.GeneralAction || actionId != 4) &&
            (actionType != ActionType.Action || actionId != 3))
        {
            return;
        }

        if (!featureGate.CanUseTestFeature(out _))
        {
            return;
        }

        var intervalMs = config.IntervalMs;
        if (intervalMs > 0 && lastSprintTime != DateTime.MinValue)
        {
            var now = DateTime.UtcNow;
            if ((now - lastSprintTime).TotalMilliseconds < intervalMs)
            {
                isPrevented = true;
                return;
            }
        }

        var localPlayer = Control.GetLocalPlayer();
        if (localPlayer is null)
        {
            return;
        }

        isPrevented = true;
        lastSprintTime = DateTime.UtcNow;

        var adjustedActionId = ActionManager.GetAdjustSprintActionID();
        if (adjustedActionId == 3)
        {
            new UseActionPacket(ActionType.GeneralAction, 4, localPlayer->EntityId, localPlayer->Rotation).Send();
            ActionManager.Instance()->StartCooldown(ActionType.Action, 3);
            return;
        }

        new UseActionPacket(ActionType.Action, adjustedActionId, localPlayer->EntityId, localPlayer->Rotation).Send();
    }
}
