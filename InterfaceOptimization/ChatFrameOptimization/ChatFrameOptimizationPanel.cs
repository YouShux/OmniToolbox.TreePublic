using System.Diagnostics;
using System.IO;
using Dalamud.Interface;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

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
