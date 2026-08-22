using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using OmniToolbox.Config;
using OmniToolbox.TreeHouse;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class FloatingInfoOverlayState : IDisposable
{
    private const int MaxStatusCount = 8;
    private readonly FloatingInfoOverlayConfig config;
    private readonly NonEntityTargetVisibility targetVisibility;
    private readonly List<FloatingInfoObject> objects = [];
    private readonly Dictionary<uint, Vector2> screenPositions = [];
    private readonly Dictionary<uint, FloatingInfoGroup> groups = [];
    private readonly List<FloatingInfoGroup> groupBuffer = [];
    private readonly Dictionary<uint, Vector2> smoothedPositions = [];
    private readonly Dictionary<uint, Vector2> stablePixelPositions = [];
    private readonly Dictionary<uint, int> lastSeenFrames = [];
    private readonly Dictionary<uint, NonEntityTargetInfo> nonEntityTargets = [];
    private readonly Dictionary<uint, string> statusNameCache = [];
    private readonly HashSet<uint> seenEntityIds = [];
    private readonly List<uint> removeBuffer = [];
    private int updateFrame;
    private long nextScanTick;
    private bool lastShowNonEntityTargets;
    private bool disposed;

    public FloatingInfoOverlayState(
        FloatingInfoOverlayConfig config,
        NonEntityTargetVisibility targetVisibility)
    {
        this.config = config;
        this.targetVisibility = targetVisibility;
        targetVisibility.NonEntityTargetDetected += OnNonEntityTargetDetected;
    }

    public IReadOnlyDictionary<uint, FloatingInfoGroup> Groups => groups;

    public bool IsActive { get; private set; }

    public void Update(IFramework _)
    {
        targetVisibility.SetOverlayPolicy(true, config.ShowNonEntityTargets);
        if (config.ShowNonEntityTargets != lastShowNonEntityTargets)
        {
            lastShowNonEntityTargets = config.ShowNonEntityTargets;
            if (!lastShowNonEntityTargets)
            {
                ClearNonEntityTargets();
            }
        }

        var services = DService.Instance();
        IsActive = services.ClientState.IsLoggedIn &&
                   !services.Condition[ConditionFlag.BetweenAreas] &&
                   !services.Condition[ConditionFlag.BetweenAreas51] &&
                   services.ObjectTable.LocalPlayer is not null;
        if (!IsActive)
        {
            ClearObjects();
            ClearNonEntityTargets();
            nextScanTick = 0;
            return;
        }

        var currentTick = Stopwatch.GetTimestamp();
        if (currentTick >= nextScanTick)
        {
            nextScanTick = currentTick + 100 * Stopwatch.Frequency / 1000;
            ScanObjects(services.ObjectTable.LocalPlayer!);
            return;
        }

        RefreshScreenPositions();
    }

    private void RefreshScreenPositions()
    {
        if (!IsActive || objects.Count == 0)
        {
            return;
        }

        screenPositions.Clear();
        for (var index = 0; index < objects.Count; index++)
        {
            var item = objects[index];
            if (DService.Instance().GameGUI.WorldToScreen(item.Position, out var screenPosition))
            {
                screenPositions[item.EntityID] = screenPosition;
            }
        }

        GroupObjects();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        targetVisibility.NonEntityTargetDetected -= OnNonEntityTargetDetected;
        targetVisibility.SetOverlayPolicy(false, false);
        ClearObjects();
        ClearNonEntityTargets();
        statusNameCache.Clear();
        lastSeenFrames.Clear();
        smoothedPositions.Clear();
        stablePixelPositions.Clear();
    }

    private void ScanObjects(IGameObject localPlayer)
    {
        objects.Clear();
        screenPositions.Clear();
        ClearGroups();
        seenEntityIds.Clear();

        var objectTable = DService.Instance().ObjectTable;
        var localPosition = localPlayer.Position;
        var range = Math.Clamp(config.Range, 5f, 200f);
        var maxObjects = Math.Clamp(config.MaxObjects, 1, 100);

        if (config.ShowNonEntityTargets)
        {
            foreach (var gameObject in objectTable)
            {
                if (gameObject.IsValid())
                {
                    TryAddOrUpdateNonEntityTarget(gameObject.ToStruct(), localPosition, range);
                }
            }
        }

        foreach (var gameObject in objectTable)
        {
            if (!gameObject.IsValid() ||
                gameObject.EntityID == 0 ||
                !ShouldShowObject(gameObject, localPlayer) ||
                !PassesDataIDFilter(gameObject.DataID))
            {
                continue;
            }

            var distance = Vector3.Distance(localPosition, gameObject.Position);
            if (distance > range ||
                config.OnlyCasting && gameObject is not IBattleChara { IsCasting: true } ||
                !DService.Instance().GameGUI.WorldToScreen(gameObject.Position, out var screenPosition))
            {
                continue;
            }

            objects.Add(CreateObjectInfo(gameObject, distance));
            screenPositions[gameObject.EntityID] = screenPosition;
            seenEntityIds.Add(gameObject.EntityID);
            if (objects.Count >= maxObjects)
            {
                break;
            }
        }

        if (config.ShowNonEntityTargets)
        {
            RefreshNonEntityTargets(localPosition, range);
            foreach (var target in nonEntityTargets.Values)
            {
                if (objects.Count >= maxObjects)
                {
                    break;
                }

                if (seenEntityIds.Contains(target.EntityID) || !PassesDataIDFilter(target.DataID))
                {
                    continue;
                }

                objects.Add(CreateNonEntityObjectInfo(target));
                screenPositions[target.EntityID] = target.ScreenPosition;
                seenEntityIds.Add(target.EntityID);
            }
        }
        else
        {
            ClearNonEntityTargets();
        }

        GroupObjects();
    }

    private void GroupObjects()
    {
        ClearGroups();
        updateFrame++;
        var smoothingAlpha = GetSmoothingAlpha();
        var mergeDistance = Math.Clamp(config.MergeDistance, 0f, 50f);
        var mergeDistanceSquared = mergeDistance * mergeDistance;

        for (var index = 0; index < objects.Count; index++)
        {
            var item = objects[index];
            if (!screenPositions.TryGetValue(item.EntityID, out var rawPosition))
            {
                continue;
            }

            var anchorPosition = GetStablePixelPosition(
                item.EntityID,
                UpdateSmoothedPosition(item.EntityID, rawPosition, smoothingAlpha));
            uint nearestGroupID = 0;
            var nearestDistanceSquared = float.MaxValue;
            foreach (var pair in groups)
            {
                var distanceSquared = Vector3.DistanceSquared(item.Position, pair.Value.Objects[0].Position);
                if (distanceSquared < mergeDistanceSquared && distanceSquared < nearestDistanceSquared)
                {
                    nearestGroupID = pair.Key;
                    nearestDistanceSquared = distanceSquared;
                }
            }

            if (nearestGroupID != 0)
            {
                groups[nearestGroupID].Objects.Add(item);
            }
            else
            {
                FloatingInfoGroup group;
                if (groups.Count < groupBuffer.Count)
                {
                    group = groupBuffer[groups.Count];
                    group.Reset(anchorPosition, item);
                }
                else
                {
                    group = new(anchorPosition, item);
                    groupBuffer.Add(group);
                }

                groups[item.EntityID] = group;
            }
        }

        PruneStalePositions();
    }

    private bool ShouldShowObject(IGameObject gameObject, IGameObject localPlayer) =>
        gameObject.ObjectKind switch
        {
            ObjectKind.Pc when gameObject.EntityID == localPlayer.EntityID => config.ShowLocalPlayer,
            ObjectKind.Pc => config.ShowPlayers,
            ObjectKind.BattleNpc => config.ShowBattleNpcs,
            ObjectKind.EventNpc => config.ShowEventNpcs,
            ObjectKind.EventObj => config.ShowEventObjects,
            ObjectKind.Companion => config.ShowCompanions,
            _ => false
        };

    private bool PassesDataIDFilter(uint dataID)
    {
        if (!config.EnableDataIDFilter || config.FilterDataIds.Count == 0)
        {
            return true;
        }

        var contains = config.FilterDataIds.Contains(dataID);
        return config.UseDataIDWhitelist ? contains : !contains;
    }

    private FloatingInfoObject CreateObjectInfo(IGameObject gameObject, float distance)
    {
        var item = new FloatingInfoObject
        {
            EntityID = gameObject.EntityID,
            DataID = gameObject.DataID,
            Name = gameObject.Name,
            ObjectKind = gameObject.ObjectKind,
            Position = gameObject.Position,
            Rotation = gameObject.Rotation,
            Distance = distance,
            Marker = config.ShowMarker ? GetObjectMarker(gameObject) : FloatingInfoMarker.None,
            HitboxRadius = gameObject.HitboxRadius
        };

        if (gameObject is not IBattleChara battleChara)
        {
            return item;
        }

        if (config.ShowHealth)
        {
            item.CurrentHp = battleChara.CurrentHp;
            item.MaxHp = battleChara.MaxHp;
        }

        if (config.ShowMana)
        {
            item.CurrentMp = battleChara.CurrentMp;
            item.MaxMp = battleChara.MaxMp;
        }

        if (config.ShowCastInfo || config.OnlyCasting)
        {
            item.IsCasting = battleChara.IsCasting;
            item.CastActionID = battleChara.CastActionID;
            item.CurrentCastTime = battleChara.CurrentCastTime;
            item.TotalCastTime = battleChara.TotalCastTime;
        }

        if (config.ShowCastInfo && battleChara.IsCasting && battleChara.Address != nint.Zero)
        {
            item.CastRotation = ((CSCharacter*)battleChara.Address)->CastRotation;
        }

        if (config.ShowStatusList)
        {
            FillStatuses(battleChara, item.Statuses);
        }

        return item;
    }

    private FloatingInfoObject CreateNonEntityObjectInfo(NonEntityTargetInfo target)
    {
        var item = new FloatingInfoObject
        {
            EntityID = target.EntityID,
            DataID = target.DataID,
            Name = OmniLoc.Get("Feature.FloatingInfoOverlay.NonEntityTarget"),
            ObjectKind = ObjectKind.BattleNpc,
            Position = target.Position,
            Distance = target.Distance,
            IsNonEntity = true
        };

        if (target.Address == nint.Zero)
        {
            return item;
        }

        var castInfo = ((CSCharacter*)target.Address)->GetCastInfo();
        if (castInfo != null && castInfo->IsCasting && castInfo->ActionId != 0)
        {
            item.IsCasting = true;
            item.CastActionID = castInfo->ActionId;
            item.CurrentCastTime = castInfo->CurrentCastTime;
            item.TotalCastTime = castInfo->TotalCastTime;
        }

        return item;
    }

    private void FillStatuses(IBattleChara battleChara, List<FloatingInfoStatus> statuses)
    {
        statuses.Clear();
        foreach (var status in battleChara.StatusList)
        {
            if (status.StatusID == 0 || status.RemainingTime <= 0f)
            {
                continue;
            }

            statuses.Add(new(
                status.StatusID,
                GetStatusName(status.StatusID),
                status.RemainingTime,
                status.Param));
        }

        statuses.Sort(static (left, right) => right.RemainingTime.CompareTo(left.RemainingTime));
        if (statuses.Count > MaxStatusCount)
        {
            statuses.RemoveRange(MaxStatusCount, statuses.Count - MaxStatusCount);
        }
    }

    private string GetStatusName(uint statusID)
    {
        if (statusNameCache.TryGetValue(statusID, out var name))
        {
            return name;
        }

        name = LuminaGetter.TryGetRow<LuminaStatus>(statusID, out var status)
            ? status.Name.ToString()
            : string.Empty;
        statusNameCache[statusID] = name;
        return name;
    }

    private void OnNonEntityTargetDetected(nint address)
    {
        if (!IsActive || !config.ShowNonEntityTargets || address == nint.Zero ||
            DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer)
        {
            return;
        }

        TryAddOrUpdateNonEntityTarget(
            (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address,
            localPlayer.Position,
            Math.Clamp(config.Range, 5f, 200f));
    }

    private bool TryAddOrUpdateNonEntityTarget(
        FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* gameObject,
        Vector3 localPosition,
        float range)
    {
        var character = (CSCharacter*)gameObject;
        if (!NonEntityTargetVisibility.IsNonEntityTreasureTarget(character) || character->EntityId == 0)
        {
            return false;
        }

        var distance = Vector3.Distance(localPosition, character->Position);
        if (distance > range || !DService.Instance().GameGUI.WorldToScreen(character->Position, out var screenPosition))
        {
            return false;
        }

        nonEntityTargets[character->EntityId] = new(
            character->EntityId,
            character->BaseId,
            character->Position,
            screenPosition,
            distance,
            (nint)character);
        return true;
    }

    private void RefreshNonEntityTargets(Vector3 localPosition, float range)
    {
        removeBuffer.Clear();
        foreach (var entityId in nonEntityTargets.Keys)
        {
            removeBuffer.Add(entityId);
        }

        for (var index = 0; index < removeBuffer.Count; index++)
        {
            var entityID = removeBuffer[index];
            var target = nonEntityTargets[entityID];
            if (target.Address == nint.Zero)
            {
                nonEntityTargets.Remove(entityID);
                continue;
            }

            var character = (CSCharacter*)target.Address;
            var distance = Vector3.Distance(localPosition, character->Position);
            if (!NonEntityTargetVisibility.IsNonEntityTreasureTarget(character) ||
                distance > range ||
                !DService.Instance().GameGUI.WorldToScreen(character->Position, out var screenPosition))
            {
                nonEntityTargets.Remove(entityID);
                continue;
            }

            nonEntityTargets[entityID] = target with
            {
                DataID = character->BaseId,
                Position = character->Position,
                ScreenPosition = screenPosition,
                Distance = distance
            };
        }

        removeBuffer.Clear();
    }

    private Vector2 UpdateSmoothedPosition(uint entityID, Vector2 rawPosition, float alpha)
    {
        if (!smoothedPositions.TryGetValue(entityID, out var previous))
        {
            smoothedPositions[entityID] = rawPosition;
            lastSeenFrames[entityID] = updateFrame;
            return rawPosition;
        }

        var distance = Vector2.Distance(previous, rawPosition);
        var smoothed = distance > OmniTheme.Scale(180f)
            ? rawPosition
            : Vector2.Lerp(previous, rawPosition, alpha);
        smoothedPositions[entityID] = smoothed;
        lastSeenFrames[entityID] = updateFrame;
        return smoothed;
    }

    private Vector2 GetStablePixelPosition(uint entityID, Vector2 smoothedPosition)
    {
        if (!stablePixelPositions.TryGetValue(entityID, out var previous))
        {
            var initial = new Vector2(MathF.Round(smoothedPosition.X), MathF.Round(smoothedPosition.Y));
            stablePixelPositions[entityID] = initial;
            return initial;
        }

        var next = new Vector2(
            StableSnap(smoothedPosition.X, previous.X),
            StableSnap(smoothedPosition.Y, previous.Y));
        stablePixelPositions[entityID] = next;
        return next;
    }

    private void PruneStalePositions()
    {
        var minimumFrame = updateFrame - 180;
        removeBuffer.Clear();
        foreach (var pair in lastSeenFrames)
        {
            if (pair.Value < minimumFrame)
            {
                removeBuffer.Add(pair.Key);
            }
        }

        for (var index = 0; index < removeBuffer.Count; index++)
        {
            var entityID = removeBuffer[index];
            lastSeenFrames.Remove(entityID);
            smoothedPositions.Remove(entityID);
            stablePixelPositions.Remove(entityID);
        }

        removeBuffer.Clear();
    }

    private void ClearObjects()
    {
        objects.Clear();
        screenPositions.Clear();
        ClearGroups();
    }

    private void ClearGroups()
    {
        foreach (var group in groups.Values)
        {
            group.Objects.Clear();
        }

        groups.Clear();
    }

    private void ClearNonEntityTargets()
    {
        nonEntityTargets.Clear();
        removeBuffer.Clear();
    }

    private static float GetSmoothingAlpha()
    {
        var deltaTime = ImGui.GetIO().DeltaTime;
        if (deltaTime <= 0f)
        {
            deltaTime = 1f / 60f;
        }

        return Math.Clamp(1f - MathF.Exp(-deltaTime / 0.04f), 0.05f, 1f);
    }

    private static float StableSnap(float value, float previousPixel)
    {
        var delta = value - previousPixel;
        if (MathF.Abs(delta) >= 3f)
        {
            return MathF.Round(value);
        }

        if (delta > 0.8f)
        {
            return previousPixel + 1f;
        }

        return delta < -0.8f ? previousPixel - 1f : previousPixel;
    }

    private static FloatingInfoMarker GetObjectMarker(IGameObject gameObject)
    {
        if (!gameObject.IsValid() || gameObject.ObjectKind != ObjectKind.Pc)
        {
            return FloatingInfoMarker.None;
        }

        var markingController = MarkingController.Instance();
        if (markingController == null)
        {
            return FloatingInfoMarker.None;
        }

        for (var index = 0; index < 17; index++)
        {
            if (markingController->Markers[index] == gameObject.GameObjectID)
            {
                return (FloatingInfoMarker)index;
            }
        }

        return FloatingInfoMarker.None;
    }

    private readonly record struct NonEntityTargetInfo(
        uint EntityID,
        uint DataID,
        Vector3 Position,
        Vector2 ScreenPosition,
        float Distance,
        nint Address);
}

internal sealed class FloatingInfoGroup(Vector2 anchorPosition, FloatingInfoObject item)
{
    public Vector2 AnchorPosition { get; private set; } = anchorPosition;

    public List<FloatingInfoObject> Objects { get; } = [item];

    public void Reset(Vector2 anchorPosition, FloatingInfoObject item)
    {
        AnchorPosition = anchorPosition;
        Objects.Clear();
        Objects.Add(item);
    }
}

internal sealed class FloatingInfoObject
{
    public uint EntityID { get; init; }

    public uint DataID { get; init; }

    public string Name { get; init; } = string.Empty;

    public ObjectKind ObjectKind { get; init; }

    public Vector3 Position { get; init; }

    public float Rotation { get; init; }

    public float Distance { get; init; }

    public uint CurrentHp { get; set; }

    public uint CurrentMp { get; set; }

    public uint MaxHp { get; set; }

    public uint MaxMp { get; set; }

    public bool IsCasting { get; set; }

    public uint CastActionID { get; set; }

    public float CurrentCastTime { get; set; }

    public float TotalCastTime { get; set; }

    public float? CastRotation { get; set; }

    public FloatingInfoMarker Marker { get; set; }

    public float HitboxRadius { get; set; }

    public bool IsNonEntity { get; init; }

    public List<FloatingInfoStatus> Statuses { get; } = [];
}

internal readonly record struct FloatingInfoStatus(
    uint StatusID,
    string Name,
    float RemainingTime,
    ushort Param);

internal enum FloatingInfoMarker
{
    None = -1,
    Attack1,
    Attack2,
    Attack3,
    Attack4,
    Attack5,
    Bind1,
    Bind2,
    Bind3,
    Ignore1,
    Ignore2,
    Square,
    Circle,
    Cross,
    Triangle,
    Attack6,
    Attack7,
    Attack8
}
