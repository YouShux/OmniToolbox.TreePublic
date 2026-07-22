using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using OmenTools;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.Config;
using OmniToolbox.Verification;

namespace OmniToolbox.TreeHouse.Actions;

public sealed unsafe class DirectionalActionReleaseFeature : TreeHouseFeatureBase
{
    private static readonly uint[] DefaultActionIds =
    [
        94, 29494, 24401, 29550, 24402, 34684, 39210, 16010, 29430, 7418, 37008, 7385, 41506,
        11390, 11414, 11403, 11383, 11399, 11388, 11422, 11428, 11430, 18296, 18297, 18299,
        18323, 23283, 23288, 23289, 34567, 34571, 34572, 34581
    ];

    private readonly DirectionalActionReleaseConfig config;
    private readonly FeatureGate featureGate;

    public DirectionalActionReleaseFeature(DirectionalActionReleaseConfig config, FeatureGate featureGate) : base(new(
        "directional-action-release",
        "Feature.DirectionalActionRelease.Title",
        "Feature.DirectionalActionRelease.Description",
        TreeHouseCategory.Action,
        true))
    {
        this.config = config;
        this.featureGate = featureGate;
        NormalizeConfig(config);
    }

    internal static ReadOnlySpan<uint> ActionIds => DefaultActionIds;

    public override bool HasSettings => true;

    public override bool DrawSettings() => DirectionalActionReleasePanel.Draw(config, ActionIds);

    internal static void NormalizeConfig(DirectionalActionReleaseConfig config)
    {
        config.Actions ??= [];
        config.ReversedActions ??= [];
        foreach (var actionId in DefaultActionIds)
        {
            config.Actions.TryAdd(actionId, DirectionalActionMode.Camera);
        }

        foreach (var actionId in new List<uint>(config.Actions.Keys))
        {
            if (Array.IndexOf(DefaultActionIds, actionId) < 0)
            {
                config.Actions.Remove(actionId);
            }
        }

        config.ReversedActions.RemoveWhere(actionId => Array.IndexOf(DefaultActionIds, actionId) < 0);
    }

    protected override void OnEnable()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var manager = UseActionManager.Instance();
        if (!manager.RegPreUseAction(OnPreUseAction))
        {
            throw new InvalidOperationException("Directional action registration failed.");
        }

        if (!manager.RegPreUseActionLocation(OnPreUseActionLocation))
        {
            manager.Unreg(OnPreUseAction);
            throw new InvalidOperationException("Directional location action registration failed.");
        }
    }

    protected override void OnDisable()
    {
        UseActionManager.Instance().Unreg(OnPreUseAction);
        UseActionManager.Instance().Unreg(OnPreUseActionLocation);
    }

    private void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionId,
        ref ulong targetId,
        ref uint extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint comboRouteId) => Align(actionType, actionId);

    private void OnPreUseActionLocation(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionId,
        ref ulong targetId,
        ref Vector3 location,
        ref uint extraParam,
        ref byte a7) => Align(actionType, actionId);

    private void Align(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action || !featureGate.CanUsePrivateFeature(out _))
        {
            return;
        }

        var adjustedActionId = ActionManager.Instance()->GetAdjustedActionId(actionId);
        if (!config.Actions.TryGetValue(adjustedActionId, out var mode) ||
            mode == DirectionalActionMode.Disabled ||
            !TryGetRotation(mode, out var rotation))
        {
            return;
        }

        if (config.ReversedActions.Contains(adjustedActionId))
        {
            rotation = RotationHelper.CharaSymmetricTransform(rotation);
        }

        ApplyRotation(rotation);
    }

    private static bool TryGetRotation(DirectionalActionMode mode, out float rotation)
    {
        if (mode == DirectionalActionMode.Camera)
        {
            var cameraManager = CameraManager.Instance();
            var camera = cameraManager is null ? null : cameraManager->GetActiveCamera();
            if (camera is null && cameraManager is not null)
            {
                camera = cameraManager->Camera;
            }

            if (camera is not null)
            {
                rotation = RotationHelper.CameraDirHToChara(camera->DirH);
                return true;
            }
        }
        else if (mode == DirectionalActionMode.Mouse && DService.Instance().ObjectTable.LocalPlayer is { } localPlayer &&
                 DService.Instance().GameGUI.ScreenToWorld(ImGui.GetMousePos(), out var world))
        {
            var delta = world - localPlayer.Position;
            if (new Vector2(delta.X, delta.Z).LengthSquared() >= 1e-6f)
            {
                rotation = MathF.Atan2(delta.X, delta.Z);
                return true;
            }
        }

        rotation = 0f;
        return false;
    }

    private static void ApplyRotation(float rotation)
    {
        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer)
        {
            return;
        }

        localPlayer.ToStruct()->SetRotation(rotation);
        if (GameState.ContentFinderCondition != 0)
        {
            new PositionUpdateInstancePacket(rotation, localPlayer.Position).Send();
        }
        else
        {
            new PositionUpdatePacket(rotation, localPlayer.Position).Send();
        }
    }
}
