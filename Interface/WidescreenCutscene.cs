using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using OmenTools;
using OmenTools.Interop.Game.Models;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class WidescreenCutscene : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "WidescreenCutsceneTitle",
            ["Description"] = "WidescreenCutsceneDescription"
        });

    private static readonly CompSig UpdateLetterboxingSignature = new(
        "E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ??");
    private delegate nint UpdateLetterboxingDelegate(nint instance);
    private Hook<UpdateLetterboxingDelegate>? UpdateLetterboxingHook;

    private readonly HookRegistry hookRegistry;

    public WidescreenCutscene(HookRegistry hookRegistry)
    {
        this.hookRegistry = hookRegistry;
    }

    protected override void OnStart(FeatureLifetime activation) => UpdateLetterboxingHook = hookRegistry.Register(UpdateLetterboxingSignature, (UpdateLetterboxingDelegate)Detour);

    protected override void OnStop()
    {
        hookRegistry.Release(UpdateLetterboxingHook);
        UpdateLetterboxingHook = null;
    }

    private nint Detour(nint instance)
    {
        if (DService.Instance().Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            DService.Instance().Condition[ConditionFlag.WatchingCutscene78])
        {
            ((LetterboxConfig*)instance)->ShouldLetterBox &= ~LETTERBOX_FLAG;
        }

        return UpdateLetterboxingHook!.Original(instance);
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct LetterboxConfig
    {
        [FieldOffset(0x40)] public int ShouldLetterBox;
    }

    #region 常量

    private const int LETTERBOX_FLAG = 1 << 5;

    #endregion
}
