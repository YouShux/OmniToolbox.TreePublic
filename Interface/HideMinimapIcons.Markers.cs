using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Extensions;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class HideMinimapIcons
{
    private readonly Dictionary<nint, (uint NodeID, ushort Priority, ushort AppliedPriority, nint Addon)> originalMarkerLayers    = [];
    private readonly Dictionary<nint, ushort>                                                             markerGroupPriorities   = [];
    private readonly List<nint>                                                                           pendingAetheryteMarkers = [];
    private uint                                                                                          aetheryteIconID;
    private uint                                                                                          aethernetIconID;

    private void ClearMarkerLayers(nint addon)
    {
        foreach (var key in originalMarkerLayers.Where(entry => entry.Value.Addon == addon).Select(static entry => entry.Key).ToArray())
            originalMarkerLayers.Remove(key);
    }

    private void ApplyAetherytePriority
    (
        AtkUnitBase* addon,
        string       addonName,
        bool         restore
    )
    {
        if (addon is null || !addon->IsFullyLoaded())
            return;

        restore |= !config.AetherytesOnTop;
        if (restore && originalMarkerLayers.Count == 0)
            return;

        markerGroupPriorities.Clear();
        pendingAetheryteMarkers.Clear();
        if (addonName == NAVI_MAP_ADDON_NAME)
        {
            foreach (ref var marker in ((AddonNaviMap*)addon)->NaviMap.NaviMapMarkers)
                CollectMarkerLayer(
                    marker.ComponentNode,
                    IsAetheryteIcon(marker.IconId) || IsAetheryteIcon(marker.SecondaryIconId),
                    restore,
                    (nint)addon);
        }
        else
        {
            var map = addonName == AREA_MAP_ADDON_NAME
                ? ((AddonAreaMap*)addon)->ComponentMap
                : ((AddonTeleportTown*)addon)->ComponentMap;
            if (map is null || map->UldManager.NodeList is null)
                return;

            for (var index = 6; index < map->UldManager.NodeListCount; index++)
            {
                var node = map->UldManager.NodeList[index];
                if (node is not null && node->GetNodeType() == NodeType.Component)
                    CollectMarkerLayer((AtkComponentNode*)node, false, restore, (nint)addon);
            }
        }

        foreach (var address in pendingAetheryteMarkers)
        {
            var node = (AtkResNode*)address;
            var priority = (ushort)Math.Min(markerGroupPriorities[(nint)node->ParentNode] + 1, ushort.MaxValue);
            var nativePriority = GetNativeMarkerPriority(node, (nint)addon);
            originalMarkerLayers[address] = (node->NodeId, nativePriority, priority, (nint)addon);
            if (node->Priority != priority)
                node->SetPriority(priority);
        }
        markerGroupPriorities.Clear();
        pendingAetheryteMarkers.Clear();
    }

    private bool IsAetheryteIcon(uint iconID) =>
        iconID != 0 && (iconID == aetheryteIconID || iconID == aethernetIconID);

    private void CollectMarkerLayer
    (
        AtkComponentNode* marker,
        bool              aetheryte,
        bool              restore,
        nint              addon
    )
    {
        if (marker is null)
            return;

        if (!restore && marker->Component is not null && marker->Component->UldManager.NodeList is not null)
        {
            ref var manager = ref marker->Component->UldManager;
            for (var index = 0; index < manager.NodeListCount; index++)
            {
                var node = manager.NodeList[index];
                if (node is null || node->Type != NodeType.Image)
                    continue;

                aetheryte |= IsAetheryteIcon(NativeImageIcon.GetIconID((AtkImageNode*)node));
            }
        }

        var markerNode = &marker->AtkResNode;
        var visible = markerNode->IsVisible() && !markerNode->IsDrawDisabled && markerNode->Color.A != 0;
        if (restore || !aetheryte || !visible || markerNode->ParentNode is null)
            RestoreMarkerPriority(markerNode, addon);
        if (restore || !visible || markerNode->ParentNode is null)
            return;

        var parent = (nint)markerNode->ParentNode;
        var priority = GetNativeMarkerPriority(markerNode, addon);
        if (!markerGroupPriorities.TryGetValue(parent, out var groupPriority) || priority > groupPriority)
            markerGroupPriorities[parent] = priority;
        if (aetheryte)
            pendingAetheryteMarkers.Add((nint)markerNode);
    }

    private ushort GetNativeMarkerPriority
    (
        AtkResNode* node,
        nint        addon
    ) =>
        originalMarkerLayers.TryGetValue((nint)node, out var original) &&
        original.Addon == addon && original.NodeID == node->NodeId && node->Priority == original.AppliedPriority
            ? original.Priority
            : node->Priority;

    private void RestoreMarkerPriority
    (
        AtkResNode* node,
        nint        addon
    )
    {
        if (originalMarkerLayers.Remove((nint)node, out var original) &&
            original.Addon == addon && original.NodeID == node->NodeId && node->Priority == original.AppliedPriority)
        {
            node->SetPriority(original.Priority);
        }
    }
}
