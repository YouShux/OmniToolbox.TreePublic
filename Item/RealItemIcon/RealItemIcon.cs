using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Items;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class RealItemIcon
(
    RealItemIconConfig       config,
    IInventorySnapshotReader inventoryService
) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        RequiresPrivateProvider = true
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "RealItemIconTitle",
            ["Description"] = "RealItemIconDescription",
            ["Feature.RealItemIcon.FacewearGlasses"] = "Feature.RealItemIcon.FacewearGlasses"
        });

    public override string ModuleName => nameof(RealItemIcon);

    private RealItemIconNativeUI? nativeUI;

    protected override void OnStart(FeatureLifetime activation) => nativeUI = new(config, inventoryService);

    protected override void OnStop()
    {
        var ui = nativeUI;
        nativeUI = null;
        ui?.Dispose();
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = Draw(config);
        if (changed)
        {
            nativeUI?.OnConfigurationChanged();
        }

        return changed;
    }

    private static bool Draw(RealItemIconConfig config)
    {
        var facewearGlasses = config.FacewearGlasses;
        if (!OmniControls.Checkbox(
                $"{OmniLoc.Get("Feature.RealItemIcon.FacewearGlasses")}##realItemIconFacewearGlasses",
                ref facewearGlasses))
        {
            return false;
        }

        config.FacewearGlasses = facewearGlasses;
        return true;
    }
}
