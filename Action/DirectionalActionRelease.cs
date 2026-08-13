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
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace OmniToolbox.TreePublic;

public sealed unsafe class DirectionalActionRelease : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("DirectionalActionReleaseTitle"),
        Description = OmniLoc.Get("DirectionalActionReleaseDescription"),
        Category = ModuleCategory.Combat,
        RequiresPrivateProvider = true
    };

    private static readonly uint[] DefaultActionIDs =
    [
        94, 29494, 24401, 29550, 24402, 34684, 39210, 16010, 29430, 7418, 37008, 7385, 41506,
        11390, 11414, 11403, 11383, 11399, 11388, 11422, 11428, 11430, 18296, 18297, 18299,
        18323, 23283, 23288, 23289, 34567, 34571, 34572, 34581
    ];

    private readonly DirectionalActionReleaseConfig config;

    public DirectionalActionRelease(DirectionalActionReleaseConfig config)
    {
        this.config = config;
        NormalizeConfig(config);
    }

    internal static ReadOnlySpan<uint> ActionIDs => DefaultActionIDs;

    public override bool HasSettings => true;

    public override bool DrawSettings() => DirectionalActionReleasePanel.Draw(config, ActionIDs);

    internal static void NormalizeConfig(DirectionalActionReleaseConfig config)
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

    protected override void OnEnable()
    {
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
}

internal static class DirectionalActionReleasePanel
{
    public static bool Draw(DirectionalActionReleaseConfig config, ReadOnlySpan<uint> actionIDs)
    {
        var changed = false;
        var rowHeight = MathF.Max(ImGui.GetFrameHeightWithSpacing(), OmniTheme.Scale(34f));
        using var cellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(ImGui.GetStyle().CellPadding.X, OmniTheme.Scale(3f)));
        using var framePadding = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, OmniTheme.Scale(3f)));
        using var table = ImRaii.Table(
            "##directionalActions",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(ImGui.GetContentRegionAvail().X, rowHeight * 6f + ImGui.GetStyle().ItemSpacing.Y));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DirectionalActionRelease.Camera"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(54f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DirectionalActionRelease.Mouse"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(54f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DirectionalActionRelease.Action"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DirectionalActionRelease.Reverse"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(54f));
        OmniControls.ScrollableTableHeadersRow();

        foreach (var actionID in actionIDs)
        {
            if (!LuminaGetter.TryGetRow<LuminaAction>(actionID, out var action))
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var camera = config.Actions[actionID] == DirectionalActionMode.Camera;
            if (OmniControls.Checkbox($"##directionalCamera{actionID}", ref camera))
            {
                config.Actions[actionID] = camera ? DirectionalActionMode.Camera : DirectionalActionMode.Disabled;
                changed = true;
            }

            ImGui.TableNextColumn();
            var mouse = config.Actions[actionID] == DirectionalActionMode.Mouse;
            if (OmniControls.Checkbox($"##directionalMouse{actionID}", ref mouse))
            {
                config.Actions[actionID] = mouse ? DirectionalActionMode.Mouse : DirectionalActionMode.Disabled;
                changed = true;
            }

            ImGui.TableNextColumn();
            DrawAction(action);

            ImGui.TableNextColumn();
            var reversed = config.ReversedActions.Contains(actionID);
            if (OmniControls.Checkbox($"##directionalReverse{actionID}", ref reversed))
            {
                if (reversed)
                {
                    config.ReversedActions.Add(actionID);
                }
                else
                {
                    config.ReversedActions.Remove(actionID);
                }

                changed = true;
            }
        }

        return changed;
    }

    private static void DrawAction(LuminaAction action)
    {
        var iconSize = new Vector2(MathF.Max(
            OmniTheme.Scale(24f),
            ImGui.GetTextLineHeightWithSpacing() + OmniTheme.Scale(6f)));
        FramedGameIcon.DrawAction(action, iconSize);
        ImGui.SameLine();

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(action.Name.ToString());
    }
}

[Serializable]
public sealed class DirectionalActionReleaseConfig
{
    public Dictionary<uint, DirectionalActionMode> Actions { get; set; } = [];
    public HashSet<uint> ReversedActions { get; set; } = [];
}

public enum DirectionalActionMode
{
    Disabled,
    Camera,
    Mouse
}
