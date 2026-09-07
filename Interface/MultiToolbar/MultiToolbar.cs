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

    private const double PluginCacheRefreshIntervalSeconds = 1d;
    private const float PopupWidth = 380f;
    private const float PopupListHeight = 420f;

    private readonly MultiToolbarBarConfig config;
    private readonly string barID;
    private readonly Action saveConfig;
    private readonly Func<Newtonsoft.Json.Linq.JObject?>? getMultiDockConfig;
    private readonly IconBrowser iconBrowser;
    private readonly TeleportService teleportService;
    private readonly string pluginEntryLabel;
    private readonly string commandEntryLabel;
    private readonly string dtrEntryLabel;
    private FeatureLifetime? runtimeLifetime;

    public MultiToolbar(MultiToolbarConfig config, Action saveConfig, IconBrowser iconBrowser, TeleportService teleportService,
        Func<Newtonsoft.Json.Linq.JObject?>? getMultiDockConfig = null)
        : this(config, saveConfig, iconBrowser, teleportService, "main", getMultiDockConfig)
    {
        if (NormalizeWidgets())
        {
            saveConfig();
        }
    }

    private MultiToolbar(MultiToolbarBarConfig config, Action saveConfig, IconBrowser iconBrowser, TeleportService teleportService,
        string barID, Func<Newtonsoft.Json.Linq.JObject?>? getMultiDockConfig = null)
    {
        this.config = config;
        this.barID = barID;
        this.saveConfig = saveConfig;
        this.iconBrowser = iconBrowser;
        this.teleportService = teleportService;
        this.getMultiDockConfig = getMultiDockConfig;

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
        var commands = config.Commands.DistinctBy(command =>
            (command.Type, command.Side, command.Enabled, command.ShowIcon, command.DisplayName,
                command.Name, command.Command, command.RightCommand, command.GameIconID, command.IconFilePath, command.IconGlyph)).ToList();
        if (commands.Count != config.Commands.Count)
        {
            config.Commands = commands;
            changed = true;
        }
        if (!config.TextEntryDefaultsApplied)
        {
            foreach (var entry in config.Widgets)
            {
                if (entry.Type is (MultiToolbarWidgetType.PluginList or MultiToolbarWidgetType.CommandList) &&
                    entry.GameIconID == 0 && entry.DisplayName is null)
                {
                    entry.ShowIcon = false;
                }
            }

            config.TextEntryDefaultsApplied = true;
            changed = true;
        }
        foreach (var widget in config.Widgets)
        {
            if (widget.Type is MultiToolbarWidgetType.ExperienceBar or MultiToolbarWidgetType.SanctuaryIndicator)
            {
                changed = true;
                continue;
            }

            if (widget.Type is not (MultiToolbarWidgetType.CustomButton or MultiToolbarWidgetType.DtrSingle) &&
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

        // 迁移旧版三个内置组件均位于左侧的默认布局。
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
            (MultiToolbarWidgetType.ToolbarPin, MultiToolbarWidgetSide.Right),
        };
        foreach (var (type, side) in defaults)
        {
            if (config.Widgets.Any(widget => widget.Type == type))
            {
                continue;
            }

            var widget = CreateDefaultWidget(type);
            widget.Side = side;
            config.Widgets.Add(widget);
            changed = true;
        }

        return changed;
    }

    protected override void OnEnable()
    {
        nextEquipmentSnapshotRefresh = nextInventorySpaceRefresh = nextCurrencyLabelRefresh = 0;
        MigrateMultiDock();
        MigrateMultiDockPlugins();
        var lifetime = new FeatureLifetime();
        try
        {
            RegisterHiddenWindowEntry(lifetime);
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
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        lifetime?.Dispose();
        RestoreHiddenWindows();
        ReleaseBarResources();
        ReleaseAuxiliaryBars();
        UpdateNativeDtrVisibility(false);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        UpdateNativeDtrVisibility(UsesNativeDtr());
    }

    private unsafe void OnNativeDtrPreDraw(AddonEvent _, AddonArgs args)
    {
        if (!UsesNativeDtr())
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
