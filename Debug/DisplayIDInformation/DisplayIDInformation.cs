using Dalamud.Plugin.Services;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class DisplayIDInformation
(
    DisplayIDInformationConfig config,
    IconBrowser                iconBrowser
) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Debug,
        Commands =
        [
            new ModuleCommand("Feature.DisplayIdInformation.CommandDescription", OmniCommandCatalog.Format("iconbrowser"))
        ]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "DisplayIdInformationTitle",
            ["Description"] = "DisplayIdInformationDescription",
            ["Feature.DisplayIdInformation.CommandDescription"] = "Feature.DisplayIdInformation.CommandDescription",
            ["Feature.DisplayIdInformation.ActionFlyText.Help"] = "Feature.DisplayIdInformation.ActionFlyText.Help",
            ["Feature.DisplayIdInformation.CastBar.Help"] = "Feature.DisplayIdInformation.CastBar.Help",
            ["Feature.DisplayIdInformation.Icon"] = "Feature.DisplayIdInformation.Icon",
            ["Feature.DisplayIdInformation.IconBrowser.Help"] = "Feature.DisplayIdInformation.IconBrowser.Help",
            ["Feature.DisplayIdInformation.Item"] = "Feature.DisplayIdInformation.Item",
            ["Feature.DisplayIdInformation.Others.Help"] = "Feature.DisplayIdInformation.Others.Help",
            ["Feature.DisplayIdInformation.StatusFlyText.Help"] = "Feature.DisplayIdInformation.StatusFlyText.Help",
            ["Feature.DisplayIdInformation.Weather"] = "Feature.DisplayIdInformation.Weather",
            ["Feature.DisplayIdInformation.Zone"] = "Feature.DisplayIdInformation.Zone",
            ["IconBrowser.Title"] = "IconBrowser.Title"
        });

    private DisplayIDInformationNativeUI? nativeUI;
    private DisplayIDInformationCombat?   combat;

    protected override void OnStart(FeatureLifetime activation)
    {
        var lifetime = Activation;
        lifetime.Add(() =>
        {
            nativeUI = null;
            combat   = null;
        });
        nativeUI = new(config);
        lifetime.Add(nativeUI.Dispose);
        combat = new(config);
        lifetime.Add(combat.Dispose);
        if (!FrameworkManager.Instance().Reg(OnUpdate, 100))
        {
            throw new InvalidOperationException("Display ID information update registration failed.");
        }

        lifetime.OnStopping(() => FrameworkManager.Instance().Unreg(OnUpdate));
        OnUpdate(DService.Instance().Framework);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => DisplayIDInformationPanel.Draw(config, iconBrowser);

    private void OnUpdate(IFramework _)
    {
        nativeUI?.UpdateDtr();
        combat?.Update();
    }
}
