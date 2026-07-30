using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed class FloatingInfoOverlayFeature(
    FloatingInfoOverlayConfig config,
    NonEntityTargetVisibility targetVisibility) : TreeHouseFeatureBase(new(
    "floating-info-overlay",
    "Feature.FloatingInfoOverlay.Title",
    "Feature.FloatingInfoOverlay.Description",
    TreeHouseCategory.InterfaceOptimization,
    true))
{
    private readonly FloatingInfoOverlayPanel panel = new(config);
    private FeatureLifetime? runtimeLifetime;

    public override bool HasSettings => true;

    public override bool DrawSettings() => panel.Draw();

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            var state = new FloatingInfoOverlayState(config, targetVisibility);
            var nativeUi = new FloatingInfoOverlayNativeUi(config, state);
            lifetime.Add(state.Dispose);
            if (!FrameworkManager.Instance().Reg(state.Update))
            {
                throw new InvalidOperationException("Floating info overlay update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(state.Update));
            DalamudServices.PluginInterface.UiBuilder.Draw += nativeUi.Draw;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= nativeUi.Draw);
            runtimeLifetime = lifetime;
            state.Update(DalamudServices.Framework);
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        lifetime?.Dispose();
    }
}
