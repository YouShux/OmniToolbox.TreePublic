using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class BackgroundChatReminder : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BackgroundChatReminderTitle"),
        Description = OmniLoc.Get("BackgroundChatReminderDescription"),
        Category = ModuleCategory.Daily
    };

    private const uint FlashAll = 3;
    private const uint FlashTimerNoForeground = 12;

    private readonly BackgroundChatReminderConfig config;
    private bool subscribed;

    public BackgroundChatReminder(BackgroundChatReminderConfig config)
    {
        this.config = config;
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = BackgroundChatReminderPanel.Draw(config);
        if (changed)
        {
            UpdateSubscription();
        }

        return changed;
    }

    protected override void OnEnable() => UpdateSubscription();

    protected override void OnDisable() => DisableSubscription();

    private void UpdateSubscription()
    {
        if (config.EnableFlashOnTell || config.EnableFlashOnParty)
        {
            EnableSubscription();
            return;
        }

        DisableSubscription();
    }

    private void EnableSubscription()
    {
        if (subscribed)
        {
            return;
        }

        DalamudServices.ChatGUI.ChatMessage += OnChatMessage;
        subscribed = true;
    }

    private void DisableSubscription()
    {
        if (!subscribed)
        {
            return;
        }

        DalamudServices.ChatGUI.ChatMessage -= OnChatMessage;
        subscribed = false;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (message.IsHandled || GameState.IsForeground)
        {
            return;
        }

        if (message.LogKind is XivChatType.TellIncoming && config.EnableFlashOnTell ||
            (message.LogKind is XivChatType.Party or XivChatType.CrossParty) && config.EnableFlashOnParty)
        {
            FlashTaskbar();
        }
    }

    private static void FlashTaskbar()
    {
        var window = Process.GetCurrentProcess().MainWindowHandle;
        if (window == nint.Zero || Environment.OSVersion.Version.Major < 5)
        {
            return;
        }

        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = window,
            Flags = FlashAll | FlashTimerNoForeground,
            Count = uint.MaxValue
        };
        FlashWindowEx(ref info);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}

internal static class BackgroundChatReminderPanel
{
    public static bool Draw(BackgroundChatReminderConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##backgroundChatReminderOptions",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##backgroundChatReminderTell", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##backgroundChatReminderParty", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##backgroundChatReminderReserved1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##backgroundChatReminderReserved2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var tell = config.EnableFlashOnTell;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.BackgroundChatReminder.Tell"), ref tell))
        {
            config.EnableFlashOnTell = tell;
            changed = true;
        }

        ImGui.TableNextColumn();
        var party = config.EnableFlashOnParty;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.BackgroundChatReminder.Party"), ref party))
        {
            config.EnableFlashOnParty = party;
            changed = true;
        }

        return changed;
    }
}

[Serializable]
public sealed class BackgroundChatReminderConfig
{
    public bool EnableFlashOnTell { get; set; } = true;

    public bool EnableFlashOnParty { get; set; } = true;
}
