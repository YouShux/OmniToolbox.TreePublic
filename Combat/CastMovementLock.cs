using TreeTools.Lifecycle;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.Expansion.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class CastMovementLock : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Combat
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "CastMovementLockTitle",
            ["Description"] = "CastMovementLockDescription",
            ["Feature.CastMovementLock.TemporaryMovementKey"] = "Feature.CastMovementLock.TemporaryMovementKey",
            ["Feature.CastMovementLock.TemporaryMovementKey.Alt"] = "Feature.CastMovementLock.TemporaryMovementKey.Alt",
            ["Feature.CastMovementLock.TemporaryMovementKey.Control"] = "Feature.CastMovementLock.TemporaryMovementKey.Control",
            ["Feature.CastMovementLock.TemporaryMovementKey.Shift"] = "Feature.CastMovementLock.TemporaryMovementKey.Shift"
        });

    private readonly Config config;
    private bool ownsMovementLock;

    public override bool HasSettings => true;

    public CastMovementLock(TreeHouseConfig configStore)
    {
        config = configStore.GetOrCreate<Config>(ModuleName);
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnUpdate));

        var input = InputIDManager.Instance();
        var framework = FrameworkManager.Instance();
        input.RegPrePressed(OnMovementInput);
        input.RegPreDown(OnMovementInput);
        input.RegPreHeld(OnMovementInput);
        input.RegPreReleased(OnMovementInput);
        framework.Reg(OnUpdate);
    }

    protected override void OnStop()
    {
        var input = InputIDManager.Instance();
        input.UnregPrePressed(OnMovementInput);
        input.UnregPreDown(OnMovementInput);
        input.UnregPreHeld(OnMovementInput);
        input.UnregPreReleased(OnMovementInput);
        ReleaseMovementLock();
    }

    public override bool DrawSettings()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "Feature.CastMovementLock.TemporaryMovementKey"));
        var preview = OmniLoc.Get(this, $"Feature.CastMovementLock.TemporaryMovementKey.{config.TemporaryMovementKey}");
        var width = OmniControls.MeasureCombo(preview, OmniTheme.Scale(140f)).X;
        OmniControls.SameLineOrWrap(width);
        if (OmniControls.BeginCombo("##castMovementLockTemporaryMovementKey", preview, width))
        {
            foreach (var key in TemporaryMovementKeys)
            {
                if (OmniControls.RoundedSelectable(
                        OmniLoc.Get(this, $"Feature.CastMovementLock.TemporaryMovementKey.{key}"),
                        config.TemporaryMovementKey == key))
                {
                    config.TemporaryMovementKey = key;
                    SaveHostConfig?.Invoke();
                }
            }

            ImGui.EndCombo();
        }

        return false;
    }

    private void OnMovementInput(ref bool? overrideResult, ref InputId id)
    {
        if (id is InputId.MOVE_FORE or InputId.MOVE_BACK or InputId.MOVE_LEFT or InputId.MOVE_RIGHT or
            InputId.MOVE_STRIFE_L or InputId.MOVE_STRIFE_R or InputId.MOVE_AND_STEER &&
            ShouldPreventMovement())
            overrideResult = false;
    }

    private void OnUpdate(IFramework framework)
    {
        if (!ShouldPreventMovement())
        {
            ReleaseMovementLock();
            return;
        }

        var controller = PlayerController.Instance();
        if (controller == null)
            return;

        if (!controller->MoveControllerWalk.IsMovementInputLocked)
        {
            controller->MoveControllerWalk.IsMovementInputLocked = true;
            ownsMovementLock = true;
        }

        controller->MoveControllerWalk.IsAutoRunForward = false;
        controller->MoveControllerWalk.IsAutoRunStrafe = false;
    }

    private bool ShouldPreventMovement()
    {
        if (!IsEnabled || DalamudServices.IsUnloading || !DalamudServices.PlayerState.IsLoaded)
            return false;

        var service = DService.Instance();
        var (key, leftKey) = config.TemporaryMovementKey switch
        {
            MovementModifier.Shift => (VirtualKey.SHIFT, 0xA0),
            MovementModifier.Alt => (VirtualKey.MENU, 0xA4),
            _ => (VirtualKey.CONTROL, 0xA2)
        };
        return service.ClientState.IsLoggedIn &&
               !service.KeyState[key] && !service.KeyState[(VirtualKey)leftKey] &&
               !service.KeyState[(VirtualKey)(leftKey + 1)] &&
               service.ObjectTable.LocalPlayer is { IsCasting: true } player &&
               player.CastActionType != ActionType.Mount &&
               service.Condition[ConditionFlag.Casting];
    }

    private void ReleaseMovementLock()
    {
        if (!ownsMovementLock)
            return;

        var controller = PlayerController.Instance();
        if (controller != null)
            controller->MoveControllerWalk.IsMovementInputLocked = false;
        ownsMovementLock = false;
    }

    private sealed class Config
    {
        public MovementModifier TemporaryMovementKey = MovementModifier.Control;
    }

    private enum MovementModifier
    {
        Control,
        Shift,
        Alt
    }

    private static readonly MovementModifier[] TemporaryMovementKeys =
    [
        MovementModifier.Control,
        MovementModifier.Shift,
        MovementModifier.Alt
    ];
}
