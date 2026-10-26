using System.Diagnostics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class AltF4Exit : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Daily
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "AltF4ExitTitle",
            ["Description"] = "AltF4ExitDescription"
        });

    private Hook<Framework.Delegates.ExitFromWindow>? ExitFromWindowHook;

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));

        ExitFromWindowHook = DService.Instance().Hook.HookFromMemberFunction(
            typeof(Framework.MemberFunctionPointers),
            nameof(Framework.MemberFunctionPointers.ExitFromWindow),
            (Framework.Delegates.ExitFromWindow)ExitFromWindowDetour);
        activation.Add(ExitFromWindowHook);
        activation.OnStopping(ExitFromWindowHook.Disable);
        activation.Add(() => ExitFromWindowHook = null);
        ExitFromWindowHook.Enable();

        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
        {
            throw new InvalidOperationException("Alt+F4 exit update registration failed.");
        }
    }

    private static void OnFrameworkUpdate(IFramework _)
    {
        var input = UIInputData.Instance();
        if (input is not null && input->IsKeyDown(SeVirtualKey.MENU) && input->IsKeyPressed(SeVirtualKey.F4))
        {
            EndGameProcess();
        }
    }

    private static void ExitFromWindowDetour(Framework* framework) => EndGameProcess();

    private static void EndGameProcess()
    {
        using var process = Process.GetCurrentProcess();
        process.Kill();
    }
}
