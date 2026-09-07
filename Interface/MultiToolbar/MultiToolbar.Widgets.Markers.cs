using System.Linq;
using System.Text;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Utility;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Lumina.Excel.Sheets;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
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
    private static readonly uint[] WaymarkIcons = [61341, 61342, 61343, 61347, 61344, 61345, 61346, 61348];
    private static readonly string[] WaymarkLabels = ["A", "B", "C", "D", "1", "2", "3", "4"];
    private static readonly MultiToolbarWorldMarkerType[] WorldMarkerTypes =
    [
        MultiToolbarWorldMarkerType.AetherCurrent, MultiToolbarWorldMarkerType.Flag, MultiToolbarWorldMarkerType.Waymark,
        MultiToolbarWorldMarkerType.Quest, MultiToolbarWorldMarkerType.Treasure, MultiToolbarWorldMarkerType.Hunt,
        MultiToolbarWorldMarkerType.Fate, MultiToolbarWorldMarkerType.Party
    ];
    private readonly Dictionary<uint, Vector3> aetherCurrentPositions = [];
    private Dictionary<uint, byte>? huntRanks;
    private uint markerTerritory;
    private uint markerMap;
    private bool aetherCurrentsLoaded;
    private long nextMarkerRefresh;

    private void DrawMarkerPopup()
    {
        foreach (var type in WorldMarkerTypes)
        {
            var enabled = config.ShowWorldMarkerOverlay && config.EnabledWorldMarkers.Contains(type);
            if (!OmniControls.CheckedSelectable($"##worldMarker{type}",
                    OmniLoc.Get($"Feature.MultiToolbar.Marker{type}"), enabled, ImGuiSelectableFlags.DontClosePopups))
            {
                continue;
            }

            enabled = !enabled;

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

    private unsafe void DrawWorldMarkerOverlay()
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

        if (worldMarkers.Count == 0)
        {
            return;
        }

        using var font = GetToolbarFont().Push();
        var drawList = ImGui.GetBackgroundDrawList();
        var iconSize = OmniTheme.Scale(32f);
        var viewport = ImGui.GetMainViewport();
        var compassOrigin = viewport.WorkPos + viewport.WorkSize * 0.5f;
        var workMaximum = viewport.WorkPos + viewport.WorkSize;
        var playerPosition = player.Position;
        foreach (var marker in worldMarkers)
        {
            var distance = Vector3.Distance(playerPosition, marker.Position);
            var opacity = Math.Clamp((distance - 8f) / 12f, 0f, 1f);
            if (opacity <= 0 || !TryProjectWorldMarker(marker.Position, viewport.Pos, out var position, out var inFront))
            {
                continue;
            }
            if (!inFront || position.X < viewport.WorkPos.X || position.X > workMaximum.X ||
                position.Y < viewport.WorkPos.Y || position.Y > workMaximum.Y)
            {
                DrawWorldMarkerDirection(drawList, marker.IconID, compassOrigin,
                    inFront ? position - compassOrigin : compassOrigin - position, viewport.WorkPos, viewport.WorkSize, opacity);
                continue;
            }

            var icon = ImageHelper.GetGameIcon(marker.IconID);
            var hasSubLabel = !string.IsNullOrEmpty(marker.SubLabel);
            var top = position.Y - iconSize - OmniTheme.Scale(hasSubLabel ? 62f : 40f);
            if (icon != null)
            {
                var iconPosition = new Vector2(position.X - iconSize * 0.5f, top);
                drawList.AddImage(icon.Handle, iconPosition,
                    iconPosition + new Vector2(iconSize), Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, opacity)));
            }

            var labelTop = top + iconSize + OmniTheme.Scale(2f);
            if (!string.IsNullOrEmpty(marker.Label))
            {
                DrawWorldMarkerLabel(drawList, marker.Label, new Vector2(position.X, labelTop), 13f, opacity, true);
                labelTop += OmniTheme.Scale(18f);
            }
            if (hasSubLabel)
            {
                DrawWorldMarkerLabel(drawList, marker.SubLabel!, new Vector2(position.X, labelTop), 12f, opacity * (208f / 255f), true);
                labelTop += OmniTheme.Scale(20f);
            }
            DrawWorldMarkerLabel(drawList, $"{MathF.Ceiling(distance):0}米",
                new Vector2(position.X, labelTop), 12f, opacity * (208f / 255f), false);
        }
    }

    private static void DrawWorldMarkerLabel(ImDrawListPtr drawList, string label, Vector2 center, float size, float opacity, bool shadow)
    {
        var capacity = Encoding.UTF8.GetMaxByteCount(label.Length);
        Span<byte> buffer = capacity <= 1024 ? stackalloc byte[capacity] : new byte[capacity];
        ReadOnlySpan<byte> text = buffer[..Encoding.UTF8.GetBytes(label, buffer)];
        var font = ImGui.GetFont();
        var fontSize = OmniTheme.Scale(size);
        var width = ImGui.CalcTextSize(text).X * fontSize / ImGui.GetFontSize();
        var position = Vector2.Round(center - new Vector2(width * 0.5f, 0f));
        for (var ring = shadow ? 4 : 1; ring >= 1; ring--)
        {
            var radius = OmniTheme.Scale(ring == 1 ? 1f : ring * 2f);
            var alpha = opacity * (ring == 1 ? 1f : 0.12f / ring);
            var color = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, alpha));
            foreach (var direction in ToolbarOutlineDirections)
            {
                drawList.AddText(font, fontSize,
                    position + direction * radius, color, text);
            }
        }
        drawList.AddText(font, fontSize, position, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, opacity)), text);
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

        if (enabled.Contains(MultiToolbarWorldMarkerType.Waymark))
        {
            var controller = MarkingController.Instance();
            if (controller != null)
            {
                for (var index = 0; index < WaymarkIcons.Length; index++)
                {
                    var marker = controller->FieldMarkers[index];
                    if (marker.Active)
                    {
                        worldMarkers.Add(new(marker.Position, WaymarkIcons[index], string.Empty));
                    }
                }
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
                    var remaining = fate->StartTimeEpoch > 0
                        ? TimeSpan.FromSeconds(Math.Max(0L, (long)fate->StartTimeEpoch + fate->Duration - now)).ToString(@"mm\:ss")
                        : OmniLoc.Get("Feature.MultiToolbar.FateWaiting");
                    worldMarkers.Add(new(fate->Location + new Vector3(0f, 1.8f, 0f), fate->IconId,
                        fate->Name.AsDalamudSeString().TextValue,
                        string.Format(OmniLoc.Get("Feature.MultiToolbar.FateProgressTime"), fate->Progress, remaining)));
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
            huntRanks ??= LuminaGetter.Get<NotoriousMonster>()
                .DistinctBy(monster => monster.BNpcBase.RowId)
                .ToDictionary(monster => monster.BNpcBase.RowId, monster => monster.Rank);
            huntRanks.TryGetValue(obj.DataID, out var rank);
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

    private readonly record struct ToolbarWorldMarker(Vector3 Position, uint IconID, string Label, string? SubLabel = null);
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
    Waymark,
}
