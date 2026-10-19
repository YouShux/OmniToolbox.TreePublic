using Dalamud.Game.ClientState.Objects.Enums;

namespace OmniToolbox.TreePublic;

internal sealed partial class FloatingInfoOverlay
{
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

}