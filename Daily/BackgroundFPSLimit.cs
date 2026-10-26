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
using OmniToolbox.UI.Theme;
using ClientFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class BackgroundFPSLimit(BackgroundFPSLimit.Settings config) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Daily
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "BackgroundFPSLimitTitle",
            ["Description"] = "BackgroundFPSLimitDescription",
            ["Feature.BackgroundFPSLimit.Help"] = "Feature.BackgroundFPSLimit.Help",
            ["Feature.BackgroundFPSLimit.Target"] = "Feature.BackgroundFPSLimit.Target"
        });

    public override bool HasSettings => true;

    private readonly Stopwatch frameTimer = new();
    private SafeWaitHandle? frameWaitHandle;

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnUpdate));

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

    protected override void OnStop()
    {
        frameWaitHandle?.Dispose();
        frameWaitHandle = null;
        frameTimer.Reset();
    }

    public override bool DrawSettings()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "Feature.BackgroundFPSLimit.Target"));
        var inputWidth = OmniControls.MeasureInput(short.MaxValue.ToString(), OmniTheme.Scale(120f), step: 1).X;
        OmniControls.SameLineOrWrap(inputWidth);
        ImGui.SetNextItemWidth(inputWidth);
        var limit = Math.Clamp(config.Limit, 20, short.MaxValue);
        if (OmniControls.InputInt("##backgroundFPSLimit", ref limit))
        {
            config.Limit = Math.Clamp(limit, 20, short.MaxValue);
        }

        var save = ImGui.IsItemDeactivatedAfterEdit();
        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
        OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.BackgroundFPSLimit.Help"));
        return save;
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
                RequestEnabled(false);
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

    #region 常量

    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;

    private const uint TIMER_MODIFY_STATE = 0x00000002;

    private const uint SYNCHRONIZE = 0x00100000;

    #endregion

    public sealed class Settings
    {
        public int Limit { get; set; } = 30;
    }
}
