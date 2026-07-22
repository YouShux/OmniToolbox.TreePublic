using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.General;

public sealed unsafe class AltF4ExitFeature : TreeHouseFeatureBase
{
    private const int WmClose = 0x10;

    public AltF4ExitFeature() : base(new(
        "alt-f4-exit",
        "Feature.AltF4Exit.Title",
        "Feature.AltF4Exit.Description",
        TreeHouseCategory.General))
    {
    }

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
