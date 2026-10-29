using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using OmenTools;
using OmenTools.Extensions;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class MoreMinimapInformation
{
    private void OnMinimapPreUpdate
    (
        AddonEvent _,
        AddonArgs  args
    ) =>
        RestoreNativeEnemyMarkers((AddonNaviMap*)args.Addon.Address);

    private void OnMinimapUpdate
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        var addon = (AddonNaviMap*)args.Addon.Address;
        var agentMap = AgentMap.Instance();
        if (addon is null || agentMap is null || addon->NaviMap.PlayerPin is null)
            return;

        if (minimapRoot is null)
        {
            minimapRoot = new ResNode();
            minimapRoot.AttachNode(addon->NaviMap.PlayerPin, NodePosition.BeforeTarget);
        }

        ref var map = ref addon->NaviMap;
        var halfSize = new Vector2(map.Width, map.Height);
        var scaling = map.MarkerPositionScaling * map.MarkerRadiusScale;
        minimapRoot.IsVisible = addon->IsVisible && halfSize.X > 0f && halfSize.Y > 0f &&
                                float.IsFinite(scaling) && scaling > 0f;
        if (!minimapRoot.IsVisible)
            return;

        minimapRoot.Size = halfSize * 2f;
        var center = new Vector2(map.X, map.Y);
        var rotation = !map.NorthLockedUp && map.CompassFrame is not null ? map.CompassFrame->Rotation : 0f;
        var transform = Matrix3x2.CreateRotation(rotation);
        var circular = addon->MainCollision is null || addon->MainCollision->IsEllipticalCollision;
        foreach (var marker in objectMarkers.Values)
            UpdateMinimapIcon(marker, agentMap->CurrentMapId, halfSize, center, scaling, transform, circular);
        foreach (var marker in aetherCurrentMarkers)
            UpdateMinimapIcon(marker, agentMap->CurrentMapId, halfSize, center, scaling, transform, circular);

        var services = DService.Instance();
        if (config.Enemies && services.ClientState.IsLoggedIn && !services.ClientState.IsPvP && !services.Condition.IsBetweenAreas)
        {
            foreach (ref var marker in map.NaviMapMarkers)
            {
                var node = marker.ComponentNode;
                if (marker.IconId != ENEMY_ICON_ID || node is null || !node->AtkResNode.IsVisible() || node->AtkResNode.Color.A == 0)
                    continue;

                hiddenEnemyNodes.TryAdd((nint)node, node->AtkResNode.Color.A);
                node->AtkResNode.Color.A = 0;
            }
        }

        if (mapController?.HideKey is { } key && services.KeyState[key])
            minimapRoot.IsVisible = false;
    }

    private void RestoreNativeEnemyMarkers(AddonNaviMap* addon)
    {
        if (addon is not null && hiddenEnemyNodes.Count > 0)
        {
            foreach (ref var marker in addon->NaviMap.NaviMapMarkers)
            {
                var node = marker.ComponentNode;
                if (node is not null && marker.IconId == ENEMY_ICON_ID &&
                    hiddenEnemyNodes.Remove((nint)node, out var alpha) && node->AtkResNode.Color.A == 0)
                    node->AtkResNode.Color.A = alpha;
            }
        }
        hiddenEnemyNodes.Clear();
    }

    private void OnMinimapFinalize
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        hiddenEnemyNodes.Clear();
        minimapRoot = null;
        foreach (var marker in objectMarkers.Values)
            marker.MinimapIcon = null;
        foreach (var marker in aetherCurrentMarkers)
            marker.MinimapIcon = null;
    }

    private void UpdateMinimapIcon
    (
        Marker    marker,
        uint      mapID,
        Vector2   halfSize,
        Vector2   center,
        float     scaling,
        Matrix3x2 transform,
        bool      circular
    )
    {
        if (!marker.IsVisible || marker.MapId != mapID)
        {
            if (marker.MinimapIcon is { } hiddenIcon)
                hiddenIcon.IsVisible = false;
            return;
        }

        var offset = Vector2.TransformNormal((marker.Position - center) * scaling, transform);
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
        {
            if (marker.MinimapIcon is { } invalidIcon)
                invalidIcon.IsVisible = false;
            return;
        }

        if (marker.MinimapIcon is null)
        {
            if (marker.IsPlayer)
            {
                if (playerMarkerTexture is null)
                    return;

                var circle = new ImGuiImageNode
                {
                    FitTexture = true,
                    TextureSize = playerMarkerTexture.Size
                };
                marker.MinimapIcon = circle;
                circle.LoadTexture(playerMarkerTexture.CreateWrapSharingLowLevelResource());
            }
            else
            {
                marker.MinimapIcon = new IconImageNode
                {
                    FitTexture = true
                };
            }
            marker.MinimapIcon.AttachNode(minimapRoot);
        }

        var icon = marker.MinimapIcon;
        if (icon is IconImageNode gameIcon)
            gameIcon.IconId = marker.IconId.GetValueOrDefault();
        icon.Size          = marker.Size;
        icon.MultiplyColor = marker.MultiplyColor;
        var limits = Vector2.Max(Vector2.One, halfSize - icon.Size / 2f);
        var normalized = offset / limits;
        var edgeScale = circular ? normalized.Length() : MathF.Max(MathF.Abs(normalized.X), MathF.Abs(normalized.Y));
        offset /= MathF.Max(1f, edgeScale);
        icon.Position  = halfSize + offset - icon.Size / 2f;
        icon.IsVisible = true;
    }
}
