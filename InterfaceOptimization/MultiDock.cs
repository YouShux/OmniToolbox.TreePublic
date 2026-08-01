using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Dalamud.Helpers;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class MultiDock : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("MultiDockTitle"),
        Description = OmniLoc.Get("MultiDockDescription"),
        Category = ModuleCategory.InterfaceOptimization
    };

    private const string PluginDockWindowName = "插件/窗口收纳###OmniToolbox.MultiDock.Plugin";
    private const string CommandDockWindowName = "指令收纳###OmniToolbox.MultiDock.Command";
    private const string PluginDockWindowKey = "###OmniToolbox.MultiDock.Plugin";
    private const string CommandDockWindowKey = "###OmniToolbox.MultiDock.Command";
    private static readonly string[] LauncherDirectoryNames = ["XIVLauncherCN", "XIVLauncher"];

    private readonly MultiDockConfig config;
    private readonly Action saveConfig;
    private readonly MultiDockPanel panel;
    private readonly List<MultiDockItemConfig> pluginItems = [];
    private readonly List<MultiDockItemConfig> commandItems = [];
    private readonly Dictionary<string, ISharedImmediateTexture?> fileTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> pluginIconSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> failedTexturePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> appliedHiddenWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> runtimeClosedPluginWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly DockRuntimeState pluginState = new();
    private readonly DockRuntimeState commandState = new();
    private FeatureLifetime? runtimeLifetime;
    private ISharedImmediateTexture? fallbackTexture;
    private bool fallbackTextureLoaded;

    public MultiDock(
        MultiDockConfig config,
        Action saveConfig,
        IconBrowser iconBrowser)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        NormalizeConfig();
        appliedHiddenWindows.UnionWith(config.HiddenWindows);
        RefreshItemCaches();
        panel = new(
            config,
            this,
            iconBrowser,
            saveConfig,
            OnItemsChanged,
            InvalidateTextures);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => panel.Draw();

    internal static bool TryNormalizeCommand(string? value, out string command)
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
        else if (command[0] != '/')
        {
            command = $"/{command}";
        }

        return command.Length > 1;
    }

    internal static bool TryParseSeIcon(string? value, out SeIconChar icon)
    {
        var text = (value ?? string.Empty).Trim();
        if (Enum.TryParse(text, true, out icon) && Enum.IsDefined(icon))
        {
            return true;
        }

        var style = NumberStyles.Integer;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            style = NumberStyles.HexNumber;
        }

        if (int.TryParse(text, style, CultureInfo.InvariantCulture, out var valueNumber) &&
            Enum.IsDefined(typeof(SeIconChar), valueNumber))
        {
            icon = (SeIconChar)valueNumber;
            return true;
        }

        icon = default;
        return false;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            DalamudServices.PluginInterface.UiBuilder.Draw += Draw;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= Draw);
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
        try
        {
            runtimeLifetime?.Dispose();
        }
        finally
        {
            foreach (var window in config.HiddenWindows)
            {
                SetImGUIWindowHidden(window, false);
            }

            foreach (var window in runtimeClosedPluginWindows)
            {
                SetImGUIWindowHidden(window, false);
            }

            runtimeClosedPluginWindows.Clear();
            runtimeLifetime = null;
            pluginState.Reset();
            commandState.Reset();
            InvalidateTextures();
        }
    }

    private void Draw()
    {
        ApplyHiddenWindows();
        if (config.PluginDock.Visible)
        {
            DrawDock(PluginDockWindowName, config.PluginDock, pluginItems, pluginState, MultiDockKind.Plugin);
        }

        if (config.CommandDock.Visible)
        {
            DrawDock(CommandDockWindowName, config.CommandDock, commandItems, commandState, MultiDockKind.Command);
        }
    }

    private void DrawDock(
        string windowName,
        MultiDockBarConfig dock,
        IReadOnlyList<MultiDockItemConfig> items,
        DockRuntimeState state,
        MultiDockKind kind)
    {
        var iconSize = OmniTheme.Scale(dock.IconSize);
        var spacing = OmniTheme.Scale(dock.IconSpacing);
        var anchor = OmniTheme.Scale(dock.Position);
        var collapsed = dock.Collapsed || state.AutoCollapsed;
        var hidden = collapsed && dock.AutoMode == MultiDockAutoMode.Hide;
        var renderPosition = GetRenderPosition(anchor, dock, iconSize, spacing, items.Count, collapsed);
        var hiddenHitSize = state.LastVisibleSize.X > 1f
            ? state.LastVisibleSize
            : iconSize * Vector2.One + ImGui.GetStyle().WindowPadding * 2f;
        if (hidden && IsMouseInside(renderPosition, hiddenHitSize))
        {
            state.AutoCollapsed = false;
            state.LastActiveAt = ImGui.GetTime();
            collapsed = false;
            hidden = false;
            renderPosition = GetRenderPosition(anchor, dock, iconSize, spacing, items.Count, false);
        }

        ImGui.SetNextWindowPos(renderPosition, ImGuiCond.Always);
        if (collapsed && !hidden)
        {
            ImGui.SetNextWindowSize(iconSize * Vector2.One + ImGui.GetStyle().WindowPadding * 2f, ImGuiCond.Always);
        }
        else if (hidden && state.LastVisibleSize.X > 1f && state.LastVisibleSize.Y > 1f)
        {
            ImGui.SetNextWindowSize(state.LastVisibleSize, ImGuiCond.Always);
        }

        var opacity = hidden ? 0f : dock.BackgroundOpacity;
        var tokens = OmniTheme.Tokens;
        using var colors = ImRaii.PushColor(ImGuiCol.WindowBg, ScaleAlpha(tokens.Background, opacity))
            .Push(ImGuiCol.Border, ScaleAlpha(tokens.Border, opacity));
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, hidden ? 0f : OmniTheme.BorderThickness())
            .Push(ImGuiStyleVar.WindowRounding, OmniTheme.Scale(tokens.BorderRadius));
        var flags = ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse |
                    ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoNav;
        if (!ImGui.Begin(windowName, flags))
        {
            ImGui.End();
            return;
        }

        var now = ImGui.GetTime();
        if (state.LastActiveAt <= 0d)
        {
            state.LastActiveAt = now;
        }

        if (hidden)
        {
            ImGui.InvisibleButton("##multiDockHiddenArea", hiddenHitSize);
        }

        var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        if (hovered)
        {
            state.AutoCollapsed = false;
            state.LastActiveAt = now;
        }
        else if (!dock.Collapsed &&
                 dock.AutoMode != MultiDockAutoMode.None &&
                 dock.AutoDelaySeconds > 0f &&
                 now - state.LastActiveAt >= dock.AutoDelaySeconds)
        {
            state.AutoCollapsed = true;
        }

        if (!hidden && DrawDockItems(dock, items, state, kind, iconSize, spacing, collapsed))
        {
            dock.Collapsed = !dock.Collapsed;
            state.AutoCollapsed = false;
            state.LastActiveAt = now;
            saveConfig();
        }

        HandleDragging(dock, state, hovered, anchor);
        if (!collapsed && !hidden)
        {
            state.LastVisibleSize = ImGui.GetWindowSize();
        }

        ImGui.End();
    }

    private bool DrawDockItems(
        MultiDockBarConfig dock,
        IReadOnlyList<MultiDockItemConfig> items,
        DockRuntimeState state,
        MultiDockKind kind,
        float iconSize,
        float spacing,
        bool collapsed)
    {
        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(spacing));
        if (collapsed || items.Count == 0)
        {
            return DrawToggleButton(dock, state, kind, iconSize, collapsed);
        }

        var toggleIndex = dock.ToggleAtEnd ? items.Count : 0;
        var toggleClicked = false;
        for (var displayIndex = 0; displayIndex <= items.Count; displayIndex++)
        {
            if (!dock.VerticalLayout && displayIndex > 0 && displayIndex % dock.ItemsPerRow != 0)
            {
                ImGui.SameLine();
            }

            var logicalIndex = dock.ReverseExpand ? items.Count - displayIndex : displayIndex;
            if (logicalIndex == toggleIndex)
            {
                toggleClicked |= DrawToggleButton(dock, state, kind, iconSize, false);
                continue;
            }

            var itemIndex = dock.ToggleAtEnd ? logicalIndex : logicalIndex - 1;
            ImGui.PushID($"multiDockItem{displayIndex}");
            if (DrawItemButton(items[itemIndex], iconSize, dock.BackgroundOpacity))
            {
                ActivateItem(items[itemIndex]);
            }

            DrawItemTooltip(items[itemIndex]);
            DrawItemContextMenu(items[itemIndex], state);
            ImGui.PopID();
        }

        return toggleClicked;
    }

    private bool DrawToggleButton(
        MultiDockBarConfig dock,
        DockRuntimeState state,
        MultiDockKind kind,
        float iconSize,
        bool collapsed)
    {
        ImGui.PushID(kind == MultiDockKind.Plugin ? "multiDockPluginToggle" : "multiDockCommandToggle");
        var clicked = DrawIconButton(dock.ToggleIcon, iconSize, dock.BackgroundOpacity);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(OmniLoc.Get(collapsed
                ? "Feature.MultiDock.Tooltip.Expand"
                : "Feature.MultiDock.Tooltip.Collapse"));
        }

        if (!state.DragMoved && ImGui.BeginPopupContextItem("toggleContext"))
        {
            if (ImGui.MenuItem(OmniLoc.Get("Feature.MultiDock.OpenSettings")))
            {
                ExecuteCommand("/omni search 多功能收纳");
            }

            ImGui.EndPopup();
        }

        ImGui.PopID();
        return clicked;
    }

    private bool DrawItemButton(MultiDockItemConfig item, float iconSize, float opacity)
    {
        if (item.Icon.gameIconID == 0 &&
            item.Icon.FilePath.Length == 0 &&
            TryParseSeIcon(item.Icon.SeIconChar, out var seIcon))
        {
            return DrawStyledButton(seIcon.ToIconString(), iconSize, opacity);
        }

        return DrawTextureButton(GetItemTexture(item), iconSize, opacity);
    }

    private bool DrawIconButton(MultiDockIconConfig icon, float iconSize, float opacity)
    {
        var wrap = icon.gameIconID != 0
            ? ImageHelper.GetGameIcon(icon.gameIconID)
            : (GetFileTexture(icon.FilePath) ?? GetFallbackTexture())?.GetWrapOrDefault();
        return DrawTextureButton(wrap, iconSize, opacity);
    }

    private static bool DrawTextureButton(IDalamudTextureWrap? wrap, float iconSize, float opacity)
    {
        if (wrap is null || wrap.Handle == nint.Zero)
        {
            return DrawStyledButton("OT", iconSize, opacity);
        }

        var tokens = OmniTheme.Tokens;
        using var colors = ImRaii.PushColor(ImGuiCol.Button, ScaleAlpha(tokens.Surface, opacity))
            .Push(ImGuiCol.ButtonHovered, ScaleAlpha(tokens.Accent, opacity))
            .Push(ImGuiCol.ButtonActive, ScaleAlpha(tokens.Secondary, opacity))
            .Push(ImGuiCol.Border, ScaleAlpha(tokens.Border, opacity));
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, OmniTheme.BorderThickness())
            .Push(ImGuiStyleVar.FrameRounding, OmniTheme.Scale(tokens.ButtonRadius));
        return ImGui.ImageButton(
            wrap.Handle,
            new Vector2(iconSize),
            Vector2.Zero,
            Vector2.One,
            0,
            Vector4.Zero,
            Vector4.One);
    }

    private static bool DrawStyledButton(string label, float iconSize, float opacity)
    {
        var tokens = OmniTheme.Tokens;
        using var colors = ImRaii.PushColor(ImGuiCol.Button, ScaleAlpha(tokens.Surface, opacity))
            .Push(ImGuiCol.ButtonHovered, ScaleAlpha(tokens.Accent, opacity))
            .Push(ImGuiCol.ButtonActive, ScaleAlpha(tokens.Secondary, opacity))
            .Push(ImGuiCol.Border, ScaleAlpha(tokens.Border, opacity));
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, OmniTheme.BorderThickness())
            .Push(ImGuiStyleVar.FrameRounding, OmniTheme.Scale(tokens.ButtonRadius));
        return ImGui.Button(label, new Vector2(iconSize));
    }

    private void DrawItemTooltip(MultiDockItemConfig item)
    {
        if (!ImGui.IsItemHovered())
        {
            return;
        }

        ImGui.BeginTooltip();
        ImGui.TextUnformatted(GetItemName(item));
        ImGui.TextDisabled(OmniLoc.Get(item.Type switch
        {
            MultiDockItemType.Plugin => "Feature.MultiDock.Type.Plugin",
            MultiDockItemType.ImGuiWindow => "Feature.MultiDock.Type.Window",
            _ => "Feature.MultiDock.Type.Command"
        }));
        ImGui.TextDisabled(GetItemTarget(item));
        ImGui.EndTooltip();
    }

    private void DrawItemContextMenu(MultiDockItemConfig item, DockRuntimeState state)
    {
        if (state.DragMoved || !ImGui.BeginPopupContextItem("context"))
        {
            return;
        }

        var hasMacro = false;
        for (var i = 0; i < item.ContextMenuMacros.Count; i++)
        {
            if (item.ContextMenuMacros[i].Command.Length == 0)
            {
                continue;
            }

            if (ImGui.MenuItem(item.ContextMenuMacros[i].Name.Length == 0
                    ? item.ContextMenuMacros[i].Command
                    : item.ContextMenuMacros[i].Name))
            {
                ExecuteCommand(item.ContextMenuMacros[i].Command);
            }

            hasMacro = true;
        }

        if (item.ClickCommand.Length > 0)
        {
            if (hasMacro)
            {
                ImGui.Separator();
            }

            if (ImGui.MenuItem(OmniLoc.Get("Feature.MultiDock.ClickCommand")))
            {
                ExecuteCommand(item.ClickCommand);
            }
        }
        else if (!hasMacro)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MultiDock.NoContextMacros"));
        }

        ImGui.EndPopup();
    }

    private void ActivateItem(MultiDockItemConfig item)
    {
        if (item.ClickCommand.Length > 0)
        {
            ExecuteCommand(item.ClickCommand);
            return;
        }

        switch (item.Type)
        {
            case MultiDockItemType.Plugin:
                ActivatePluginItem(item);
                break;
            case MultiDockItemType.ImGuiWindow:
                ActivateWindowItem(item);
                break;
            case MultiDockItemType.Command:
                ExecuteCommand(item.Command);
                break;
        }
    }

    private void ActivatePluginItem(MultiDockItemConfig item)
    {
        var plugin = FindPlugin(item.PluginInternalName);
        if (plugin is null)
        {
            return;
        }

        if (TryActivatePluginWindowSystem(plugin, item.WindowName, item.preferConfigUI))
        {
            return;
        }

        var targetWindow = item.WindowName.Length > 0 ? item.WindowName : FindPluginWindowName(plugin);
        var targetKey = NormalizeWindowKey(targetWindow);
        if (targetKey.Length > 0)
        {
        var targetKnown = TryGetImGUIWindowState(targetKey, out var targetOpen);
            if (runtimeClosedPluginWindows.Remove(targetKey))
            {
                SetImGUIWindowHidden(targetKey, false);
                if (targetKnown)
                {
                    ImGui.SetWindowFocus(targetWindow);
                    return;
                }
            }
            else if (targetOpen)
            {
                runtimeClosedPluginWindows.Add(targetKey);
                SetImGUIWindowHidden(targetKey, true);
                return;
            }
        }

        OpenPlugin(plugin, item.preferConfigUI);
    }

    private void ActivateWindowItem(MultiDockItemConfig item)
    {
        var targetKey = NormalizeWindowKey(item.WindowName);
        if (targetKey.Length == 0 || IsOwnDockWindowKey(targetKey))
        {
            return;
        }

        if (config.HiddenWindows.Remove(targetKey))
        {
            appliedHiddenWindows.Remove(targetKey);
            var plugin = FindPlugin(item.LinkedPluginInternalName);
            if (plugin is not null)
            {
                OpenPlugin(plugin, item.preferConfigUI);
            }

            SetImGUIWindowHidden(targetKey, false);
            ImGui.SetWindowFocus(item.WindowName);
        }
        else
        {
            config.HiddenWindows.Add(targetKey);
            appliedHiddenWindows.Add(targetKey);
            SetImGUIWindowHidden(targetKey, true);
        }

        saveConfig();
    }

    private static IExposedPlugin? FindPlugin(string internalName)
    {
        foreach (var plugin in DalamudServices.PluginInterface.InstalledPlugins)
        {
            if (plugin.IsLoaded &&
                plugin.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase))
            {
                return plugin;
            }
        }

        return null;
    }

    private bool TryActivatePluginWindowSystem(
        IExposedPlugin plugin,
        string configuredWindowName,
        bool preferConfigUI)
    {
        var windowSystems = FindPluginWindowSystems(plugin);
        if (windowSystems.Count == 0)
        {
            return false;
        }

        var targetKey = NormalizeWindowKey(configuredWindowName);
        IWindow? firstWindow = null;
        IWindow? openWindow = null;
        var windowCount = 0;
        var seenWindows = new HashSet<IWindow>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < windowSystems.Count; i++)
        {
            foreach (var window in windowSystems[i].Windows)
            {
                if (!seenWindows.Add(window))
                {
                    continue;
                }

                windowCount++;
                firstWindow ??= window;
                var windowKey = NormalizeWindowKey(window.WindowName);
                var wasRuntimeClosed = runtimeClosedPluginWindows.Remove(windowKey);
                if (wasRuntimeClosed)
                {
                    SetImGUIWindowHidden(windowKey, false);
                }

                if (targetKey.Length > 0 && windowKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
                {
                    window.IsOpen = wasRuntimeClosed || !window.IsOpen;
                    window.RequestFocus = window.IsOpen;
                    return true;
                }

                if (targetKey.Length == 0 && wasRuntimeClosed)
                {
                    window.IsOpen = true;
                    window.RequestFocus = true;
                    return true;
                }

                if (targetKey.Length == 0 && window.IsOpen)
                {
                    openWindow ??= window;
                }
            }
        }

        if (openWindow is not null)
        {
            openWindow.IsOpen = false;
        }
        else if (windowCount == 1 && firstWindow is not null)
        {
            firstWindow.IsOpen = true;
            firstWindow.RequestFocus = true;
        }
        else
        {
            OpenPlugin(plugin, preferConfigUI);
        }

        return true;
    }

    private static List<IWindowSystem> FindPluginWindowSystems(IExposedPlugin plugin)
    {
        object? pluginInstance = null;
        try
        {
            if (DalamudReflector.GetPluginManager().GetFieldOrProperty("InstalledPlugins") is not IEnumerable installedPlugins)
            {
                return [];
            }

            foreach (var installedPlugin in installedPlugins)
            {
                if (installedPlugin is null ||
                    (!(installedPlugin.GetFieldOrProperty<string>("InternalName") ?? string.Empty)
                        .Equals(plugin.InternalName, StringComparison.OrdinalIgnoreCase) &&
                     !(installedPlugin.GetFieldOrProperty<string>("Name") ?? string.Empty)
                         .Equals(plugin.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                pluginInstance = installedPlugin.GetFieldOrProperty("instance") ??
                                 installedPlugin.GetFieldOrProperty("Instance");
                for (var type = installedPlugin.GetType().BaseType;
                     pluginInstance is null && type is not null;
                     type = type.BaseType)
                {
                    pluginInstance = type.GetField(
                                             "instance",
                                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                             BindingFlags.DeclaredOnly)
                                         ?.GetValue(installedPlugin) ??
                                     type.GetProperty(
                                             "Instance",
                                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                             BindingFlags.DeclaredOnly)
                                         ?.GetValue(installedPlugin);
                }

                break;
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Debug(ex, "MultiDock failed to inspect plugin {PluginName}.", plugin.InternalName);
            return [];
        }

        if (pluginInstance is null)
        {
            return [];
        }

        var result = new List<IWindowSystem>();
        var seen = new HashSet<IWindowSystem>(ReferenceEqualityComparer.Instance);
        for (var type = pluginInstance.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                if (!typeof(IWindowSystem).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                IWindowSystem? windowSystem;
                try
                {
                    windowSystem = field.GetValue(pluginInstance) as IWindowSystem;
                }
                catch
                {
                    continue;
                }

                if (windowSystem is not null && seen.Add(windowSystem))
                {
                    result.Add(windowSystem);
                }
            }

            foreach (var property in type.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                if (property.GetIndexParameters().Length != 0 ||
                    property.GetGetMethod(true) is null ||
                    !typeof(IWindowSystem).IsAssignableFrom(property.PropertyType))
                {
                    continue;
                }

                IWindowSystem? windowSystem;
                try
                {
                    windowSystem = property.GetValue(pluginInstance) as IWindowSystem;
                }
                catch
                {
                    continue;
                }

                if (windowSystem is not null && seen.Add(windowSystem))
                {
                    result.Add(windowSystem);
                }
            }
        }

        return result;
    }

    internal IReadOnlyList<MultiDockWindowCandidate> GetWindowCandidates()
    {
        var result = new List<MultiDockWindowCandidate>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in DalamudServices.PluginInterface.InstalledPlugins)
        {
            if (!plugin.IsLoaded ||
                plugin.InternalName.Equals(
                    DalamudServices.PluginInterface.InternalName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var windowSystems = FindPluginWindowSystems(plugin);
            for (var i = 0; i < windowSystems.Count; i++)
            {
                foreach (var window in windowSystems[i].Windows)
                {
                    var key = NormalizeWindowKey(window.WindowName);
                    if (key.Length == 0 || IsOwnDockWindowKey(key) || !seenKeys.Add(key))
                    {
                        continue;
                    }

                    result.Add(new(
                        plugin.InternalName,
                        plugin.Name,
                        window.WindowName,
                        BuildWindowDisplayName(window.WindowName)));
                }
            }
        }

        result.Sort(static (left, right) =>
        {
            var order = StringComparer.OrdinalIgnoreCase.Compare(left.PluginName, right.PluginName);
            return order != 0
                ? order
                : StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
        });
        return result;
    }

    private static string BuildWindowDisplayName(string name)
    {
        var value = name.Trim();
        var separator = value.IndexOf("###", StringComparison.Ordinal);
        if (separator == 0)
        {
            value = value[3..].Trim();
        }
        else if (separator > 0)
        {
            value = value[..separator].Trim();
        }

        return value.Length > 0 ? value : name;
    }

    private static void OpenPlugin(IExposedPlugin plugin, bool preferConfigUI)
    {
        try
        {
            if (preferConfigUI && plugin.HasConfigUi)
            {
                plugin.OpenConfigUi();
            }
            else if (plugin.HasMainUi)
            {
                plugin.OpenMainUi();
            }
            else if (plugin.HasConfigUi)
            {
                plugin.OpenConfigUi();
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiDock failed to open plugin {PluginName}.", plugin.InternalName);
        }
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
            DalamudServices.PluginLog.Error(ex, "MultiDock command execution failed: {Command}", normalized);
        }
    }

    private unsafe void ApplyHiddenWindows()
    {
        if (config.HiddenWindows.Count == 0 && runtimeClosedPluginWindows.Count == 0)
        {
            return;
        }

        var context = ImGuiNative.GetCurrentContext();
        if (context == null)
        {
            return;
        }

        var contextPtr = new ImGuiContextPtr(context);
        ref var windows = ref contextPtr.Windows;
        for (var i = 0; i < windows.Size; i++)
        {
            var window = windows[i];
            if (window.Handle == null || window.Name == null)
            {
                continue;
            }

            var key = NormalizeWindowKey(Marshal.PtrToStringUTF8((nint)window.Name));
            if (config.HiddenWindows.Contains(key) || runtimeClosedPluginWindows.Contains(key))
            {
                SetImGUIWindowHidden(window, true);
            }
        }
    }

    private static unsafe void SetImGUIWindowHidden(string windowName, bool hidden)
    {
        var key = NormalizeWindowKey(windowName);
        var context = ImGuiNative.GetCurrentContext();
        if (key.Length == 0 || context == null)
        {
            return;
        }

        var contextPtr = new ImGuiContextPtr(context);
        ref var windows = ref contextPtr.Windows;
        for (var i = 0; i < windows.Size; i++)
        {
            var window = windows[i];
            if (window.Handle == null || window.Name == null ||
                !NormalizeWindowKey(Marshal.PtrToStringUTF8((nint)window.Name))
                    .Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SetImGUIWindowHidden(window, hidden);
        }
    }

    private static unsafe void SetImGUIWindowHidden(ImGuiWindowPtr window, bool hidden)
    {
        if (hidden)
        {
            const sbyte hiddenFrames = 2;
            window.Hidden = true;
            window.SkipItems = true;
            window.HiddenFramesCanSkipItems = hiddenFrames;
            window.HiddenFramesCannotSkipItems = hiddenFrames;
            window.HiddenFramesForRenderOnly = hiddenFrames;
            ref var drawList = ref window.DrawList;
            drawList.CmdBuffer.Clear();
            drawList.IdxBuffer.Clear();
            drawList.VtxBuffer.Clear();
            return;
        }

        window.Hidden = false;
        window.SkipItems = false;
        window.HiddenFramesCanSkipItems = 0;
        window.HiddenFramesCannotSkipItems = 0;
        window.HiddenFramesForRenderOnly = 0;
    }

    private static unsafe bool TryGetImGUIWindowState(string windowName, out bool open)
    {
        open = false;
        var key = NormalizeWindowKey(windowName);
        var context = ImGuiNative.GetCurrentContext();
        if (key.Length == 0 || context == null)
        {
            return false;
        }

        var contextPtr = new ImGuiContextPtr(context);
        ref var windows = ref contextPtr.Windows;
        for (var i = 0; i < windows.Size; i++)
        {
            var window = windows[i];
            if (window.Handle != null && window.Name != null &&
                NormalizeWindowKey(Marshal.PtrToStringUTF8((nint)window.Name))
                    .Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                open = window.WasActive && !window.Hidden;
                return true;
            }
        }

        return false;
    }

    private static unsafe string FindPluginWindowName(IExposedPlugin plugin)
    {
        var context = ImGuiNative.GetCurrentContext();
        if (context == null)
        {
            return string.Empty;
        }

        var result = string.Empty;
        var resultDisplayNameLength = int.MaxValue;
        var contextPtr = new ImGuiContextPtr(context);
        ref var windows = ref contextPtr.Windows;
        for (var i = 0; i < windows.Size; i++)
        {
            var window = windows[i];
            if (window.Handle == null || window.Name == null)
            {
                continue;
            }

        var name = Marshal.PtrToStringUTF8((nint)window.Name) ?? string.Empty;
            var key = NormalizeWindowKey(name);
            if (IsOwnDockWindowKey(key))
            {
                continue;
            }

            var separator = name.IndexOf("###", StringComparison.Ordinal);
            var displayName = (separator >= 0 ? name[..separator] : name).Trim();
            if ((plugin.Name.Length == 0 ||
                 (!displayName.Contains(plugin.Name, StringComparison.OrdinalIgnoreCase) &&
                  !key.Contains(plugin.Name, StringComparison.OrdinalIgnoreCase))) &&
                (plugin.InternalName.Length == 0 ||
                 (!displayName.Contains(plugin.InternalName, StringComparison.OrdinalIgnoreCase) &&
                  !key.Contains(plugin.InternalName, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            if (displayName.Length < resultDisplayNameLength)
            {
                result = name;
                resultDisplayNameLength = displayName.Length;
            }
        }

        return result;
    }

    internal static string NormalizeWindowKey(string? windowName)
    {
        var value = (windowName ?? string.Empty).Trim();
        var separator = value.IndexOf("###", StringComparison.Ordinal);
        return separator >= 0 ? value[separator..] : value;
    }

    private static bool IsOwnDockWindowKey(string key) =>
        key.Equals(PluginDockWindowKey, StringComparison.OrdinalIgnoreCase) ||
        key.Equals(CommandDockWindowKey, StringComparison.OrdinalIgnoreCase);

    private void HandleDragging(
        MultiDockBarConfig dock,
        DockRuntimeState state,
        bool hovered,
        Vector2 anchor)
    {
        if (dock.LockPosition)
        {
            state.Dragging = false;
            state.DragMoved = false;
            return;
        }

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            state.Dragging = true;
            state.DragMoved = false;
        }

        if (state.Dragging && ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (delta.LengthSquared() > 0.001f)
            {
                var windowPosition = ImGui.GetWindowPos();
                dock.Position = OmniTheme.Unscale(
                    anchor + ClampPosition(windowPosition + delta, ImGui.GetWindowSize()) - windowPosition);
                state.DragMoved = true;
            }
        }

        if (!state.Dragging || !ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            return;
        }

        state.Dragging = false;
        if (state.DragMoved)
        {
            saveConfig();
        }

        state.DragMoved = false;
    }

    private static Vector2 GetRenderPosition(
        Vector2 anchor,
        MultiDockBarConfig dock,
        float iconSize,
        float spacing,
        int itemCount,
        bool collapsed)
    {
        if (!dock.ReverseExpand || collapsed)
        {
            return anchor;
        }

        var size = GetExpectedSize(dock, iconSize, spacing, itemCount);
        var padding = ImGui.GetStyle().WindowPadding * 2f;
        return dock.VerticalLayout
            ? new(anchor.X, anchor.Y - MathF.Max(0f, size.Y - iconSize - padding.Y))
            : new(anchor.X - MathF.Max(0f, size.X - iconSize - padding.X), anchor.Y);
    }

    private static Vector2 GetExpectedSize(
        MultiDockBarConfig dock,
        float iconSize,
        float spacing,
        int itemCount)
    {
        var count = Math.Max(1, itemCount + 1);
        var padding = ImGui.GetStyle().WindowPadding * 2f;
        if (dock.VerticalLayout)
        {
            return new Vector2(iconSize, count * iconSize + (count - 1) * spacing) + padding;
        }

        var columns = Math.Min(dock.ItemsPerRow, count);
        var rows = (count + dock.ItemsPerRow - 1) / dock.ItemsPerRow;
        return new Vector2(
            columns * iconSize + (columns - 1) * spacing,
            rows * iconSize + (rows - 1) * spacing) + padding;
    }

    private static Vector2 ClampPosition(Vector2 position, Vector2 size)
    {
        var viewport = ImGuiHelpers.MainViewport;
        return new(
            Math.Clamp(position.X, viewport.WorkPos.X, viewport.WorkPos.X + MathF.Max(0f, viewport.WorkSize.X - size.X)),
            Math.Clamp(position.Y, viewport.WorkPos.Y, viewport.WorkPos.Y + MathF.Max(0f, viewport.WorkSize.Y - size.Y)));
    }

    private static bool IsMouseInside(Vector2 position, Vector2 size)
    {
        var mouse = ImGui.GetIO().MousePos;
        return mouse.X >= position.X &&
               mouse.X <= position.X + size.X &&
               mouse.Y >= position.Y &&
               mouse.Y <= position.Y + size.Y;
    }

    private IDalamudTextureWrap? GetItemTexture(MultiDockItemConfig item)
    {
        if (item.Icon.gameIconID != 0)
        {
            return ImageHelper.GetGameIcon(item.Icon.gameIconID);
        }

        var customTexture = GetFileTexture(item.Icon.FilePath)?.GetWrapOrDefault();
        if (customTexture is not null)
        {
            return customTexture;
        }

        var pluginInternalName = item.Type switch
        {
            MultiDockItemType.Plugin => item.PluginInternalName,
            MultiDockItemType.ImGuiWindow => item.LinkedPluginInternalName,
            _ => string.Empty
        };
        var plugin = FindPlugin(pluginInternalName);
        return plugin is not null
            ? GetPluginTexture(plugin)
            : GetFallbackTexture()?.GetWrapOrDefault();
    }

    internal IDalamudTextureWrap? GetPluginTexture(IExposedPlugin plugin)
    {
        var source = GetPluginIconSource(plugin);
        return source.Length > 0 && ImageHelper.Instance().GetImage(source) is { } texture
            ? texture
            : GetFallbackTexture()?.GetWrapOrDefault();
    }

    private string GetPluginIconSource(IExposedPlugin plugin)
    {
        if (pluginIconSources.TryGetValue(plugin.InternalName, out var source))
        {
            return source;
        }

        source = FindLocalPluginIcon(plugin) ?? plugin.Manifest.IconUrl?.Trim() ?? string.Empty;
        if (source.Length == 0 && !string.IsNullOrWhiteSpace(plugin.Manifest.Dip17Channel))
        {
            source = $"https://raw.githubusercontent.com/goatcorp/PluginDistD17/main/{Uri.EscapeDataString(plugin.Manifest.Dip17Channel)}/{Uri.EscapeDataString(plugin.InternalName)}/images/icon.png";
        }

        pluginIconSources[plugin.InternalName] = source;
        return source;
    }

    private static string? FindLocalPluginIcon(IExposedPlugin plugin)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (var launcher in LauncherDirectoryNames)
            {
                var versionDirectory = Path.Combine(
                    appData,
                    launcher,
                    "installedPlugins",
                    plugin.InternalName,
                    plugin.Version.ToString());
                if (!Directory.Exists(versionDirectory))
                {
                    continue;
                }

                string? fallback = null;
                foreach (var file in Directory.EnumerateFiles(versionDirectory, "*", SearchOption.AllDirectories))
                {
                    if (!IsSupportedPluginIcon(file))
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.Equals("icon", StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }

                    if (fallback is null && name.Contains("icon", StringComparison.OrdinalIgnoreCase))
                    {
                        fallback = file;
                    }
                }

                if (fallback is not null)
                {
                    return fallback;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static bool IsSupportedPluginIcon(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";

    private ISharedImmediateTexture? GetFileTexture(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        if (fileTextures.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var resolved = Path.IsPathFullyQualified(path)
            ? path
            : Path.Combine(DalamudServices.PluginInterface.AssemblyLocation.DirectoryName!, path);
        if (!File.Exists(resolved))
        {
            fileTextures[path] = null;
            return null;
        }

        try
        {
            return fileTextures[path] = DalamudServices.TextureProvider.GetFromFile(resolved);
        }
        catch (Exception ex)
        {
            fileTextures[path] = null;
            if (failedTexturePaths.Add(path))
            {
                DalamudServices.PluginLog.Warning(ex, "MultiDock icon load failed: {IconPath}", resolved);
            }

            return null;
        }
    }

    private ISharedImmediateTexture? GetFallbackTexture()
    {
        if (fallbackTextureLoaded)
        {
            return fallbackTexture;
        }

        fallbackTextureLoaded = true;
        fallbackTexture = GetFileTexture(Path.Combine("images", "icon.png"));
        return fallbackTexture;
    }

    private void NormalizeConfig()
    {
        config.PluginDock ??= new();
        config.CommandDock ??= new() { Position = new(520f, 260f) };
        config.Items ??= [];
        NormalizeDock(config.PluginDock, new(360f, 260f));
        NormalizeDock(config.CommandDock, new(520f, 260f));

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var windowKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < config.Items.Count;)
        {
            if (!NormalizeItem(config.Items[i], out var key) || !keys.Add(key))
            {
                config.Items.RemoveAt(i);
                continue;
            }

            if (config.Items[i].Type == MultiDockItemType.ImGuiWindow)
            {
                windowKeys.Add(NormalizeWindowKey(config.Items[i].WindowName));
            }

            i++;
        }

        var hiddenWindows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in config.HiddenWindows ?? [])
        {
            var key = NormalizeWindowKey(window);
            if (windowKeys.Contains(key) && !IsOwnDockWindowKey(key))
            {
                hiddenWindows.Add(key);
            }
        }

        config.HiddenWindows = hiddenWindows;
    }

    private static void NormalizeDock(MultiDockBarConfig dock, Vector2 defaultPosition)
    {
        if (dock.Position.X <= 0f || dock.Position.Y <= 0f)
        {
            dock.Position = defaultPosition;
        }

        dock.IconSize = Math.Clamp(dock.IconSize <= 0f ? 45f : dock.IconSize, 24f, 96f);
        dock.IconSpacing = Math.Clamp(dock.IconSpacing, 0f, 40f);
        dock.BackgroundOpacity = Math.Clamp(dock.BackgroundOpacity, 0f, 1f);
        dock.ItemsPerRow = Math.Clamp(dock.ItemsPerRow, 1, 20);
        dock.AutoMode = Enum.IsDefined(dock.AutoMode) ? dock.AutoMode : MultiDockAutoMode.None;
        dock.AutoDelaySeconds = Math.Clamp(dock.AutoDelaySeconds, 1f, 120f);
        dock.ToggleIcon ??= new();
        NormalizeIcon(dock.ToggleIcon);
    }

    private static bool NormalizeItem(MultiDockItemConfig? item, out string key)
    {
        key = string.Empty;
        if (item is null || !Enum.IsDefined(item.Type))
        {
            return false;
        }

        item.Name = (item.Name ?? string.Empty).Trim();
        item.PluginInternalName = (item.PluginInternalName ?? string.Empty).Trim();
        item.WindowName = (item.WindowName ?? string.Empty).Trim();
        item.Command = (item.Command ?? string.Empty).Trim();
        item.ClickCommand = TryNormalizeCommand(item.ClickCommand, out var clickCommand) ? clickCommand : string.Empty;
        item.LinkedPluginInternalName = (item.LinkedPluginInternalName ?? string.Empty).Trim();
        item.Icon ??= new();
        NormalizeIcon(item.Icon);
        item.ContextMenuMacros ??= [];
        for (var i = 0; i < item.ContextMenuMacros.Count;)
        {
            var macro = item.ContextMenuMacros[i];
            if (macro is null || !TryNormalizeCommand(macro.Command, out var macroCommand))
            {
                item.ContextMenuMacros.RemoveAt(i);
                continue;
            }

            macro.Name = (macro.Name ?? string.Empty).Trim();
            macro.Command = macroCommand;
            i++;
        }

        switch (item.Type)
        {
            case MultiDockItemType.Plugin when item.PluginInternalName.Length > 0:
                key = $"plugin:{item.PluginInternalName}";
                return true;
            case MultiDockItemType.ImGuiWindow when item.WindowName.Length > 0:
                var windowKey = NormalizeWindowKey(item.WindowName);
                if (windowKey.Length > 0 && !IsOwnDockWindowKey(windowKey))
                {
                    key = $"window:{windowKey}";
                    return true;
                }

                return false;
            case MultiDockItemType.Command when TryNormalizeCommand(item.Command, out var command):
                item.Command = command;
                key = $"command:{command}";
                return true;
            default:
                return false;
        }
    }

    private static void NormalizeIcon(MultiDockIconConfig icon)
    {
        icon.FilePath = (icon.FilePath ?? string.Empty).Trim();
        icon.SeIconChar = (icon.SeIconChar ?? string.Empty).Trim();
    }

    private void OnItemsChanged()
    {
        foreach (var window in runtimeClosedPluginWindows)
        {
            SetImGUIWindowHidden(window, false);
        }

        runtimeClosedPluginWindows.Clear();
        NormalizeConfig();
        foreach (var window in appliedHiddenWindows)
        {
            if (!config.HiddenWindows.Contains(window))
            {
                SetImGUIWindowHidden(window, false);
            }
        }

        appliedHiddenWindows.Clear();
        appliedHiddenWindows.UnionWith(config.HiddenWindows);
        RefreshItemCaches();
        InvalidateTextures();
    }

    private void RefreshItemCaches()
    {
        pluginItems.Clear();
        commandItems.Clear();
        for (var i = 0; i < config.Items.Count; i++)
        {
            if (config.Items[i].Type == MultiDockItemType.Command)
            {
                commandItems.Add(config.Items[i]);
            }
            else
            {
                pluginItems.Add(config.Items[i]);
            }
        }
    }

    private void InvalidateTextures()
    {
        fileTextures.Clear();
        pluginIconSources.Clear();
        failedTexturePaths.Clear();
        fallbackTexture = null;
        fallbackTextureLoaded = false;
    }

    private static string GetItemName(MultiDockItemConfig item) => item.Name.Length > 0
        ? item.Name
        : GetItemTarget(item);

    private static string GetItemTarget(MultiDockItemConfig item) => item.Type switch
    {
        MultiDockItemType.Plugin => item.PluginInternalName,
        MultiDockItemType.ImGuiWindow => item.WindowName,
        _ => item.Command
    };

    private static Vector4 ScaleAlpha(Vector4 color, float opacity) =>
        new(color.X, color.Y, color.Z, color.W * Math.Clamp(opacity, 0f, 1f));

    private enum MultiDockKind
    {
        Plugin,
        Command
    }

    private sealed class DockRuntimeState
    {
        public bool AutoCollapsed { get; set; }

        public bool Dragging { get; set; }

        public bool DragMoved { get; set; }

        public double LastActiveAt { get; set; }

        public Vector2 LastVisibleSize { get; set; }

        public void Reset()
        {
            AutoCollapsed = false;
            Dragging = false;
            DragMoved = false;
            LastActiveAt = 0d;
            LastVisibleSize = Vector2.Zero;
        }
    }
}

internal readonly record struct MultiDockWindowCandidate(
    string PluginInternalName,
    string PluginName,
    string WindowName,
    string DisplayName);

[Serializable]
public sealed class MultiDockConfig
{
    public MultiDockBarConfig PluginDock { get; set; } = new();
    public MultiDockBarConfig CommandDock { get; set; } = new() { Position = new(520f, 260f) };
    public List<MultiDockItemConfig> Items { get; set; } = [];
    public HashSet<string> HiddenWindows { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[Serializable]
public sealed class MultiDockBarConfig
{
    public bool Visible { get; set; } = true;
    public bool Collapsed { get; set; }
    public Vector2 Position { get; set; } = new(360f, 260f);
    public float IconSize { get; set; } = 45f;
    public float IconSpacing { get; set; } = 6f;
    public float BackgroundOpacity { get; set; } = 0.6f;
    public int ItemsPerRow { get; set; } = 1;
    public MultiDockAutoMode AutoMode { get; set; }
    public float AutoDelaySeconds { get; set; } = 1f;
    public bool VerticalLayout { get; set; } = true;
    public bool LockPosition { get; set; }
    public bool ToggleAtEnd { get; set; }
    public bool ReverseExpand { get; set; }
    public MultiDockIconConfig ToggleIcon { get; set; } = new();
}

public enum MultiDockAutoMode { None, Hide, Collapse }

[Serializable]
public sealed class MultiDockItemConfig
{
    public MultiDockItemType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PluginInternalName { get; set; } = string.Empty;
    public string WindowName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string ClickCommand { get; set; } = string.Empty;
    public string LinkedPluginInternalName { get; set; } = string.Empty;
    public bool preferConfigUI { get; set; }
    public MultiDockIconConfig Icon { get; set; } = new();
    public List<MultiDockMacroConfig> ContextMenuMacros { get; set; } = [];
}

public enum MultiDockItemType { Plugin, ImGuiWindow, Command }

[Serializable]
public sealed class MultiDockIconConfig
{
    public uint gameIconID { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string SeIconChar { get; set; } = string.Empty;
}

[Serializable]
public sealed class MultiDockMacroConfig
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
}

internal sealed class MultiDockPanel
{
    private const int ClipboardVersion = 1;
    private const float SourcePickerHeight = 320f;
    private const float CommandSectionHeight = 520f;
    private static readonly MultiDockAutoMode[] AutoModes = Enum.GetValues<MultiDockAutoMode>();
    private static readonly JsonSerializerOptions ClipboardJSONOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly MultiDockConfig config;
    private readonly MultiDock feature;
    private readonly IconBrowser iconBrowser;
    private readonly Action saveConfig;
    private readonly Action itemsChanged;
    private readonly Action iconsChanged;
    private readonly List<IExposedPlugin> pluginRows = [];
    private readonly List<MultiDockWindowCandidate> windowRows = [];
    private readonly Dictionary<string, int> pluginItemIndices = new(StringComparer.OrdinalIgnoreCase);
    private string pluginFilter = string.Empty;
    private string windowFilter = string.Empty;
    private string commandName = string.Empty;
    private string commandText = string.Empty;
    private string commandFilter = string.Empty;
    private string clipboardStatusKey = string.Empty;
    private double clipboardStatusUntil;
    private bool clipboardStatusIsError;
    private bool windowRowsLoaded;
    private int editingItemIndex = -1;
    private string editingToggleIconID = string.Empty;

    public MultiDockPanel(
        MultiDockConfig config,
        MultiDock feature,
        IconBrowser iconBrowser,
        Action saveConfig,
        Action itemsChanged,
        Action iconsChanged)
    {
        this.config = config;
        this.feature = feature;
        this.iconBrowser = iconBrowser;
        this.saveConfig = saveConfig;
        this.itemsChanged = itemsChanged;
        this.iconsChanged = iconsChanged;
    }

    public bool Draw()
    {
        var changed = DrawClipboardToolbar();
        ImGui.Spacing();
        if (OmniControls.CollapsingHeader(
                OmniLoc.Get("Feature.MultiDock.PluginDock"),
                ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= DrawDockSettings(config.PluginDock, "plugin");
            ImGui.Spacing();
            changed |= DrawPluginAndWindowManagement();
        }

        ImGui.Spacing();
        if (OmniControls.CollapsingHeader(
                OmniLoc.Get("Feature.MultiDock.CommandDock"),
                ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= DrawDockSettings(config.CommandDock, "command");
            ImGui.Spacing();
            changed |= DrawCommandManagement();
        }

        return changed;
    }

    private bool DrawClipboardToolbar()
    {
        var changed = false;
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.MultiDock.Export"), false))
        {
            ImGui.SetClipboardText(JsonSerializer.Serialize(
                new MultiDockClipboardSnapshot { Config = config },
                ClipboardJSONOptions));
            SetClipboardStatus("Feature.MultiDock.ExportSuccess", false);
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.MultiDock.Import"), false))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<MultiDockClipboardSnapshot>(
                    ImGui.GetClipboardText(),
                    ClipboardJSONOptions);
                if (snapshot is null || snapshot.Version != ClipboardVersion || snapshot.Config is null)
                {
                    throw new JsonException("Unsupported MultiDock clipboard data.");
                }

                config.PluginDock = snapshot.Config.PluginDock;
                config.CommandDock = snapshot.Config.CommandDock;
                config.Items = snapshot.Config.Items;
                config.HiddenWindows = snapshot.Config.HiddenWindows;
                itemsChanged();
                editingItemIndex = -1;
                editingToggleIconID = string.Empty;
                changed = true;
                SetClipboardStatus("Feature.MultiDock.ImportSuccess", false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                DalamudServices.PluginLog.Warning(ex, "MultiDock configuration import failed.");
                SetClipboardStatus("Feature.MultiDock.ImportFailed", true);
            }
        }

        if (clipboardStatusKey.Length == 0 || ImGui.GetTime() >= clipboardStatusUntil)
        {
            return changed;
        }

        ImGui.SameLine();
        ImGui.TextColored(
            clipboardStatusIsError ? OmniTheme.Tokens.Error : OmniTheme.Tokens.Success,
            OmniLoc.Get(clipboardStatusKey));
        return changed;
    }

    private bool DrawDockSettings(MultiDockBarConfig dock, string id)
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   $"##multiDockFlags{id}",
                   3,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    id == "plugin" ? "Feature.MultiDock.Visible.Plugin" : "Feature.MultiDock.Visible.Command",
                    $"visible{id}",
                    dock.Visible,
                    value => dock.Visible = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.MultiDock.LockPosition",
                    $"lock{id}",
                    dock.LockPosition,
                    value => dock.LockPosition = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.MultiDock.VerticalLayout",
                    $"vertical{id}",
                    dock.VerticalLayout,
                    value => dock.VerticalLayout = value);
            }
        }

        using (var settingsTable = ImRaii.Table(
                   $"##multiDockSettings{id}",
                   4,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (settingsTable)
            {
                ImGui.TableNextRow();
                changed |= DrawFloatSliderCell(
                    "Feature.MultiDock.IconSize",
                    $"iconSize{id}",
                    dock.IconSize,
                    value => dock.IconSize = value,
                    24f,
                    96f,
                    "%.0f");
                changed |= DrawFloatSliderCell(
                    "Feature.MultiDock.BackgroundOpacity",
                    $"opacity{id}",
                    dock.BackgroundOpacity,
                    value => dock.BackgroundOpacity = value,
                    0f,
                    1f,
                    "%.2f");
                changed |= DrawIntSliderCell(
                    "Feature.MultiDock.ItemsPerRow",
                    $"itemsPerRow{id}",
                    dock.ItemsPerRow,
                    value => dock.ItemsPerRow = value,
                    1,
                    20);
                changed |= DrawFloatSliderCell(
                    "Feature.MultiDock.IconSpacing",
                    $"spacing{id}",
                    dock.IconSpacing,
                    value => dock.IconSpacing = value,
                    0f,
                    40f,
                    "%.0f");
            }
        }

        using var behaviorTable = ImRaii.Table(
            $"##multiDockBehavior{id}",
            4,
            ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!behaviorTable)
        {
            return changed;
        }

        ImGui.TableNextRow();
        changed |= DrawFloatSliderCell(
            "Feature.MultiDock.AutoDelay",
            $"autoDelay{id}",
            dock.AutoDelaySeconds,
            value => dock.AutoDelaySeconds = value,
            1f,
            120f,
            "%.0f");
        changed |= DrawAutoModeComboCell(dock, id);
        changed |= DrawBoolComboCell(
            "Feature.MultiDock.TogglePosition",
            $"togglePosition{id}",
            dock.ToggleAtEnd,
            value => dock.ToggleAtEnd = value,
            "Feature.MultiDock.TogglePosition.Start",
            "Feature.MultiDock.TogglePosition.End");
        changed |= DrawBoolComboCell(
            "Feature.MultiDock.ExpandDirection",
            $"expandDirection{id}",
            dock.ReverseExpand,
            value => dock.ReverseExpand = value,
            "Feature.MultiDock.ExpandDirection.Forward",
            "Feature.MultiDock.ExpandDirection.Reverse");
        return changed;
    }

    private bool DrawPluginAndWindowManagement()
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##multiDockPluginWindowSources",
                   2,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiDock.PluginSource"));
                ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiDock.WindowSource"));
                OmniControls.BeginTableHeaderRow();
                OmniControls.TableHeader(
                    OmniLoc.Get("Feature.MultiDock.PluginSource"),
                    OmniLoc.Get("Feature.MultiDock.PluginSource.Help"));
                OmniControls.TableHeader(
                    OmniLoc.Get("Feature.MultiDock.WindowSource"),
                    OmniLoc.Get("Feature.MultiDock.WindowSource.Help"));
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawPluginPicker();
                ImGui.TableNextColumn();
                changed |= DrawWindowPicker();
            }
        }
        return changed;
    }

    private bool DrawPluginPicker()
    {
        if (!ImGui.BeginChild(
                "##multiDockPluginPicker",
                new Vector2(0f, OmniTheme.Scale(SourcePickerHeight)),
                true))
        {
            ImGui.EndChild();
            return false;
        }

        ImGui.SetNextItemWidth(-1f);
        OmniControls.InputTextWithHint(
            "##multiDockPluginFilter",
            OmniLoc.Get("Feature.MultiDock.Filter"),
            ref pluginFilter,
            128);
        var changed = DrawDockToggleIconRow(
            config.PluginDock,
            "plugin",
            "Feature.MultiDock.ToggleIcon.Plugin");
        pluginItemIndices.Clear();
        for (var i = 0; i < config.Items.Count; i++)
        {
            if (config.Items[i].Type == MultiDockItemType.Plugin)
            {
                pluginItemIndices[config.Items[i].PluginInternalName] = i;
            }
        }

        pluginRows.Clear();
        foreach (var plugin in DalamudServices.PluginInterface.InstalledPlugins)
        {
            if (!plugin.IsLoaded || (!plugin.HasMainUi && !plugin.HasConfigUi) ||
                (pluginFilter.Length > 0 &&
                 !plugin.Name.Contains(pluginFilter, StringComparison.OrdinalIgnoreCase) &&
                 !plugin.InternalName.Contains(pluginFilter, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            pluginRows.Add(plugin);
        }

        pluginRows.Sort(ComparePluginRows);
        var controlSize = new Vector2(OmniTheme.CheckboxSize());
        var moveSpacing = OmniTheme.Scale(2f);
        var cellPadding = ImGui.GetStyle().CellPadding.X * 2f;
        for (var row = 0; row < pluginRows.Count; row++)
        {
            var plugin = pluginRows[row];
            var index = pluginItemIndices.TryGetValue(plugin.InternalName, out var itemIndex) ? itemIndex : -1;
            var previousIndex = index >= 0 ? FindAdjacent(index, MultiDockItemType.Plugin, -1) : -1;
            var nextIndex = index >= 0 ? FindAdjacent(index, MultiDockItemType.Plugin, 1) : -1;
            var selected = index >= 0;
            ImGui.PushID(plugin.InternalName);
            var restart = false;
            using (var table = ImRaii.Table(
                       "##multiDockPluginRow",
                       5,
                       ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                       new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
            {
                if (table)
                {
                    ImGui.TableSetupColumn(
                        "##move",
                        ImGuiTableColumnFlags.WidthFixed,
                        controlSize.X * 2f + moveSpacing + cellPadding);
                    ImGui.TableSetupColumn(
                        "##check",
                        ImGuiTableColumnFlags.WidthFixed,
                        controlSize.X + cellPadding);
                    ImGui.TableSetupColumn(
                        "##icon",
                        ImGuiTableColumnFlags.WidthFixed,
                        controlSize.X + cellPadding);
                    ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn(
                        "##edit",
                        ImGuiTableColumnFlags.WidthFixed,
                        controlSize.X + cellPadding);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    using (ImRaii.Disabled(previousIndex < 0))
                    {
                        if (OmniControls.IconButton(
                                "up",
                                FontAwesomeIcon.ChevronCircleUp,
                                false,
                                controlSize,
                                OmniLoc.Get("Feature.MultiDock.MoveUp")))
                        {
                            MoveItem(index, previousIndex);
                            changed = true;
                            restart = true;
                        }
                    }

                    ImGui.SameLine(0f, moveSpacing);
                    using (ImRaii.Disabled(nextIndex < 0))
                    {
                        if (OmniControls.IconButton(
                                "down",
                                FontAwesomeIcon.ChevronCircleDown,
                                false,
                                controlSize,
                                OmniLoc.Get("Feature.MultiDock.MoveDown")))
                        {
                            MoveItem(index, nextIndex);
                            changed = true;
                            restart = true;
                        }
                    }

                    ImGui.TableNextColumn();
                    if (OmniControls.Checkbox("##selected", ref selected))
                    {
                        if (selected)
                        {
                            config.Items.Add(new()
                            {
                                Type = MultiDockItemType.Plugin,
                                Name = plugin.Name,
                                PluginInternalName = plugin.InternalName
                            });
                        }
                        else
                        {
                            config.Items.RemoveAt(index);
                        }

                        editingItemIndex = -1;
                        editingToggleIconID = string.Empty;
                        itemsChanged();
                        changed = true;
                        restart = true;
                    }

                    ImGui.TableNextColumn();
                    if (feature.GetPluginTexture(plugin) is { } texture)
                    {
                        ImGui.Image(texture.Handle, controlSize);
                    }
                    else
                    {
                        ImGui.Dummy(controlSize);
                    }

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(plugin.Name);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(plugin.InternalName);
                    }

                    ImGui.TableNextColumn();
                    if (index >= 0 && OmniControls.IconButton(
                            "edit",
                            FontAwesomeIcon.Cog,
                            editingItemIndex == index,
                            controlSize,
                            OmniLoc.Get("Feature.MultiDock.Edit")))
                    {
                        editingToggleIconID = string.Empty;
                        editingItemIndex = editingItemIndex == index ? -1 : index;
                    }
                }
            }

            if (!restart && index >= 0 && editingItemIndex == index)
            {
                changed |= DrawItemEditor(config.Items[index], index);
            }

            ImGui.Separator();
            ImGui.PopID();
            if (restart)
            {
                break;
            }
        }

        ImGui.EndChild();
        return changed;
    }

    private bool DrawWindowPicker()
    {
        if (!ImGui.BeginChild(
                "##multiDockWindowPicker",
                new Vector2(0f, OmniTheme.Scale(SourcePickerHeight)),
                true))
        {
            ImGui.EndChild();
            return false;
        }

        if (!windowRowsLoaded)
        {
            RefreshWindowRows();
        }

        var refreshSize = new Vector2(OmniTheme.CheckboxSize());
        ImGui.SetNextItemWidth(MathF.Max(
            OmniTheme.Scale(120f),
            ImGui.GetContentRegionAvail().X - refreshSize.X - ImGui.GetStyle().ItemSpacing.X));
        OmniControls.InputTextWithHint(
            "##multiDockWindowFilter",
            OmniLoc.Get("Feature.MultiDock.Filter"),
            ref windowFilter,
            128);
        ImGui.SameLine();
        if (OmniControls.IconButton(
                "refreshWindows",
                FontAwesomeIcon.SyncAlt,
                false,
                refreshSize,
                OmniLoc.Get("Feature.MultiDock.WindowSource.Refresh")))
        {
            RefreshWindowRows();
        }

        var changed = false;
        var hasVisibleRows = false;
        var filter = windowFilter.Trim();
        for (var row = 0; row < windowRows.Count; row++)
        {
            var candidate = windowRows[row];
            if (filter.Length > 0 &&
                !candidate.PluginName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !candidate.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !candidate.WindowName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            hasVisibleRows = true;
        var key = MultiDock.NormalizeWindowKey(candidate.WindowName);
            var index = FindWindow(candidate.WindowName);
            var selected = config.HiddenWindows.Contains(key);
            ImGui.PushID($"{candidate.PluginInternalName}_{key}_{row}");
            if (OmniControls.Checkbox("##selected", ref selected))
            {
                if (selected)
                {
                    if (index < 0)
                    {
                        config.Items.Add(new()
                        {
                            Type = MultiDockItemType.ImGuiWindow,
                            Name = candidate.DisplayName,
                            WindowName = candidate.WindowName,
                            LinkedPluginInternalName = candidate.PluginInternalName
                        });
                    }
                    else
                    {
                        config.Items[index].LinkedPluginInternalName = candidate.PluginInternalName;
                    }

                    config.HiddenWindows.Add(key);
                }
                else
                {
                    if (index >= 0)
                    {
                        config.Items.RemoveAt(index);
                    }

                    config.HiddenWindows.Remove(key);
                }

                editingItemIndex = -1;
                editingToggleIconID = string.Empty;
                itemsChanged();
                changed = true;
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"{candidate.PluginName} · {candidate.DisplayName}");
            if (!candidate.DisplayName.Equals(candidate.WindowName, StringComparison.Ordinal))
            {
                ImGui.TextDisabled(candidate.WindowName);
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (!hasVisibleRows)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MultiDock.WindowSource.Empty"));
        }

        ImGui.EndChild();
        return changed;
    }

    private void RefreshWindowRows()
    {
        windowRows.Clear();
        windowRows.AddRange(feature.GetWindowCandidates());
        windowRowsLoaded = true;
    }

    private bool DrawCommandManagement()
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##multiDockCommandSources",
                   2,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiDock.CommandSource"));
                ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiDock.CommandBrowser"));
                OmniControls.BeginTableHeaderRow();
                OmniControls.TableHeader(OmniLoc.Get("Feature.MultiDock.CommandSource"));
                OmniControls.TableHeader(
                    OmniLoc.Get("Feature.MultiDock.CommandBrowser"),
                    OmniLoc.Get("Feature.MultiDock.CommandBrowser.Help"));
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.BeginChild(
                        "##multiDockCommandManagement",
                        new Vector2(0f, OmniTheme.Scale(CommandSectionHeight)),
                        true))
                {
                    changed |= DrawDockToggleIconRow(
                        config.CommandDock,
                        "command",
                        "Feature.MultiDock.ToggleIcon.Command");
                    changed |= DrawManagedCommands();
                    ImGui.Spacing();
                    changed |= DrawCommandCreator();
                }

                ImGui.EndChild();
                ImGui.TableNextColumn();
                if (ImGui.BeginChild(
                        "##multiDockCommandBrowser",
                        new Vector2(0f, OmniTheme.Scale(CommandSectionHeight)),
                        true))
                {
                    changed |= DrawCommandBrowser();
                }

                ImGui.EndChild();
            }
        }
        return changed;
    }

    private bool DrawCommandCreator()
    {
        ImGui.SetNextItemWidth(-1f);
        OmniControls.InputTextWithHint(
            "##multiDockCommandName",
            OmniLoc.Get("Feature.MultiDock.DisplayName"),
            ref commandName,
            128);
        ImGui.SetNextItemWidth(-1f);
        OmniControls.InputTextWithHint(
            "##multiDockCommandText",
            OmniLoc.Get("Feature.MultiDock.Command"),
            ref commandText,
            512);
        var canAdd = MultiDock.TryNormalizeCommand(commandText, out var normalized) &&
                     FindCommand(normalized) < 0;
        using var disabled = ImRaii.Disabled(!canAdd);
        if (!OmniControls.SmallButton(
                OmniLoc.Get("Feature.MultiDock.AddCommand"),
                false,
                new Vector2(ImGui.GetContentRegionAvail().X, OmniTheme.SmallButtonSize().Y)))
        {
            return false;
        }

        config.Items.Add(new()
        {
            Type = MultiDockItemType.Command,
            Name = commandName.Trim(),
            Command = normalized
        });
        commandName = string.Empty;
        commandText = string.Empty;
        itemsChanged();
        return true;
    }

    private bool DrawCommandBrowser()
    {
        ImGui.SetNextItemWidth(-1f);
        OmniControls.InputTextWithHint(
            "##multiDockCommandFilter",
            OmniLoc.Get("Feature.MultiDock.Filter"),
            ref commandFilter,
            128);
        var changed = false;
        foreach (var pair in DalamudServices.CommandManager.Commands)
        {
            if (!MultiDock.TryNormalizeCommand(pair.Key, out var command))
            {
                continue;
            }

            var help = pair.Value.HelpMessage ?? string.Empty;
            if (commandFilter.Length > 0 &&
                !command.Contains(commandFilter, StringComparison.OrdinalIgnoreCase) &&
                !help.Contains(commandFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var index = FindCommand(command);
            var selected = index >= 0;
            ImGui.PushID(command);
            if (OmniControls.Checkbox(
                    help.Length > 0 ? $"{command} → {help}" : command,
                    ref selected))
            {
                if (selected)
                {
                    config.Items.Add(new()
                    {
                        Type = MultiDockItemType.Command,
                        Name = command,
                        Command = command
                    });
                }
                else
                {
                    config.Items.RemoveAt(index);
                }

                editingItemIndex = -1;
                itemsChanged();
                changed = true;
            }

            ImGui.PopID();
        }
        return changed;
    }

    private bool DrawManagedCommands()
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##multiDockManagedCommands",
                   5,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (!table)
            {
                return false;
            }

            ImGui.TableSetupColumn("##item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##up", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(32f));
            ImGui.TableSetupColumn("##down", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(32f));
            ImGui.TableSetupColumn("##edit", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(32f));
            ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(32f));
            for (var i = 0; i < config.Items.Count; i++)
            {
                if (config.Items[i].Type != MultiDockItemType.Command)
                {
                    continue;
                }

                var previousIndex = FindAdjacent(i, MultiDockItemType.Command, -1);
                var nextIndex = FindAdjacent(i, MultiDockItemType.Command, 1);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(GetItemName(config.Items[i]));
                ImGui.TableNextColumn();
                using (ImRaii.Disabled(previousIndex < 0))
                {
                    if (OmniControls.IconButton(
                            $"moveUp{i}",
                            FontAwesomeIcon.ChevronCircleUp,
                            false,
                            OmniLoc.Get("Feature.MultiDock.MoveUp")))
                    {
                        MoveItem(i, previousIndex);
                        changed = true;
                    }
                }

                ImGui.TableNextColumn();
                using (ImRaii.Disabled(nextIndex < 0))
                {
                    if (OmniControls.IconButton(
                            $"moveDown{i}",
                            FontAwesomeIcon.ChevronCircleDown,
                            false,
                            OmniLoc.Get("Feature.MultiDock.MoveDown")))
                    {
                        MoveItem(i, nextIndex);
                        changed = true;
                    }
                }

                ImGui.TableNextColumn();
                if (OmniControls.IconButton(
                        $"edit{i}",
                        FontAwesomeIcon.Cog,
                        editingItemIndex == i,
                        OmniLoc.Get("Feature.MultiDock.Edit")))
                {
                    editingToggleIconID = string.Empty;
                    editingItemIndex = editingItemIndex == i ? -1 : i;
                }

                ImGui.TableNextColumn();
                if (!OmniControls.IconButton(
                        $"delete{i}",
                        FontAwesomeIcon.TrashAlt,
                        false,
                        OmniLoc.Get("Feature.MultiDock.Delete")))
                {
                    continue;
                }

                config.Items.RemoveAt(i);
                editingItemIndex = -1;
                itemsChanged();
                changed = true;
                i--;
            }
        }

        if (editingItemIndex >= 0 && editingItemIndex < config.Items.Count &&
            config.Items[editingItemIndex].Type == MultiDockItemType.Command)
        {
            changed |= DrawItemEditor(config.Items[editingItemIndex], editingItemIndex);
        }

        return changed;
    }

    private bool DrawItemEditor(MultiDockItemConfig item, int index)
    {
        var changed = false;
        DrawSeparatorText(OmniLoc.Get("Feature.MultiDock.Edit"));
        changed |= DrawTextProperty(
            "Feature.MultiDock.DisplayName",
            $"name{index}",
            item.Name,
            128,
            value => item.Name = value);
        switch (item.Type)
        {
            case MultiDockItemType.Plugin:
                ImGui.TextDisabled(item.PluginInternalName);
                changed |= DrawTextProperty(
                    "Feature.MultiDock.WindowName",
                    $"window{index}",
                    item.WindowName,
                    256,
                    value => item.WindowName = value);
                changed |= DrawCheckbox(
                    "Feature.MultiDock.PreferConfigUi",
                    $"preferConfig{index}",
                    item.preferConfigUI,
                    value => item.preferConfigUI = value);
                break;
            case MultiDockItemType.ImGuiWindow:
                changed |= DrawTextProperty(
                    "Feature.MultiDock.WindowName",
                    $"window{index}",
                    item.WindowName,
                    256,
                    value => item.WindowName = value);
                var linkedPluginName = item.LinkedPluginInternalName;
                var linkedPluginDisplayName = linkedPluginName;
                foreach (var plugin in DalamudServices.PluginInterface.InstalledPlugins)
                {
                    if (plugin.InternalName.Equals(linkedPluginName, StringComparison.OrdinalIgnoreCase))
                    {
                        linkedPluginDisplayName = plugin.Name;
                        break;
                    }
                }

                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiDock.PluginSource"));
                if (OmniControls.BeginCombo(
                        $"##linkedPlugin{index}",
                        linkedPluginDisplayName.Length == 0 ? "-" : linkedPluginDisplayName,
                        ImGui.GetContentRegionAvail().X))
                {
                    if (ImGui.Selectable("-", linkedPluginName.Length == 0))
                    {
                        item.LinkedPluginInternalName = string.Empty;
                        changed = true;
                    }

                    foreach (var plugin in DalamudServices.PluginInterface.InstalledPlugins)
                    {
                        if (!plugin.IsLoaded || (!plugin.HasMainUi && !plugin.HasConfigUi))
                        {
                            continue;
                        }

                        var selected = plugin.InternalName.Equals(linkedPluginName, StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(plugin.Name, selected))
                        {
                            item.LinkedPluginInternalName = plugin.InternalName;
                            changed = true;
                        }
                    }

                    ImGui.EndCombo();
                }

                changed |= DrawCheckbox(
                    "Feature.MultiDock.PreferConfigUi",
                    $"preferConfig{index}",
                    item.preferConfigUI,
                    value => item.preferConfigUI = value);
                break;
            case MultiDockItemType.Command:
                changed |= DrawTextProperty(
                    "Feature.MultiDock.Command",
                    $"command{index}",
                    item.Command,
                    512,
                    value => item.Command = value);
                break;
        }

        changed |= DrawTextProperty(
            "Feature.MultiDock.ClickCommand",
            $"click{index}",
            item.ClickCommand,
            512,
            value => item.ClickCommand = value);
        changed |= DrawIconEditor(item.Icon, $"item{index}");
        changed |= DrawMacroEditor(item, index);
        if (changed)
        {
            item.Name = item.Name.Trim();
            item.WindowName = item.WindowName.Trim();
            item.LinkedPluginInternalName = item.LinkedPluginInternalName.Trim();
        item.Command = MultiDock.TryNormalizeCommand(item.Command, out var command)
                ? command
                : string.Empty;
        item.ClickCommand = MultiDock.TryNormalizeCommand(item.ClickCommand, out var clickCommand)
                ? clickCommand
                : string.Empty;
            for (var i = 0; i < item.ContextMenuMacros.Count; i++)
            {
                item.ContextMenuMacros[i].Name = item.ContextMenuMacros[i].Name.Trim();
            if (MultiDock.TryNormalizeCommand(item.ContextMenuMacros[i].Command, out var macroCommand))
                {
                    item.ContextMenuMacros[i].Command = macroCommand;
                }
            }

            itemsChanged();
        }

        return changed;
    }

    private bool DrawMacroEditor(MultiDockItemConfig item, int itemIndex)
    {
        var changed = false;
        DrawSeparatorText(OmniLoc.Get("Feature.MultiDock.ContextMacros"));
        for (var i = 0; i < item.ContextMenuMacros.Count; i++)
        {
            ImGui.PushID($"macro{itemIndex}_{i}");
            changed |= DrawTextProperty(
                "Feature.MultiDock.DisplayName",
                "name",
                item.ContextMenuMacros[i].Name,
                128,
                value => item.ContextMenuMacros[i].Name = value);
            changed |= DrawTextProperty(
                "Feature.MultiDock.Command",
                "command",
                item.ContextMenuMacros[i].Command,
                512,
                value => item.ContextMenuMacros[i].Command = value);
            if (OmniControls.IconButton(
                    "delete",
                    FontAwesomeIcon.TrashAlt,
                    false,
                    OmniLoc.Get("Feature.MultiDock.Delete")))
            {
                item.ContextMenuMacros.RemoveAt(i);
                changed = true;
                i--;
            }

            ImGui.PopID();
        }

        if (OmniControls.IconButton(
                $"addMacro{itemIndex}",
                FontAwesomeIcon.Plus,
                false,
                OmniLoc.Get("Feature.MultiDock.AddMacro")))
        {
            item.ContextMenuMacros.Add(new());
            changed = true;
        }

        return changed;
    }

    private bool DrawDockToggleIconRow(
        MultiDockBarConfig dock,
        string id,
        string labelKey)
    {
        var changed = false;
        ImGui.PushID($"toggleIcon{id}");
        using (var table = ImRaii.Table(
                   "##multiDockToggleIconRow",
                   2,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##edit", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(32f));
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get(labelKey));
                ImGui.TableNextColumn();
                if (OmniControls.IconButton(
                        "edit",
                        FontAwesomeIcon.Cog,
                        editingToggleIconID == id,
                        OmniLoc.Get("Feature.MultiDock.Edit")))
                {
                    editingItemIndex = -1;
                    editingToggleIconID = editingToggleIconID == id ? string.Empty : id;
                }
            }
        }

        if (editingToggleIconID == id)
        {
            changed |= DrawIconEditor(dock.ToggleIcon, id);
        }

        ImGui.Separator();
        ImGui.PopID();
        return changed;
    }

    private bool DrawIconEditor(MultiDockIconConfig icon, string id)
    {
        var changed = false;
        ImGui.PushID(id);
        var gameIconID = icon.gameIconID > int.MaxValue ? int.MaxValue : (int)icon.gameIconID;
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiDock.GameIcon"));
        ImGui.SetNextItemWidth(MathF.Max(
            OmniTheme.Scale(100f),
            ImGui.GetContentRegionAvail().X - OmniTheme.Scale(36f) - ImGui.GetStyle().ItemSpacing.X));
        if (OmniControls.InputInt("##gameIcon", ref gameIconID))
        {
            icon.gameIconID = (uint)Math.Max(0, gameIconID);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            iconsChanged();
            changed = true;
        }

        ImGui.SameLine();
        if (OmniControls.IconButton(
                "gameIconBrowser",
                FontAwesomeIcon.SlidersH,
                false,
                OmniLoc.Get("IconBrowser.Title")))
        {
            iconBrowser.OpenForSelection(
                false,
                value =>
                {
                    icon.gameIconID = value;
                    icon.FilePath = string.Empty;
                    icon.SeIconChar = string.Empty;
                    iconsChanged();
                    saveConfig();
                },
                null);
        }

        changed |= DrawTextProperty(
            "Feature.MultiDock.IconFile",
            "file",
            icon.FilePath,
            512,
            value => icon.FilePath = value,
            iconsChanged);
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiDock.SeIconChar"));
        ImGui.SetNextItemWidth(MathF.Max(
            OmniTheme.Scale(100f),
            ImGui.GetContentRegionAvail().X - OmniTheme.Scale(36f) - ImGui.GetStyle().ItemSpacing.X));
        var seIcon = icon.SeIconChar;
        if (OmniControls.InputText("##seIcon", ref seIcon, 128))
        {
            icon.SeIconChar = seIcon;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            iconsChanged();
            changed = true;
        }

        ImGui.SameLine();
        if (OmniControls.IconButton(
                "seIconBrowser",
                FontAwesomeIcon.SlidersH,
                false,
                OmniLoc.Get("IconBrowser.Title")))
        {
            iconBrowser.OpenForSelection(
                true,
                null,
                value =>
                {
                    icon.gameIconID = 0;
                    icon.FilePath = string.Empty;
                    icon.SeIconChar = value;
                    iconsChanged();
                    saveConfig();
                });
        }

        ImGui.PopID();
        return changed;
    }

    private static bool DrawTextProperty(
        string labelKey,
        string id,
        string current,
        int maxLength,
        Action<string> setter,
        Action? onDeactivated = null)
    {
        var value = current;
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.SetNextItemWidth(-1f);
        if (OmniControls.InputText($"##{id}", ref value, maxLength))
        {
            setter(value);
        }

        if (!ImGui.IsItemDeactivatedAfterEdit())
        {
            return false;
        }

        onDeactivated?.Invoke();
        return true;
    }

    private static void DrawSeparatorText(string text)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(text);
        ImGui.Separator();
    }

    private static bool DrawCheckbox(
        string labelKey,
        string id,
        bool current,
        Action<bool> setter)
    {
        var value = current;
        if (!OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##{id}", ref value))
        {
            return false;
        }

        setter(value);
        return true;
    }

    private static bool DrawAutoModeComboCell(MultiDockBarConfig dock, string id)
    {
        ImGui.TableNextColumn();
        var label = OmniLoc.Get("Feature.MultiDock.AutoMode");
        var changed = false;
        if (OmniControls.BeginCombo(
                $"##autoMode{id}",
                GetAutoModeLabel(dock.AutoMode),
                GetInlineControlWidth(label)))
        {
            for (var i = 0; i < AutoModes.Length; i++)
            {
                if (!ImGui.Selectable(GetAutoModeLabel(AutoModes[i]), dock.AutoMode == AutoModes[i]))
                {
                    continue;
                }

                dock.AutoMode = AutoModes[i];
                changed = true;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.MultiDock.AutoMode.Help"));
        return changed;
    }

    private static string GetAutoModeLabel(MultiDockAutoMode mode) => OmniLoc.Get(mode switch
    {
        MultiDockAutoMode.Hide => "Feature.MultiDock.AutoMode.Hide",
        MultiDockAutoMode.Collapse => "Feature.MultiDock.AutoMode.Collapse",
        _ => "Feature.MultiDock.AutoMode.None"
    });

    private static bool DrawFloatSliderCell(
        string labelKey,
        string id,
        float current,
        Action<float> setter,
        float min,
        float max,
        string format)
    {
        ImGui.TableNextColumn();
        var label = OmniLoc.Get(labelKey);
        var value = current;
        OmniControls.SliderFloat($"##{id}", ref value, min, max, format, GetInlineControlWidth(label));
        var changed = ImGui.IsItemDeactivatedAfterEdit();
        setter(value);
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        return changed;
    }

    private static bool DrawIntSliderCell(
        string labelKey,
        string id,
        int current,
        Action<int> setter,
        int min,
        int max)
    {
        ImGui.TableNextColumn();
        var label = OmniLoc.Get(labelKey);
        var value = current;
        OmniControls.SliderInt($"##{id}", ref value, min, max, "%d", GetInlineControlWidth(label));
        var changed = ImGui.IsItemDeactivatedAfterEdit();
        setter(value);
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        return changed;
    }

    private static bool DrawBoolComboCell(
        string labelKey,
        string id,
        bool current,
        Action<bool> setter,
        string falseLabelKey,
        string trueLabelKey)
    {
        ImGui.TableNextColumn();
        var label = OmniLoc.Get(labelKey);
        var changed = false;
        if (OmniControls.BeginCombo(
                $"##{id}",
                OmniLoc.Get(current ? trueLabelKey : falseLabelKey),
                GetInlineControlWidth(label)))
        {
            if (ImGui.Selectable(OmniLoc.Get(falseLabelKey), !current))
            {
                setter(false);
                changed = true;
            }

            if (ImGui.Selectable(OmniLoc.Get(trueLabelKey), current))
            {
                setter(true);
                changed = true;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        return changed;
    }

    private static float GetInlineControlWidth(string label) =>
        MathF.Max(
            OmniTheme.Scale(80f),
            ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(label).X - ImGui.GetStyle().ItemSpacing.X);

    private int ComparePluginRows(IExposedPlugin left, IExposedPlugin right)
    {
        var hasLeftIndex = pluginItemIndices.TryGetValue(left.InternalName, out var leftIndex);
        var hasRightIndex = pluginItemIndices.TryGetValue(right.InternalName, out var rightIndex);
        if (hasLeftIndex != hasRightIndex)
        {
            return hasLeftIndex ? -1 : 1;
        }

        if (hasLeftIndex)
        {
            var order = leftIndex.CompareTo(rightIndex);
            if (order != 0)
            {
                return order;
            }
        }

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private int FindPlugin(string internalName)
    {
        for (var i = 0; i < config.Items.Count; i++)
        {
            if (config.Items[i].Type == MultiDockItemType.Plugin &&
                config.Items[i].PluginInternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindWindow(string name)
    {
        var key = MultiDock.NormalizeWindowKey(name);
        for (var i = 0; i < config.Items.Count; i++)
        {
            if (config.Items[i].Type == MultiDockItemType.ImGuiWindow &&
            MultiDock.NormalizeWindowKey(config.Items[i].WindowName)
                    .Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindCommand(string command)
    {
        for (var i = 0; i < config.Items.Count; i++)
        {
            if (config.Items[i].Type == MultiDockItemType.Command &&
                config.Items[i].Command.Equals(command, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindAdjacent(int index, MultiDockItemType itemType, int step)
    {
        for (var i = index + step; i >= 0 && i < config.Items.Count; i += step)
        {
            if (config.Items[i].Type == itemType)
            {
                return i;
            }
        }

        return -1;
    }

    private void MoveItem(int source, int target)
    {
        if (target < 0)
        {
            return;
        }

        (config.Items[source], config.Items[target]) = (config.Items[target], config.Items[source]);
        if (editingItemIndex == source)
        {
            editingItemIndex = target;
        }
        else if (editingItemIndex == target)
        {
            editingItemIndex = source;
        }

        itemsChanged();
    }

    private static string GetItemName(MultiDockItemConfig item)
    {
        if (item.Name.Length > 0)
        {
            return item.Name;
        }

        return item.Type switch
        {
            MultiDockItemType.Plugin => item.PluginInternalName,
            MultiDockItemType.ImGuiWindow => item.WindowName,
            _ => item.Command
        };
    }

    private void SetClipboardStatus(string key, bool isError)
    {
        clipboardStatusKey = key;
        clipboardStatusIsError = isError;
        clipboardStatusUntil = ImGui.GetTime() + 5d;
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true, Feature = "renaming")]
    private sealed class MultiDockClipboardSnapshot
    {
        public int Version { get; set; } = ClipboardVersion;

        public MultiDockConfig? Config { get; set; }
    }
}
