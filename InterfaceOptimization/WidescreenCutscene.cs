using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Interop.Game.Models;

namespace OmniToolbox.TreePublic;

public sealed unsafe class WidescreenCutscene : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("WidescreenCutsceneTitle"),
        Description = OmniLoc.Get("WidescreenCutsceneDescription"),
        Category = ModuleCategory.Interface
    };

    private const int LetterboxFlag = 1 << 5;
    private static readonly CompSig UpdateLetterboxingSignature = new(
        "E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ??");

    private Hook<UpdateLetterboxingDelegate>? hook;
    private readonly HookRegistry hookRegistry;

    private delegate nint UpdateLetterboxingDelegate(nint instance);

    public WidescreenCutscene(HookRegistry hookRegistry)
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
