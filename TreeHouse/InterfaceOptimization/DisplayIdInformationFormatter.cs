using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Gui.FlyText;
using OmniToolbox.Config;
using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class DisplayIdInformationFormatter
{
    public static string FormatActionSuffix(
        uint originalId,
        uint resolvedId,
        bool showResolved,
        bool showOriginal,
        uint iconId,
        bool showIcon)
    {
        var suffix = showResolved && !showOriginal
            ? $" [{resolvedId}]"
            : showResolved && showOriginal && originalId != resolvedId
                ? $" [{originalId} → {resolvedId}]"
                : $" [{originalId}]";
        return showIcon && iconId != 0 ? $"{suffix}/[{iconId}]" : suffix;
    }

    public static string FormatIdSuffix(uint id, uint iconId, bool showIcon) =>
        showIcon && iconId != 0 ? $" [{id}]/[{iconId}]" : $" [{id}]";

    public static string AppendStatusTooltipId(string text, uint statusId, uint iconId, bool showIcon)
    {
        var suffix = $" {FormatIdSuffix(statusId, iconId, showIcon)}";
        var newlineIndex = text.IndexOf('\n');
        var firstLine = newlineIndex < 0 ? text : text[..newlineIndex];
        if (firstLine.EndsWith(suffix, StringComparison.Ordinal))
        {
            return text;
        }

        return newlineIndex < 0
            ? text + suffix
            : text.Insert(newlineIndex, suffix);
    }

    public static string? FormatDtr(bool showZone, bool showWeather, uint mapId, uint territoryId, uint weatherId)
    {
        if (!showZone || mapId == 0 || territoryId == 0)
        {
            return null;
        }

        return string.Format(
            OmniLoc.Get(showWeather
                ? "Feature.DisplayIdInformation.Dtr.ZoneWeather"
                : "Feature.DisplayIdInformation.Dtr.Zone"),
            territoryId,
            mapId,
            weatherId);
    }

    public static bool ShouldDisplayTarget(ObjectKind objectKind, DisplayIdInformationConfig config) => objectKind switch
    {
        ObjectKind.BattleNpc => config.DisplayTargetIdBattleNpc,
        ObjectKind.EventNpc => config.DisplayTargetIdEventNpc,
        ObjectKind.Companion => config.DisplayTargetIdCompanion,
        _ => config.DisplayTargetIdOthers
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

    public static string InsertFlyTextId(string text, string matchedName, uint id)
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
