using System.Drawing;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using KamiToolKit.MapOverlay;
using KamiToolKit.Nodes.Simplified;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using BattleNpcSubKind = Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind;
using ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class MoreMinimapInformation
{
    private void UpdateMarkers(IFramework framework)
    {
        mapController?.Update();
        foreach (var marker in objectMarkers.Values)
            marker.IsVisible = false;

        var services = DService.Instance();
        var canShow = services.ClientState.IsLoggedIn && !services.ClientState.IsPvP && !services.Condition.IsBetweenAreas;
        foreach (var marker in aetherCurrentMarkers)
            marker.IsVisible = canShow && config.AetherCurrents;

        var manager = GameObjectManager.Instance();
        var agentMap = AgentMap.Instance();
        if (!canShow || manager is null || agentMap is null || mapController is null)
            return;

        var objects = manager->Objects.IndexSorted;
        var localPlayer = objects[0].Value;
        if (localPlayer is null)
            return;

        enemyHaters.Clear();
        var uiState = UIState.Instance();
        if (config.Enemies && uiState is not null)
        {
            ref var hater = ref uiState->Hater;
            for (var index = 0; index < Math.Clamp(hater.HaterCount, 0, hater.Haters.Length); index++)
                enemyHaters.Add(hater.Haters[index].EntityId);
        }

        for (var index = 1; index < objects.Length; index++)
        {
            var obj = objects[index].Value;
            if (obj is null || !obj->GetIsTargetable())
                continue;

            if (!TryGetAppearance(obj, out var iconID, out var color, out var size))
                continue;

            var isPlayer = obj->ObjectKind == ObjectKind.Pc;
            objectMarkers.TryGetValue(index, out var marker);
            if (marker is not null && marker.IsPlayer != isPlayer)
            {
                marker.MinimapIcon?.Dispose();
                mapController.RemoveMarker(marker);
                objectMarkers.Remove(index);
                marker = null;
            }
            if (marker is null)
            {
                marker = new Marker
                {
                    IsPlayer = isPlayer
                };
                objectMarkers.Add(index, marker);
                mapController.AddMarker(marker);
                if (isPlayer && playerMarkerTexture is not null)
                    marker.Texture = playerMarkerTexture.CreateWrapSharingLowLevelResource();
            }

            var objectID = obj->GetGameObjectId();
            if (marker.ObjectID != objectID || marker.MapId != agentMap->CurrentMapId)
            {
                marker.ObjectID = objectID;
                marker.TextTooltip = obj->NameString;
            }

            marker.MapId = agentMap->CurrentMapId;
            marker.Position = new Vector2(obj->Position.X, obj->Position.Z);
            marker.Size = new Vector2(size);
            if (!isPlayer && marker.IconId != iconID)
                marker.IconId = iconID;
            marker.MultiplyColor = color.ToVector4().ToVector3();
            marker.IsVisible = true;
        }
    }

    private bool TryGetAppearance(GameObject* obj, out uint iconID, out KnownColor color, out float size)
    {
        iconID = 0;
        color = KnownColor.White;
        size = 16f;
        switch (obj->ObjectKind)
        {
            case ObjectKind.Pc:
                var player = (Character*)obj;
                if (player->IsPartyMember || player->IsAllianceMember)
                    return false;
                if (player->IsFriend)
                {
                    color = KnownColor.Orange;
                    return config.Friends;
                }
                color = KnownColor.Gray;
                return config.Players;

            case ObjectKind.BattleNpc when config.Enemies && obj->SubKind == (byte)BattleNpcSubKind.Combatant:
                iconID = enemyHaters.Contains(obj->EntityId) ? ENEMY_ICON_ID : 60403;
                size = 48f;
                return !obj->IsDead();

            case ObjectKind.GatheringPoint when config.GatheringPoints:
                if (!gatheringIcons.TryGetValue(obj->BaseId, out iconID))
                {
                    iconID = 0;
                    if (LuminaGetter.TryGetRow<GatheringPoint>(obj->BaseId, out var point) &&
                        point.GatheringPointBase.IsValid && point.GatheringPointBase.Value.GatheringType.IsValid)
                        iconID = (uint)point.GatheringPointBase.Value.GatheringType.Value.IconMain;
                    gatheringIcons.Add(obj->BaseId, iconID);
                }
                size = 24f;
                return iconID != 0;

            case ObjectKind.Treasure when config.TreasureChests:
                iconID = 60003;
                size = 24f;
                return true;

            default:
                return false;
        }
    }

    private void LoadAetherCurrents()
    {
        if (mapController is null)
            return;

        var currents = LuminaGetter.Get<AetherCurrentCompFlgSet>()
            .SelectMany(set => set.AetherCurrents)
            .Where(current => current.IsValid && !current.Value.Quest.IsValid)
            .Select(current => current.RowId).ToHashSet();
        var objects = LuminaGetter.Get<EObj>()
            .Where(obj => currents.Contains(obj.Data.RowId))
            .ToDictionary(obj => obj.RowId, obj => obj.Data.RowId);
        var added = new HashSet<(uint CurrentID, uint MapID)>();
        foreach (var level in LuminaGetter.Get<Level>())
        {
            if (!level.Map.IsValid || !objects.TryGetValue(level.Object.RowId, out var currentID) ||
                !added.Add((currentID, level.Map.RowId)))
                continue;

            var marker = new Marker
            {
                MapId = level.Map.RowId,
                Position = new Vector2(level.X, level.Z),
                IconId = 60653,
                Size = new Vector2(24f),
                TextTooltip = OmniLoc.Get(this, "Feature.MoreMinimapInformation.AetherCurrents"),
                IsVisible = false
            };
            aetherCurrentMarkers.Add(marker);
            mapController.AddMarker(marker);
        }
    }

    private static IDalamudTextureWrap CreatePlayerMarkerTexture()
    {
        const int TEXTURE_SIZE = 64;
        var pixels = new byte[TEXTURE_SIZE * TEXTURE_SIZE * 4];
        for (var y = 0; y < TEXTURE_SIZE; y++)
        {
            for (var x = 0; x < TEXTURE_SIZE; x++)
            {
                var distance = new Vector2(x + 0.5f - TEXTURE_SIZE / 2f, y + 0.5f - TEXTURE_SIZE / 2f).Length() / 4f;
                var fill = Math.Clamp((5f - distance) * 4f + 0.5f, 0f, 1f);
                var shade = (byte)MathF.Round(255f * (0.7f + 0.3f * fill));
                var offset = (y * TEXTURE_SIZE + x) * 4;
                pixels[offset] = shade;
                pixels[offset + 1] = shade;
                pixels[offset + 2] = shade;
                pixels[offset + 3] = (byte)MathF.Round(255f * Math.Clamp((7f - distance) * 4f + 0.5f, 0f, 1f));
            }
        }
        return DService.Instance().Texture.CreateFromRaw(RawImageSpecification.Rgba32(TEXTURE_SIZE, TEXTURE_SIZE), pixels,
            "MoreMinimapInformation.PlayerCircle");
    }

    private sealed class Marker : MapMarkerNode
    {
        public bool IsPlayer;
        public GameObjectId ObjectID;
        public SimpleImageNode? MinimapIcon;
    }
}
