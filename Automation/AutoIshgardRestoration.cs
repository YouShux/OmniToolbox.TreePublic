using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.Dalamud;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Info.Game.Packets.Upstream;
using GameEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;
using GameEventHandlerContent = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandlerContent;
using GameEventID = FFXIVClientStructs.FFXIV.Client.Game.Event.EventId;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "自动重建伊修加德",
        Description = "准备好生产材料后，模块将通过Artisan插件进行自动生产，随后进行自动提交与库啵好运道",
        Category = ModuleCategory.Automation,
        Author = "Angelways",
        SupportUrls = ["https://github.com/Angelways"],
        Commands =
        [
            new ModuleCommand(
                "/omni 自动重建伊修加德 → 打开自动重建伊修加德窗口",
                "/omni 自动重建伊修加德"),
            new ModuleCommand(
                "/omni 开关自动重建伊修加德 → 控制重建伊修加德自动化流程",
                "/omni 开关自动重建伊修加德")
        ]
    };

    private const string OPEN_COMMAND = "自动重建伊修加德";

    private const string TOGGLE_COMMAND = "开关自动重建伊修加德";

    private OmenTools.OmenService.CommandManager? commandManager;

    private readonly Dictionary<string, CommandInfo> registeredCommands = [];

    private AutoIshgardRestorationConfig config = new();

    private bool startPending;

    private bool hostConnected;

    public override bool HasSettings => true;

    public AutoIshgardRestoration(AutoIshgardRestorationConfig config) : this(true)
    {
        this.config = config;
    }

    // Allows isolated checks without a game process.

    private AutoIshgardRestoration(bool connectHost)
    {
        if (connectHost) EnsureHostIntegration();
    }

    private void EnsureHostIntegration()
    {
        if (hostConnected) return;
        RegisterCommands(OmenTools.OmenService.CommandManager.Instance());
        DalamudServices.Framework.Update += OnFrameworkUpdate;
        DalamudServices.PluginInterface.UiBuilder.Draw += DrawConfigurationWindow;
        hostConnected = true;
    }

    private bool SaveConfiguration()
    {
        try
        {
            // Module assemblies are loaded separately from Common, so use the
            // callback that the host injects instead of keeping another config store.
            var save = typeof(ModuleBase).GetProperty("SaveHostConfig",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(this) as System.Action;
            if (save is null) throw new InvalidOperationException("宿主尚未提供配置保存接口。");
            save();
            return true;
        }
        catch (Exception ex)
        {
            lastError = "配置保存失败，请勿退出，并检查日志。";
            DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to save host configuration.");
            return false;
        }
    }

    protected override void OnEnable()
    {
        EnsureHostIntegration();
        // Module availability is independent from starting the workflow.
        startPending = false;
        status = "待机";
    }

    protected override void OnDisable()
    {
        startPending = false;
        var previousStatus = status;
        StopProduction("模块已停用");
        if (!string.IsNullOrEmpty(lastError)) status = previousStatus;
        ReleaseArtisan();
        // Configuration and commands remain available while disabled.
    }

    protected override bool OnInterruptAutomation()
    {
        if (!IsEnabled) return false;
        startPending = false;
        StopProduction("已由 Omni 中断");
        return true;
    }

    protected override void OnDispose()
    {
        StopProduction("模块已卸载");
        UnregisterCommands();
        if (hostConnected)
        {
            DalamudServices.Framework.Update -= OnFrameworkUpdate;
            DalamudServices.PluginInterface.UiBuilder.Draw -= DrawConfigurationWindow;
            hostConnected = false;
        }
        windowOpen = false;
    }

    private void RegisterCommands(OmenTools.OmenService.CommandManager manager)
    {
        UnregisterCommands();
        commandManager = manager;
        try
        {
            Register(OPEN_COMMAND, "打开自动重建伊修加德窗口");
            Register(TOGGLE_COMMAND, "控制重建伊修加德自动化流程");
        }
        catch
        {
            UnregisterCommands();
            throw;
        }

        void Register(string name, string description)
        {
            // TreeHouse hot-reload can construct the replacement instance before
            // the old instance has had a chance to unregister its handlers. Only
            // reclaim a handler whose delegate still targets this module type;
            // a command owned by another module remains a real conflict.
            if (manager.SubCommands.TryGetValue(name, out var existing))
            {
                var handler = existing.GetType().GetProperty("Handler")?.GetValue(existing) as Delegate;
                var targetType = handler?.Target?.GetType();
                if (targetType is not null &&
                    targetType.FullName == typeof(AutoIshgardRestoration).FullName &&
                    targetType.Assembly.GetName().Name == typeof(AutoIshgardRestoration).Assembly.GetName().Name)
                {
                    manager.RemoveSubCommand(name);
                }
            }

            var info = new CommandInfo(OnRegisteredCommand) { HelpMessage = description };
            if (!manager.AddSubCommand(name, info))
            {
                throw new InvalidOperationException($"无法注册 /omni {name}：该命令已被占用。");
            }

            registeredCommands.Add(name, info);
        }
    }

    private void UnregisterCommands()
    {
        if (commandManager is not null)
        {
            foreach (var (name, info) in registeredCommands)
            {
                // 仅注销本实例注册的处理器，避免移除其他模块的命令。
                if (commandManager.SubCommands.TryGetValue(name, out var current) &&
                    ReferenceEquals(current, info))
                {
                    commandManager.RemoveSubCommand(name);
                }
            }
        }

        registeredCommands.Clear();
        commandManager = null;
    }

    private void OnRegisteredCommand(string command, string arguments)
    {
        TryHandleCommand(command, arguments);
    }

    public override bool TryHandleCommand(string arguments)
    {
        return HandleCommandArguments(arguments);
    }

    public override bool TryHandleCommand(string command, string arguments)
    {
        // 兼容宿主按模块类名分发的入口。
        if (string.Equals(command.Trim(), ModuleName, StringComparison.OrdinalIgnoreCase))
        {
            return HandleCommandArguments(arguments);
        }

        // 子命令表传入的是中文命令名，箭头和说明不参与解析。
        return string.IsNullOrWhiteSpace(arguments) && HandleCommandArguments(command);
    }

    private bool HandleCommandArguments(string arguments)
    {
        var command = arguments.Trim();
        if (string.Equals(command, OPEN_COMMAND, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "open", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "窗口", StringComparison.OrdinalIgnoreCase))
        {
            OpenConfigurationWindow();
            return true;
        }

        if (string.Equals(command, TOGGLE_COMMAND, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "toggle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "开关", StringComparison.OrdinalIgnoreCase))
        {
            ToggleProduction();
            return true;
        }

        return false;
    }

    private void ToggleProduction()
    {
        if (!IsEnabled)
        {
            Fail("请先在 Omni 主界面启用此模块。");
            return;
        }

        if (running || startPending)
        {
            startPending = false;
            StopProduction("已手动停止");
            return;
        }

        lastError = string.Empty;
        status = "准备启动自动流程";
        startPending = true;
    }

    public override bool ResetSettings()
    {
        var defaults = new AutoIshgardRestorationConfig();
        config.SelectedRecipeID = defaults.SelectedRecipeID;
        config.StopMode = defaults.StopMode;
        config.MinimumFreeSlots = defaults.MinimumFreeSlots;
        config.TargetItemCount = defaults.TargetItemCount;
        config.TicketThreshold = defaults.TicketThreshold;
        config.AutoBuyScrips = defaults.AutoBuyScrips;
        config.ScripThreshold = defaults.ScripThreshold;
        config.ScripShopID = defaults.ScripShopID;
        config.ScripItemID = defaults.ScripItemID;
        config.ScripQuantity = defaults.ScripQuantity;
        SaveConfiguration();
        return true;
    }
}
