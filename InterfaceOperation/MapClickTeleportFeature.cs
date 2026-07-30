using OmniToolbox.Config;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed class MapClickTeleportFeature : TreeHouseFeatureBase
{
    public const string FeatureId = "map-click-teleport";

    private readonly TeleportConfig config;

    public MapClickTeleportFeature(TeleportConfig config)
        : base(new(
            FeatureId,
            "Feature.MapClickTeleport.Title",
            "Feature.MapClickTeleport.Description",
            TreeHouseCategory.InterfaceOperation))
    {
        this.config = config;
        config.EnableMapClickTeleport = false;
    }

    protected override void OnEnable() => config.EnableMapClickTeleport = true;

    protected override void OnDisable() => config.EnableMapClickTeleport = false;
}
