using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Lumina.Excel.Sheets;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using GameMap = FFXIVClientStructs.FFXIV.Client.Game.UI.Map;
using TreasureObject = FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private static readonly HashSet<uint> QuestObjectiveIcons = [
        71003, 71004, 71005, 71006, 71023, 71024, 71025, 71026, 71043, 71044, 71045, 71046,
        71063, 71064, 71065, 71066, 71083, 71084, 71085, 71086, 71113, 71123, 71124, 71125,
        71126, 71143, 71144, 71145, 71146, 71203, 71223, 71225, 71243, 71244, 71245, 71263,
        71264, 71265, 71283, 71284, 71285, 71286, 71312, 71313, 71323, 71324, 71325, 71326,
        71343, 71344, 71345, 71346, 70997, 70998, 70999,
    ];

    private readonly List<ToolbarWorldMarker> worldMarkers = [];
    private readonly Dictionary<uint, Vector3> aetherCurrentPositions = [];
    private readonly Dictionary<uint, byte> huntRanks = [];
    private uint markerTerritory;
    private uint markerMap;
    private bool aetherCurrentsLoaded;
    private long nextMarkerRefresh;

    private void DrawMarkerPopup()
    {
        foreach (var type in Enum.GetValues<MultiToolbarWorldMarkerType>())
        {
            var enabled = config.ShowWorldMarkerOverlay && config.EnabledWorldMarkers.Contains(type);
            if (!ImGui.Checkbox(OmniLoc.Get($"Feature.MultiToolbar.Marker{type}"), ref enabled))
            {
                continue;
            }

            if (!config.ShowWorldMarkerOverlay)
            {
                config.EnabledWorldMarkers.Clear();
                config.ShowWorldMarkerOverlay = true;
            }
            if (enabled)
            {
                config.EnabledWorldMarkers.Add(type);
            }
            else
            {
                config.EnabledWorldMarkers.Remove(type);
            }
            nextMarkerRefresh = 0;
            saveConfig();
        }
    }

    private void DrawWorldMarkerOverlay()
    {
        var player = DService.Instance().ObjectTable.LocalPlayer;
        if (player == null || !config.ShowWorldMarkerOverlay ||
            !config.Widgets.Any(widget => widget.Enabled && widget.Type == MultiToolbarWidgetType.MarkerControl))
        {
            return;
        }

        if (markerTerritory != GameState.TerritoryType || markerMap != GameState.Map || Environment.TickCount64 >= nextMarkerRefresh)
        {
            RefreshWorldMarkers();
            nextMarkerRefresh = Environment.TickCount64 + 250;
        }

        var drawList = ImGui.GetForegroundDrawList();
        var iconSize = ScaleToolbar(30f);
        foreach (var marker in worldMarkers)
        {
            var distance = Vector3.Distance(player.Position, marker.Position);
            var opacity = Math.Clamp((distance - 8f) / 12f, 0f, 1f);
            if (opacity <= 0 || !DService.Instance().GameGUI.WorldToScreen(marker.Position, out var position))
            {
                continue;
            }

            var icon = ImageHelper.GetGameIcon(marker.IconID);
            if (icon != null)
            {
                drawList.AddImage(icon.Handle, position - new Vector2(iconSize * 0.5f),
                    position + new Vector2(iconSize * 0.5f), Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, opacity)));
            }

            var label = $"{marker.Label}  {distance:0}m";
            var textPosition = position + new Vector2(-ImGui.CalcTextSize(label).X * 0.5f, iconSize * 0.5f);
            drawList.AddText(textPosition + Vector2.One, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, opacity)), label);
            var textColor = ToolbarTheme.Text;
            textColor.W *= opacity;
            drawList.AddText(textPosition, ImGui.GetColorU32(textColor), label);
        }
    }

    private unsafe void RefreshWorldMarkers()
    {
        worldMarkers.Clear();
        if (markerTerritory != GameState.TerritoryType)
        {
            markerTerritory = GameState.TerritoryType;
            aetherCurrentPositions.Clear();
            aetherCurrentsLoaded = false;
        }
        markerMap = GameState.Map;
        var enabled = config.EnabledWorldMarkers;

        if (enabled.Contains(MultiToolbarWorldMarkerType.AetherCurrent))
        {
            RefreshAetherCurrentMarkers();
        }

        if (enabled.Contains(MultiToolbarWorldMarkerType.Flag) && IsFlagSet())
        {
            var flag = GetFlagMarker();
            if (flag.MapId == markerMap)
            {
                worldMarkers.Add(new(GetWorldMarkerGroundPosition(new Vector3(flag.XFloat, 0f, flag.YFloat)), flag.MapMarker.IconId,
                    OmniLoc.Get("Feature.MultiToolbar.MarkerFlag")));
            }
        }

        if (enabled.Contains(MultiToolbarWorldMarkerType.Quest))
        {
            var map = GameMap.Instance();
            if (map != null)
            {
                foreach (var quest in map->QuestMarkers)
                {
                    foreach (var marker in quest.MarkerData)
                    {
                        if (marker.MapId != markerMap ||
                            !(QuestObjectiveIcons.Contains(marker.IconId) || marker.IconId is >= 70961 and <= 70996 or >= 60490 and <= 60499))
                        {
                            continue;
                        }
                        var label = marker.TooltipString == null ? OmniLoc.Get("Feature.MultiToolbar.MarkerQuest") :
                            marker.TooltipString->AsDalamudSeString().TextValue;
                        var iconID = marker.IconId is >= 60490 and <= 60499 ? 70999u : marker.IconId;
                        worldMarkers.Add(new(marker.Position + new Vector3(0f, 2f, 0f), iconID, label));
                    }
                }
            }
        }

        if (enabled.Contains(MultiToolbarWorldMarkerType.Fate))
        {
            var manager = FateManager.Instance();
            if (manager != null)
            {
                var now = DateTimeOffset.Now.ToUnixTimeSeconds();
                foreach (var entry in manager->Fates)
                {
                    var fate = entry.Value;
                    if (fate == null || fate->StartTimeEpoch > 0 &&
                        (fate->StartTimeEpoch > now || fate->StartTimeEpoch + fate->Duration < now))
                    {
                        continue;
                    }
                    worldMarkers.Add(new(fate->Location + new Vector3(0f, 1.8f, 0f), fate->IconId,
                        $"{fate->Name.AsDalamudSeString().TextValue} {fate->Progress}%"));
                }
            }
        }

        if (enabled.Contains(MultiToolbarWorldMarkerType.Party))
        {
            foreach (var member in DService.Instance().PartyList)
            {
                if (member.MaxHP == 0 || member.ClassJob.RowId == 0 || member.GameObject == null ||
                    Vector3.Distance(member.Position, DService.Instance().ObjectTable.LocalPlayer!.Position) < 50f)
                {
                    continue;
                }
                worldMarkers.Add(new(member.Position + new Vector3(0f, 1.5f, 0f),
                    62100 + member.ClassJob.RowId, member.Name.TextValue));
            }
        }

        if (!enabled.Contains(MultiToolbarWorldMarkerType.Treasure) && !enabled.Contains(MultiToolbarWorldMarkerType.Hunt))
        {
            return;
        }

        foreach (var obj in DService.Instance().ObjectTable)
        {
            if (!obj.IsTargetable)
            {
                continue;
            }
            if (enabled.Contains(MultiToolbarWorldMarkerType.Treasure) &&
                (obj.ObjectKind == ObjectKind.Treasure || obj.ObjectKind == ObjectKind.EventObj && obj.DataID is 2007357 or 2007358 or 2007543) &&
                ((TreasureObject*)obj.Address)->State == TreasureObject.TreasureState.Unopened)
            {
                worldMarkers.Add(new(obj.Position, 60356u, obj.Name));
            }
            if (!enabled.Contains(MultiToolbarWorldMarkerType.Hunt) || obj.ObjectKind != ObjectKind.BattleNpc || obj.IsDead)
            {
                continue;
            }
            if (!huntRanks.TryGetValue(obj.DataID, out var rank))
            {
                rank = LuminaGetter.Get<NotoriousMonster>()
                    .Where(monster => monster.BNpcBase.RowId == obj.DataID)
                    .Select(monster => monster.Rank)
                    .FirstOrDefault();
                huntRanks[obj.DataID] = rank;
            }
            if (rank is >= 2 and <= 4)
            {
                var prefix = rank == 2 ? "A" : rank == 3 ? "S" : "SS";
                worldMarkers.Add(new(obj.Position + new Vector3(0f, 1.5f, 0f), 61704u, $"[{prefix}] {obj.Name}"));
            }
        }
    }

    private unsafe void RefreshAetherCurrentMarkers()
    {
        if (!aetherCurrentsLoaded)
        {
            aetherCurrentsLoaded = true;
            var currents = LuminaGetter.Get<AetherCurrentCompFlgSet>()
                .Where(set => set.Territory.RowId == markerTerritory)
                .SelectMany(set => set.AetherCurrents)
                .Where(current => current.IsValid && !current.Value.Quest.IsValid)
                .Select(current => current.RowId).ToHashSet();
            if (currents.Count == 0)
            {
                return;
            }
            var objects = LuminaGetter.Get<EObj>().Where(obj => currents.Contains(obj.Data.RowId))
                .ToDictionary(obj => obj.RowId, obj => obj.Data.RowId);
            foreach (var level in LuminaGetter.Get<Level>())
            {
                if (objects.TryGetValue(level.Object.RowId, out var currentID))
                {
                    aetherCurrentPositions.TryAdd(currentID, new Vector3(level.X, level.Y, level.Z));
                }
            }
        }
        var playerState = PlayerState.Instance();
        if (playerState == null)
        {
            return;
        }
        foreach (var (currentID, position) in aetherCurrentPositions)
        {
            if (!playerState->IsAetherCurrentUnlocked(currentID))
            {
                worldMarkers.Add(new(position, 60033u, OmniLoc.Get("Feature.MultiToolbar.MarkerAetherCurrent")));
            }
        }
    }

    private static unsafe Vector3 GetWorldMarkerGroundPosition(Vector3 position)
    {
        var playerHeight = DService.Instance().ObjectTable.LocalPlayer!.Position.Y;
        position.Y = BGCollisionModule.RaycastMaterialFilter(position, Vector3.UnitY, out var above)
            ? above.Point.Y + 1.8f
            : playerHeight + 250f;
        if (BGCollisionModule.RaycastMaterialFilter(position, -Vector3.UnitY, out var below))
        {
            position.Y = below.Point.Y + 1f;
            return position;
        }

        position.Y += 500f;
        position.Y = BGCollisionModule.RaycastMaterialFilter(position, -Vector3.UnitY, out below)
            ? below.Point.Y + 1f
            : playerHeight;
        return position;
    }

    private readonly record struct ToolbarWorldMarker(Vector3 Position, uint IconID, string Label);
}

public enum MultiToolbarWorldMarkerType
{
    AetherCurrent,
    Flag,
    Quest,
    Treasure,
    Hunt,
    Fate,
    Party,
}
