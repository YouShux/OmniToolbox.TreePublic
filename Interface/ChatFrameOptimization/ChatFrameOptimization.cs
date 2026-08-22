using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Lumina.Text.ReadOnly;
using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Dalamud.Helpers;
using OmenTools.Interop.Game;
using OmenTools.Interop.Game.Models;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class ChatFrameOptimization(
    ChatFrameOptimizationConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ChatFrameOptimizationTitle"),
        Description = OmniLoc.Get("ChatFrameOptimizationDescription"),
        Category = ModuleCategory.Interface,
        Commands =
        [
            new("Feature.ChatFrameOptimization.CommandDescription", "/omni 日志回放")
        ]
    };

    private static readonly CompSig ScrollToBottomSignature = new(
        "E8 ?? ?? ?? ?? 48 8B 43 10 33 D2");

    private const string StickyShoutAutoSwitchSignature = "05 75 0C 8B D7 E8 ?? ?? ?? ?? E9";

    private static readonly Regex URLRegex = new(
        @"(http|ftp|https)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ChatFrameOptimizationPanel panel = new(config);
    private readonly ChatLogReplayWindow replayWindow = new();
    private FeatureLifetime? runtimeLifetime;
    private ChatLogReplayStorage? storage;
    private Hook<ScrollToBottomDelegate>? scrollToBottomHook;
    private Hook<RaptureShellModule.Delegates.ChangeChatChannel>? changeChatChannelHook;
    private MemoryPatch? stickyShoutAutoSwitchPatch;
    private DalamudLinkPayload? urlPayload;
    private uint urlCommandID;

    private delegate void* ScrollToBottomDelegate(LogViewer* logViewer);

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = panel.Draw(config, storage, IsEnabled, OpenReplay);
        if (changed)
        {
            stickyShoutAutoSwitchPatch?.Set(config.StickyChat);
        }

        return changed;
    }

    public bool OpenReplay()
    {
        if (storage is null)
        {
            return false;
        }

        replayWindow.Open(storage);
        return true;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            storage = new(config);
            lifetime.Add(storage.Dispose);
            lifetime.Add(replayWindow.Close);

            DalamudServices.PluginInterface.UiBuilder.Draw += replayWindow.Draw;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= replayWindow.Draw);

            DalamudServices.ChatGUI.ChatMessage += OnChatMessage;
            lifetime.Add(() => DalamudServices.ChatGUI.ChatMessage -= OnChatMessage);
            DalamudServices.ChatGUI.CheckMessageHandled += OnCheckMessageHandled;
            lifetime.Add(() => DalamudServices.ChatGUI.CheckMessageHandled -= OnCheckMessageHandled);

            var linkManager = LinkPayloadManager.Instance();
            urlPayload = linkManager.Reg(OnURLLinkClicked, out urlCommandID);
            var commandID = urlCommandID;
            lifetime.Add(() => linkManager.Unreg(commandID));

            var chatManager = ChatManager.Instance();
            if (!chatManager.RegPostExecuteCommandInner(OnPostExecuteCommand))
            {
                throw new InvalidOperationException("Sticky-chat callback registration failed.");
            }

            lifetime.Add(() => chatManager.Unreg(OnPostExecuteCommand));

            if (DService.Instance().SigScanner.TryScanText(StickyShoutAutoSwitchSignature, out var stickyShoutAddress))
            {
                stickyShoutAutoSwitchPatch = new(stickyShoutAddress, "FE");
                lifetime.Add(stickyShoutAutoSwitchPatch.Dispose);
                stickyShoutAutoSwitchPatch.Set(config.StickyChat);
            }

            changeChatChannelHook = DService.Instance().Hook.HookFromAddress<RaptureShellModule.Delegates.ChangeChatChannel>(
                DalamudReflector.GetMemberFuncByName(
                    typeof(RaptureShellModule.MemberFunctionPointers),
                    nameof(RaptureShellModule.ChangeChatChannel)),
                OnChangeChatChannel);
            lifetime.Add(changeChatChannelHook.Dispose);
            changeChatChannelHook.Enable();

            scrollToBottomHook = ScrollToBottomSignature.GetHook<ScrollToBottomDelegate>(OnScrollToBottom);
            lifetime.Add(scrollToBottomHook.Dispose);
            scrollToBottomHook.Enable();
            runtimeLifetime = lifetime;
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                ClearRuntimeReferences();
            }

            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            ClearRuntimeReferences();
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            if (config.AutoSaveLogs && !message.IsHandled)
            {
                storage?.QueueLog(ChatFrameOptimizationLogFormatter.Create(message));
            }

            if (config.ClickableLinks && TryBuildClickableLinks(message.LogKind, message.Message, out var modified))
            {
                message.Message = modified;
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Chat-frame message processing failed.");
        }
    }

    private void OnCheckMessageHandled(IHandleableChatMessage message)
    {
        try
        {
            if (config.StickyChat &&
                message.LogKind == XivChatType.ErrorMessage &&
                message.Message.TextValue.Contains("/shout", StringComparison.OrdinalIgnoreCase))
            {
                SetChatChannel(5);
            }

            if (!config.ClickableLinks)
            {
                return;
            }

            if (TryBuildClickableLinks(message.LogKind, message.Message, out var modified))
            {
                message.Message = modified;
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Chat-frame message preprocessing failed.");
        }
    }

    private bool TryBuildClickableLinks(XivChatType chatType, SeString message, out SeString modified)
    {
        modified = message;
        if (urlPayload is null || IsBattleType(chatType))
        {
            return false;
        }

        if (!ContainsUnlinkedURL(message))
        {
            return false;
        }

        var linkDepth = 0;
        var payloads = new List<Payload>(message.Payloads.Count);
        foreach (var payload in message.Payloads)
        {
            if (payload is DalamudLinkPayload)
            {
                linkDepth++;
                payloads.Add(payload);
                continue;
            }

            if (linkDepth > 0 && payload is RawPayload raw && RawPayloadEquals(raw, RawPayload.LinkTerminator))
            {
                linkDepth--;
                payloads.Add(payload);
                continue;
            }

            if (linkDepth != 0 || payload is not TextPayload textPayload)
            {
                payloads.Add(payload);
                continue;
            }

            var text = textPayload.Text ?? string.Empty;
            var matches = URLRegex.Matches(text);
            if (matches.Count == 0)
            {
                payloads.Add(payload);
                continue;
            }

            var lastIndex = 0;
            foreach (Match match in matches)
            {
                if (match.Index > lastIndex)
                {
                    payloads.Add(new TextPayload(text[lastIndex..match.Index]));
                }

                payloads.Add(urlPayload);
                payloads.Add(new TextPayload(match.Value));
                payloads.Add(RawPayload.LinkTerminator);
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                payloads.Add(new TextPayload(text[lastIndex..]));
            }

        }

        modified = new(payloads);
        return true;
    }

    private static bool ContainsUnlinkedURL(SeString message)
    {
        var linkDepth = 0;
        foreach (var payload in message.Payloads)
        {
            if (payload is DalamudLinkPayload)
            {
                linkDepth++;
                continue;
            }

            if (linkDepth > 0 && payload is RawPayload raw && RawPayloadEquals(raw, RawPayload.LinkTerminator))
            {
                linkDepth--;
                continue;
            }

            if (linkDepth == 0 && payload is TextPayload text && URLRegex.IsMatch(text.Text ?? string.Empty))
            {
                return true;
            }
        }

        return false;
    }

    private void OnURLLinkClicked(uint commandID, SeString clicked)
    {
        if (commandID != urlCommandID)
        {
            return;
        }

        var value = clicked.TextValue.Trim().Replace("\u00A0", string.Empty, StringComparison.Ordinal);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeFtp, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Failed to open chat link: {Url}", uri.AbsoluteUri);
        }
    }

    private void OnPostExecuteCommand(ReadOnlySeString command)
    {
        if (!config.StickyChat)
        {
            return;
        }

        try
        {
            var input = command.ToString();
            ApplyStickyChat(input);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Sticky-chat channel update failed.");
        }
    }

    private static void ApplyStickyChat(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (input.StartsWith("/party ", StringComparison.Ordinal) ||
            input.StartsWith("/p ", StringComparison.Ordinal))
        {
            SetChatChannel(2);
            return;
        }

        if (input.StartsWith("/say ", StringComparison.Ordinal) ||
            input.StartsWith("/s ", StringComparison.Ordinal))
        {
            SetChatChannel(1);
            return;
        }

        if (input.StartsWith("/alliance ", StringComparison.Ordinal) ||
            input.StartsWith("/a ", StringComparison.Ordinal))
        {
            SetChatChannel(3);
            return;
        }

        if (input.StartsWith("/freecompany ", StringComparison.Ordinal) ||
            input.StartsWith("/fc ", StringComparison.Ordinal))
        {
            SetChatChannel(6);
            return;
        }

        if (input.StartsWith("/novice ", StringComparison.Ordinal) ||
            input.StartsWith("/beginner ", StringComparison.Ordinal) ||
            input.StartsWith("/n ", StringComparison.Ordinal))
        {
            SetChatChannel(8);
            return;
        }

        if (input.StartsWith("/yell ", StringComparison.Ordinal) ||
            input.StartsWith("/y ", StringComparison.Ordinal))
        {
            SetChatChannel(4);
            return;
        }

        if (input.StartsWith("/shout ", StringComparison.Ordinal) ||
            input.StartsWith("/sh ", StringComparison.Ordinal) ||
            input.StartsWith("/喊话频道 ", StringComparison.Ordinal) ||
            input.StartsWith("/喊 ", StringComparison.Ordinal))
        {
            SetChatChannel(5);
            return;
        }

        if (TryGetNumberedChannel(input, "/cwl", "/cwlinkshell", out var crossWorld))
        {
            SetChatChannel(crossWorld + 8);
            return;
        }

        if (TryGetNumberedChannel(input, "/l", "/linkshell", out var linkshell))
        {
            SetChatChannel(linkshell + 18);
        }
    }

    private static bool TryGetNumberedChannel(
        string input,
        string shortCommand,
        string longCommand,
        out int channel) =>
        TryGetNumberedChannel(input, shortCommand, out channel) ||
        TryGetNumberedChannel(input, longCommand, out channel);

    private static bool TryGetNumberedChannel(string input, string command, out int channel)
    {
        channel = 0;
        var numberIndex = command.Length;
        return input.Length > numberIndex + 1 &&
               input.StartsWith(command, StringComparison.Ordinal) &&
               input[numberIndex] is >= '1' and <= '8' &&
               input[numberIndex + 1] == ' ' &&
               int.TryParse(
                   input.AsSpan(numberIndex, 1),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out channel);
    }

    private static void SetChatChannel(int chatType)
    {
        var shell = RaptureShellModule.Instance();
        if (shell is null)
        {
            return;
        }

        using var target = new Utf8String();
        shell->ChangeChatChannel(
            chatType,
            chatType is >= 9 and <= 16 ? (uint)(chatType - 9) : chatType is >= 19 and <= 26 ? (uint)(chatType - 19) : 0,
            &target,
            true);
    }

    private bool OnChangeChatChannel(
        RaptureShellModule* shell,
        int channel,
        uint linkshellIndex,
        Utf8String* target,
        bool setChatType)
    {
        var result = changeChatChannelHook!.Original(shell, channel, linkshellIndex, target, setChatType);
        if (result && config.StickyChat && !setChatType)
        {
            shell->ChatType = channel;
            shell->CurrentChannel.SetString(channel == 5 ? "/shout" : shell->TempChatCommand.ToString());
            shell->TempChatType = -2;
        }

        return result;
    }

    private void* OnScrollToBottom(LogViewer* logViewer)
    {
        if (config.SmartAutoScroll && ShouldPreventScroll(logViewer))
        {
            return null;
        }

        return scrollToBottomHook!.Original(logViewer);
    }

    private static bool ShouldPreventScroll(LogViewer* logViewer) =>
        logViewer is not null &&
        logViewer->ChatLogPanel is not null &&
        logViewer->TotalLineCount != uint.MaxValue &&
        logViewer->TotalLineCount > logViewer->LastLineVisible;

    private static bool IsBattleType(XivChatType type) =>
        ((int)type & 0x7F) is 41 or 42 or 43 or 44 or 45 or 46 or 47 or 48 or 49 or 58;

    private static bool RawPayloadEquals(RawPayload left, RawPayload right)
    {
        var leftData = left.Data;
        var rightData = right.Data;
        if (leftData.Length != rightData.Length)
        {
            return false;
        }

        for (var index = 0; index < leftData.Length; index++)
        {
            if (leftData[index] != rightData[index])
            {
                return false;
            }
        }

        return true;
    }

    private void ClearRuntimeReferences()
    {
        runtimeLifetime = null;
        storage = null;
        scrollToBottomHook = null;
        changeChatChannelHook = null;
        stickyShoutAutoSwitchPatch = null;
        urlPayload = null;
        urlCommandID = 0;
        replayWindow.Close();
    }
}

[Serializable]
public sealed class ChatFrameOptimizationConfig
{
    public bool SmartAutoScroll { get; set; } = true;

    public bool StickyChat { get; set; } = true;

    public bool ClickableLinks { get; set; } = true;

    public bool AutoSaveLogs { get; set; } = true;

    public int LogMaxFileSizeKb { get; set; } = 1024;

    public int LogMaxFileCount { get; set; } = 20;
}

internal sealed class ChatFrameOptimizationPanel
{
    private int logFileSizeKb;
    private int logFileCount;

    public ChatFrameOptimizationPanel(ChatFrameOptimizationConfig config)
    {
        logFileSizeKb = ChatLogReplayStorage.NormalizeFileSize(config.LogMaxFileSizeKb);
        logFileCount = ChatLogReplayStorage.NormalizeFileCount(config.LogMaxFileCount);
    }

    public bool Draw(
        ChatFrameOptimizationConfig config,
        ChatLogReplayStorage? storage,
        bool isEnabled,
        Func<bool> openReplay)
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##chatFrameOptimizationSettings",
                   4,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var smartAutoScroll = config.SmartAutoScroll;
                if (DrawCheckbox(
                        "Feature.ChatFrameOptimization.SmartAutoScroll",
                        "smartAutoScroll",
                        ref smartAutoScroll,
                        "Feature.ChatFrameOptimization.SmartAutoScroll.Help"))
                {
                    config.SmartAutoScroll = smartAutoScroll;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var stickyChat = config.StickyChat;
                if (DrawCheckbox(
                        "Feature.ChatFrameOptimization.StickyChat",
                        "stickyChat",
                        ref stickyChat,
                        "Feature.ChatFrameOptimization.StickyChat.Help"))
                {
                    config.StickyChat = stickyChat;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var clickableLinks = config.ClickableLinks;
                if (DrawCheckbox(
                        "Feature.ChatFrameOptimization.ClickableLinks",
                        "clickableLinks",
                        ref clickableLinks))
                {
                    config.ClickableLinks = clickableLinks;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var autoSaveLogs = config.AutoSaveLogs;
                if (DrawCheckbox(
                        "Feature.ChatFrameOptimization.AutoSaveLogs",
                        "autoSaveLogs",
                        ref autoSaveLogs))
                {
                    config.AutoSaveLogs = autoSaveLogs;
                    changed = true;
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
                OmniControls.InputInt(
                    $"{OmniLoc.Get("Feature.ChatFrameOptimization.LogFileSizeKb")}##chatFrameOptimizationLogFileSize",
                    ref logFileSizeKb);
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    logFileSizeKb = ChatLogReplayStorage.NormalizeFileSize(logFileSizeKb);
                    if (config.LogMaxFileSizeKb != logFileSizeKb)
                    {
                        config.LogMaxFileSizeKb = logFileSizeKb;
                        changed = true;
                    }
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
                OmniControls.InputInt(
                    $"{OmniLoc.Get("Feature.ChatFrameOptimization.LogFileCount")}##chatFrameOptimizationLogFileCount",
                    ref logFileCount);
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    logFileCount = ChatLogReplayStorage.NormalizeFileCount(logFileCount);
                    if (config.LogMaxFileCount != logFileCount)
                    {
                        config.LogMaxFileCount = logFileCount;
                        storage?.RequestPrune();
                        changed = true;
                    }
                }

                ImGuiOm.HelpMarker(OmniLoc.Get("Feature.ChatFrameOptimization.LogFileCount.Help"));
            }
        }

        using (ImRaii.Disabled(!isEnabled || storage is null))
        {
            if (ImGuiOm.ButtonIconWithText(
                    FontAwesomeIcon.History,
                    OmniLoc.Get("Feature.ChatFrameOptimization.OpenReplay")))
            {
                openReplay();
            }

            ImGui.SameLine();
            if (ImGuiOm.ButtonIconWithText(
                    FontAwesomeIcon.FolderOpen,
                    OmniLoc.Get("Feature.ChatFrameOptimization.OpenDirectory")) &&
                storage is not null)
            {
                OpenDirectory(storage.DirectoryPath);
            }
        }

        ImGui.TextDisabled(string.Format(
            OmniLoc.Get("Feature.ChatFrameOptimization.Directory"),
            storage?.DirectoryPath ?? ChatLogReplayStorage.GetDirectoryPath()));
        return changed;
    }

    private static bool DrawCheckbox(string labelKey, string id, ref bool value, string? helpKey = null)
    {
        var changed = OmniControls.Checkbox(
            $"{OmniLoc.Get(labelKey)}##chatFrameOptimization{id}",
            ref value);
        if (helpKey is not null)
        {
            ImGuiOm.HelpMarker(OmniLoc.Get(helpKey));
        }

        return changed;
    }

    private static void OpenDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Failed to open chat log directory.");
        }
    }
}
