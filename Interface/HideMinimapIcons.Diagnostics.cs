using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.OmenService;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class HideMinimapIcons
{
    private readonly Dictionary<string, MapIconDiagnosticState> mapDiagnostics     = [];
    private int                                                 diagnosticSampleID;

    private void ArmMapDiagnostics(string reason)
    {
        mapDiagnostics.Clear();
        DalamudServices.PluginLog.Information(
            "[HideMinimapIcons] Arm={Reason} Minimap={Minimap} AreaMap={AreaMap} KeepText={KeepText} Scale={Scale} OnTop={OnTop} SelectedCount={SelectedCount} Selected=[{Selected}]",
            reason, config.HideOnMinimap, config.HideOnAreaMap, config.KeepTextWhenHidden, config.IconScale,
            config.AetherytesOnTop, config.HiddenIconIDs.Count, string.Join(",", config.HiddenIconIDs.Order().Take(64)));
    }

    private void OnMapDiagnosticSetup
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        mapDiagnostics.Remove(args.AddonName);
        DalamudServices.PluginLog.Information("[HideMinimapIcons] Setup Addon={Addon} Address=0x{Address:X}",
            args.AddonName, args.Addon.Address);
    }

    private bool BeginMapDiagnosticSample(AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon is null || !addon->IsVisible)
            return false;

        var diagnostic = GetMapDiagnosticState(args);
        var now = Environment.TickCount64;
        if (diagnostic.LastPreDraw != 0 && now - diagnostic.LastPreDraw > 1_000)
        {
            diagnostic.RemainingSamples = 3;
            diagnostic.NextSample       = 0;
        }
        diagnostic.LastPreDraw = now;
        if (diagnostic.RemainingSamples == 0 || now < diagnostic.NextSample)
            return false;

        diagnostic.RemainingSamples--;
        diagnostic.NextSample      = now + 3_000;
        diagnostic.SampleID        = ++diagnosticSampleID;
        diagnostic.PendingPostDraw = true;
        diagnostic.PendingRestore  = true;
        return true;
    }

    private MapIconDiagnosticState GetMapDiagnosticState(AddonArgs args)
    {
        if (mapDiagnostics.TryGetValue(args.AddonName, out var diagnostic) && diagnostic.Addon == args.Addon.Address)
            return diagnostic;

        diagnostic                     = new MapIconDiagnosticState
        {
            Addon = args.Addon.Address
        };
        mapDiagnostics[args.AddonName] = diagnostic;
        return diagnostic;
    }

    private void OnMapDiagnosticPostDraw
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        if (!mapDiagnostics.TryGetValue(args.AddonName, out var diagnostic) ||
            diagnostic.Addon != args.Addon.Address || !diagnostic.PendingPostDraw)
            return;

        diagnostic.PendingPostDraw = false;
        LogMapDiagnosticSnapshot(args, "PostDraw");
    }

    private void LogMapDiagnosticSnapshot
    (
        AddonArgs args,
        string    phase
    )
    {
        if (!mapDiagnostics.TryGetValue(args.AddonName, out var diagnostic))
            return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon is null)
            return;

        var markerCount = 0;
        var imageCount = 0;
        var selectedImages = 0;
        var eligibleSelectedImages = 0;
        var disabledImages = 0;
        var nodeCount = 0;
        var detailCount = 0;
        var iconCounts = new Dictionary<uint, int>();
        var layout = new StringBuilder();
        var details = new StringBuilder();
        var contents = new StringBuilder();
        var nativeState = string.Empty;
        var mapAgent = AgentMap.Instance();
        var selectedMapID = mapAgent is null ? 0 : mapAgent->SelectedMapId;
        if (mapAgent is not null)
        {
            var nativeIcons = new StringBuilder();
            var nativeIconCount = Math.Min(32, Math.Min((int)mapAgent->MapMarkerCount, mapAgent->MapMarkers.Length));
            for (var index = 0; index < nativeIconCount; index++)
            {
                nativeIcons.Append($"{index}:{mapAgent->MapMarkers[index].MapMarker.IconId} ");
            }
            nativeState = $"markerCount={mapAgent->MapMarkerCount} icons=[{nativeIcons}] ctrl={mapAgent->IsControlKeyPressed}";
        }

        if (args.AddonName == NAVI_MAP_ADDON_NAME)
        {
            var minimap = (AddonNaviMap*)addon;
            for (var index = 0; index < minimap->NaviMap.NaviMapMarkers.Length; index++)
            {
                ref var marker = ref minimap->NaviMap.NaviMapMarkers[index];
                CollectMarker(marker.ComponentNode, $"index={index} nativeIcons={marker.IconId}/{marker.SecondaryIconId}");
            }
        }
        else
        {
            var map = args.AddonName == AREA_MAP_ADDON_NAME
                ? ((AddonAreaMap*)addon)->ComponentMap
                : ((AddonTeleportTown*)addon)->ComponentMap;
            if (args.AddonName == AREA_MAP_ADDON_NAME)
            {
                var areaMap = (AddonAreaMap*)addon;
                nativeState += $" showMarkers={areaMap->ShowMapMarkersCheckbox is not null && areaMap->ShowMapMarkersCheckbox->IsChecked}" +
                              $" showText={areaMap->ShowMapTextCheckbox is not null && areaMap->ShowMapTextCheckbox->IsChecked}";
            }
            if (map is not null && map->UldManager.NodeList is not null)
            {
                layout.Append($"count={map->UldManager.NodeListCount} ");
                for (var index = 0; index < Math.Min((int)map->UldManager.NodeListCount, 1_024); index++)
                {
                    var node = map->UldManager.NodeList[index];
                    if (node is null)
                        continue;
                    var type = node->GetNodeType();
                    if (index < 16)
                        layout.Append($"{index}:{node->NodeId}:{(ushort)node->Type}/{type} ");
                    if (index >= 6 && type == NodeType.Component)
                        CollectMarker((AtkComponentNode*)node, $"index={index}");
                }
            }
        }

        var iconSummary = string.Join(",", iconCounts.OrderBy(static entry => entry.Key).Take(64)
            .Select(static entry => $"{entry.Key}x{entry.Value}"));
        DalamudServices.PluginLog.Information(
            "[HideMinimapIcons] Sample={Sample} Addon={Addon} Phase={Phase} Visible={Visible} Map={Map} KeepText={KeepText} Scale={Scale} Markers={Markers} Nodes={Nodes} Images={Images} SelectedImages={SelectedImages} EligibleSelected={EligibleSelected} DisabledImages={DisabledImages} Owned={Owned} Truncated={Truncated} Icons=[{Icons}] Layout=[{Layout}] Native=[{Native}] Details=[{Details}]",
            diagnostic.SampleID, args.AddonName, phase, addon->IsVisible, selectedMapID, config.KeepTextWhenHidden,
            config.IconScale, markerCount, nodeCount, imageCount, selectedImages, eligibleSelectedImages,
            disabledImages, hiddenNodes.Count(entry => entry.Value.Addon == args.Addon.Address), nodeCount >= 2_048, iconSummary,
            layout.ToString(), nativeState, details.ToString());
        return;

        void CollectMarker
        (
            AtkComponentNode* marker,
            string            source
        )
        {
            if (marker is null || marker->Component is null || nodeCount >= 2_048)
                return;

            markerCount++;
            var previousSelected = selectedImages;
            contents.Clear();
            CollectContents(marker->Component, 0);
            if (detailCount >= 6 || !marker->AtkResNode.IsVisible() ||
                detailCount >= 2 && selectedImages == previousSelected)
                return;

            detailCount++;
            details.Append($"{{{source} marker=0x{(nint)marker:X}/{marker->AtkResNode.NodeId} ");
            var parent = &marker->AtkResNode;
            for (var depth = 0; parent is not null && depth < 8; depth++, parent = parent->ParentNode)
                details.Append($"parent{depth}={parent->NodeId}/flags:{(ushort)parent->NodeFlags:X}/draw:{parent->DrawFlags:X}/a:{parent->Color.A}/{parent->Alpha_2} ");
            details.Append(contents).Append("} ");
        }

        void CollectContents
        (
            AtkComponentBase* component,
            int               depth
        )
        {
            if (component is null || component->UldManager.NodeList is null || depth >= 8)
                return;

            for (var index = 0; index < component->UldManager.NodeListCount && nodeCount < 2_048; index++)
            {
                var node = component->UldManager.NodeList[index];
                if (node is null)
                    continue;
                nodeCount++;
                if (node->GetNodeType() == NodeType.Component)
                {
                    CollectContents(((AtkComponentNode*)node)->Component, depth + 1);
                    continue;
                }
                if (node->Type == NodeType.Image)
                {
                    var image = (AtkImageNode*)node;
                    var iconID = NativeImageIcon.GetIconID(image);
                    imageCount++;
                    iconCounts[iconID] = iconCounts.GetValueOrDefault(iconID) + 1;
                    var selected = config.HiddenIconIDs.Contains(iconID);
                    var eligible = IsMarkerNodeVisible(node);
                    selectedImages += selected ? 1 : 0;
                    eligibleSelectedImages += selected && eligible ? 1 : 0;
                    disabledImages += node->IsDrawDisabled ? 1 : 0;
                    if (contents.Length < 1_200)
                    {
                        uint firstIconID = 0;
                        var parts = image->PartsList;
                        if (parts is not null && parts->Parts is not null && parts->PartCount > 0)
                        {
                            var asset = parts->Parts[0].UldAsset;
                            if (asset is not null && asset->AtkTexture.TextureType == TextureType.Resource && asset->AtkTexture.Resource is not null)
                                firstIconID = asset->AtkTexture.Resource->IconId;
                        }
                        contents.Append($"image=0x{(nint)node:X}/{node->NodeId}:icon={iconID}:first={firstIconID}:part={image->PartId}/{(parts is null ? 0 : parts->PartCount)}:selected={selected}:eligible={eligible}:disabled={node->IsDrawDisabled}:owned={hiddenNodes.ContainsKey((nint)node)}:flags={(ushort)node->NodeFlags:X}:draw={node->DrawFlags:X}:a={node->Color.A}/{node->Alpha_2}:scale={node->ScaleX:F2}/{node->ScaleY:F2} ");
                    }
                }
                else if (node->Type == NodeType.Text && contents.Length < 1_200)
                {
                    var text = (AtkTextNode*)node;
                    contents.Append($"text={node->NodeId}:len={text->NodeText.StringLength}:flags={(ushort)node->NodeFlags:X}:draw={node->DrawFlags:X}:a={node->Color.A}/{node->Alpha_2} ");
                }
            }
        }
    }

    private sealed class MapIconDiagnosticState
    {
        public nint Addon;
        public long LastPreDraw;
        public long NextSample;
        public int  RemainingSamples  = 3;
        public int  SampleID;
        public bool PendingPostDraw;
        public bool PendingRestore;
        public bool ReportedPreUpdate;
    }
}
