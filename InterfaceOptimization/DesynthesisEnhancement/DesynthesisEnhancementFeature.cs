using OmniToolbox.Config;
using OmniToolbox.Lifecycle;
using OmniToolbox.Tooltips;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class DesynthesisEnhancementFeature(
    DesynthesisEnhancementConfig config,
    HookRegistry hookRegistry,
    ItemSupplementTooltip itemSupplementTooltip) : TreeHouseFeatureBase(new(
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
        itemSupplementTooltip.SetDesynthesisEnabled(IsEnabled);
        return true;
    }

    protected override void OnEnable()
    {
        nativeUi = new(config, hookRegistry);
        try
        {
            itemSupplementTooltip.SetDesynthesisEnabled(true);
        }
        catch
        {
            nativeUi.Dispose();
            nativeUi = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            itemSupplementTooltip.SetDesynthesisEnabled(false);
        }
        finally
        {
            nativeUi?.Dispose();
            nativeUi = null;
        }
    }
}
