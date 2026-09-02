using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Extensions;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class HideMinimapIcons : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("HideMinimapIconsTitle"),
        Description = OmniLoc.Get("HideMinimapIconsDescription"),
        Category = ModuleCategory.Interface
    };

    private const string NaviMapAddonName = "_NaviMap";
    private const string AreaMapAddonName = "AreaMap";
    private const string TeleportTownAddonName = "TelepotTown";
    private const int MaxVisibleRows = 6;

    private static readonly uint[] KnownIconIDs =
    [
        60091, 60311, 60314, 60318, 60319, 60320, 60321, 60322, 60326, 60330, 60331, 60333,
        60334, 60335, 60337, 60339, 60342, 60344, 60345, 60346, 60347, 60348, 60351, 60352,
        60362, 60363, 60364, 60401, 60402, 60404, 60412, 60414, 60421, 60422, 60424, 60425,
        60426, 60427, 60428, 60430, 60434, 60436, 60441, 60442, 60443, 60446, 60447, 60448,
        60449, 60450, 60451, 60453, 60456, 60457, 60458, 60459, 60460, 60467, 60473, 60495,
        60496, 60501, 60502, 60503, 60504, 60505, 60506, 60507, 60541, 60542, 60543, 60545,
        60546, 60547, 60551, 60554, 60555, 60567, 60568, 60569, 60570, 60571, 60581, 60582,
        60600, 60601, 60603, 60604, 60751, 60752, 60753, 60754, 60755, 60756, 60757, 60758,
        60761, 60762, 60763, 60764, 60765, 60766, 60767, 60768, 60769, 60770, 60771, 60772,
        60773, 60774, 60775, 60776, 60777, 60778, 60779, 60780, 60781, 60782, 60783, 60784,
        60785, 60786, 60787, 60788, 60789, 60791, 60905, 60906, 60907, 60908, 60910, 60926,
        60927, 60934, 60935, 60958, 60959, 60960, 60961, 60968, 60969, 60971, 60983, 60986,
        60987, 60988, 60993, 61731, 61732, 61733, 63903, 63905, 63906, 63907, 63919, 63920,
        63921, 63922, 63932, 63933, 63934, 63963, 63964, 63965, 63966, 63970, 63971, 63972,
        63973, 70961, 70962, 70963, 70964, 70965, 70966, 70967, 70968, 70969, 70970, 70971,
        70972, 70973, 70974, 70975, 70976, 71001, 71002, 71003, 71004, 71005, 71006, 71011,
        71012, 71013, 71015, 71016, 71021, 71022, 71023, 71024, 71025, 71026, 71031, 71032,
        71033, 71034, 71035, 71036, 71041, 71042, 71043, 71044, 71045, 71046, 71051, 71052,
        71053, 71054, 71055, 71056, 71061, 71062, 71063, 71064, 71065, 71066, 71071, 71072,
        71073, 71074, 71075, 71076, 71081, 71082, 71083, 71084, 71085, 71086, 71091, 71092,
        71093, 71094, 71095, 71096, 71101, 71102, 71111, 71112, 71121, 71122, 71123, 71124,
        71125, 71126, 71131, 71132, 71133, 71134, 71135, 71136, 71141, 71142, 71143, 71145,
        71146, 71151, 71152, 71153, 71155, 71156
    ];

    private readonly HideMinimapIconsConfig config;
    private readonly List<uint> displayIconIDs = [];
    private readonly HashSet<nint> hiddenNodes = [];
    private readonly Dictionary<nint, (uint IconID, Vector2 Scale)> originalIconScales = [];
    private FeatureLifetime? runtimeLifetime;

    public HideMinimapIcons(HideMinimapIconsConfig config)
    {
        this.config = config;
        RefreshDisplayIconIDs();
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        var mapSettingsChanged = false;
        {
            using var settingsTable = ImRaii.Table(
                "##hideMinimapIconsSettings",
                4,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                new Vector2(ImGui.GetContentRegionAvail().X, 0f));
            if (!settingsTable)
            {
                return false;
            }

            ImGui.TableSetupColumn("##hideMinimapIconsMinimap", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##hideMinimapIconsAreaMap", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##hideMinimapIconsIconScale", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##hideMinimapIconsUnused2", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var hideOnMinimap = config.HideOnMinimap;
            if (OmniControls.Checkbox(
                    OmniLoc.Get("Feature.HideMinimapIcons.HideOnMinimap"),
                    ref hideOnMinimap))
            {
                config.HideOnMinimap = hideOnMinimap;
                changed = true;
                mapSettingsChanged = true;
            }

            ImGui.TableNextColumn();
            var hideOnAreaMap = config.HideOnAreaMap;
            if (OmniControls.Checkbox(
                    OmniLoc.Get("Feature.HideMinimapIcons.HideOnAreaMap"),
                    ref hideOnAreaMap))
            {
                config.HideOnAreaMap = hideOnAreaMap;
                changed = true;
                mapSettingsChanged = true;
            }

            ImGui.TableNextColumn();
            var iconScaleLabel = OmniLoc.Get("Feature.HideMinimapIcons.IconScale");
            var iconScale = Math.Clamp(config.IconScale, 0f, 3f);
            var iconScaleWidth = MathF.Max(
                1f,
                ImGui.GetContentRegionAvail().X -
                ImGui.GetStyle().ItemSpacing.X -
                ImGui.CalcTextSize(iconScaleLabel).X);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(iconScaleLabel);
            ImGui.SameLine();
            if (OmniControls.SliderFloat(
                    "##hideMinimapIconsIconScale",
                    ref iconScale,
                    0f,
                    3f,
                    "%.1f",
                    iconScaleWidth))
            {
                config.IconScale = iconScale;
                changed = true;
                mapSettingsChanged = true;
            }
        }

        if (mapSettingsChanged)
        {
            ApplyCurrentMaps(false);
        }

        ImGui.Spacing();
        var style = ImGui.GetStyle();
        var iconSize = OmniTheme.Scale(32f);
        var rowContentHeight = iconSize;
        var optionContentWidth = OmniTheme.CheckboxSize() + style.ItemInnerSpacing.X + iconSize;
        var optionOccupiedWidth = optionContentWidth + style.CellPadding.X * 2f;
        var availableWidth = MathF.Max(
            optionOccupiedWidth,
            ImGui.GetContentRegionAvail().X -
            style.ScrollbarSize -
            style.ItemSpacing.X -
            style.CellPadding.X * 2f);
        var optionsPerRow = Math.Clamp((int)(availableWidth / optionOccupiedWidth), 1, 16);
        var rowCount = (displayIconIDs.Count + optionsPerRow - 1) / optionsPerRow;
        var rowHeight = rowContentHeight + style.CellPadding.Y * 2f;
        using var table = ImRaii.Table(
            "##hideMinimapIcons",
            optionsPerRow,
            ImGuiTableFlags.NoPadOuterX |
            ImGuiTableFlags.NoSavedSettings |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                rowHeight * Math.Min(rowCount, MaxVisibleRows)));
        if (!table)
        {
            return false;
        }

        for (var option = 0; option < optionsPerRow; option++)
        {
            ImGui.TableSetupColumn(
                $"##hideMinimapIconOption{option}",
                ImGuiTableColumnFlags.WidthFixed,
                optionContentWidth);
        }

        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(rowCount, rowHeight);
        while (clipper.Step())
        {
            for (var row = Math.Max(0, clipper.DisplayStart); row < Math.Min(rowCount, clipper.DisplayEnd); row++)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
                var rowStart = row * optionsPerRow;
                var rowEnd = Math.Min(rowStart + optionsPerRow, displayIconIDs.Count);
                for (var index = rowStart; index < rowEnd; index++)
                {
                    changed |= DrawIconOption(
                        displayIconIDs[index],
                        iconSize,
                        optionContentWidth,
                        rowContentHeight);
                }
            }
        }

        clipper.End();
        clipper.Destroy();
        if (changed)
        {
            RefreshDisplayIconIDs();
        }

        return changed;
    }

    protected override void OnEnable()
    {
        runtimeLifetime = new();
        DalamudServices.AddonLifecycle.RegisterListener(
            AddonEvent.PostUpdate,
            NaviMapAddonName,
            OnNaviMapUpdate);
        DalamudServices.AddonLifecycle.RegisterListener(
            AddonEvent.PostUpdate,
            AreaMapAddonName,
            OnAreaMapUpdate);
        DalamudServices.AddonLifecycle.RegisterListener(
            AddonEvent.PostUpdate,
            TeleportTownAddonName,
            OnTeleportTownUpdate);
        runtimeLifetime.Add(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PostUpdate,
            NaviMapAddonName,
            OnNaviMapUpdate));
        runtimeLifetime.Add(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PostUpdate,
            AreaMapAddonName,
            OnAreaMapUpdate));
        runtimeLifetime.Add(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PostUpdate,
            TeleportTownAddonName,
            OnTeleportTownUpdate));
        ApplyCurrentMaps(false);
    }

    protected override void OnDisable()
    {
        runtimeLifetime?.Dispose();
        runtimeLifetime = null;
        ApplyCurrentMaps(true);
        hiddenNodes.Clear();
        originalIconScales.Clear();
    }

    private bool DrawIconOption(
        uint iconID,
        float iconSize,
        float optionContentWidth,
        float rowContentHeight)
    {
        ImGui.TableNextColumn();
        OmniControls.CenterTableItem(
            new Vector2(optionContentWidth, rowContentHeight),
            rowContentHeight);
        var hidden = config.HiddenIconIDs.Contains(iconID);
        var changed = false;
        if (OmniControls.Checkbox($"##hideMinimapIcon{iconID}", ref hidden))
        {
            SetHidden(iconID, hidden);
            changed = true;
        }

        ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImageHelper.GetGameIcon(iconID) is { } texture)
        {
            ImGui.Image(texture.Handle, new Vector2(iconSize));
        }
        else
        {
            ImGui.Dummy(new Vector2(iconSize));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(string.Format(OmniLoc.Get("Feature.HideMinimapIcons.IconTooltip"), iconID));
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SetHidden(iconID, !hidden);
            changed = true;
        }

        return changed;
    }

    private void OnNaviMapUpdate(AddonEvent type, AddonArgs args) =>
        ApplyMinimap((AddonNaviMap*)args.Addon.Address, false);

    private void OnAreaMapUpdate(AddonEvent type, AddonArgs args) =>
        ApplyAreaMap((AddonAreaMap*)args.Addon.Address, false);

    private void OnTeleportTownUpdate(AddonEvent type, AddonArgs args) =>
        ApplyTeleportTown((AddonTeleportTown*)args.Addon.Address, false);

    private void ApplyCurrentMaps(bool restore)
    {
        var unitManager = RaptureAtkUnitManager.Instance();
        if (unitManager != null)
        {
            ApplyMinimap((AddonNaviMap*)unitManager->GetAddonByName(NaviMapAddonName), restore);
            ApplyAreaMap((AddonAreaMap*)unitManager->GetAddonByName(AreaMapAddonName), restore);
            ApplyTeleportTown((AddonTeleportTown*)unitManager->GetAddonByName(TeleportTownAddonName), restore);
        }
    }

    private void ApplyMinimap(AddonNaviMap* addon, bool restore)
    {
        if (addon == null || !addon->AtkUnitBase.IsVisible)
        {
            return;
        }

        for (var index = 0; index < addon->NaviMap.NaviMapMarkers.Length; index++)
        {
            ref var marker = ref addon->NaviMap.NaviMapMarkers[index];
            if (marker.ComponentNode == null || !marker.ComponentNode->AtkResNode.IsVisible())
            {
                continue;
            }

            ApplyMarkerNode(
                marker.ComponentNode,
                GetMarkerImageNode(marker.ComponentNode),
                config.HiddenIconIDs.Contains(marker.IconId) ? marker.IconId : marker.SecondaryIconId,
                restore,
                config.HideOnMinimap);
        }
    }

    private void ApplyAreaMap(AddonAreaMap* addon, bool restore)
    {
        if (addon == null || !addon->AtkUnitBase.IsVisible)
        {
            return;
        }

        ApplyAreaMapMarkers(addon->ComponentMap, restore);
    }

    private void ApplyTeleportTown(AddonTeleportTown* addon, bool restore)
    {
        if (addon == null || !addon->AtkUnitBase.IsVisible)
        {
            return;
        }

        ApplyAreaMapMarkers(addon->ComponentMap, restore);
    }

    private void ApplyAreaMapMarkers(AtkComponentMap* componentMap, bool restore)
    {
        var component = (AtkComponentBase*)componentMap;
        if (component == null || component->UldManager.NodeList == null)
        {
            return;
        }

        for (var index = 6; index < component->UldManager.NodeListCount; index++)
        {
            var markerNode = (AtkComponentNode*)component->UldManager.NodeList[index];
            if (markerNode == null || !markerNode->AtkResNode.IsVisible())
            {
                continue;
            }

            var imageNode = GetMarkerImageNode(markerNode);
            if (imageNode != null)
            {
                ApplyMarkerNode(markerNode, imageNode, imageNode->IconId, restore, config.HideOnAreaMap);
            }
        }
    }

    private void ApplyMarkerNode(
        AtkComponentNode* node,
        AtkImageNode* imageNode,
        uint iconID,
        bool restore,
        bool hideEnabled)
    {
        var address = (nint)node;
        var hidden = !restore && hideEnabled && config.HiddenIconIDs.Contains(iconID);
        if (hidden)
        {
            if (node->AtkResNode.Color.A != 0)
            {
                hiddenNodes.Add(address);
                node->AtkResNode.Color.A = 0;
            }
        }
        else if (hiddenNodes.Remove(address))
        {
            node->AtkResNode.Color.A = byte.MaxValue;
        }

        if (imageNode == null)
        {
            return;
        }

        var imageAddress = (nint)imageNode;
        var iconScale = Math.Clamp(config.IconScale, 0f, 3f);
        if (restore || hidden || Math.Abs(iconScale - 1f) < 0.001f)
        {
            if (originalIconScales.Remove(imageAddress, out var originalScale))
            {
                imageNode->AtkResNode.SetScale(originalScale.Scale.X, originalScale.Scale.Y);
            }

            return;
        }

        var hasOriginalScale = originalIconScales.TryGetValue(imageAddress, out var iconNodeScale);
        if (!hasOriginalScale || iconNodeScale.IconID != iconID)
        {
            if (hasOriginalScale)
            {
                imageNode->AtkResNode.SetScale(iconNodeScale.Scale.X, iconNodeScale.Scale.Y);
            }

            iconNodeScale = (iconID, new Vector2(
                imageNode->AtkResNode.GetScaleX(),
                imageNode->AtkResNode.GetScaleY()));
            originalIconScales[imageAddress] = iconNodeScale;
        }

        imageNode->AtkResNode.SetScale(
            iconNodeScale.Scale.X * iconScale,
            iconNodeScale.Scale.Y * iconScale);
    }

    private static AtkImageNode* GetMarkerImageNode(AtkComponentNode* markerNode)
    {
        if (markerNode->Component == null ||
            markerNode->Component->UldManager.NodeList == null ||
            markerNode->Component->UldManager.NodeListCount <= 4)
        {
            return null;
        }

        var imageNode = markerNode->Component->UldManager.NodeList[4];
        if (imageNode != null && imageNode->Type == NodeType.Image)
        {
            return (AtkImageNode*)imageNode;
        }

        var fallbackImageNode = markerNode->Component->UldManager.NodeList[3];
        if (fallbackImageNode != null && fallbackImageNode->Type == NodeType.Image)
        {
            return (AtkImageNode*)fallbackImageNode;
        }

        return null;
    }

    private void SetHidden(uint iconID, bool hidden)
    {
        if (hidden)
        {
            config.HiddenIconIDs.Add(iconID);
        }
        else
        {
            config.HiddenIconIDs.Remove(iconID);
        }

        ApplyCurrentMaps(false);
    }

    private void RefreshDisplayIconIDs()
    {
        displayIconIDs.Clear();
        foreach (var iconID in KnownIconIDs)
        {
            if (config.HiddenIconIDs.Contains(iconID))
            {
                displayIconIDs.Add(iconID);
            }
        }

        foreach (var iconID in KnownIconIDs)
        {
            if (!config.HiddenIconIDs.Contains(iconID))
            {
                displayIconIDs.Add(iconID);
            }
        }
    }
}

[Serializable]
public sealed class HideMinimapIconsConfig
{
    public bool HideOnMinimap { get; set; } = true;

    public bool HideOnAreaMap { get; set; } = true;

    public float IconScale { get; set; } = 1f;

    public HashSet<uint> HiddenIconIDs { get; set; } = [];
}
