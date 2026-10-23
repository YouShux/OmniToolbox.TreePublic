using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.UI;
using KamiToolKit.MapOverlay;
using KamiToolKit.Nodes;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class MoreMinimapInformation(MoreMinimapInformation.Settings config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("MoreMinimapInformationTitle"),
        Description = OmniLoc.Get("MoreMinimapInformationDescription"),
        Category = ModuleCategory.Interface
    };

    public override string ModuleName => nameof(MoreMinimapInformation);

    public override bool HasSettings => true;

    private MapOverlayController? mapController;
    private AddonEventRegistry? addonEvents;
    private ResNode? minimapRoot;
    private IDalamudTextureWrap? playerMarkerTexture;
    private readonly Dictionary<int, Marker> objectMarkers = [];
    private readonly List<Marker> aetherCurrentMarkers = [];
    private readonly Dictionary<uint, uint> gatheringIcons = [];
    private readonly HashSet<uint> enemyHaters = [];
    private readonly Dictionary<nint, byte> hiddenEnemyNodes = [];

    protected override void OnEnable()
    {
        try
        {
            playerMarkerTexture = CreatePlayerMarkerTexture();
            mapController = new MapOverlayController
            {
                HideKey = config.HideKey
            };
            LoadAetherCurrents();
            UpdateMarkers(DalamudServices.Framework);
            mapController.Enable();
            addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            addonEvents.Register(AddonEvent.PreUpdate, MINIMAP_ADDON_NAME, OnMinimapPreUpdate);
            addonEvents.Register(AddonEvent.PostUpdate, MINIMAP_ADDON_NAME, OnMinimapUpdate);
            addonEvents.Register(AddonEvent.PreFinalize, MINIMAP_ADDON_NAME, OnMinimapFinalize);
            DalamudServices.Framework.Update += UpdateMarkers;
        }
        catch
        {
            OnDisable();
            throw;
        }
    }

    protected override void OnDisable()
    {
        DalamudServices.Framework.Update -= UpdateMarkers;
        addonEvents?.Dispose();
        addonEvents = null;
        var unitManager = RaptureAtkUnitManager.Instance();
        RestoreNativeEnemyMarkers(unitManager is null ? null : (AddonNaviMap*)unitManager->GetAddonByName(MINIMAP_ADDON_NAME));
        minimapRoot?.Dispose();
        minimapRoot = null;
        mapController?.Dispose();
        mapController = null;
        playerMarkerTexture?.Dispose();
        playerMarkerTexture = null;
        objectMarkers.Clear();
        aetherCurrentMarkers.Clear();
        gatheringIcons.Clear();
        enemyHaters.Clear();
    }

    public override bool DrawSettings()
    {
        ReadOnlySpan<string> keys = ["Friends", "Players", "Enemies", "AetherCurrents", "GatheringPoints", "TreasureChests"];
        Span<Vector2> sizes = stackalloc Vector2[keys.Length + 1];
        for (var index = 0; index < keys.Length; index++)
        {
            sizes[index] = OmniControls.MeasureCheckbox(OmniLoc.Get($"Feature.MoreMinimapInformation.{keys[index]}"));
            if (index < 4)
                sizes[index] = OmniControls.MeasureGroup([sizes[index], OmniControls.HelpIconSize()]);
        }
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var hideKeyLabel = OmniLoc.Get("Feature.MoreMinimapInformation.HideKey");
        var hideKeySize = OmniControls.MeasureCombo(GetHideKeyName(config.HideKey), OmniTheme.Scale(140f));
        sizes[^1] = OmniControls.MeasureGroup([ImGui.CalcTextSize(hideKeyLabel), hideKeySize], spacing);

        using var table = OmniControls.SettingsTable(
            "##moreMinimapInformation", sizes,
            ["##friends", "##players", "##enemies", "##aetherCurrents", "##gatheringPoints", "##treasureChests", "##hideKey"],
            columnsPerRow: 4, flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
        if (!table)
            return false;

        using var rowStyle = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(
                ImGui.GetStyle().FramePadding.X,
                MathF.Max(0f, (OmniTheme.CheckboxSize() - ImGui.GetTextLineHeight()) * 0.5f)));
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var changed = OmniControls.Checkbox(OmniLoc.Get("Feature.MoreMinimapInformation.Friends"), ref config.Friends);
        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.MoreMinimapInformation.Friends.Help"));

        ImGui.TableNextColumn();
        changed |= OmniControls.Checkbox(OmniLoc.Get("Feature.MoreMinimapInformation.Players"), ref config.Players);
        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.MoreMinimapInformation.Players.Help"));

        ImGui.TableNextColumn();
        changed |= OmniControls.Checkbox(OmniLoc.Get("Feature.MoreMinimapInformation.Enemies"), ref config.Enemies);
        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.MoreMinimapInformation.Enemies.Help"));

        ImGui.TableNextColumn();
        changed |= OmniControls.Checkbox(OmniLoc.Get("Feature.MoreMinimapInformation.AetherCurrents"), ref config.AetherCurrents);
        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.MoreMinimapInformation.AetherCurrents.Help"));

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        changed |= OmniControls.Checkbox(OmniLoc.Get("Feature.MoreMinimapInformation.GatheringPoints"), ref config.GatheringPoints);
        ImGui.TableNextColumn();
        changed |= OmniControls.Checkbox(OmniLoc.Get("Feature.MoreMinimapInformation.TreasureChests"), ref config.TreasureChests);

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(hideKeyLabel);
        OmniControls.SameLineOrWrap(hideKeySize.X, spacing);
        using (var combo = OmniControls.Combo("##moreMapInformationHideKey", GetHideKeyName(config.HideKey),
                   MathF.Min(hideKeySize.X, MathF.Max(1f, ImGui.GetContentRegionAvail().X))))
        {
            if (combo)
            {
                ReadOnlySpan<VirtualKey> hideKeys = [VirtualKey.SHIFT, VirtualKey.MENU, VirtualKey.CONTROL];
                foreach (var key in hideKeys)
                {
                    if (!ImGui.Selectable(GetHideKeyName(key), config.HideKey == key))
                        continue;

                    config.HideKey = key;
                    if (mapController is not null)
                        mapController.HideKey = key;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private static string GetHideKeyName(VirtualKey key) => key switch
    {
        VirtualKey.MENU => "Alt",
        VirtualKey.CONTROL => "Ctrl",
        _ => "Shift"
    };

    public sealed class Settings
    {
        public bool Friends = true;
        public bool Players;
        public bool Enemies;
        public bool AetherCurrents = true;
        public bool GatheringPoints = true;
        public bool TreasureChests = true;
        public VirtualKey HideKey = VirtualKey.SHIFT;
    }

    #region 常量

    private const string MINIMAP_ADDON_NAME = "_NaviMap";
    private const uint ENEMY_ICON_ID = 60422;

    #endregion
}
