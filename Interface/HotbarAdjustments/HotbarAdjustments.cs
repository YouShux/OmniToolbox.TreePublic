using Dalamud.Game.Addon.Lifecycle;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class HotbarAdjustments(HotbarAdjustments.Settings config) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "HotbarAdjustmentsTitle",
            ["Description"] = "HotbarAdjustmentsDescription",
            ["Feature.HotbarAdjustments.BackgroundOpacity"] = "Feature.HotbarAdjustments.BackgroundOpacity",
            ["Feature.HotbarAdjustments.HideBackground"] = "Feature.HotbarAdjustments.HideBackground",
            ["Feature.HotbarAdjustments.HideBackground.Help"] = "Feature.HotbarAdjustments.HideBackground.Help",
            ["Feature.HotbarAdjustments.HideLocks"] = "Feature.HotbarAdjustments.HideLocks",
            ["Feature.HotbarAdjustments.Modifier"] = "Feature.HotbarAdjustments.Modifier",
            ["Feature.HotbarAdjustments.Modifier.Alt"] = "Feature.HotbarAdjustments.Modifier.Alt",
            ["Feature.HotbarAdjustments.Modifier.Control"] = "Feature.HotbarAdjustments.Modifier.Control",
            ["Feature.HotbarAdjustments.Modifier.Help"] = "Feature.HotbarAdjustments.Modifier.Help",
            ["Feature.HotbarAdjustments.Modifier.Shift"] = "Feature.HotbarAdjustments.Modifier.Shift",
            ["Feature.HotbarAdjustments.UnlockActions"] = "Feature.HotbarAdjustments.UnlockActions",
            ["Feature.HotbarAdjustments.UnlockActions.Help"] = "Feature.HotbarAdjustments.UnlockActions.Help"
        });

    public override bool HasSettings => true;

    private readonly Dictionary<(string AddonName, int SlotIndex), BackgroundState> slotBackgrounds = [];

    protected override void OnStart(FeatureLifetime activation)
    {
        var lifetime = Activation;
        lifetime.Add(() => UpdateAllHotbars(true));
        InitializeActionDragDrop(lifetime);
        var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        lifetime.OnStopping(addonEvents.Dispose);
        foreach (var addonName in AddonNames)
        {
            addonEvents.Register(AddonEvent.PostSetup, addonName, OnActionBarAddon);
            addonEvents.Register(AddonEvent.PostRequestedUpdate, addonName, OnActionBarAddon);
            addonEvents.Register(AddonEvent.PreDraw, addonName, OnActionBarAddon);
            addonEvents.Register(AddonEvent.PreFinalize, addonName, OnActionBarFinalize);
        }
        UpdateAllHotbars(false);
        UpdateActionDragDropRestrictions();
    }

    public sealed class Settings
    {
        public bool HideBackground = true;

        public float BackgroundOpacity;

        public bool HideLocks = true;

        public int ModifierKey;

        public bool UnlockActions = true;
    }

    private static readonly string[] AddonNames =
    [
        "_ActionBar",
        "_ActionBar01",
        "_ActionBar02",
        "_ActionBar03",
        "_ActionBar04",
        "_ActionBar05",
        "_ActionBar06",
        "_ActionBar07",
        "_ActionBar08",
        "_ActionBar09"
    ];
}
