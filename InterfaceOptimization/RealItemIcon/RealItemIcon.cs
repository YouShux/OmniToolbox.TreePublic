using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.Items;

namespace OmniToolbox.TreePublic;

public sealed class RealItemIcon(
    RealItemIconConfig config,
    PlayerInventoryService inventoryService) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("RealItemIconTitle"),
        Description = OmniLoc.Get("RealItemIconDescription"),
        Category = ModuleCategory.Item,
        RequiresPrivateProvider = true
    };

    private RealItemIconNativeUI? nativeUI;

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

    protected override void OnEnable()
    {
        nativeUI = new(config, inventoryService);
    }

    protected override void OnDisable()
    {
        var ui = nativeUI;
        nativeUI = null;
        ui?.Dispose();
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

public sealed class RealItemIconConfig
{
    public bool FacewearGlasses { get; set; } = true;
}
