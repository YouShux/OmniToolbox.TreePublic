using System.Text;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Gui.FlyText;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace OmniToolbox.TreePublic;

public sealed partial class DisplayIDInformation
{
    private static class DisplayIDInformationFormatter
    {
        public static string FormatActionSuffix
        (
            uint originalID,
            uint resolvedID,
            bool showResolved,
            bool showOriginal,
            uint iconID,
            bool showIcon
        )
        {
            var suffix = showResolved && !showOriginal
                ? $" [{resolvedID}]"
                : showResolved && showOriginal && originalID != resolvedID
                    ? $" [{originalID} → {resolvedID}]"
                    : $" [{originalID}]";
            return showIcon && iconID != 0 ? $"{suffix}/[{iconID}]" : suffix;
        }

        public static string FormatIDSuffix
        (
            uint id,
            uint iconID,
            bool showIcon
        ) =>
            showIcon && iconID != 0 ? $" [{id}]/[{iconID}]" : $" [{id}]";

        public static bool TryAppendTooltipID
        (
            ReadOnlySeStringSpan text,
            uint                 id,
            uint                 iconID,
            bool                 showIcon,
            out byte[]           modified
        )
        {
            modified = [];
            if (text.IsEmpty)
                return false;

            var firstLineLength = text.Data.Length;
            var payloads = text.GetOffsetEnumerator();
            while (payloads.MoveNext())
            {
                var entry = payloads.Current;
                if (entry.Payload.Type == ReadOnlySePayloadType.Invalid)
                    return false;
                if (firstLineLength != text.Data.Length)
                    continue;

                if (entry.Payload.Type == ReadOnlySePayloadType.Macro && entry.Payload.MacroCode == MacroCode.NewLine)
                {
                    firstLineLength = entry.Offset;
                    continue;
                }
                if (entry.Payload.Type != ReadOnlySePayloadType.Text)
                    continue;

                var newlineIndex = entry.Payload.Body.IndexOf((byte)'\n');
                if (newlineIndex < 0)
                    continue;

                firstLineLength = entry.Offset + newlineIndex;
            }

            var suffix = Encoding.UTF8.GetBytes($" {FormatIDSuffix(id, iconID, showIcon)}");
            if (text.Data[..firstLineLength].EndsWith(suffix))
                return false;

            modified = new byte[text.Data.Length + suffix.Length];
            text.Data[..firstLineLength].CopyTo(modified);
            suffix.CopyTo(modified.AsSpan(firstLineLength));
            text.Data[firstLineLength..].CopyTo(modified.AsSpan(firstLineLength + suffix.Length));
            return true;
        }

        public static string? FormatDtr
        (
            bool showZone,
            bool showWeather,
            uint mapID,
            uint territoryID,
            uint weatherID
        )
        {
            if (!showZone || mapID == 0 || territoryID == 0)
            {
                return null;
            }

            return string.Format(
                OmniLoc.Get(showWeather
                    ? "Feature.DisplayIdInformation.Dtr.ZoneWeather"
                    : "Feature.DisplayIdInformation.Dtr.Zone"),
                territoryID,
                mapID,
                weatherID);
        }

        public static bool ShouldDisplayTarget
        (
            ObjectKind                 objectKind,
            DisplayIDInformationConfig config
        ) => objectKind switch
        {
            ObjectKind.Pc => false,
            ObjectKind.BattleNpc => config.DisplayTargetIDBattleNPC,
            ObjectKind.EventNpc => config.DisplayTargetIDEventNPC,
            ObjectKind.Companion => config.DisplayTargetIDCompanion,
            _ => config.DisplayTargetIDOthers
        };

        public static bool IsStatusFlyTextKind(FlyTextKind kind) => kind is
            FlyTextKind.Buff or
            FlyTextKind.Debuff or
            FlyTextKind.DebuffNoEffect or
            FlyTextKind.BuffFading or
            FlyTextKind.DebuffFading or
            FlyTextKind.DebuffResisted or
            FlyTextKind.DebuffInvulnerable;

        public static bool IsDamageFlyTextKind(FlyTextKind kind) => kind is
            FlyTextKind.Damage or
            FlyTextKind.DamageDh or
            FlyTextKind.DamageCrit or
            FlyTextKind.DamageCritDh or
            FlyTextKind.AutoAttackOrDot or
            FlyTextKind.AutoAttackOrDotDh or
            FlyTextKind.AutoAttackOrDotCrit or
            FlyTextKind.AutoAttackOrDotCritDh or
            FlyTextKind.HpDrain or
            FlyTextKind.MpDrain;

        public static string InsertFlyTextID
        (
            string text,
            string matchedName,
            uint   id
        )
        {
            var suffix = $"({id})";
            if (text.Contains(suffix, StringComparison.Ordinal))
            {
                return text;
            }

            var index = text.IndexOf(matchedName, StringComparison.Ordinal);
            return index < 0 ? text + suffix : text.Insert(index + matchedName.Length, suffix);
        }

        public static string FormatSortedCastIds(IReadOnlyList<uint> ids)
        {
            var sorted = new List<uint>(ids.Count);
            for (var index = 0; index < ids.Count; index++)
            {
                if (!sorted.Contains(ids[index]))
                {
                    sorted.Add(ids[index]);
                }
            }

            sorted.Sort();
            return $"({string.Join('|', sorted)})";
        }
    }

}
