using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using OmenTools;
using OmniToolbox.Lifecycle;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private readonly HashSet<uint> mouseBlockedWindowIDs = [];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte MouseButtonQuery(int button);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte MouseClickQuery(int button, byte repeat);

    private void RegisterHiddenWindowMouseGuard(FeatureLifetime lifetime)
    {
        using var process = Process.GetCurrentProcess();
        nint moduleHandle = 0;
        foreach (ProcessModule module in process.Modules)
        {
            if (module.ModuleName.Equals("cimgui.dll", StringComparison.OrdinalIgnoreCase))
            {
                moduleHandle = module.BaseAddress;
                break;
            }
        }
        if (moduleHandle == 0)
        {
            throw new InvalidOperationException("ImGui input module unavailable.");
        }

        Hook<MouseClickQuery>? clickHook = null;
        clickHook = DService.Instance().Hook.HookFromAddress<MouseClickQuery>(
            NativeLibrary.GetExport(moduleHandle, "igIsMouseClicked"),
            (button, repeat) => IsHiddenWindowMouseBlocked() ? (byte)0 : clickHook!.Original(button, repeat));
        lifetime.Add(clickHook.Dispose);
        clickHook.Enable();

        foreach (var export in new[] { "igIsMouseDown", "igIsMouseReleased", "igIsMouseDoubleClicked" })
        {
            Hook<MouseButtonQuery>? hook = null;
            hook = DService.Instance().Hook.HookFromAddress<MouseButtonQuery>(
                NativeLibrary.GetExport(moduleHandle, export),
                button => IsHiddenWindowMouseBlocked() ? (byte)0 : hook!.Original(button));
            lifetime.Add(hook.Dispose);
            hook.Enable();
        }
        lifetime.Add(mouseBlockedWindowIDs.Clear);
    }

    private unsafe bool IsHiddenWindowMouseBlocked()
    {
        if (mouseBlockedWindowIDs.Count == 0)
        {
            return false;
        }
        var context = ImGuiNative.GetCurrentContext();
        if (context == null)
        {
            return false;
        }
        var window = new ImGuiContextPtr(context).CurrentWindow;
        // 第三方手工命中检测可能绕过 Begin/NoInputs，仅屏蔽该隐藏窗口自身的鼠标查询。
        return window.Handle != null && mouseBlockedWindowIDs.Contains(window.ID);
    }
}
