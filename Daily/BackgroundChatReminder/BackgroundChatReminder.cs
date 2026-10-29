using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class BackgroundChatReminder : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Daily
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "BackgroundChatReminderTitle",
            ["Description"] = "BackgroundChatReminderDescription",
            ["Feature.BackgroundChatReminder.Party"] = "Feature.BackgroundChatReminder.Party",
            ["Feature.BackgroundChatReminder.Tell"] = "Feature.BackgroundChatReminder.Tell"
        });

    private readonly Settings config;
    private bool              subscribed;

    public BackgroundChatReminder(Settings config)
    {
        this.config = config;
    }

    protected override void OnStart(FeatureLifetime activation) => UpdateSubscription();

    protected override void OnStop() => DisableSubscription();

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
            Flags = FLASH_ALL | FLASH_TIMER_NO_FOREGROUND,
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

    #region 常量

    private const uint FLASH_ALL = 3;

    private const uint FLASH_TIMER_NO_FOREGROUND = 12;

    #endregion

    public sealed class Settings
    {
        public bool EnableFlashOnTell { get; set; } = true;

        public bool EnableFlashOnParty { get; set; } = true;
    }
}
