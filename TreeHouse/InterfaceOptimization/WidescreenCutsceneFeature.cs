using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Interop.Game.Models;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed unsafe class WidescreenCutsceneFeature : TreeHouseFeatureBase
{
    private const int LetterboxFlag = 1 << 5;
    private static readonly CompSig UpdateLetterboxingSignature = new(
        "E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ??");

    private Hook<UpdateLetterboxingDelegate>? hook;
    private readonly HookRegistry hookRegistry;

    private delegate nint UpdateLetterboxingDelegate(nint instance);

    public WidescreenCutsceneFeature(HookRegistry hookRegistry) : base(new(
        "widescreen-cutscene",
        "Feature.WidescreenCutscene.Title",
        "Feature.WidescreenCutscene.Description",
        TreeHouseCategory.InterfaceOptimization))
    {
        this.hookRegistry = hookRegistry;
    }

    protected override void OnEnable()
    {
        hook = hookRegistry.Register(UpdateLetterboxingSignature, (UpdateLetterboxingDelegate)Detour);
    }

    protected override void OnDisable()
    {
        hookRegistry.Release(hook);
        hook = null;
    }

    private nint Detour(nint instance)
    {
        if (DService.Instance().Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            DService.Instance().Condition[ConditionFlag.WatchingCutscene78])
        {
            ((LetterboxConfig*)instance)->ShouldLetterBox &= ~LetterboxFlag;
        }

        return hook!.Original(instance);
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct LetterboxConfig
    {
        [FieldOffset(0x40)] public int ShouldLetterBox;
    }
}
