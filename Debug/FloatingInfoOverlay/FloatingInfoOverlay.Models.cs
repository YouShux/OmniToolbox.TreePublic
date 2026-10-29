using Dalamud.Game.ClientState.Objects.Enums;

namespace OmniToolbox.TreePublic;

internal sealed partial class FloatingInfoOverlay
{
    private sealed class FloatingInfoObject
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

    private sealed class FloatingInfoGroup
    (
        Vector2            anchorPosition,
        FloatingInfoObject item
    )
    {
        public Vector2 AnchorPosition { get; private set; } = anchorPosition;

        public List<FloatingInfoObject> Objects { get; } = [item];

        public void Reset
        (
            Vector2            anchorPosition,
            FloatingInfoObject item
        )
        {
            AnchorPosition = anchorPosition;
            Objects.Clear();
            Objects.Add(item);
        }
    }

    private enum FloatingInfoMarker
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

    private readonly record struct FloatingInfoStatus
    (
        uint   StatusID,
        string Name,
        float  RemainingTime,
        ushort Param
    );
}
