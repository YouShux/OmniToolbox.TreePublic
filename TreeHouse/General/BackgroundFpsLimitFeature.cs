using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.OmenService;
using OmenTools.ImGuiOm;
using ClientFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace OmniToolbox.TreeHouse.General;

public sealed unsafe class BackgroundFpsLimitFeature(TreeHouseConfig config) : TreeHouseFeatureBase(new(
    "background-fps-limit",
    "Feature.BackgroundFpsLimit.Title",
    "Feature.BackgroundFpsLimit.Description",
    TreeHouseCategory.General))
{
    private bool hasOriginalState;
    private bool originalLimited;
    private short originalLimit;
    private int currentTargetLimit;
    private short deviceLimit;
    private DateTime nextCalibrationUtc;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.BackgroundFpsLimit.Target"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        var limit = Math.Clamp(config.BackgroundFpsLimit.Limit, 20, short.MaxValue);
        if (ImGui.InputInt("##backgroundFpsLimit", ref limit, 10, 10))
        {
            config.BackgroundFpsLimit.Limit = Math.Clamp(limit, 20, short.MaxValue);
        }

        var save = ImGui.IsItemDeactivatedAfterEdit();
        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.BackgroundFpsLimit.Help"));
        return save;
    }

    protected override void OnEnable()
    {
        if (!FrameworkManager.Instance().Reg(OnUpdate, 100))
        {
            throw new InvalidOperationException("Background FPS update registration failed.");
        }
    }

    protected override void OnDisable()
    {
        FrameworkManager.Instance().Unreg(OnUpdate);
        RestoreOriginalState();
    }

    private void OnUpdate(IFramework _)
    {
        if (GameState.IsForeground)
        {
            RestoreOriginalState();
            return;
        }

        var device = Device.Instance();
        if (device is null)
        {
            return;
        }

        if (!hasOriginalState)
        {
            originalLimited = device->IsFrameRateLimited;
            originalLimit = device->FrameRateLimit;
            hasOriginalState = true;
        }

        var limit = Math.Clamp(config.BackgroundFpsLimit.Limit, 20, short.MaxValue);
        if (currentTargetLimit != limit)
        {
            currentTargetLimit = limit;
            deviceLimit = (short)limit;
            nextCalibrationUtc = DateTime.MinValue;
        }

        device->IsFrameRateLimited = true;
        device->FrameRateLimit = deviceLimit;
        CalibrateDeviceLimit(limit, device);
    }

    private void RestoreOriginalState()
    {
        if (!hasOriginalState)
        {
            return;
        }

        var device = Device.Instance();
        if (device is not null)
        {
            device->IsFrameRateLimited = originalLimited;
            device->FrameRateLimit = originalLimit;
        }

        hasOriginalState = false;
        currentTargetLimit = 0;
        deviceLimit = 0;
        nextCalibrationUtc = DateTime.MinValue;
    }

    private void CalibrateDeviceLimit(int targetLimit, Device* device)
    {
        var now = DateTime.UtcNow;
        if (now < nextCalibrationUtc)
        {
            return;
        }

        nextCalibrationUtc = now.AddMilliseconds(500);
        var framework = ClientFramework.Instance();
        if (framework is null)
        {
            return;
        }

        var currentFps = (int)MathF.Round(Math.Max(framework->FrameRate, 0f));
        if (currentFps <= 0 || currentFps == targetLimit)
        {
            return;
        }

        deviceLimit = (short)Math.Clamp(deviceLimit + targetLimit - currentFps, targetLimit, short.MaxValue);
        device->FrameRateLimit = deviceLimit;
    }
}
