using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using OmniToolbox.Lifecycle;
using OmenTools.Interop.Game.Models;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace OmniToolbox.TreeHouse;

internal sealed unsafe class NonEntityTargetVisibility(HookRegistry hookRegistry) : IDisposable
{
    private static readonly CompSig GetIsTargetableSignature = new(
        "40 53 48 83 EC 20 F3 0F 10 81 ?? ?? ?? ?? 0F 57 C9 0F 2E C1 48 8B D9 7A 02 74 35");

    private Hook<Character.Delegates.GetIsTargetable>? hook;
    private bool overlayEnabled;
    private bool showNonEntityTargets;
    private bool blockOtherTreasureTargets;

    public event Action<nint>? NonEntityTargetDetected;

    public void SetOverlayPolicy(bool enabled, bool showTargets)
    {
        if (overlayEnabled == enabled && showNonEntityTargets == (enabled && showTargets))
        {
            return;
        }

        overlayEnabled = enabled;
        showNonEntityTargets = enabled && showTargets;
        UpdateHook();
    }

    public void SetTreasureMapPolicy(bool blockOthers)
    {
        if (blockOtherTreasureTargets == blockOthers)
        {
            return;
        }

        blockOtherTreasureTargets = blockOthers;
        UpdateHook();
    }

    public static bool IsNonEntityTreasureTarget(Character* target) =>
        target != null &&
        (int)target->ObjectKind == (int)ObjectKind.BattleNpc &&
        target->NamePlateIconId is not 60094 and not 60096;

    public void Dispose()
    {
        NonEntityTargetDetected = null;
        overlayEnabled = false;
        showNonEntityTargets = false;
        blockOtherTreasureTargets = false;
        ReleaseHook();
    }

    private void UpdateHook()
    {
        if (overlayEnabled || blockOtherTreasureTargets)
        {
            hook ??= hookRegistry.Register<Character.Delegates.GetIsTargetable>(
                GetIsTargetableSignature,
                IsTargetableDetour);
            return;
        }

        ReleaseHook();
    }

    private void ReleaseHook()
    {
        if (hook is null)
        {
            return;
        }

        hookRegistry.Release(hook);
        hook = null;
    }

    private bool IsTargetableDetour(Character* target)
    {
        var original = hook!.Original(target);
        if (blockOtherTreasureTargets && IsOtherTreasureTarget(target))
        {
            return false;
        }

        if (!showNonEntityTargets || !IsNonEntityTreasureTarget(target))
        {
            return original;
        }

        NonEntityTargetDetected?.Invoke((nint)target);
        return true;
    }

    private static bool IsOtherTreasureTarget(Character* target) =>
        target != null &&
        (int)target->ObjectKind == (int)ObjectKind.BattleNpc &&
        target->SubKind == 5 &&
        target->EventId.ContentId == EventHandlerContent.TreasureHuntDirector &&
        target->NamePlateIconId is not 60094 and not 60096;
}
