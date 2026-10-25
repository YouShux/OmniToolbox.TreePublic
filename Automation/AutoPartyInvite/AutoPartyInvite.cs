using TreeTools.Lifecycle;
using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class AutoPartyInvite : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Automation,
        RequiresPrivateProvider = true
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "AutoPartyInviteTitle",
            ["Description"] = "AutoPartyInviteDescription",
            ["Feature.AutoPartyInvite.Channel.Alliance"] = "Feature.AutoPartyInvite.Channel.Alliance",
            ["Feature.AutoPartyInvite.Channel.Clear"] = "Feature.AutoPartyInvite.Channel.Clear",
            ["Feature.AutoPartyInvite.Channel.CrossLinkshell"] = "Feature.AutoPartyInvite.Channel.CrossLinkshell",
            ["Feature.AutoPartyInvite.Channel.CrossParty"] = "Feature.AutoPartyInvite.Channel.CrossParty",
            ["Feature.AutoPartyInvite.Channel.FreeCompany"] = "Feature.AutoPartyInvite.Channel.FreeCompany",
            ["Feature.AutoPartyInvite.Channel.Linkshell"] = "Feature.AutoPartyInvite.Channel.Linkshell",
            ["Feature.AutoPartyInvite.Channel.NoviceNetwork"] = "Feature.AutoPartyInvite.Channel.NoviceNetwork",
            ["Feature.AutoPartyInvite.Channel.Party"] = "Feature.AutoPartyInvite.Channel.Party",
            ["Feature.AutoPartyInvite.Channel.PvPTeam"] = "Feature.AutoPartyInvite.Channel.PvPTeam",
            ["Feature.AutoPartyInvite.Channel.Say"] = "Feature.AutoPartyInvite.Channel.Say",
            ["Feature.AutoPartyInvite.Channel.SelectAll"] = "Feature.AutoPartyInvite.Channel.SelectAll",
            ["Feature.AutoPartyInvite.Channel.Shout"] = "Feature.AutoPartyInvite.Channel.Shout",
            ["Feature.AutoPartyInvite.Channel.TellIncoming"] = "Feature.AutoPartyInvite.Channel.TellIncoming",
            ["Feature.AutoPartyInvite.Channel.Title"] = "Feature.AutoPartyInvite.Channel.Title",
            ["Feature.AutoPartyInvite.Channel.Yell"] = "Feature.AutoPartyInvite.Channel.Yell",
            ["Feature.AutoPartyInvite.Territory.Blacklist"] = "Feature.AutoPartyInvite.Territory.Blacklist",
            ["Feature.AutoPartyInvite.Territory.Whitelist"] = "Feature.AutoPartyInvite.Territory.Whitelist",
            ["Feature.AutoPartyInvite.Territory.WorkMode"] = "Feature.AutoPartyInvite.Territory.WorkMode",
            ["Feature.AutoPartyInvite.Territory.WorkMode.Help"] = "Feature.AutoPartyInvite.Territory.WorkMode.Help",
            ["Feature.AutoPartyInvite.Trigger.Action"] = "Feature.AutoPartyInvite.Trigger.Action",
            ["Feature.AutoPartyInvite.Trigger.Add"] = "Feature.AutoPartyInvite.Trigger.Add",
            ["Feature.AutoPartyInvite.Trigger.Content"] = "Feature.AutoPartyInvite.Trigger.Content",
            ["Feature.AutoPartyInvite.Trigger.Delete"] = "Feature.AutoPartyInvite.Trigger.Delete",
            ["Feature.AutoPartyInvite.Trigger.DigitFormat"] = "Feature.AutoPartyInvite.Trigger.DigitFormat",
            ["Feature.AutoPartyInvite.Trigger.Enabled"] = "Feature.AutoPartyInvite.Trigger.Enabled",
            ["Feature.AutoPartyInvite.Trigger.Fixed"] = "Feature.AutoPartyInvite.Trigger.Fixed",
            ["Feature.AutoPartyInvite.Trigger.Hint"] = "Feature.AutoPartyInvite.Trigger.Hint",
            ["Feature.AutoPartyInvite.Trigger.Type"] = "Feature.AutoPartyInvite.Trigger.Type",
            ["Feature.AutoPartyInvite.Trigger.Type.Digit"] = "Feature.AutoPartyInvite.Trigger.Type.Digit",
            ["Feature.AutoPartyInvite.Trigger.Type.Exact"] = "Feature.AutoPartyInvite.Trigger.Type.Exact"
        });

    public override bool HasSettings => true;

    internal static readonly Channel[] ChannelOptions =
    [
        new(XivChatType.Say, "Feature.AutoPartyInvite.Channel.Say"),
        new(XivChatType.Shout, "Feature.AutoPartyInvite.Channel.Shout"),
        new(XivChatType.Yell, "Feature.AutoPartyInvite.Channel.Yell"),
        new(XivChatType.TellIncoming, "Feature.AutoPartyInvite.Channel.TellIncoming"),
        new(XivChatType.Party, "Feature.AutoPartyInvite.Channel.Party"),
        new(XivChatType.CrossParty, "Feature.AutoPartyInvite.Channel.CrossParty"),
        new(XivChatType.Alliance, "Feature.AutoPartyInvite.Channel.Alliance"),
        new(XivChatType.FreeCompany, "Feature.AutoPartyInvite.Channel.FreeCompany"),
        new(XivChatType.NoviceNetwork, "Feature.AutoPartyInvite.Channel.NoviceNetwork"),
        new(XivChatType.PvPTeam, "Feature.AutoPartyInvite.Channel.PvPTeam"),
        new(XivChatType.Ls1, "Feature.AutoPartyInvite.Channel.Linkshell", 1),
        new(XivChatType.Ls2, "Feature.AutoPartyInvite.Channel.Linkshell", 2),
        new(XivChatType.Ls3, "Feature.AutoPartyInvite.Channel.Linkshell", 3),
        new(XivChatType.Ls4, "Feature.AutoPartyInvite.Channel.Linkshell", 4),
        new(XivChatType.Ls5, "Feature.AutoPartyInvite.Channel.Linkshell", 5),
        new(XivChatType.Ls6, "Feature.AutoPartyInvite.Channel.Linkshell", 6),
        new(XivChatType.Ls7, "Feature.AutoPartyInvite.Channel.Linkshell", 7),
        new(XivChatType.Ls8, "Feature.AutoPartyInvite.Channel.Linkshell", 8),
        new(XivChatType.CrossLinkShell1, "Feature.AutoPartyInvite.Channel.CrossLinkshell", 1),
        new(XivChatType.CrossLinkShell2, "Feature.AutoPartyInvite.Channel.CrossLinkshell", 2),
        new(XivChatType.CrossLinkShell3, "Feature.AutoPartyInvite.Channel.CrossLinkshell", 3),
        new(XivChatType.CrossLinkShell4, "Feature.AutoPartyInvite.Channel.CrossLinkshell", 4),
        new(XivChatType.CrossLinkShell5, "Feature.AutoPartyInvite.Channel.CrossLinkshell", 5),
        new(XivChatType.CrossLinkShell6, "Feature.AutoPartyInvite.Channel.CrossLinkshell", 6),
        new(XivChatType.CrossLinkShell7, "Feature.AutoPartyInvite.Channel.CrossLinkshell", 7),
        new(XivChatType.CrossLinkShell8, "Feature.AutoPartyInvite.Channel.CrossLinkshell", 8)
    ];

    private static readonly HashSet<ushort> SupportedChannelIDs = [];
    private static readonly HashSet<TerritoryIntendedUse> InvitableInstanceTypes =
    [
        TerritoryIntendedUse.Eureka,
        TerritoryIntendedUse.Diadem,
        TerritoryIntendedUse.Bozja,
        TerritoryIntendedUse.DelubrumReginae,
        TerritoryIntendedUse.DelubrumReginaeSavage,
        TerritoryIntendedUse.OccultCrescent
    ];

    private readonly AutoPartyInviteConfig config;
    private readonly Queue<PendingInvite> pendingInvites = [];
    private readonly Panel panel = new();
    private Hook<AddMsgSourceEntryDelegate>? messageSourceHook;

    static AutoPartyInvite()
    {
        foreach (var option in ChannelOptions)
        {
            SupportedChannelIDs.Add((ushort)option.Type);
        }
    }

    internal AutoPartyInvite(AutoPartyInviteConfig config, System.Action saveConfig)
    {
        this.config = config;
        if (Panel.NormalizeConfig(config))
        {
            saveConfig();
        }
    }

    private delegate void AddMsgSourceEntryDelegate(
        RaptureLogModule* logModule,
        ulong contentID,
        ulong accountID,
        int messageIndex,
        ushort worldID,
        ushort chatType);

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnUpdate));
        messageSourceHook = DService.Instance().Hook.HookFromMemberFunction(
            typeof(RaptureLogModule.MemberFunctionPointers),
            nameof(RaptureLogModule.MemberFunctionPointers.AddMsgSourceEntry),
            (AddMsgSourceEntryDelegate)OnAddMsgSourceEntry);
        activation.Add(messageSourceHook);
        activation.OnStopping(messageSourceHook.Disable);
        if (!FrameworkManager.Instance().Reg(OnUpdate))
        {
            throw new InvalidOperationException("Auto party invite framework registration failed.");
        }
        messageSourceHook.Enable();
    }

    protected override void OnStop()
    {
        messageSourceHook = null;
        pendingInvites.Clear();
    }

    protected override bool OnInterruptAutomation()
    {
        if (pendingInvites.Count == 0)
        {
            return false;
        }

        pendingInvites.Clear();
        return true;
    }

    public override bool DrawSettings() => panel.Draw(config);

    private void OnAddMsgSourceEntry(
        RaptureLogModule* logModule,
        ulong contentID,
        ulong accountID,
        int messageIndex,
        ushort worldID,
        ushort chatType)
    {
        messageSourceHook!.Original(logModule, contentID, accountID, messageIndex, worldID, chatType);

        try
        {
            HandleChatMessage(logModule, contentID, messageIndex, worldID, (XivChatType)chatType);
        }
        catch (Exception)
        {
        }
    }

    private void HandleChatMessage(
        RaptureLogModule* logModule,
        ulong contentID,
        int messageIndex,
        ushort worldID,
        XivChatType chatType)
    {
        var services = DService.Instance();
        if (!IsEnabled ||
            !services.ClientState.IsLoggedIn ||
            contentID == 0 ||
            contentID == services.PlayerState.ContentId ||
            !IsInAllowedTerritory() ||
            !IsAllowedChannel(chatType) ||
            logModule == null ||
            !logModule->GetLogMessageDetail(messageIndex, out var sender, out var message, out _, out _, out _, out _))
        {
            return;
        }

        var text = SeString.Parse(message.AsSpan()).TextValue;
        if (text.Length == 0 || !HasEnabledTrigger(text))
        {
            return;
        }

        PlayerPayload? playerPayload = null;
        foreach (var payload in SeString.Parse(sender.AsSpan()).Payloads)
        {
            if (payload is PlayerPayload player)
            {
                playerPayload = player;
                break;
            }
        }

        if (playerPayload is null || string.IsNullOrWhiteSpace(playerPayload.PlayerName))
        {
            return;
        }

        var payloadWorldID = (ushort)playerPayload.World.RowId;
        if ((payloadWorldID == 0 && worldID == 0) || !CanInvite(contentID))
        {
            return;
        }

        pendingInvites.Enqueue(new(
            contentID,
            playerPayload.PlayerName,
            payloadWorldID == 0 ? worldID : payloadWorldID,
            chatType,
            Stopwatch.GetTimestamp() + Stopwatch.Frequency * 250 / 1000));
    }

    private void OnUpdate(IFramework _)
    {
        var currentTick = Stopwatch.GetTimestamp();
        while (pendingInvites.TryPeek(out var invite) && invite.ReadyAt <= currentTick)
        {
            pendingInvites.Dequeue();
            ExecuteInvite(invite);
        }
    }

    private void ExecuteInvite(PendingInvite invite)
    {
        if (!IsEnabled ||
            !IsInAllowedTerritory() ||
            !IsAllowedChannel(invite.ChatType) ||
            !CanInvite(invite.ContentID))
        {
            return;
        }

        var inviteProxy = InfoProxyPartyInvite.Instance();
        if (inviteProxy == null)
        {
            return;
        }

        if (DService.Instance().Condition[ConditionFlag.BoundByDuty56] &&
            InvitableInstanceTypes.Contains(GameState.TerritoryIntendedUse))
        {
            inviteProxy->InviteToPartyInInstanceByContentId(invite.ContentID);
            return;
        }

        inviteProxy->InviteToParty(invite.ContentID, invite.PlayerName, invite.WorldID);
    }

    private bool HasEnabledTrigger(string text)
    {
        for (var index = 0; index < text.Length;)
        {
            var character = text[index];
            if (character is < '0' or > '9')
            {
                index++;
                continue;
            }

            var runLength = 1;
            index++;
            while (index < text.Length && text[index] == character)
            {
                runLength++;
                index++;
            }

            if (runLength >= 3 && config.EnabledDigits.Contains(character - '0'))
            {
                return true;
            }
        }

        for (var index = 0; index < config.CustomTriggers.Count; index++)
        {
            var trigger = config.CustomTriggers[index];
            if (trigger.Enabled &&
                trigger.Text.Length > 0 &&
                string.Equals(trigger.Text, text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInAllowedTerritory() =>
        config.UseWhitelist
            ? config.WhitelistTerritoryIds.Contains(DService.Instance().ClientState.TerritoryType)
            : !config.BlacklistTerritoryIds.Contains(DService.Instance().ClientState.TerritoryType);

    private bool IsAllowedChannel(XivChatType chatType) =>
        SupportedChannelIDs.Contains((ushort)chatType) &&
        config.EnabledChannels.Contains((ushort)chatType);

    private static bool CanInvite(ulong contentID)
    {
        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn ||
            !services.PlayerState.IsLoaded ||
            contentID == 0 ||
            contentID == services.PlayerState.ContentId)
        {
            return false;
        }

        for (var index = 0; index < services.PartyList.Length; index++)
        {
            if (services.PartyList[index]?.ContentId == contentID)
            {
                return false;
            }
        }

        var groupManager = GroupManager.Instance();
        if (groupManager == null)
        {
            return false;
        }

        var group = groupManager->GetGroup();
        var memberCount = group == null ? 0 : group->MemberCount;
        if (memberCount >= 8)
        {
            return false;
        }

        return memberCount == 0 ||
               services.PlayerState.EntityId != 0 &&
               groupManager->MainGroup.IsEntityIdPartyLeader(services.PlayerState.EntityId);
    }

    internal readonly record struct Channel(XivChatType Type, string LabelKey, int Number = 0);

    internal sealed class Trigger
    {
        public string Text { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;
    }

    private readonly record struct PendingInvite(
        ulong ContentID,
        string PlayerName,
        ushort WorldID,
        XivChatType ChatType,
        long ReadyAt);
}
