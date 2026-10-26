using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using OmenTools;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class DirectionalActionRelease : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Combat,
        RequiresPrivateProvider = true
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "DirectionalActionReleaseTitle",
            ["Description"] = "DirectionalActionReleaseDescription",
            ["Feature.DirectionalActionRelease.Action"] = "Feature.DirectionalActionRelease.Action",
            ["Feature.DirectionalActionRelease.Camera"] = "Feature.DirectionalActionRelease.Camera",
            ["Feature.DirectionalActionRelease.Mouse"] = "Feature.DirectionalActionRelease.Mouse",
            ["Feature.DirectionalActionRelease.Reverse"] = "Feature.DirectionalActionRelease.Reverse"
        });

    public override bool HasSettings => true;

    private static readonly uint[] DefaultActionIDs =
    [
        94, 29494, 24401, 29550, 24402, 34684, 39210, 16010, 29430, 7418, 37008, 7385, 41506,
        11390, 11414, 11403, 11383, 11399, 11388, 11422, 11428, 11430, 18296, 18297, 18299,
        18323, 23283, 23288, 23289, 34567, 34571, 34572, 34581
    ];

    private readonly Settings config;

    public DirectionalActionRelease(Settings config)
    {
        this.config = config;
        NormalizeConfig(config);
    }

    internal static ReadOnlySpan<uint> ActionIDs => DefaultActionIDs;

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => UseActionManager.Instance().Unreg(OnPreUseAction));
        activation.OnStopping(() => UseActionManager.Instance().Unreg(OnPreUseActionLocation));

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

    public override bool DrawSettings() => Panel.Draw(config, ActionIDs);

    internal static void NormalizeConfig(Settings config)
    {
        config.Actions ??= [];
        config.ReversedActions ??= [];
        foreach (var actionID in DefaultActionIDs)
        {
            config.Actions.TryAdd(actionID, DirectionalActionMode.Camera);
        }

        foreach (var actionID in new List<uint>(config.Actions.Keys))
        {
            if (Array.IndexOf(DefaultActionIDs, actionID) < 0)
            {
                config.Actions.Remove(actionID);
            }
        }

        config.ReversedActions.RemoveWhere(actionID => Array.IndexOf(DefaultActionIDs, actionID) < 0);
    }

    private void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionID,
        ref ulong targetID,
        ref uint extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint comboRouteID) => Align(actionType, actionID);

    private void OnPreUseActionLocation(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionID,
        ref ulong targetID,
        ref Vector3 location,
        ref uint extraParam,
        ref byte a7) => Align(actionType, actionID);

    private void Align(ActionType actionType, uint actionID)
    {
        if (actionType != ActionType.Action)
        {
            return;
        }

        var adjustedActionID = ActionManager.Instance()->GetAdjustedActionId(actionID);
        if (!config.Actions.TryGetValue(adjustedActionID, out var mode) ||
            mode == DirectionalActionMode.Disabled ||
            !TryGetRotation(mode, out var rotation))
        {
            return;
        }

        if (config.ReversedActions.Contains(adjustedActionID))
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

    public sealed class Settings
    {
        public Dictionary<uint, DirectionalActionMode> Actions { get; set; } = [];

        public HashSet<uint> ReversedActions { get; set; } = [];
    }
}
