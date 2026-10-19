using Dalamud.Plugin.Services;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class DisplayIDInformation(
    DisplayIDInformationConfig config,
    IconBrowser iconBrowser) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("DisplayIdInformationTitle"),
        Description = OmniLoc.Get("DisplayIdInformationDescription"),
        Category = ModuleCategory.Debug,
        Commands =
        [
            new ModuleCommand("Feature.DisplayIdInformation.CommandDescription", OmniCommandCatalog.Format("iconbrowser"))
        ]
    };

    public override bool HasSettings => true;

    private FeatureLifetime? runtimeLifetime;
    private DisplayIDInformationNativeUI? nativeUI;
    private DisplayIDInformationCombat? combat;

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            nativeUI = new(config);
            lifetime.Add(nativeUI.Dispose);
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
            nativeUI = null;
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
            nativeUI = null;
            combat = null;
        }
    }

    public override bool DrawSettings() => DisplayIDInformationPanel.Draw(config, iconBrowser);

    private void OnUpdate(IFramework _)
    {
        nativeUI?.UpdateDtr();
        combat?.Update();
    }
}