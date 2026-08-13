using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe class AltF4Exit : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AltF4ExitTitle"),
        Description = OmniLoc.Get("AltF4ExitDescription"),
        Category = ModuleCategory.Daily
    };

    private const int WmClose = 0x10;

    protected override void OnEnable()
    {
        if (!FrameworkManager.Instance().Reg(OnUpdate))
        {
            throw new InvalidOperationException("Alt+F4 update registration failed.");
        }
    }

    protected override void OnDisable() => FrameworkManager.Instance().Unreg(OnUpdate);

    private static void OnUpdate(IFramework _)
    {
        var input = UIInputData.Instance();
        if (input is not null && input->IsKeyDown(SeVirtualKey.MENU) && input->IsKeyPressed(SeVirtualKey.F4))
        {
            SendMessage(Process.GetCurrentProcess().MainWindowHandle, WmClose, 0, 0);
        }
    }

    [DllImport("user32.dll")]
    private static extern int SendMessage(nint window, int message, int wParam, int lParam);
}
