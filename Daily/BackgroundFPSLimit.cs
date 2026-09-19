using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using Microsoft.Win32.SafeHandles;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using ClientFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace OmniToolbox.TreePublic;

public sealed unsafe class BackgroundFPSLimit(BackgroundFPSLimitConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BackgroundFPSLimitTitle"),
        Description = OmniLoc.Get("BackgroundFPSLimitDescription"),
        Category = ModuleCategory.Daily
    };

    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint TIMER_MODIFY_STATE = 0x00000002;
    private const uint SYNCHRONIZE = 0x00100000;

    private readonly Stopwatch frameTimer = new();
    private SafeWaitHandle? frameWaitHandle;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.BackgroundFPSLimit.Target"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        var limit = Math.Clamp(config.Limit, 20, short.MaxValue);
        if (OmniControls.InputInt("##backgroundFPSLimit", ref limit))
        {
            config.Limit = Math.Clamp(limit, 20, short.MaxValue);
        }

        var save = ImGui.IsItemDeactivatedAfterEdit();
        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.BackgroundFPSLimit.Help"));
        return save;
    }

    protected override void OnEnable()
    {
        try
        {
            frameWaitHandle = CreateWaitableTimerExW(
                nint.Zero,
                nint.Zero,
                CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                TIMER_MODIFY_STATE | SYNCHRONIZE);
            if (frameWaitHandle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (!FrameworkManager.Instance().Reg(OnUpdate))
            {
                throw new InvalidOperationException("Background FPS update registration failed.");
            }

            frameTimer.Restart();
        }
        catch
        {
            frameWaitHandle?.Dispose();
            frameWaitHandle = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        FrameworkManager.Instance().Unreg(OnUpdate);
        frameWaitHandle?.Dispose();
        frameWaitHandle = null;
        frameTimer.Reset();
    }

    private void OnUpdate(IFramework _)
    {
        var framework = ClientFramework.Instance();
        if (framework is null || framework->GameWindow is null ||
            framework->GameWindow->WindowHandle == nint.Zero ||
            GetForegroundWindow() == framework->GameWindow->WindowHandle)
        {
            frameTimer.Restart();
            return;
        }

        var delayMS = GetFrameDelayMilliseconds(config.Limit, frameTimer.Elapsed.TotalMilliseconds);
        if (delayMS > 0)
        {
            try
            {
                WaitForFrame(frameWaitHandle!, delayMS);
            }
            catch (Win32Exception)
            {
                SetEnabled(false);
                throw;
            }
        }

        frameTimer.Restart();
    }

    internal static int GetFrameDelayMilliseconds(int limit, double elapsedMS)
    {
        limit = Math.Clamp(limit, 20, short.MaxValue);
        return limit == short.MaxValue
            ? 0
            : (int)Math.Max(0, Math.Ceiling(1000d / limit - elapsedMS));
    }

    private static void WaitForFrame(SafeWaitHandle timer, int delayMS)
    {
        // 负值表示相对等待时间，单位与 TimeSpan.Ticks 相同；等待期间让出 CPU。
        var dueTime = -delayMS * TimeSpan.TicksPerMillisecond;
        if (!SetWaitableTimer(timer, in dueTime, 0, nint.Zero, nint.Zero, false) ||
            WaitForSingleObject(timer, uint.MaxValue) == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(
        nint timerAttributes, nint timerName, uint flags, uint desiredAccess);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        SafeWaitHandle timer, in long dueTime, int period, nint completionRoutine, nint argument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}

[Serializable]
public sealed class BackgroundFPSLimitConfig
{
    public int Limit { get; set; } = 30;
}
