using System.Linq;
using System.Numerics;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using OmenTools;

namespace OmniToolbox.TreePublic;

internal static class BetterChatFilters
{
    public static readonly LogFilter[] Rows = DService.Instance().Data.GetExcelSheet<LogFilter>()
        .Where(static row => row.LogKind != 0 && !row.Name.IsEmpty)
        .OrderBy(static row => row.Category)
        .ThenBy(static row => row.DisplayOrder)
        .ToArray();

    private static readonly Dictionary<uint, LogFilter> RowsByID = Rows.ToDictionary(static row => row.RowId);
    private static readonly HashSet<ushort> FilterKinds = Rows.Select(static row => (ushort)row.LogKind).ToHashSet();

    public static List<uint> FromChatTypes(IReadOnlyCollection<ushort> types) => Rows
        .Where(row => types.Any(type => GetFilterKind(type) == row.LogKind))
        .Select(static row => row.RowId)
        .ToList();

    public static List<ushort> GetChatTypes(IReadOnlyCollection<uint> rowIDs) => Rows
        .Where(row => rowIDs.Contains(row.RowId))
        .Select(static row => (ushort)row.LogKind)
        .Distinct()
        .ToList();

    public static bool Matches(BetterChatTabConfig tab, ushort type, byte source, byte target)
    {
        var kind = GetFilterKind(type);
        if (!FilterKinds.Contains(kind))
            return true;

        if (tab.FilterRows is null)
            return tab.ChatTypes.Any(selected => GetFilterKind(selected) == kind);

        var appliesRelations = ((XivChatType)type).AppliesRelationKind();

        foreach (var rowID in tab.FilterRows)
        {
            if (!RowsByID.TryGetValue(rowID, out var row) || row.LogKind != kind)
                continue;

            if (!appliesRelations ||
                source < 16 && target < 16 &&
                (row.Caster & (1 << source)) != 0 && (row.Target & (1 << target)) != 0)
                return true;
        }
        return false;
    }

    public static Vector4 GetColor(ushort type, Vector4 fallback)
    {
        var option = (XivChatType)GetFilterKind(type) switch
        {
            XivChatType.Say => ConfigOption.ColorSay,
            XivChatType.Shout => ConfigOption.ColorShout,
            XivChatType.TellOutgoing => ConfigOption.ColorTell,
            XivChatType.Party => ConfigOption.ColorParty,
            XivChatType.Alliance => ConfigOption.ColorAlliance,
            XivChatType.Ls1 => ConfigOption.ColorLS1,
            XivChatType.Ls2 => ConfigOption.ColorLS2,
            XivChatType.Ls3 => ConfigOption.ColorLS3,
            XivChatType.Ls4 => ConfigOption.ColorLS4,
            XivChatType.Ls5 => ConfigOption.ColorLS5,
            XivChatType.Ls6 => ConfigOption.ColorLS6,
            XivChatType.Ls7 => ConfigOption.ColorLS7,
            XivChatType.Ls8 => ConfigOption.ColorLS8,
            XivChatType.FreeCompany => ConfigOption.ColorFCompany,
            XivChatType.NoviceNetwork => ConfigOption.ColorBeginner,
            XivChatType.NoviceNetworkSystem => ConfigOption.ColorBeginnerAnnounce,
            XivChatType.CustomEmote => ConfigOption.ColorEmoteUser,
            XivChatType.StandardEmote => ConfigOption.ColorEmote,
            XivChatType.Yell => ConfigOption.ColorYell,
            XivChatType.PvPTeam => ConfigOption.ColorPvPGroup,
            XivChatType.CrossLinkShell1 => ConfigOption.ColorCWLS,
            XivChatType.CrossLinkShell2 => ConfigOption.ColorCWLS2,
            XivChatType.CrossLinkShell3 => ConfigOption.ColorCWLS3,
            XivChatType.CrossLinkShell4 => ConfigOption.ColorCWLS4,
            XivChatType.CrossLinkShell5 => ConfigOption.ColorCWLS5,
            XivChatType.CrossLinkShell6 => ConfigOption.ColorCWLS6,
            XivChatType.CrossLinkShell7 => ConfigOption.ColorCWLS7,
            XivChatType.CrossLinkShell8 => ConfigOption.ColorCWLS8,
            XivChatType.Damage => ConfigOption.ColorAttackSuccess,
            XivChatType.Miss => ConfigOption.ColorAttackFailure,
            XivChatType.Action => ConfigOption.ColorAction,
            XivChatType.Item => ConfigOption.ColorItem,
            XivChatType.Healing => ConfigOption.ColorCureGive,
            XivChatType.GainBuff or XivChatType.LoseBuff => ConfigOption.ColorBuffGive,
            XivChatType.GainDebuff or XivChatType.LoseDebuff => ConfigOption.ColorDebuffGive,
            XivChatType.Echo => ConfigOption.ColorEcho,
            XivChatType.SystemMessage or XivChatType.Alarm or XivChatType.GlamourNotifications or
                XivChatType.RetainerSale or XivChatType.PeriodicRecruitmentNotification or XivChatType.Sign or
                XivChatType.Orchestrion or XivChatType.MessageBook => ConfigOption.ColorSysMsg,
            XivChatType.SystemError => ConfigOption.ColorSysBattle,
            XivChatType.GatheringSystemMessage => ConfigOption.ColorSysGathering,
            XivChatType.ErrorMessage => ConfigOption.ColorSysErr,
            XivChatType.NPCDialogue or XivChatType.NPCDialogueAnnouncements => ConfigOption.ColorNpcSay,
            XivChatType.LootNotice => ConfigOption.ColorItemNotice,
            XivChatType.Progress => ConfigOption.ColorGrowup,
            XivChatType.LootRoll or XivChatType.RandomNumber => ConfigOption.ColorLoot,
            XivChatType.FreeCompanyAnnouncement or XivChatType.FreeCompanyLoginLogout => ConfigOption.ColorFCAnnounce,
            XivChatType.PvpTeamAnnouncement or XivChatType.PvpTeamLoginLogout => ConfigOption.ColorPvPGroupAnnounce,
            XivChatType.Crafting => ConfigOption.ColorCraft,
            XivChatType.Gathering => ConfigOption.ColorGathering,
            _ => (ConfigOption?)null,
        };
        if (option is { } colorOption &&
            DService.Instance().GameConfig.UiConfig.TryGetUInt(colorOption.ToString(), out var color) &&
            (color & 0xFFFFFF) != 0)
        {
            return new((color >> 16 & 0xFF) / 255f, (color >> 8 & 0xFF) / 255f, (color & 0xFF) / 255f, 1f);
        }
        return fallback with { W = 1f };
    }

    // 原生设置将双向悄悄话与跨服小队分别归入同一个过滤项。
    private static ushort GetFilterKind(ushort type) => (XivChatType)type switch
    {
        XivChatType.TellIncoming or XivChatType.GmTell => (ushort)XivChatType.TellOutgoing,
        XivChatType.CrossParty or XivChatType.GmParty => (ushort)XivChatType.Party,
        XivChatType.GmSay => (ushort)XivChatType.Say,
        XivChatType.GmShout => (ushort)XivChatType.Shout,
        XivChatType.GmYell => (ushort)XivChatType.Yell,
        XivChatType.GmFreeCompany => (ushort)XivChatType.FreeCompany,
        XivChatType.GmNoviceNetwork => (ushort)XivChatType.NoviceNetwork,
        XivChatType.GmLinkshell1 => (ushort)XivChatType.Ls1,
        XivChatType.GmLinkshell2 => (ushort)XivChatType.Ls2,
        XivChatType.GmLinkshell3 => (ushort)XivChatType.Ls3,
        XivChatType.GmLinkshell4 => (ushort)XivChatType.Ls4,
        XivChatType.GmLinkshell5 => (ushort)XivChatType.Ls5,
        XivChatType.GmLinkshell6 => (ushort)XivChatType.Ls6,
        XivChatType.GmLinkshell7 => (ushort)XivChatType.Ls7,
        XivChatType.GmLinkshell8 => (ushort)XivChatType.Ls8,
        _ => type,
    };
}
