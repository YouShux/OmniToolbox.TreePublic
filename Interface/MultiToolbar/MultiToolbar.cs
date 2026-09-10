using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Dalamud.Helpers;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Notifications;
using OmniToolbox.Teleport;
using OmniToolbox.UI;

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

    // 仅移除已下线的组件，保留用户保存的布局和条目。
    private bool NormalizeWidgets()
    {
        if (config.Widgets is null)
        {
            config.Widgets = [];
            return true;
        }

        return config.Widgets.RemoveAll(static widget =>
            widget.Type is MultiToolbarWidgetType.ExperienceBar or MultiToolbarWidgetType.SanctuaryIndicator) > 0;
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
            RegisterHiddenWindowMouseGuard(lifetime);
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
