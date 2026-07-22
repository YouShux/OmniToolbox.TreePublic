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
using OmniToolbox.Config;
using OmniToolbox.Verification;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed unsafe class AutoPartyInviteFeature : TreeHouseFeatureBase
{
    private const int InviteDigitRunLength = 3;
    private const int InviteDelayMs = 250;
    private static readonly long InviteDelayTicks = Stopwatch.Frequency * InviteDelayMs / 1000;

    internal static readonly AutoPartyInviteChannel[] ChannelOptions =
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

    private static readonly HashSet<ushort> SupportedChannelIds = [];
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
    private readonly FeatureGate featureGate;
    private readonly Queue<PendingInvite> pendingInvites = [];
    private Hook<AddMsgSourceEntryDelegate>? messageSourceHook;

    static AutoPartyInviteFeature()
    {
        foreach (var option in ChannelOptions)
        {
            SupportedChannelIds.Add((ushort)option.Type);
        }
    }

    public AutoPartyInviteFeature(AutoPartyInviteConfig config, FeatureGate featureGate) : base(new(
        "auto-party-invite",
        "Feature.AutoPartyInvite.Title",
        "Feature.AutoPartyInvite.Description",
        TreeHouseCategory.InterfaceOperation,
        true))
    {
        this.config = config;
        this.featureGate = featureGate;
        AutoPartyInvitePanel.NormalizeConfig(config);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => AutoPartyInvitePanel.Draw(config);

    protected override void OnEnable()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        Hook<AddMsgSourceEntryDelegate>? hook = null;
        try
        {
            hook = DService.Instance().Hook.HookFromMemberFunction(
                typeof(RaptureLogModule.MemberFunctionPointers),
                nameof(RaptureLogModule.MemberFunctionPointers.AddMsgSourceEntry),
                (AddMsgSourceEntryDelegate)OnAddMsgSourceEntry);
            if (!FrameworkManager.Instance().Reg(OnUpdate))
            {
                throw new InvalidOperationException("Auto party invite framework registration failed.");
            }

            messageSourceHook = hook;
            hook.Enable();
        }
        catch
        {
            messageSourceHook = null;
            FrameworkManager.Instance().Unreg(OnUpdate);
            hook?.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            messageSourceHook?.Dispose();
        }
        finally
        {
            messageSourceHook = null;
            FrameworkManager.Instance().Unreg(OnUpdate);
            pendingInvites.Clear();
        }
    }

    private void OnAddMsgSourceEntry(
        RaptureLogModule* logModule,
        ulong contentId,
        ulong accountId,
        int messageIndex,
        ushort worldId,
        ushort chatType)
    {
        messageSourceHook!.Original(logModule, contentId, accountId, messageIndex, worldId, chatType);

        try
        {
            HandleChatMessage(logModule, contentId, messageIndex, worldId, (XivChatType)chatType);
        }
        catch (Exception ex)
        {
            DService.Instance().Log.Warning(ex, "Auto party invite failed to handle a chat message.");
        }
    }

    private void HandleChatMessage(
        RaptureLogModule* logModule,
        ulong contentId,
        int messageIndex,
        ushort worldId,
        XivChatType chatType)
    {
        var services = DService.Instance();
        if (!IsEnabled ||
            !featureGate.CanUsePrivateFeature(out _) ||
            !services.ClientState.IsLoggedIn ||
            contentId == 0 ||
            contentId == services.PlayerState.ContentId ||
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

        var payloadWorldId = (ushort)playerPayload.World.RowId;
        if ((payloadWorldId == 0 && worldId == 0) || !CanInvite(contentId))
        {
            return;
        }

        pendingInvites.Enqueue(new(
            contentId,
            playerPayload.PlayerName,
            payloadWorldId == 0 ? worldId : payloadWorldId,
            chatType,
            Stopwatch.GetTimestamp() + InviteDelayTicks));
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
            !featureGate.CanUsePrivateFeature(out _) ||
            !IsInAllowedTerritory() ||
            !IsAllowedChannel(invite.ChatType) ||
            !CanInvite(invite.ContentId))
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
            inviteProxy->InviteToPartyInInstanceByContentId(invite.ContentId);
            return;
        }

        inviteProxy->InviteToParty(invite.ContentId, invite.PlayerName, invite.WorldId);
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

            if (runLength >= InviteDigitRunLength && config.EnabledDigits.Contains(character - '0'))
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
        config.AllowedTerritoryIds.Count > 0 &&
        config.AllowedTerritoryIds.Contains(DService.Instance().ClientState.TerritoryType);

    private bool IsAllowedChannel(XivChatType chatType) =>
        SupportedChannelIds.Contains((ushort)chatType) &&
        (!config.ChannelsInitialized || config.EnabledChannels.Contains((ushort)chatType));

    private static bool CanInvite(ulong contentId)
    {
        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn ||
            !services.PlayerState.IsLoaded ||
            contentId == 0 ||
            contentId == services.PlayerState.ContentId)
        {
            return false;
        }

        for (var index = 0; index < services.PartyList.Length; index++)
        {
            if (services.PartyList[index]?.ContentId == contentId)
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

    private delegate void AddMsgSourceEntryDelegate(
        RaptureLogModule* logModule,
        ulong contentId,
        ulong accountId,
        int messageIndex,
        ushort worldId,
        ushort chatType);

    private readonly record struct PendingInvite(
        ulong ContentId,
        string PlayerName,
        ushort WorldId,
        XivChatType ChatType,
        long ReadyAt);
}

internal readonly record struct AutoPartyInviteChannel(XivChatType Type, string LabelKey, int Number = 0);
