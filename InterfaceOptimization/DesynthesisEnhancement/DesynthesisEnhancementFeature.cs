using OmniToolbox.Config;
using OmniToolbox.Lifecycle;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class DesynthesisEnhancementFeature(DesynthesisEnhancementConfig config, HookRegistry hookRegistry) : TreeHouseFeatureBase(new(
    "desynthesis-enhancement",
    "Feature.DesynthesisEnhancement.Title",
    "Feature.DesynthesisEnhancement.Description",
    TreeHouseCategory.InterfaceOptimization))
{
    private DesynthesisEnhancementNativeUi? nativeUi;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (!DesynthesisEnhancementPanel.Draw(config))
        {
            return false;
        }

        nativeUi?.RefreshSettings();
        return true;
    }

    protected override void OnEnable()
    {
        try
        {
            nativeUi = new(config, hookRegistry);
        }
        catch
        {
            nativeUi = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            nativeUi?.Dispose();
        }
        finally
        {
            nativeUi = null;
        }
    }
}
