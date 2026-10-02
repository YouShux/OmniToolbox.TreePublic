using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.Config;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Component.Log;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Dalamud.Helpers;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.Interop.Game;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

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

    private static readonly CompSig MessageFilterSizeSignature = new(
        "FF C5 81 FD ?? ?? ?? ?? 0F 82 ?? ?? ?? ?? 48 8B 0D");

    private const string STICKY_SHOUT_AUTO_SWITCH_SIGNATURE = "05 75 0C 8B D7 E8 ?? ?? ?? ?? E9";

    private static readonly Regex URLRegex = new(
        @"(http|ftp|https)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ChatFrameOptimizationPanel panel = new(config);
    private readonly ChatLogReplayWindow replayWindow = new();
    private readonly BetterChatWindow betterChatWindow = new();
    private BetterChatNativeSettings? betterChatNativeSettings;
    private BetterChatExtendedFilterWindow? betterChatExtendedFilterWindow;
    private static readonly string[] BetterChatAddonNames =
        ["ChatLog", "ChatLogPanel_0", "ChatLogPanel_1", "ChatLogPanel_2", "ChatLogPanel_3"];
    private FeatureLifetime? runtimeLifetime;
    private AddonEventRegistry? betterChatAddonEvents;
    private readonly Dictionary<string, Dictionary<uint, bool>> nativeChatNodeVisibility = new(StringComparer.Ordinal);
    private bool nativeTabsDirty = true;
    private ChatLogReplayStorage? storage;
    private Hook<ScrollToBottomDelegate>? scrollToBottomHook;
    private Hook<RaptureShellModule.Delegates.ChangeChatChannel>? changeChatChannelHook;
    private Hook<LogModule.Delegates.ClearLog>? clearLogHook;
    private MemoryPatch? stickyShoutAutoSwitchPatch;
    private DalamudLinkPayload? urlPayload;
    private uint urlCommandID;

    private delegate void* ScrollToBottomDelegate(LogViewer* logViewer);

    public override bool HasSettings => true;

    public void SetOpenSettingsAction(Action _) => betterChatWindow.OnOpenSettings = () =>
    {
        var index = betterChatWindow.SelectedTabIndex;
        if (betterChatWindow.UsesNativeMessages)
        {
            OpenChatSettings(index);
            return;
        }

        OpenExtendedFilterSettings(index);
    };

    public override bool DrawSettings()
    {
        EnsureBetterChatTabs();
        var betterChatEnabled = config.BetterChatEnabled;
        var changed = panel.Draw(config, storage, IsEnabled, OpenReplay,
            index => OpenChatSettings(index));
        if (betterChatEnabled != config.BetterChatEnabled)
        {
            if (config.BetterChatEnabled)
            {
                OpenBetterChat();
            }
            else
            {
                betterChatWindow.Close();
                betterChatExtendedFilterWindow?.Close();
                betterChatNativeSettings?.Clear();
                RestoreNativeChat();
            }
        }

        if (changed)
        {
            betterChatWindow.SetTabs(config.BetterChatTabs);
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

    public bool OpenBetterChat()
    {
        var chatLog = AddonHelper.GetByName<AddonChatLog>("ChatLog");
        if (!IsEnabled || chatLog is null || !chatLog->IsReady)
        {
            return false;
        }

        EnsureBetterChatTabs();
        betterChatWindow.SetTabs(config.BetterChatTabs);
        SyncBetterChatFromNative(chatLog);
        HideNativeChatContent();
        return true;
    }

    protected override void OnEnable()
    {
        nativeTabsDirty = true;
        betterChatWindow.ResetMessages();
        var lifetime = new FeatureLifetime();
        try
        {
            storage = new(config);
            lifetime.Add(storage.Dispose);
            lifetime.Add(replayWindow.Close);
            lifetime.Add(betterChatWindow.Close);
            betterChatNativeSettings = new(OpenExtendedFilterSettings);
            betterChatExtendedFilterWindow = new(config, OnBetterChatTabsChanged);
            lifetime.Add(() =>
            {
                betterChatExtendedFilterWindow?.Dispose();
                betterChatExtendedFilterWindow = null;
                betterChatNativeSettings?.Dispose();
                betterChatNativeSettings = null;
            });
            betterChatWindow.OnAddTab = () =>
            {
                ChatFrameOptimizationPanel.AddBetterChatTab(config, betterChatWindow.SelectedTabIndex);
                OnBetterChatTabsChanged();
                betterChatWindow.SelectTab(config.BetterChatTabs.Count - 1);
            };
            EnsureBetterChatTabs();
            betterChatWindow.SetTabs(config.BetterChatTabs);
            betterChatAddonEvents = new(DalamudServices.AddonLifecycle);
            foreach (var addonName in BetterChatAddonNames)
            {
                betterChatAddonEvents.Register(AddonEvent.PostSetup, addonName, OnBetterChatAddon);
                betterChatAddonEvents.Register(AddonEvent.PreDraw, addonName, OnBetterChatAddon);
                betterChatAddonEvents.Register(AddonEvent.PreFinalize, addonName, OnBetterChatAddon);
            }
            betterChatAddonEvents.Register(AddonEvent.PostSetup, "ConfigLog", OnConfigLogAddon);
            betterChatAddonEvents.Register(AddonEvent.PreDraw, "ConfigLog", OnConfigLogAddon);
            betterChatAddonEvents.Register(AddonEvent.PreFinalize, "ConfigLog", OnConfigLogAddon);
            betterChatAddonEvents.Register(AddonEvent.PreFinalize, "ConfigLog", OnChatSettingsChanged);
            betterChatAddonEvents.Register(AddonEvent.PreFinalize, "ConfigLogFilter", OnChatSettingsChanged);
            lifetime.Add(() =>
            {
                betterChatAddonEvents?.Dispose();
                betterChatAddonEvents = null;
                RestoreNativeChat();
            });
            DService.Instance().GameConfig.UiConfigChanged += OnChatConfigChanged;
            lifetime.Add(() => DService.Instance().GameConfig.UiConfigChanged -= OnChatConfigChanged);

            if (config.BetterChatEnabled)
            {
                InitializeBetterChat();
            }

            DalamudServices.PluginInterface.UiBuilder.Draw += replayWindow.Draw;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= replayWindow.Draw);

            DalamudServices.ChatGUI.ChatMessage += OnChatMessage;
            lifetime.Add(() => DalamudServices.ChatGUI.ChatMessage -= OnChatMessage);
            DalamudServices.ChatGUI.CheckMessageHandled += OnCheckMessageHandled;
            lifetime.Add(() => DalamudServices.ChatGUI.CheckMessageHandled -= OnCheckMessageHandled);
            DalamudServices.ChatGUI.ChatMessageUnhandled += OnChatMessageUnhandled;
            lifetime.Add(() => DalamudServices.ChatGUI.ChatMessageUnhandled -= OnChatMessageUnhandled);

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

            if (DService.Instance().SigScanner.TryScanText(STICKY_SHOUT_AUTO_SWITCH_SIGNATURE, out var stickyShoutAddress))
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

            lifetime.Add(() => clearLogHook?.Dispose());
            TryInstallClearLogHook();
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
            {
                throw new InvalidOperationException("Chat-frame update registration failed.");
            }
            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));

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

    protected override void OnDispose()
    {
        betterChatWindow.Dispose();
        betterChatExtendedFilterWindow?.Dispose();
        betterChatNativeSettings?.Dispose();
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

    private void OnBetterChatAddon(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            nativeChatNodeVisibility.Remove(args.AddonName);
            if (args.AddonName == "ChatLog")
            {
                betterChatWindow.Close((AtkUnitBase*)args.Addon.Address);
                nativeTabsDirty = true;
            }
            else if (args.Addon.Address != nint.Zero &&
                     ((AtkUnitBase*)args.Addon.Address)->Id == betterChatWindow.AttachedPanelID)
                betterChatWindow.Close();
            return;
        }

        if (!config.BetterChatEnabled || args.Addon.Address == nint.Zero)
        {
            return;
        }

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsReady)
        {
            return;
        }

        if (eventType == AddonEvent.PostSetup)
            nativeTabsDirty = true;

        if (args.AddonName == "ChatLog")
        {
            if (EnsureBetterChatTabs())
            {
                betterChatWindow.SetTabs(config.BetterChatTabs);
            }

            SyncBetterChatFromNative((AddonChatLog*)addon);
        }
        if (betterChatWindow.IsOpen)
            HideNativeChatContent();
    }

    private void InitializeBetterChat()
    {
        if (!config.BetterChatEnabled)
        {
            return;
        }

        var chatLog = AddonHelper.GetByName<AddonChatLog>("ChatLog");
        if (chatLog is null || !chatLog->IsReady)
        {
            betterChatWindow.Close();
            return;
        }

        if (EnsureBetterChatTabs())
        {
            betterChatWindow.SetTabs(config.BetterChatTabs);
        }

        // 常规布局只在 ChatLog.PreDraw 同步；初始化时先完成首次挂接。
        if (!betterChatWindow.IsOpen)
        {
            SyncBetterChatFromNative(chatLog);
            HideNativeChatContent();
        }
    }

    private void SyncBetterChatFromNative(AddonChatLog* chatLog)
    {
        if (nativeTabsDirty)
            RefreshNativeChatTabs();
        betterChatWindow.SyncNativeTabIndex(chatLog->TabIndex);
        var nativeTabIndex = betterChatWindow.SelectedTabIndex < config.BetterChatTabs.Count
            ? config.BetterChatTabs[betterChatWindow.SelectedTabIndex].NativeTabIndex
            : chatLog->TabIndex;
        nativeTabIndex = Math.Clamp(nativeTabIndex < 0 ? chatLog->TabIndex : nativeTabIndex, 0, 3);
        var gameConfig = DService.Instance().GameConfig.UiConfig;
        var fontOption = nativeTabIndex switch
        {
            1 => ConfigOption.LogFontSizeLog2,
            2 => ConfigOption.LogFontSizeLog3,
            3 => ConfigOption.LogFontSizeLog4,
            _ => ConfigOption.LogFontSize,
        };
        gameConfig.TryGetUInt(fontOption.ToString(), out var fontSize);
        var panel = AddonHelper.GetByName<AddonChatLogPanel>($"ChatLogPanel_{nativeTabIndex}");
        betterChatWindow.SyncToNative(chatLog, panel);
        if (panel is not null && panel->ChatText is not null)
            betterChatWindow.SyncAppearance(panel->ChatText, fontSize);
        var logModule = RaptureLogModule.Instance();
        if (logModule is not null)
            betterChatWindow.SyncTimeSettings(logModule->ChatTabShouldDisplayTime[nativeTabIndex],
                logModule->Use12HourClock, logModule->UseServerTime);
        betterChatWindow.UpdateContent(chatLog);
    }

    private void OnChatConfigChanged(object? sender, ConfigChangeEvent args)
    {
        if (!config.BetterChatEnabled)
            return;

        if (args.Name.StartsWith("Color", StringComparison.Ordinal))
        {
            betterChatWindow.RefreshColors();
            betterChatExtendedFilterWindow?.RefreshColors();
        }
        else if (args.Name.StartsWith("Log", StringComparison.Ordinal))
        {
            nativeTabsDirty = true;
        }
    }

    private void OnChatSettingsChanged(AddonEvent eventType, AddonArgs args) => nativeTabsDirty = true;

    private void OnConfigLogAddon(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            betterChatNativeSettings?.Clear();
            return;
        }

        if (!config.BetterChatEnabled)
        {
            betterChatNativeSettings?.Clear();
            return;
        }

        if (args.Addon.Address == nint.Zero)
            return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon is null || !addon->IsReady)
            return;

        betterChatNativeSettings?.Update(addon, config.BetterChatTabs);
    }

    private bool OpenChatSettings(int _)
    {
        var agents = AgentModule.Instance();
        var agent = agents is null ? null : agents->GetAgentByInternalId(AgentId.ConfigLog);
        if (agent is null)
            return false;
        agent->Show();
        nativeTabsDirty = true;
        return true;
    }

    private void OpenExtendedFilterSettings(int index)
    {
        betterChatExtendedFilterWindow?.OpenFor(index);
    }

    private void OnBetterChatTabsChanged()
    {
        foreach (var tab in config.BetterChatTabs)
        {
            if (tab.FilterRows is { } rows)
                tab.ChatTypes = BetterChatFilters.GetChatTypes(rows);
        }
        betterChatWindow.SetTabs(config.BetterChatTabs);
        SaveHostConfig?.Invoke();
    }

    private void RefreshNativeChatTabs()
    {
        var logModule = RaptureLogModule.Instance();
        if (logModule is null)
            return;
        foreach (var tab in config.BetterChatTabs)
        {
            if (tab.NativeTabIndex is < 0 or > 3)
                continue;
            var name = logModule->GetTabName(tab.NativeTabIndex);
            if (name is not null && !string.IsNullOrWhiteSpace(name->ToString()))
                tab.Name = name->ToString();
            if (ReadNativeChatFilters(tab.NativeTabIndex) is { } filterRows)
            {
                tab.FilterRows = filterRows;
                tab.ChatTypes = BetterChatFilters.GetChatTypes(filterRows);
            }
        }
        betterChatWindow.SetTabs(config.BetterChatTabs);
        SaveHostConfig?.Invoke();
        nativeTabsDirty = false;
    }

    private void RestoreNativeChat()
    {
        foreach (var (addonName, nodes) in nativeChatNodeVisibility)
        {
            if (!AddonHelper.TryGetByName(addonName, out AtkUnitBase* addon) || addon is null)
            {
                continue;
            }

            foreach (var (nodeID, wasVisible) in nodes)
            {
                var node = addon->GetNodeById(nodeID);
                if (addonName.StartsWith("ChatLogPanel_", StringComparison.Ordinal))
                {
                    var panel = (AddonChatLogPanel*)addon;
                    if (panel->ChatText is not null && panel->ChatText->NodeId == nodeID)
                        node = (AtkResNode*)panel->ChatText;
                    else if (panel->LogViewer.ScrollBarNode is not null && panel->LogViewer.ScrollBarNode->NodeId == nodeID)
                        node = (AtkResNode*)panel->LogViewer.ScrollBarNode;
                }
                if (node is not null)
                    node->ToggleVisibility(wasVisible);
            }
        }

        nativeChatNodeVisibility.Clear();
    }

    private void HideNativeChatContent()
    {
        var nativeTabIndex = -1;
        foreach (var addonName in BetterChatAddonNames)
        {
            if (!AddonHelper.TryGetByName(addonName, out AtkUnitBase* addon) || addon is null || !addon->IsReady)
            {
                continue;
            }

            if (addonName == "ChatLog")
            {
                var chatLog = (AddonChatLog*)addon;
                nativeTabIndex = chatLog->TabIndex;
                HideNativeChatNode(addonName, (AtkResNode*)chatLog->AddTabComponentNode);
                if (betterChatWindow.UsesNativeMessages)
                    RestoreNativeChatNode(addonName, (AtkResNode*)chatLog->SettingsComponentNode);
                else
                    HideNativeChatNode(addonName, (AtkResNode*)chatLog->SettingsComponentNode);
                for (var index = 0; index < chatLog->ChatTabs.Length; index++)
                {
                    var tab = chatLog->ChatTabs[index].Value;
                    if (tab is not null)
                        HideNativeChatNode(addonName, (AtkResNode*)tab->OwnerNode);
                }
            }
            else
            {
                var chatPanel = (AddonChatLogPanel*)addon;
                if (chatPanel->ChatComponent is not null)
                    RestoreNativeChatNode(addonName, (AtkResNode*)chatPanel->ChatComponent->OwnerNode);
                var chatText = (AtkResNode*)chatPanel->ChatText;
                var scrollBar = (AtkResNode*)chatPanel->LogViewer.ScrollBarNode;
                if (betterChatWindow.UsesNativeMessages || addonName != $"ChatLogPanel_{nativeTabIndex}")
                {
                    RestoreNativeChatNode(addonName, chatText);
                    RestoreNativeChatNode(addonName, scrollBar);
                }
                else
                {
                    HideNativeChatNode(addonName, chatText);
                    HideNativeChatNode(addonName, scrollBar);
                }
            }
        }
    }

    private void RestoreNativeChatNode(string addonName, AtkResNode* node)
    {
        if (node is not null && nativeChatNodeVisibility.TryGetValue(addonName, out var nodes) &&
            nodes.Remove(node->NodeId, out var wasVisible))
            node->ToggleVisibility(wasVisible);
    }

    private void HideNativeChatNode(string addonName, AtkResNode* node)
    {
        if (node is null)
            return;
        if (!nativeChatNodeVisibility.TryGetValue(addonName, out var nodes))
            nativeChatNodeVisibility[addonName] = nodes = [];
        nodes.TryAdd(node->NodeId, node->IsVisible());
        node->ToggleVisibility(false);
    }

    private bool EnsureBetterChatTabs()
    {
        if (config.BetterChatTabs.Count > 0)
        {
            return false;
        }

        var allTypes = Enum.GetValues<XivChatType>()
            .Select(static type => (ushort)type)
            .Distinct()
            .ToList();
        var chatLog = AddonHelper.GetByName<AddonChatLog>("ChatLog");
        var logModule = RaptureLogModule.Instance();
        if (chatLog is null || !chatLog->IsReady || logModule is null)
        {
            return false;
        }

        var tabCount = Math.Clamp(chatLog->TabCount, (byte)1, (byte)4);

        for (var index = 0; index < tabCount; index++)
        {
            var tabName = logModule is null ? null : logModule->GetTabName(index);
            var name = tabName is null ? null : tabName->ToString();
            config.BetterChatTabs.Add(new()
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"Tab {index + 1}" : name,
                NativeTabIndex = index,
                ChatTypes = [.. allTypes],
                FilterRows = ReadNativeChatFilters(index),
            });
        }

        return config.BetterChatTabs.Count > 0;
    }

    private static List<uint>? ReadNativeChatFilters(int index)
    {
        try
        {
            var filters = LogFilterConfig.Instance();
            if (filters is null)
            {
                return null;
            }

            var signatureAddress = MessageFilterSizeSignature.ScanText();
            if (signatureAddress == nint.Zero)
            {
                return null;
            }

            var instruction = MemoryHelper.ReadRaw(signatureAddress + 2, 6);
            var filterSize = instruction[0] == 0x81 && instruction[1] == 0xFD
                ? BitConverter.ToInt32(instruction, 2)
                : instruction[0] == 0x83 && instruction[1] == 0xFD
                    ? (sbyte)instruction[2]
                    : 0;
            if (filterSize <= 0 || filterSize > (sizeof(LogFilterConfig) - 0x48) / 4 || index is < 0 or >= 4)
            {
                return null;
            }

            var filter = (byte*)filters + 0x48 + filterSize * index;
            var nativeData = new ReadOnlySpan<byte>(filter, filterSize);
            var selected = new List<uint>();
            foreach (var row in BetterChatFilters.Rows)
            {
                if (row.RowId < filterSize && nativeData[(int)row.RowId] is not 0)
                    selected.Add(row.RowId);
            }
            return selected;
        }
        catch
        {
            return null;
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

            if (config.ClickableLinks && TryBuildClickableLinks(message.LogKind, message.Message, out var modified))
            {
                message.Message = modified;
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Chat-frame message preprocessing failed.");
        }
    }

    private void OnChatMessageUnhandled(IChatMessage _) => betterChatWindow.NotifyMessagesChanged();

    private void OnFrameworkUpdate(IFramework _)
    {
        TryInstallClearLogHook();
        if (config.BetterChatEnabled)
            betterChatWindow.PollMessages();
    }

    private void TryInstallClearLogHook()
    {
        if (clearLogHook is not null)
            return;

        var logModule = (LogModule*)RaptureLogModule.Instance();
        if (logModule is null || logModule->VirtualTable is null || logModule->VirtualTable->ClearLog == null)
            return;

        clearLogHook = DService.Instance().Hook.HookFromVirtualTable(
            logModule->VirtualTable,
            nameof(LogModule.ClearLog),
            (LogModule.Delegates.ClearLog)ClearLogDetour);
        clearLogHook.Enable();
    }

    private void ClearLogDetour(LogModule* logModule)
    {
        clearLogHook!.Original(logModule);
        var raptureLogModule = RaptureLogModule.Instance();
        if (raptureLogModule is not null && logModule == (LogModule*)raptureLogModule)
            betterChatWindow.ResetMessages();
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
        clearLogHook = null;
        stickyShoutAutoSwitchPatch = null;
        urlPayload = null;
        urlCommandID = 0;
        replayWindow.Close();
        betterChatWindow.ResetMessages();
    }
}

[Serializable]
public sealed class ChatFrameOptimizationConfig
{
    public bool SmartAutoScroll { get; set; } = true;

    public bool StickyChat { get; set; } = true;

    public bool ClickableLinks { get; set; } = true;

    public bool AutoSaveLogs { get; set; } = true;

    public bool BetterChatEnabled { get; set; }

    public List<BetterChatTabConfig> BetterChatTabs { get; set; } = [];

    public Vector2? BetterChatPosition { get; set; }

    public Vector2? BetterChatSize { get; set; }

    public int LogMaxFileSizeKb { get; set; } = 1024;

    public int LogMaxFileCount { get; set; } = 20;
}

[Serializable]
public sealed class BetterChatTabConfig
{
    public string Name { get; set; } = "General";

    public int NativeTabIndex { get; set; } = -1;

    public List<ushort> ChatTypes { get; set; } = [];

    public List<uint>? FilterRows { get; set; }

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
        Func<bool> openReplay,
        Action<int> openChatSettings)
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

                OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
                OmniControls.HelpIcon(OmniLoc.Get("Feature.ChatFrameOptimization.LogFileCount.Help"));
                ImGui.TableNextColumn();
                using (ImRaii.Disabled(!isEnabled || storage is null))
                {
                    if (OmniControls.SmallButton(OmniLoc.Get("Feature.ChatFrameOptimization.OpenReplay"), false))
                    {
                        openReplay();
                    }

                    ImGui.TableNextColumn();
                    if (OmniControls.SmallButton(OmniLoc.Get("Feature.ChatFrameOptimization.OpenDirectory"), false) &&
                        storage is not null)
                    {
                        OpenDirectory(storage.DirectoryPath);
                    }
                }
                ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
                OmniControls.HelpIcon(string.Format(
                    OmniLoc.Get("Feature.ChatFrameOptimization.Directory"),
                    storage?.DirectoryPath ?? ChatLogReplayStorage.GetDirectoryPath()));

#if DEBUG
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var betterChatEnabled = config.BetterChatEnabled;
                if (DrawCheckbox(
                        "Feature.ChatFrameOptimization.BetterChat",
                        "betterChat",
                        ref betterChatEnabled,
                        "Feature.ChatFrameOptimization.BetterChat.Help"))
                {
                    config.BetterChatEnabled = betterChatEnabled;
                    changed = true;
                }

                if (config.BetterChatEnabled)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    changed |= DrawBetterChatTabs(config, openChatSettings);
                }
#endif
            }
        }
        return changed;
    }

#if DEBUG
    private bool DrawBetterChatTabs(ChatFrameOptimizationConfig config, Action<int> openChatSettings)
    {
        var changed = false;
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.Tabs"));
        for (var index = 0; index < config.BetterChatTabs.Count; index++)
        {
            var tab = config.BetterChatTabs[index];
            using var id = ImRaii.PushId($"betterChatTab{index}");
            if (tab.NativeTabIndex >= 0)
            {
                ImGui.TextUnformatted(tab.Name);
            }
            else
            {
                var name = tab.Name;
                ImGui.SetNextItemWidth(OmniTheme.Scale(140f));
                if (ImGui.InputText("##name", ref name, 64))
                    tab.Name = name;
                if (ImGui.IsItemDeactivatedAfterEdit())
                    changed = true;
            }

            if (tab.NativeTabIndex >= 0)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.Filters")))
                    openChatSettings(index);
            }

            ImGui.SameLine();
            if (config.BetterChatTabs.Count > 1 && ImGui.SmallButton("-"))
            {
                config.BetterChatTabs.RemoveAt(index);
                changed = true;
                break;
            }
        }

        if (ImGui.SmallButton("+"))
        {
            AddBetterChatTab(config);
            changed = true;
        }

        return changed;
    }
#endif

    internal static void AddBetterChatTab(ChatFrameOptimizationConfig config, int sourceIndex = 0)
    {
        var source = sourceIndex >= 0 && sourceIndex < config.BetterChatTabs.Count ? config.BetterChatTabs[sourceIndex] : null;
        var allTypes = Enum.GetValues<XivChatType>()
            .Select(static type => (ushort)type)
            .Distinct()
            .ToList();
        config.BetterChatTabs.Add(new()
        {
            Name = string.Format(OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.TabName"), config.BetterChatTabs.Count + 1),
            ChatTypes = source is null ? allTypes : [.. source.ChatTypes],
            FilterRows = source?.FilterRows is { } rows ? [.. rows] : null,
        });
    }

    private static bool DrawCheckbox(string labelKey, string id, ref bool value, string? helpKey = null)
    {
        var changed = OmniControls.Checkbox(
            $"{OmniLoc.Get(labelKey)}##chatFrameOptimization{id}",
            ref value);
        if (helpKey is not null)
        {
            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
            OmniControls.HelpIcon(OmniLoc.Get(helpKey));
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
