using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Notifications;
using OmniToolbox.Teleport;
using OmniToolbox.UI;
using OmenTools.Dalamud.Helpers;
using OmenTools.Extensions;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("MultiToolbarTitle"),
        Description = OmniLoc.Get("MultiToolbarDescription"),
        Category = ModuleCategory.Interface
    };

    // 弹窗打开期间插件缓存的最小刷新间隔
    private const double PluginCacheRefreshIntervalSeconds = 1d;
    private const float PopupWidth = 380f;
    private const float PopupListHeight = 300f;

    private readonly MultiToolbarConfig config;
    private readonly Action saveConfig;
    private readonly IconBrowser iconBrowser;
    private readonly TeleportService teleportService;
    private readonly string pluginEntryLabel;
    private readonly string commandEntryLabel;
    private readonly string dtrEntryLabel;
    private FeatureLifetime? runtimeLifetime;

    public MultiToolbar(MultiToolbarConfig config, Action saveConfig, IconBrowser iconBrowser, TeleportService teleportService)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.iconBrowser = iconBrowser;
        this.teleportService = teleportService;
        if (NormalizeWidgets())
        {
            saveConfig();
        }

        pluginEntryLabel = OmniLoc.Get("Feature.MultiToolbar.PluginList");
        commandEntryLabel = OmniLoc.Get("Feature.MultiToolbar.CommandList");
        dtrEntryLabel = OmniLoc.Get("Feature.MultiToolbar.DtrList");
    }

    public override bool HasSettings => true;

    // 兼容旧版本重复写入内置按钮的配置，保留用户自定义按钮。
    private bool NormalizeWidgets()
    {
        if (config.Widgets is null)
        {
            config.Widgets = [];
        }

        var seenBuiltIns = new HashSet<MultiToolbarWidgetType>();
        var normalized = new List<MultiToolbarWidgetConfig>(config.Widgets.Count);
        var changed = false;
        foreach (var widget in config.Widgets)
        {
            if (widget.Type != MultiToolbarWidgetType.CustomButton &&
                !seenBuiltIns.Add(widget.Type))
            {
                changed = true;
                continue;
            }

            normalized.Add(widget);
        }

        if (changed)
        {
            config.Widgets = normalized;
        }

        // 旧版本默认布局曾将三个内置组件全部放在左侧，首次加载时迁移到 Umbra 风格的三区布局。
        if (config.Widgets.Count == 3 &&
            config.Widgets.All(static widget => widget.Type != MultiToolbarWidgetType.CustomButton) &&
            config.Widgets.All(static widget => widget.Side == MultiToolbarWidgetSide.Left))
        {
            foreach (var widget in config.Widgets)
            {
                widget.Side = widget.Type == MultiToolbarWidgetType.DtrList
                    ? MultiToolbarWidgetSide.Right
                    : MultiToolbarWidgetSide.Center;
            }

            changed = true;
        }

        // 为旧配置补齐 Umbra 风格内置组件；已存在的组件和用户自定义按钮保持原顺序。
        var defaults = new (MultiToolbarWidgetType Type, MultiToolbarWidgetSide Side)[]
        {
            (MultiToolbarWidgetType.BattleEffects, MultiToolbarWidgetSide.Left),
            (MultiToolbarWidgetType.Societies, MultiToolbarWidgetSide.Left),
            (MultiToolbarWidgetType.OnlineStatus, MultiToolbarWidgetSide.Left),
            (MultiToolbarWidgetType.GearsetSwitcher, MultiToolbarWidgetSide.Left),
            (MultiToolbarWidgetType.Durability, MultiToolbarWidgetSide.Left),
            (MultiToolbarWidgetType.RetainerList, MultiToolbarWidgetSide.Left),
            (MultiToolbarWidgetType.Currencies, MultiToolbarWidgetSide.Left),
            (MultiToolbarWidgetType.Flag, MultiToolbarWidgetSide.Right),
            (MultiToolbarWidgetType.Volume, MultiToolbarWidgetSide.Right),
            (MultiToolbarWidgetType.MailIndicator, MultiToolbarWidgetSide.Right),
            (MultiToolbarWidgetType.MarkerControl, MultiToolbarWidgetSide.Right),
            (MultiToolbarWidgetType.WalkingIndicator, MultiToolbarWidgetSide.Right),
            (MultiToolbarWidgetType.StackedClock, MultiToolbarWidgetSide.Right),
        };
        foreach (var (type, side) in defaults)
        {
            if (config.Widgets.Any(widget => widget.Type == type))
            {
                continue;
            }

            config.Widgets.Add(new MultiToolbarWidgetConfig
            {
                Type = type,
                Side = side,
                Command = MultiToolbarWidgetCommand(type),
                ShowIcon = type is not MultiToolbarWidgetType.DtrList and not MultiToolbarWidgetType.StackedClock,
            });
            changed = true;
        }

        return changed;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            DalamudServices.PluginInterface.UiBuilder.Draw += Draw;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= Draw);
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
            {
                throw new InvalidOperationException("Multi-toolbar framework registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "_DTR", OnNativeDtrPreDraw);
            lifetime.Add(() => DalamudServices.AddonLifecycle.UnregisterListener(
                AddonEvent.PreDraw,
                "_DTR",
                OnNativeDtrPreDraw));
            runtimeLifetime = lifetime;
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        ClosePopup();
        UpdateNativeDtrVisibility(false);
        RestoreDtrEntries();
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        lifetime?.Dispose();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        UpdateNativeDtrVisibility(config.Widgets.Any(widget => widget.Enabled && widget.Type == MultiToolbarWidgetType.DtrList));
    }

    private unsafe void OnNativeDtrPreDraw(AddonEvent _, AddonArgs args)
    {
        if (!config.Widgets.Any(widget => widget.Enabled && widget.Type == MultiToolbarWidgetType.DtrList))
        {
            return;
        }

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon is null)
        {
            return;
        }

        addon->IsVisible = false;
    }

    // 与 MultiDock 同源的指令规范化：统一为 "/xxx" 或 "\\xxx" 形式
    private static bool TryNormalizeCommand(string? value, out string command)
    {
        command = (value ?? string.Empty).Trim().TrimEnd(';').Trim();
        if (command.Length == 0)
        {
            return false;
        }

        if (command[0] == '／')
        {
            command = $"/{command[1..]}";
        }
        else if (command[0] == '＼')
        {
            command = $"\\{command[1..]}";
        }
        else if (command[0] is not ('/' or '\\'))
        {
            command = $"/{command}";
        }

        return command.Length > 1;
    }

    private static void ExecuteCommand(string command)
    {
        if (!TryNormalizeCommand(command, out var normalized))
        {
            return;
        }

        try
        {
            if (!DalamudServices.CommandManager.ProcessCommand(normalized))
            {
                ChatManager.Instance().SendCommand(normalized);
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar command execution failed: {Command}", normalized);
        }
    }

    // 使用 Dalamud 官方插件管理指令，交由卫月处理插件生命周期和默认配置。
    private static bool TryEnablePlugin(IExposedPlugin plugin)
    {
        ExecuteCommand($"/xlenableplugin \"{plugin.InternalName}\"");
        return true;
    }

    private static bool TryDisablePlugin(IExposedPlugin plugin)
    {
        ExecuteCommand($"/xldisableplugin \"{plugin.InternalName}\"");
        return true;
    }
}
