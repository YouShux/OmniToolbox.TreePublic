using Dalamud.Plugin.Services;
using OmniToolbox.Config;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class DisplayIdInformationFeature(
    DisplayIdInformationConfig config,
    IconBrowser iconBrowser) : TreeHouseFeatureBase(new(
    "display-id-information",
    "Feature.DisplayIdInformation.Title",
    "Feature.DisplayIdInformation.Description",
    TreeHouseCategory.Utility,
    Commands:
    [
        new("Feature.DisplayIdInformation.CommandDescription", "/omni 图标浏览器")
    ]))
{
    private FeatureLifetime? runtimeLifetime;
    private DisplayIdInformationNativeUi? nativeUi;
    private DisplayIdInformationCombat? combat;

    public override bool HasSettings => true;

    public override bool DrawSettings() => DisplayIdInformationPanel.Draw(config, iconBrowser);

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            nativeUi = new(config);
            lifetime.Add(nativeUi.Dispose);
            combat = new(config);
            lifetime.Add(combat.Dispose);
            if (!FrameworkManager.Instance().Reg(OnUpdate, 100))
            {
                throw new InvalidOperationException("Display ID information update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnUpdate));
            runtimeLifetime = lifetime;
            OnUpdate(DService.Instance().Framework);
        }
        catch
        {
            runtimeLifetime = null;
            nativeUi = null;
            combat = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            runtimeLifetime?.Dispose();
        }
        finally
        {
            runtimeLifetime = null;
            nativeUi = null;
            combat = null;
        }
    }

    private void OnUpdate(IFramework _)
    {
        nativeUi?.UpdateDtr();
        combat?.Update();
    }
}
