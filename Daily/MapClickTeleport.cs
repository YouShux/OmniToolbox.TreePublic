using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed class MapClickTeleport : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("MapClickTeleportTitle"),
        Description = OmniLoc.Get("MapClickTeleportDescription"),
        Category = ModuleCategory.Daily
    };

    private readonly TeleportConfig config;

    public MapClickTeleport(TeleportConfig config)
    {
        this.config = config;
        config.EnableMapClickTeleport = false;
    }

    protected override void OnEnable() => config.EnableMapClickTeleport = true;

    protected override void OnDisable() => config.EnableMapClickTeleport = false;
}
