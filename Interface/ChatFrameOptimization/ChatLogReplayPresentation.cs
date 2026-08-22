using System.Drawing;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Extensions;

namespace OmniToolbox.TreePublic;

internal static class ChatLogReplayPresentation
{
    public static string GetChannelDisplayName(string channel) =>
        OmniLoc.Get(GetChannelKey(channel));

    public static Vector4 GetChannelColor(string channel) =>
        channel switch
        {
            "Party" => KnownColor.LightSkyBlue.ToVector4(),
            "Say" => KnownColor.WhiteSmoke.ToVector4(),
            "Yell" => KnownColor.Gold.ToVector4(),
            "Shout" => OmniTheme.Orange,
            "TellIncoming" or "TellOutgoing" => KnownColor.HotPink.ToVector4(),
            "Alliance" => KnownColor.DarkOrange.ToVector4(),
            "FreeCompany" => KnownColor.SkyBlue.ToVector4(),
            _ when channel.StartsWith("CrossLinkShell", StringComparison.Ordinal) ||
                   channel.StartsWith("Cwls", StringComparison.Ordinal) ||
                   channel.StartsWith("Ls", StringComparison.Ordinal) ||
                   channel.StartsWith("Linkshell", StringComparison.Ordinal) => KnownColor.LightGreen.ToVector4(),
            _ => KnownColor.Gainsboro.ToVector4(),
        };

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes}B";
        }

        return bytes < 1024 * 1024
            ? $"{bytes / 1024d:0.#}KB"
            : $"{bytes / 1024d / 1024d:0.#}MB";
    }

    private static string GetChannelKey(string channel) =>
        channel switch
        {
            "Debug" => "Feature.ChatLogReplay.Channel.Debug",
            "Action" => "Feature.ChatLogReplay.Channel.Action",
            "Damage" => "Feature.ChatLogReplay.Channel.Damage",
            "Healing" => "Feature.ChatLogReplay.Channel.Healing",
            "Item" => "Feature.ChatLogReplay.Channel.Item",
            "GainBuff" => "Feature.ChatLogReplay.Channel.GainBuff",
            "GainDebuff" => "Feature.ChatLogReplay.Channel.GainDebuff",
            "LoseBuff" => "Feature.ChatLogReplay.Channel.LoseBuff",
            "LoseDebuff" => "Feature.ChatLogReplay.Channel.LoseDebuff",
            "Miss" => "Feature.ChatLogReplay.Channel.Miss",
            "GlamourNotifications" => "Feature.ChatLogReplay.Channel.GlamourNotifications",
            "PeriodicRecruitmentNotification" => "Feature.ChatLogReplay.Channel.RecruitmentNotification",
            "SystemMessage" => "Feature.ChatLogReplay.Channel.SystemMessage",
            "SystemError" => "Feature.ChatLogReplay.Channel.SystemError",
            "NPCDialogueAnnouncements" => "Feature.ChatLogReplay.Channel.NpcDialogue",
            "Say" => "Feature.ChatLogReplay.Channel.Say",
            "Party" => "Feature.ChatLogReplay.Channel.Party",
            "Alliance" => "Feature.ChatLogReplay.Channel.Alliance",
            "Yell" => "Feature.ChatLogReplay.Channel.Yell",
            "Shout" => "Feature.ChatLogReplay.Channel.Shout",
            "TellIncoming" or "TellOutgoing" => "Feature.ChatLogReplay.Channel.Tell",
            "FreeCompany" => "Feature.ChatLogReplay.Channel.FreeCompany",
            "NoviceNetwork" => "Feature.ChatLogReplay.Channel.NoviceNetwork",
            "Echo" => "Feature.ChatLogReplay.Channel.Echo",
            "ErrorMessage" => "Feature.ChatLogReplay.Channel.ErrorMessage",
            "GatheringSystemMessage" => "Feature.ChatLogReplay.Channel.Gathering",
            "LootNotice" => "Feature.ChatLogReplay.Channel.Loot",
            "Crafting" => "Feature.ChatLogReplay.Channel.Crafting",
            "BattleSystem" => "Feature.ChatLogReplay.Channel.BattleSystem",
            "PvPTeam" => "Feature.ChatLogReplay.Channel.PvpTeam",
            "StandardEmote" => "Feature.ChatLogReplay.Channel.StandardEmote",
            "CustomEmote" => "Feature.ChatLogReplay.Channel.CustomEmote",
            "RandomNumber" => "Feature.ChatLogReplay.Channel.RandomNumber",
            "ServerEcho" => "Feature.ChatLogReplay.Channel.ServerEcho",
            "PartyFinder" => "Feature.ChatLogReplay.Channel.PartyFinder",
            "Death" => "Feature.ChatLogReplay.Channel.Death",
            _ when channel.StartsWith("CrossLinkShell", StringComparison.Ordinal) ||
                   channel.StartsWith("Cwls", StringComparison.Ordinal) => "Feature.ChatLogReplay.Channel.CrossLinkshell",
            _ when channel.StartsWith("Ls", StringComparison.Ordinal) ||
                   channel.StartsWith("Linkshell", StringComparison.Ordinal) => "Feature.ChatLogReplay.Channel.Linkshell",
            _ => "Feature.ChatLogReplay.Channel.Other",
        };
}
