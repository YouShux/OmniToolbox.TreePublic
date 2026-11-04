using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class HideNotificationPopups : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "HideNotificationPopupsTitle",
            ["Description"] = "HideNotificationPopupsDescription",
            ["Guildhest"] = "Feature.HideNotificationPopups.Guildhest",
            ["Hidden"] = "Feature.HideNotificationPopups.Hidden",
            ["Visible"] = "Feature.HideNotificationPopups.Visible",
            ["Banner"] = "Feature.HideNotificationPopups.Banner",
            ["PreviewUnavailable"] = "Feature.HideNotificationPopups.PreviewUnavailable"
        });

    private readonly Settings config;
    private readonly Action saveConfig;

    private readonly Dictionary<uint, IconSubFolder?> pendingBanners = [];
    private readonly Dictionary<nint, uint> imageBanners = [];
    private uint[]? displayBannerIDs;

    private Hook<AddonImage.Delegates.SetImage>? SetImageHook;

    public HideNotificationPopups(Settings config, Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        displayBannerIDs = null;
        activation.OnStopping(() => FrameworkManager.Instance().Unreg(FlushDiscoveredBanners));

        var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        activation.OnStopping(addonEvents.Dispose);
        addonEvents.Register(AddonEvent.PreSetup, GUILDHEST_ADDON_NAME, OnGuildhest);
        addonEvents.Register(AddonEvent.PostSetup, GUILDHEST_ADDON_NAME, OnGuildhest);
        addonEvents.Register(AddonEvent.PreShow, GUILDHEST_ADDON_NAME, OnGuildhest);
        addonEvents.Register(AddonEvent.PreShow, IMAGE_ADDON_NAME, OnBanner);
        addonEvents.Register(AddonEvent.PreDraw, IMAGE_ADDON_NAME, OnBanner);
        addonEvents.Register(AddonEvent.PreFinalize, IMAGE_ADDON_NAME, OnImageFinalize);
        addonEvents.Register(AddonEvent.PreShow, MISSION_ADDON_NAME, OnBanner);
        addonEvents.Register(AddonEvent.PostSetup, MISSION_ADDON_NAME, OnBanner);
        addonEvents.Register(AddonEvent.PostRefresh, MISSION_ADDON_NAME, OnBanner);
        addonEvents.Register(AddonEvent.PostRequestedUpdate, MISSION_ADDON_NAME, OnBanner);
        addonEvents.Register(AddonEvent.PreDraw, MISSION_ADDON_NAME, OnBanner);

        SetImageHook = DService.Instance().Hook.HookFromMemberFunction(
            typeof(AddonImage.MemberFunctionPointers),
            nameof(AddonImage.MemberFunctionPointers.SetImage),
            (AddonImage.Delegates.SetImage)SetImageDetour);
        activation.Add(SetImageHook);
        activation.OnStopping(SetImageHook.Disable);
        SetImageHook.Enable();

        ApplyCurrentPopups();
    }

    protected override void OnStop()
    {
        FlushDiscoveredBanners(DalamudServices.Framework);
        imageBanners.Clear();
        SetImageHook = null;
    }

    public override bool HasSettings => true;

    public override bool ResetSettings()
    {
        var changed = !config.HideGuildhest;
        config.HideGuildhest = true;
        foreach (var (iconID, banner) in config.Banners)
        {
            var hidden = DefaultHiddenBannerIDs.Contains(iconID);
            changed |= banner.Hidden != hidden;
            banner.Hidden = hidden;
        }
        return changed;
    }

    private void OnGuildhest(AddonEvent type, AddonArgs args)
    {
        if (!config.HideGuildhest || !IsGuildhest())
            return;

        if (args is AddonShowArgs showArgs)
        {
            showArgs.SilenceOpenSoundEffect = true;
            showArgs.PreventOriginal();
        }

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon != null)
            HideSilently(addon);
    }

    private void OnBanner(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null)
            return;

        var iconID = args.AddonName == IMAGE_ADDON_NAME
            ? GetImageBannerID((AddonImage*)addon)
            : GetMissionBannerID(addon);
        ObserveBanner(iconID, null);
        if (!IsHidden(iconID))
            return;

        if (args is AddonShowArgs showArgs)
        {
            showArgs.SilenceOpenSoundEffect = true;
            showArgs.PreventOriginal();
        }

        if (addon->IsVisible)
            HideSilently(addon);
    }

    private void OnImageFinalize(AddonEvent type, AddonArgs args) => imageBanners.Remove(args.Addon.Address);

    private void SetImageDetour(AddonImage* addon, int iconID, IconSubFolder folder, int soundEffectID)
    {
        var hook = SetImageHook;
        if (hook is null)
            return;

        try
        {
            if (iconID > 0)
            {
                imageBanners[(nint)addon] = (uint)iconID;
                ObserveBanner((uint)iconID, folder);
                if (IsHidden((uint)iconID))
                {
                    if (addon->IsVisible)
                        HideSilently((AtkUnitBase*)addon);
                    return;
                }
            }
            else
                imageBanners.Remove((nint)addon);
        }
        catch (Exception exception)
        {
            DalamudServices.PluginLog.Error(exception, "Failed to process a notification banner.");
        }

        hook.Original(addon, iconID, folder, soundEffectID);
    }

    private void ObserveBanner(uint iconID, IconSubFolder? folder)
    {
        if (iconID == 0 ||
            (config.Banners.TryGetValue(iconID, out var banner) &&
             (folder is null || banner.Folder == folder)))
            return;

        if (folder is not null || !pendingBanners.ContainsKey(iconID))
            pendingBanners[iconID] = folder;
        FrameworkManager.Instance().Reg(FlushDiscoveredBanners);
    }

    private void FlushDiscoveredBanners(IFramework framework)
    {
        FrameworkManager.Instance().Unreg(FlushDiscoveredBanners);
        if (pendingBanners.Count == 0)
            return;

        foreach (var (iconID, folder) in pendingBanners)
        {
            if (!config.Banners.TryGetValue(iconID, out var banner))
            {
                banner = new();
                config.Banners.Add(iconID, banner);
                displayBannerIDs = null;
            }
            if (folder is not null)
                banner.Folder = folder;
        }
        pendingBanners.Clear();
        saveConfig();
    }

    private bool IsHidden(uint iconID) => config.Banners.TryGetValue(iconID, out var banner) && banner.Hidden;

    private void ApplyCurrentPopups()
    {
        var ui = RaptureAtkUnitManager.Instance();
        if (ui == null)
            return;

        var guildhest = ui->GetAddonByName(GUILDHEST_ADDON_NAME);
        if (config.HideGuildhest && IsGuildhest() && guildhest != null && guildhest->IsVisible)
            HideSilently(guildhest);

        var image = (AddonImage*)ui->GetAddonByName(IMAGE_ADDON_NAME);
        if (image != null && image->IsVisible && IsHidden(GetImageBannerID(image)))
            HideSilently((AtkUnitBase*)image);

        var mission = ui->GetAddonByName(MISSION_ADDON_NAME);
        if (mission != null && mission->IsVisible && IsHidden(GetMissionBannerID(mission)))
            HideSilently(mission);
    }

    private uint GetImageBannerID(AddonImage* addon) => imageBanners.TryGetValue((nint)addon, out var iconID)
        ? iconID
        : GetIconID(addon->ImageNode);

    private static uint GetMissionBannerID(AtkUnitBase* addon)
    {
        if (addon->UldManager.NodeList == null)
            return 0;

        for (var index = 0; index < addon->UldManager.NodeListCount; index++)
        {
            var node = addon->UldManager.NodeList[index];
            if (node == null || node->Type != NodeType.Image)
                continue;

            var iconID = GetIconID((AtkImageNode*)node);
            if (MissionBannerIDs.Contains(iconID))
                return iconID;
        }
        return 0;
    }

    private static uint GetIconID(AtkImageNode* node) =>
        node != null && node->PartsList != null && node->PartId < node->PartsList->PartCount
            ? node->GetIconID()
            : 0;

    private static bool IsGuildhest()
    {
        if (!GameState.IsLoggedIn)
            return false;

        var events = EventFramework.Instance();
        var director = events == null ? null : events->GetInstanceContentDirector();
        return director != null && director->InstanceContentType == InstanceContentType.GuildOrder;
    }

    private static void HideSilently(AtkUnitBase* addon)
    {
        var disableSound = addon->DisableShowHideSoundEffects;
        addon->DisableShowHideSoundEffects = true;
        try
        {
            addon->Hide(false, false, 1);
        }
        finally
        {
            addon->DisableShowHideSoundEffects = disableSound;
        }
    }

    public sealed class Settings
    {
        public bool HideGuildhest = true;
        public Dictionary<uint, BannerSettings> Banners = KnownBannerIDs.ToDictionary(static id => id,
            static id => new BannerSettings { Hidden = DefaultHiddenBannerIDs.Contains(id) });
    }

    public sealed class BannerSettings
    {
        public bool Hidden;
        public IconSubFolder? Folder;
    }

    #region 常量

    private const string GUILDHEST_ADDON_NAME = "JournalAccept";
    private const string IMAGE_ADDON_NAME = "_Image";
    private const string MISSION_ADDON_NAME = "_WKSMissionChain";

    private static readonly uint[] KnownBannerIDs =
    [
        120031, 120032, 120055, 120081, 120082, 120083, 120084, 120085, 120086,
        120093, 120094, 120095, 120096, 120141, 120142, 121081, 121082, 121561,
        121562, 121563, 128370, 128371, 128372, 128373, 128525, 128526,
        128527, 128528, 128529, 128530, 128531, 128532
    ];

    private static readonly HashSet<uint> DefaultHiddenBannerIDs = [120031, 120032, 120055, 120095, 120096, 120141, 120142];

    private static readonly HashSet<uint> MissionBannerIDs = [128527, 128528, 128529, 128530, 128531, 128532];

    #endregion
}
