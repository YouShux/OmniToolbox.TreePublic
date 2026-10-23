using Dalamud.Game.Addon.Lifecycle;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class HotbarAdjustments(HotbarAdjustments.Settings config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("HotbarAdjustmentsTitle"),
        Description = OmniLoc.Get("HotbarAdjustmentsDescription"),
        Category = ModuleCategory.Interface
    };

    public override bool HasSettings => true;

    private FeatureLifetime? runtimeLifetime;
    private readonly Dictionary<(string AddonName, int SlotIndex), BackgroundState> slotBackgrounds = [];

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            lifetime.Add(() => UpdateAllHotbars(true));
            InitializeActionDragDrop(lifetime);
            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            foreach (var addonName in AddonNames)
            {
                addonEvents.Register(AddonEvent.PostSetup, addonName, OnActionBarAddon);
                addonEvents.Register(AddonEvent.PostRequestedUpdate, addonName, OnActionBarAddon);
                addonEvents.Register(AddonEvent.PreDraw, addonName, OnActionBarAddon);
                addonEvents.Register(AddonEvent.PreFinalize, addonName, OnActionBarFinalize);
            }

            runtimeLifetime = lifetime;
            UpdateAllHotbars(false);
            UpdateActionDragDropRestrictions();
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        lifetime?.Dispose();
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
