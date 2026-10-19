using System.Globalization;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.TreeHouse;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

internal sealed partial class FloatingInfoOverlay(
    FloatingInfoOverlayConfig config,
    NonEntityTargetVisibility targetVisibility) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("FloatingInfoOverlayTitle"),
        Description = OmniLoc.Get("FloatingInfoOverlayDescription"),
        Category = ModuleCategory.Debug,
        RequiresPrivateProvider = true
    };

    public override bool HasSettings => true;

    private readonly FloatingInfoOverlayPanel panel = new(config);
    private FeatureLifetime? runtimeLifetime;

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            var state = new FloatingInfoOverlayState(config, targetVisibility);
            var nativeUI = new FloatingInfoOverlayNativeUI(config, state);
            lifetime.Add(state.Dispose);
            if (!FrameworkManager.Instance().Reg(state.Update))
            {
                throw new InvalidOperationException("Floating info overlay update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(state.Update));
            DalamudServices.PluginInterface.UiBuilder.Draw += nativeUI.Draw;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= nativeUI.Draw);
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

    public override bool DrawSettings() => panel.Draw();
}