using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.General;

public sealed class BackgroundChatReminderFeature : TreeHouseFeatureBase
{
    private const uint FlashAll = 3;
    private const uint FlashTimerNoForeground = 12;

    private readonly BackgroundChatReminderConfig config;
    private bool subscribed;

    public BackgroundChatReminderFeature(BackgroundChatReminderConfig config)
        : base(new(
            "background-chat-reminder",
            "Feature.BackgroundChatReminder.Title",
            "Feature.BackgroundChatReminder.Description",
            TreeHouseCategory.General))
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

        DalamudServices.ChatGui.ChatMessage += OnChatMessage;
        subscribed = true;
    }

    private void DisableSubscription()
    {
        if (!subscribed)
        {
            return;
        }

        DalamudServices.ChatGui.ChatMessage -= OnChatMessage;
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
        if (window == IntPtr.Zero || Environment.OSVersion.Version.Major < 5)
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
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}
