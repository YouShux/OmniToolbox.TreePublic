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
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe class AutoPartyInvite : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AutoPartyInviteTitle"),
        Description = OmniLoc.Get("AutoPartyInviteDescription"),
        Category = ModuleCategory.Automation,
        RequiresPrivateProvider = true
    };

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
    private readonly Queue<PendingInvite> pendingInvites = [];
    private Hook<AddMsgSourceEntryDelegate>? messageSourceHook;

    static AutoPartyInvite()
    {
        foreach (var option in ChannelOptions)
        {
            SupportedChannelIds.Add((ushort)option.Type);
        }
    }

    public AutoPartyInvite(AutoPartyInviteConfig config, System.Action saveConfig)
    {
        this.config = config;
        if (AutoPartyInvitePanel.NormalizeConfig(config))
        {
            saveConfig();
        }
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => AutoPartyInvitePanel.Draw(config);

    protected override void OnEnable()
    {
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

    protected override bool OnInterruptAutomation()
    {
        if (pendingInvites.Count == 0)
        {
            return false;
        }

        pendingInvites.Clear();
        return true;
    }

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
        catch (Exception ex)
        {
            DService.Instance().Log.Warning(ex, "Auto party invite failed to handle a chat message.");
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
        SupportedChannelIds.Contains((ushort)chatType) &&
        (!config.ChannelsInitialized || config.EnabledChannels.Contains((ushort)chatType));

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

    private delegate void AddMsgSourceEntryDelegate(
        RaptureLogModule* logModule,
        ulong contentID,
        ulong accountID,
        int messageIndex,
        ushort worldID,
        ushort chatType);

    private readonly record struct PendingInvite(
        ulong ContentID,
        string PlayerName,
        ushort WorldID,
        XivChatType ChatType,
        long ReadyAt);
}

internal readonly record struct AutoPartyInviteChannel(XivChatType Type, string LabelKey, int Number = 0);

[Serializable]
public sealed class AutoPartyInviteConfig
{
    public bool UseWhitelist { get; set; } = true;
    public bool ChannelsInitialized { get; set; }
    public HashSet<ushort> EnabledChannels { get; set; } = [];
    public HashSet<int> EnabledDigits { get; set; } = [1];
    public List<AutoPartyInviteTrigger> CustomTriggers { get; set; } = [];
    public HashSet<uint> BlacklistTerritoryIds { get; set; } = [];
    public HashSet<uint> WhitelistTerritoryIds { get; set; } = [];
    public HashSet<uint> AllowedTerritoryIds { get; set; } = [];
}

[Serializable]
public sealed class AutoPartyInviteTrigger
{
    public string Text { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

internal static class AutoPartyInvitePanel
{
    private static readonly string[] DigitRuns =
    [
        "000", "111", "222", "333", "444", "555", "666", "777", "888", "999"
    ];

    private static readonly TerritorySelector TerritorySelector = new("autoPartyInviteTerritory");
    private static string customTriggerInput = string.Empty;

    public static bool Draw(AutoPartyInviteConfig config)
    {
        var changed = DrawTriggers(config);
        ImGui.Spacing();
        changed |= DrawTerritorySelector(config);
        ImGui.Spacing();
        changed |= DrawChannels(config);
        return changed;
    }

    internal static bool NormalizeConfig(AutoPartyInviteConfig config)
    {
        var changed = false;
        config.EnabledChannels ??= [];
        config.EnabledDigits ??= [];
        config.CustomTriggers ??= [];
        config.AllowedTerritoryIds ??= [];
        config.BlacklistTerritoryIds ??= [];
        config.WhitelistTerritoryIds ??= [];
        if (config.AllowedTerritoryIds.Count > 0)
        {
            config.WhitelistTerritoryIds.UnionWith(config.AllowedTerritoryIds);
            config.AllowedTerritoryIds.Clear();
            config.UseWhitelist = true;
            changed = true;
        }

        changed |= config.EnabledDigits.RemoveWhere(static digit => digit is < 0 or > 9) > 0;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = config.CustomTriggers.Count - 1; index >= 0; index--)
        {
            var trigger = config.CustomTriggers[index];
            var text = trigger?.Text.Trim() ?? string.Empty;
            if (trigger is null || text.Length == 0 || IsDigitsOnly(text) || !seen.Add(text))
            {
                config.CustomTriggers.RemoveAt(index);
                changed = true;
                continue;
            }

            if (!string.Equals(trigger.Text, text, StringComparison.Ordinal))
            {
                trigger.Text = text;
                changed = true;
            }
        }

        for (var index = 1; index < config.CustomTriggers.Count; index++)
        {
            if (string.CompareOrdinal(config.CustomTriggers[index - 1].Text, config.CustomTriggers[index].Text) <= 0)
            {
                continue;
            }

            config.CustomTriggers.Sort(static (left, right) => string.CompareOrdinal(left.Text, right.Text));
            changed = true;
            break;
        }

        return changed;
    }

    private static bool DrawTerritorySelector(AutoPartyInviteConfig config)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.AutoPartyInvite.Territory.WorkMode"));
        ImGui.SameLine();
        var changed = false;
        if (ImGui.RadioButton(
                $"{OmniLoc.Get("Feature.AutoPartyInvite.Territory.Blacklist")}##autoPartyInviteBlacklist",
                !config.UseWhitelist))
        {
            config.UseWhitelist = false;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton(
                $"{OmniLoc.Get("Feature.AutoPartyInvite.Territory.Whitelist")}##autoPartyInviteWhitelist",
                config.UseWhitelist))
        {
            config.UseWhitelist = true;
            changed = true;
        }

        ImGui.SameLine();
        OmniControls.HelpIcon(OmniLoc.Get("Feature.AutoPartyInvite.Territory.WorkMode.Help"));
        ImGui.Spacing();
        changed |= TerritorySelector.Draw(
            config.UseWhitelist ? config.WhitelistTerritoryIds : config.BlacklistTerritoryIds,
            OmniLoc.Get(config.UseWhitelist
                ? "Feature.AutoPartyInvite.Territory.Whitelist.Empty"
                : "Feature.AutoPartyInvite.Territory.Blacklist.Empty"));
        return changed;
    }

    private static bool DrawTriggers(AutoPartyInviteConfig config)
    {
        var changed = false;
        var digitToggleMask = 0;
        AutoPartyInviteTrigger? toggledTrigger = null;
        var toggledTriggerEnabled = false;
        var addLabel = OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Add");
        var addButtonSize = OmniControls.CompactButtonSize(addLabel);
        ImGui.SetNextItemWidth(MathF.Max(
            1f,
            ImGui.GetContentRegionAvail().X - addButtonSize.X - ImGui.GetStyle().ItemSpacing.X -
            OmniTheme.BorderThickness() -
            OmniTheme.Scale(MathF.Max(0f, OmniTheme.Tokens.ShadowOffset))));
        OmniControls.InputTextWithHint(
            "##autoPartyInviteTriggerInput",
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Hint"),
            ref customTriggerInput,
            128);
        ImGui.SameLine();
        if (OmniControls.SmallButton(
                $"{addLabel}##autoPartyInviteTriggerAdd",
                false,
                addButtonSize))
        {
            changed |= AddCustomTrigger(config);
        }

        ImGui.Spacing();
        using var table = ImRaii.Table(
            "##autoPartyInviteTriggers",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                ImGui.GetFrameHeightWithSpacing() * 6.35f));
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Enabled"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(56f));
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Content"),
            ImGuiTableColumnFlags.WidthStretch,
            1.45f);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type"),
            ImGuiTableColumnFlags.WidthStretch,
            0.8f);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Action"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(86f));
        OmniControls.ScrollableTableHeadersRow();

        changed |= DrawTriggerState(
            config,
            true,
            ref digitToggleMask,
            ref toggledTrigger,
            ref toggledTriggerEnabled);
        changed |= DrawTriggerState(
            config,
            false,
            ref digitToggleMask,
            ref toggledTrigger,
            ref toggledTriggerEnabled);
        for (var digit = 0; digit < DigitRuns.Length; digit++)
        {
            if ((digitToggleMask & (1 << digit)) == 0)
            {
                continue;
            }

            if (!config.EnabledDigits.Remove(digit))
            {
                config.EnabledDigits.Add(digit);
            }
        }

        if (toggledTrigger is not null)
        {
            toggledTrigger.Enabled = toggledTriggerEnabled;
        }

        return changed;
    }

    private static bool DrawTriggerState(
        AutoPartyInviteConfig config,
        bool enabledState,
        ref int digitToggleMask,
        ref AutoPartyInviteTrigger? toggledTrigger,
        ref bool toggledTriggerEnabled)
    {
        var changed = false;
        for (var digit = 0; digit < DigitRuns.Length; digit++)
        {
            if (config.EnabledDigits.Contains(digit) != enabledState)
            {
                continue;
            }

            ImGui.PushID($"autoPartyInviteDigit{digit}");
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = enabledState;
            if (OmniControls.Checkbox("##enabled", ref enabled))
            {
                digitToggleMask |= 1 << digit;
                changed = true;
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(string.Format(
                OmniLoc.Get("Feature.AutoPartyInvite.Trigger.DigitFormat"),
                digit,
                DigitRuns[digit]));
            ImGui.TableNextColumn();
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type.Digit"));
            ImGui.TableNextColumn();
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Fixed"));
            ImGui.PopID();
        }

        for (var index = 0; index < config.CustomTriggers.Count; index++)
        {
            var trigger = config.CustomTriggers[index];
            if (trigger.Enabled != enabledState)
            {
                continue;
            }

            ImGui.PushID($"autoPartyInviteCustom{index}");
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = trigger.Enabled;
            if (OmniControls.Checkbox("##enabled", ref enabled))
            {
                toggledTrigger = trigger;
                toggledTriggerEnabled = enabled;
                changed = true;
            }

            ImGui.TableNextColumn();
            ImGui.TextWrapped(trigger.Text);
            ImGui.TableNextColumn();
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type.Exact"));
            ImGui.TableNextColumn();
            if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Delete")}##delete", false))
            {
                config.CustomTriggers.RemoveAt(index);
                index--;
                changed = true;
            }

            ImGui.PopID();
        }

        return changed;
    }

    private static bool AddCustomTrigger(AutoPartyInviteConfig config)
    {
        var text = customTriggerInput.Trim();
        if (text.Length == 0 || IsDigitsOnly(text))
        {
            return false;
        }

        for (var index = 0; index < config.CustomTriggers.Count; index++)
        {
            if (string.Equals(config.CustomTriggers[index].Text, text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        config.CustomTriggers.Add(new()
        {
            Text = text,
            Enabled = true
        });
        config.CustomTriggers.Sort(static (left, right) => string.CompareOrdinal(left.Text, right.Text));
        customTriggerInput = string.Empty;
        return true;
    }

    private static bool DrawChannels(AutoPartyInviteConfig config)
    {
        var changed = EnsureChannelsInitialized(config);
        if (!OmniControls.CollapsingHeader(
                string.Format(
                    OmniLoc.Get("Feature.AutoPartyInvite.Channel.Title"),
                    config.EnabledChannels.Count),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            if (config.EnabledChannels.Count == 0)
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Channel.EmptySelection"));
            }

            return changed;
        }

        if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.AutoPartyInvite.Channel.SelectAll")}##channelSelectAll", false))
        {
            for (var index = 0; index < AutoPartyInvite.ChannelOptions.Length; index++)
            {
                changed |= config.EnabledChannels.Add((ushort)AutoPartyInvite.ChannelOptions[index].Type);
            }
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.AutoPartyInvite.Channel.Clear")}##channelClear", false))
        {
            changed |= config.EnabledChannels.Count > 0;
            config.EnabledChannels.Clear();
        }

        ImGui.Spacing();
        var columnCount = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / OmniTheme.Scale(150f)), 1, 4);
        using (var table = ImRaii.Table(
                   "##autoPartyInviteChannels",
                   columnCount,
                   ImGuiTableFlags.SizingStretchSame,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                for (var index = 0; index < AutoPartyInvite.ChannelOptions.Length; index++)
                {
                    if (index % columnCount == 0)
                    {
                        ImGui.TableNextRow();
                    }

                    var option = AutoPartyInvite.ChannelOptions[index];
                    ImGui.TableSetColumnIndex(index % columnCount);
                    var channelID = (ushort)option.Type;
                    var enabled = config.EnabledChannels.Contains(channelID);
                    if (OmniControls.Checkbox($"{GetChannelLabel(option)}##autoPartyInviteChannel{channelID}", ref enabled))
                    {
                        if (enabled)
                        {
                            config.EnabledChannels.Add(channelID);
                        }
                        else
                        {
                            config.EnabledChannels.Remove(channelID);
                        }

                        changed = true;
                    }
                }
            }
        }

        if (config.EnabledChannels.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Channel.EmptySelection"));
        }

        return changed;
    }

    private static bool EnsureChannelsInitialized(AutoPartyInviteConfig config)
    {
        if (config.ChannelsInitialized)
        {
            return false;
        }

        config.EnabledChannels.Clear();
        for (var index = 0; index < AutoPartyInvite.ChannelOptions.Length; index++)
        {
            config.EnabledChannels.Add((ushort)AutoPartyInvite.ChannelOptions[index].Type);
        }

        config.ChannelsInitialized = true;
        return true;
    }

    private static string GetChannelLabel(AutoPartyInviteChannel option) =>
        option.Number == 0
            ? OmniLoc.Get(option.LabelKey)
            : string.Format(OmniLoc.Get(option.LabelKey), option.Number);

    private static bool IsDigitsOnly(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsDigit(text[index]))
            {
                return false;
            }
        }

        return text.Length > 0;
    }
}
