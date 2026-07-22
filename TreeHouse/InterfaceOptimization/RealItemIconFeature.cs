using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Verification;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class RealItemIconFeature(
    RealItemIconConfig config,
    PlayerInventoryService inventoryService,
    FeatureGate featureGate) : TreeHouseFeatureBase(new(
    "real-item-icon",
    "Feature.RealItemIcon.Title",
    "Feature.RealItemIcon.Description",
    TreeHouseCategory.InterfaceOptimization,
    true))
{
    private RealItemIconNativeUi? nativeUi;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = RealItemIconPanel.Draw(config);
        if (changed)
        {
            nativeUi?.OnConfigurationChanged();
        }

        return changed;
    }

    protected override void OnEnable()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        nativeUi = new(config, inventoryService, featureGate);
    }

    protected override void OnDisable()
    {
        var ui = nativeUi;
        nativeUi = null;
        ui?.Dispose();
    }
}
