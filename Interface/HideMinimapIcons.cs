using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class HideMinimapIcons : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "HideMinimapIconsTitle",
            ["Description"] = "HideMinimapIconsDescription",
            ["Feature.HideMinimapIcons.AetherytesOnTop"] = "Feature.HideMinimapIcons.AetherytesOnTop",
            ["Feature.HideMinimapIcons.AetherytesOnTopHint"] = "Feature.HideMinimapIcons.AetherytesOnTopHint",
            ["Feature.HideMinimapIcons.HideOnAreaMap"] = "Feature.HideMinimapIcons.HideOnAreaMap",
            ["Feature.HideMinimapIcons.HideOnMinimap"] = "Feature.HideMinimapIcons.HideOnMinimap",
            ["Feature.HideMinimapIcons.IconScale"] = "Feature.HideMinimapIcons.IconScale",
            ["Feature.HideMinimapIcons.IconTooltip"] = "Feature.HideMinimapIcons.IconTooltip",
            ["Feature.HideMinimapIcons.KeepTextWhenHidden"] = "Feature.HideMinimapIcons.KeepTextWhenHidden",
            ["Feature.HideMinimapIcons.KeepTextWhenHiddenHint"] = "Feature.HideMinimapIcons.KeepTextWhenHiddenHint"
        });

    public override bool HasSettings => true;

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

    private readonly Settings config;
    private readonly List<uint> displayIconIDs = [];
    private readonly Dictionary<nint, (uint NodeID, nint Addon)> hiddenNodes = [];
    private readonly Dictionary<nint, (uint NodeID, uint IconID, Vector2 Scale, nint Addon)> originalIconScales = [];

    public HideMinimapIcons(Settings config)
    {
        this.config = config;
        RefreshDisplayIconIDs();
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        aetheryteIconID = (uint)LuminaGetter.GetRowOrDefault<MapSymbol>(1).Icon;
        aethernetIconID = (uint)LuminaGetter.GetRowOrDefault<MapSymbol>(2).Icon;
        var lifetime = Activation;
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PreDraw,
            NAVI_MAP_ADDON_NAME,
            OnMapPreDraw));
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PreDraw,
            AREA_MAP_ADDON_NAME,
            OnMapPreDraw));
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PreDraw,
            TELEPORT_TOWN_ADDON_NAME,
            OnMapPreDraw));
        DalamudServices.AddonLifecycle.RegisterListener(
            AddonEvent.PreDraw,
            NAVI_MAP_ADDON_NAME,
            OnMapPreDraw);
        DalamudServices.AddonLifecycle.RegisterListener(
            AddonEvent.PreDraw,
            AREA_MAP_ADDON_NAME,
            OnMapPreDraw);
        DalamudServices.AddonLifecycle.RegisterListener(
            AddonEvent.PreDraw,
            TELEPORT_TOWN_ADDON_NAME,
            OnMapPreDraw);
        foreach (var addonName in new[] { NAVI_MAP_ADDON_NAME, AREA_MAP_ADDON_NAME, TELEPORT_TOWN_ADDON_NAME })
        {
            lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
                AddonEvent.PreUpdate, addonName, OnMapPreUpdate));
            DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreUpdate, addonName, OnMapPreUpdate);
            lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
                AddonEvent.PostDraw, addonName, OnMapDiagnosticPostDraw));
            DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, addonName, OnMapDiagnosticPostDraw);
            lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
                AddonEvent.PostSetup, addonName, OnMapDiagnosticSetup));
            DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, addonName, OnMapDiagnosticSetup);
            lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
                AddonEvent.PreFinalize, addonName, OnMapFinalize));
            DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonName, OnMapFinalize);
        }
        ArmMapDiagnostics("Start");
        ApplyCurrentMaps(false);
    }

    protected override void OnStop()
    {
        ApplyCurrentMaps(true);
        hiddenNodes.Clear();
        originalIconScales.Clear();
        originalMarkerLayers.Clear();
        mapDiagnostics.Clear();
    }

    public override bool DrawSettings()
    {
        var changed = false;
        var mapSettingsChanged = false;
        {
            using var settingsTable = OmniControls.SettingsTable(
                "##hideMinimapIconsSettings",
                [OmniControls.MeasureCheckbox(OmniLoc.Get(this, "Feature.HideMinimapIcons.HideOnMinimap")),
                    OmniControls.MeasureCheckbox(OmniLoc.Get(this, "Feature.HideMinimapIcons.HideOnAreaMap")),
                    OmniControls.MeasureGroup([ImGui.CalcTextSize(OmniLoc.Get(this, "Feature.HideMinimapIcons.IconScale")),
                        OmniControls.MeasureFloatInput(config.IconScale, "%.1f", OmniTheme.Scale(64f))])],
                ["##hideMinimapIconsMinimap", "##hideMinimapIconsAreaMap", "##hideMinimapIconsIconScale"],
                flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 4);
            if (!settingsTable)
            {
                return false;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var hideOnMinimap = config.HideOnMinimap;
            if (OmniControls.Checkbox(
                    OmniLoc.Get(this, "Feature.HideMinimapIcons.HideOnMinimap"),
                    ref hideOnMinimap))
            {
                config.HideOnMinimap = hideOnMinimap;
                changed = true;
                mapSettingsChanged = true;
            }

            ImGui.TableNextColumn();
            var hideOnAreaMap = config.HideOnAreaMap;
            if (OmniControls.Checkbox(
                    OmniLoc.Get(this, "Feature.HideMinimapIcons.HideOnAreaMap"),
                    ref hideOnAreaMap))
            {
                config.HideOnAreaMap = hideOnAreaMap;
                changed = true;
                mapSettingsChanged = true;
            }

            ImGui.TableNextColumn();
            var iconScaleLabel = OmniLoc.Get(this, "Feature.HideMinimapIcons.IconScale");
            var iconScale = Math.Clamp(config.IconScale, 0f, 3f);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(iconScaleLabel);
            OmniControls.SameLineOrWrap(OmniControls.MeasureFloatInput(iconScale, "%.1f").X);
            if (OmniControls.SliderFloat(
                    "##hideMinimapIconsIconScale",
                    ref iconScale,
                    0f,
                    3f,
                    "%.1f",
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
            {
                config.IconScale = iconScale;
                mapSettingsChanged = true;
            }
            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }

        {
            var aetherytesOnTopLabel = OmniLoc.Get(this, "Feature.HideMinimapIcons.AetherytesOnTop");
            var keepTextLabel = OmniLoc.Get(this, "Feature.HideMinimapIcons.KeepTextWhenHidden");
            using var settingsTable = OmniControls.SettingsTable(
                "##hideMinimapIconsMarkerSettings",
                [OmniControls.MeasureGroup([OmniControls.MeasureCheckbox(aetherytesOnTopLabel), OmniControls.HelpIconSize()]),
                    OmniControls.MeasureGroup([OmniControls.MeasureCheckbox(keepTextLabel), OmniControls.HelpIconSize()])],
                ["##hideMinimapIconsAetherytesOnTop", "##hideMinimapIconsKeepText"],
                flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 4);
            if (!settingsTable)
                return changed;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (OmniControls.Checkbox(aetherytesOnTopLabel, ref config.AetherytesOnTop))
            {
                changed = true;
                mapSettingsChanged = true;
            }
            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
            OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.HideMinimapIcons.AetherytesOnTopHint"));

            ImGui.TableNextColumn();
            if (OmniControls.Checkbox(keepTextLabel, ref config.KeepTextWhenHidden))
            {
                changed = true;
                mapSettingsChanged = true;
            }
            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
            OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.HideMinimapIcons.KeepTextWhenHiddenHint"));
        }

        if (changed && IsEnabled)
            ArmMapDiagnostics("SettingsChanged");
        if (mapSettingsChanged && IsEnabled)
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
                rowHeight * Math.Min(rowCount, MAX_VISIBLE_ROWS)));
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
            OmniControls.HelpTooltip(string.Format(OmniLoc.Get(this, "Feature.HideMinimapIcons.IconTooltip"), iconID));
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SetHidden(iconID, !hidden);
            changed = true;
        }

        return changed;
    }

    private void OnMapPreDraw(AddonEvent _, AddonArgs args)
    {
        var capture = BeginMapDiagnosticSample(args);
        if (capture)
            LogMapDiagnosticSnapshot(args, "PreDraw.Before");

        switch (args.AddonName)
        {
            case NAVI_MAP_ADDON_NAME:
                ApplyMinimap((AddonNaviMap*)args.Addon.Address, false);
                break;
            case AREA_MAP_ADDON_NAME:
                ApplyAreaMap((AddonAreaMap*)args.Addon.Address, false);
                break;
            case TELEPORT_TOWN_ADDON_NAME:
                ApplyTeleportTown((AddonTeleportTown*)args.Addon.Address, false);
                break;
        }

        if (capture)
            LogMapDiagnosticSnapshot(args, "PreDraw.After");
    }

    private void OnMapPreUpdate(AddonEvent _, AddonArgs args)
    {
        var diagnostic = GetMapDiagnosticState(args);
        if (!diagnostic.ReportedPreUpdate)
        {
            diagnostic.ReportedPreUpdate = true;
            LogMapDiagnosticSnapshot(args, "PreUpdate.First");
        }
        var capture = diagnostic.PendingRestore;
        if (capture)
            LogMapDiagnosticSnapshot(args, "PreUpdate.BeforeRestore");
        RestoreHiddenMapNodes((AtkUnitBase*)args.Addon.Address, args.AddonName);
        if (capture)
        {
            diagnostic.PendingRestore = false;
            LogMapDiagnosticSnapshot(args, "PreUpdate.AfterRestore");
        }
    }

    private void OnMapFinalize(AddonEvent _, AddonArgs args)
    {
        var addon = args.Addon.Address;
        mapDiagnostics.Remove(args.AddonName);
        ClearMarkerLayers(addon);
        foreach (var address in hiddenNodes.Where(entry => entry.Value.Addon == addon).Select(static entry => entry.Key).ToArray())
            hiddenNodes.Remove(address);
        foreach (var address in originalIconScales.Where(entry => entry.Value.Addon == addon).Select(static entry => entry.Key).ToArray())
            originalIconScales.Remove(address);
    }

    private void ApplyCurrentMaps(bool restore)
    {
        var unitManager = RaptureAtkUnitManager.Instance();
        if (unitManager != null)
        {
            ApplyMinimap((AddonNaviMap*)unitManager->GetAddonByName(NAVI_MAP_ADDON_NAME), restore);
            ApplyAreaMap((AddonAreaMap*)unitManager->GetAddonByName(AREA_MAP_ADDON_NAME), restore);
            ApplyTeleportTown((AddonTeleportTown*)unitManager->GetAddonByName(TELEPORT_TOWN_ADDON_NAME), restore);
        }
    }

    private void ApplyMinimap(AddonNaviMap* addon, bool restore)
    {
        if (addon == null || !restore && !addon->AtkUnitBase.IsVisible)
        {
            return;
        }

        RestoreHiddenMapNodes(&addon->AtkUnitBase, NAVI_MAP_ADDON_NAME);
        for (var index = 0; index < addon->NaviMap.NaviMapMarkers.Length; index++)
        {
            ref var marker = ref addon->NaviMap.NaviMapMarkers[index];
            if (marker.ComponentNode == null)
            {
                continue;
            }

            ApplyMarkerNode(
                marker.ComponentNode,
                config.HiddenIconIDs.Contains(marker.IconId) || config.HiddenIconIDs.Contains(marker.SecondaryIconId),
                restore,
                config.HideOnMinimap,
                (nint)addon);
        }

        ApplyAetherytePriority(&addon->AtkUnitBase, NAVI_MAP_ADDON_NAME, restore);
    }

    private void ApplyAreaMap(AddonAreaMap* addon, bool restore)
    {
        if (addon == null || !restore && !addon->AtkUnitBase.IsVisible)
        {
            return;
        }

        RestoreHiddenMapNodes(&addon->AtkUnitBase, AREA_MAP_ADDON_NAME);
        ApplyAreaMapMarkers(addon->ComponentMap, restore, (nint)addon);
        ApplyAetherytePriority(&addon->AtkUnitBase, AREA_MAP_ADDON_NAME, restore);
    }

    private void ApplyTeleportTown(AddonTeleportTown* addon, bool restore)
    {
        if (addon == null || !restore && !addon->AtkUnitBase.IsVisible)
        {
            return;
        }

        RestoreHiddenMapNodes(&addon->AtkUnitBase, TELEPORT_TOWN_ADDON_NAME);
        ApplyAreaMapMarkers(addon->ComponentMap, restore, (nint)addon);
        ApplyAetherytePriority(&addon->AtkUnitBase, TELEPORT_TOWN_ADDON_NAME, restore);
    }

    private void ApplyAreaMapMarkers(AtkComponentMap* componentMap, bool restore, nint addon)
    {
        var component = (AtkComponentBase*)componentMap;
        if (component == null || component->UldManager.NodeList == null)
        {
            return;
        }

        for (var index = 6; index < component->UldManager.NodeListCount; index++)
        {
            var node = component->UldManager.NodeList[index];
            if (node == null || node->GetNodeType() != NodeType.Component)
                continue;

            ApplyMarkerNode((AtkComponentNode*)node, false, restore, config.HideOnAreaMap, addon);
        }
    }

    private void ApplyMarkerNode(
        AtkComponentNode* node,
        bool selected,
        bool restore,
        bool hideEnabled,
        nint addon)
    {
        restore |= (node->AtkResNode.NodeFlags & NodeFlags.Visible) == 0;
        var component = node->Component;
        if (component == null || component->UldManager.NodeList == null)
            return;

        if (!restore && hideEnabled && !selected)
            selected = HasSelectedMarkerIcon(component, 0);
        var hidden = !restore && hideEnabled && selected;
        ApplyMarkerContents(component, hidden, restore, addon, 0);
    }

    private bool HasSelectedMarkerIcon(AtkComponentBase* component, int depth)
    {
        if (component is null || component->UldManager.NodeList is null || depth >= MAX_MARKER_DEPTH)
            return false;

        ref var manager = ref component->UldManager;
        for (var index = 0; index < manager.NodeListCount; index++)
        {
            var node = manager.NodeList[index];
            if (node is null)
                continue;

            if (node->Type == NodeType.Image && IsMarkerNodeVisible(node) &&
                config.HiddenIconIDs.Contains(NativeImageIcon.GetIconID((AtkImageNode*)node)))
                return true;
            if (node->GetNodeType() == NodeType.Component &&
                HasSelectedMarkerIcon(((AtkComponentNode*)node)->Component, depth + 1))
                return true;
        }
        return false;
    }

    private static bool IsMarkerNodeVisible(AtkResNode* node)
    {
        for (var depth = 0; node is not null && depth < MAX_MARKER_DEPTH; depth++, node = node->ParentNode)
        {
            if ((node->NodeFlags & NodeFlags.Visible) == 0 || node->Color.A == 0 || node->IsDrawDisabled)
                return false;
        }
        return node is null;
    }

    private void ApplyMarkerContents(AtkComponentBase* component, bool hidden, bool restore, nint addon, int depth)
    {
        if (component is null || component->UldManager.NodeList is null || depth >= MAX_MARKER_DEPTH)
            return;

        ref var manager = ref component->UldManager;
        for (var index = 0; index < manager.NodeListCount; index++)
        {
            var node = manager.NodeList[index];
            if (node is null)
                continue;

            if (node->GetNodeType() == NodeType.Component)
            {
                var child = ((AtkComponentNode*)node)->Component;
                if (child is not null && child->GetComponentType() == ComponentType.TextNineGrid &&
                    (!hidden || config.KeepTextWhenHidden))
                    continue;
                ApplyMarkerContents(child, hidden, restore, addon, depth + 1);
                continue;
            }

            if (node->Type == NodeType.Image)
            {
                var image = (AtkImageNode*)node;
                ApplyIconScale(image, NativeImageIcon.GetIconID(image), restore, hidden, addon);
            }
            if (hidden && (node->Type == NodeType.Image ||
                           !config.KeepTextWhenHidden && (node->Type is NodeType.Text or NodeType.NineGrid or NodeType.Collision)))
                HideMarkerNode(node, addon);
        }
    }

    private void ApplyIconScale(AtkImageNode* imageNode, uint iconID, bool restore, bool hidden, nint addon)
    {
        var imageAddress = (nint)imageNode;
        var iconScale = hidden ? 0f : Math.Clamp(config.IconScale, 0f, 3f);
        if (restore || (!hidden && iconID == 0) || Math.Abs(iconScale - 1f) < 0.001f)
        {
            if (originalIconScales.Remove(imageAddress, out var originalScale) &&
                originalScale.NodeID == imageNode->AtkResNode.NodeId && originalScale.Addon == addon)
            {
                imageNode->AtkResNode.SetScale(originalScale.Scale.X, originalScale.Scale.Y);
            }

            return;
        }

        var hasOriginalScale = originalIconScales.TryGetValue(imageAddress, out var iconNodeScale);
        if (hasOriginalScale &&
            (iconNodeScale.NodeID != imageNode->AtkResNode.NodeId || iconNodeScale.Addon != addon))
        {
            originalIconScales.Remove(imageAddress);
            hasOriginalScale = false;
        }
        if (!hasOriginalScale || iconNodeScale.IconID != iconID)
        {
            if (hasOriginalScale)
            {
                imageNode->AtkResNode.SetScale(iconNodeScale.Scale.X, iconNodeScale.Scale.Y);
            }

            iconNodeScale = (imageNode->AtkResNode.NodeId, iconID, new Vector2(
                imageNode->AtkResNode.GetScaleX(),
                imageNode->AtkResNode.GetScaleY()), addon);
            originalIconScales[imageAddress] = iconNodeScale;
        }

        imageNode->AtkResNode.SetScale(
            iconNodeScale.Scale.X * iconScale,
            iconNodeScale.Scale.Y * iconScale);
    }

    private void HideMarkerNode(AtkResNode* node, nint addon)
    {
        if ((node->NodeFlags & NodeFlags.Visible) == 0 || node->IsDrawDisabled)
            return;

        hiddenNodes[(nint)node] = (node->NodeId, addon);
        node->IsDrawDisabled = true;
    }

    private void RestoreHiddenMapNodes(AtkUnitBase* addon, string addonName)
    {
        if (addon is null || hiddenNodes.Count == 0)
            return;

        if (addonName == NAVI_MAP_ADDON_NAME)
        {
            foreach (ref var marker in ((AddonNaviMap*)addon)->NaviMap.NaviMapMarkers)
                RestoreHiddenMarkerNodes(marker.ComponentNode, (nint)addon, 0);
            return;
        }

        var map = addonName == AREA_MAP_ADDON_NAME
            ? ((AddonAreaMap*)addon)->ComponentMap
            : ((AddonTeleportTown*)addon)->ComponentMap;
        if (map is null || map->UldManager.NodeList is null)
            return;

        for (var index = 6; index < map->UldManager.NodeListCount; index++)
        {
            var node = map->UldManager.NodeList[index];
            if (node is not null && node->GetNodeType() == NodeType.Component)
                RestoreHiddenMarkerNodes((AtkComponentNode*)node, (nint)addon, 0);
        }
    }

    private void RestoreHiddenMarkerNodes(AtkComponentNode* marker, nint addon, int depth)
    {
        if (marker is null || marker->Component is null || marker->Component->UldManager.NodeList is null ||
            depth >= MAX_MARKER_DEPTH)
            return;

        ref var manager = ref marker->Component->UldManager;
        for (var index = 0; index < manager.NodeListCount; index++)
        {
            var node = manager.NodeList[index];
            if (node is null)
                continue;

            var address = (nint)node;
            if (hiddenNodes.TryGetValue(address, out var original) && original.Addon == addon)
            {
                hiddenNodes.Remove(address);
                if (original.NodeID == node->NodeId)
                    node->IsDrawDisabled = false;
            }
            if (node->GetNodeType() == NodeType.Component)
                RestoreHiddenMarkerNodes((AtkComponentNode*)node, addon, depth + 1);
        }
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

        ArmMapDiagnostics($"IconSelection:{iconID}:{hidden}");
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

    #region 常量

    private const string NAVI_MAP_ADDON_NAME = "_NaviMap";

    private const string AREA_MAP_ADDON_NAME = "AreaMap";

    private const string TELEPORT_TOWN_ADDON_NAME = "TelepotTown";

    private const int MAX_VISIBLE_ROWS = 6;

    private const int MAX_MARKER_DEPTH = 32;

    #endregion

    public sealed class Settings
    {
        public bool AetherytesOnTop = true;

        public bool KeepTextWhenHidden = true;

        public bool HideOnMinimap { get; set; } = true;

        public bool HideOnAreaMap { get; set; } = true;

        public float IconScale { get; set; } = 1f;

        public HashSet<uint> HiddenIconIDs { get; set; } = [];
    }
}
