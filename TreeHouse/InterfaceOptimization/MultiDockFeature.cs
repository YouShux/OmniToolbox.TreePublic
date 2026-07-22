using System.Globalization;
using System.IO;
using Dalamud.Game.Text;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class MultiDockFeature : TreeHouseFeatureBase
{
    private const string PluginDockWindowName = "插件/窗口收纳###OmniToolbox.MultiDock.Plugin";
    private const string CommandDockWindowName = "指令收纳###OmniToolbox.MultiDock.Command";

    private readonly MultiDockConfig config;
    private readonly Action saveConfig;
    private readonly MultiDockPanel panel;
    private readonly List<MultiDockItemConfig> pluginItems = [];
    private readonly List<MultiDockItemConfig> commandItems = [];
    private readonly Dictionary<string, ISharedImmediateTexture?> fileTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> failedTexturePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly DockRuntimeState pluginState = new();
    private readonly DockRuntimeState commandState = new();
    private FeatureLifetime? runtimeLifetime;
    private ISharedImmediateTexture? fallbackTexture;
    private bool fallbackTextureLoaded;

    public MultiDockFeature(
        MultiDockConfig config,
        Action saveConfig,
        IconBrowser iconBrowser) : base(new(
        "multi-dock",
        "Feature.MultiDock.Title",
        "Feature.MultiDock.Description",
        TreeHouseCategory.InterfaceOptimization))
    {
        this.config = config;
        this.saveConfig = saveConfig;
        NormalizeConfig();
        RefreshItemCaches();
        panel = new(
            config,
            iconBrowser,
            saveConfig,
            OnItemsChanged,
            InvalidateTextures,
            NormalizeConfig);
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
            runtimeLifetime = null;
            pluginState.Reset();
            commandState.Reset();
            InvalidateTextures();
        }
    }

    private void Draw()
    {
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
        if (item.Icon.GameIconId == 0 &&
            item.Icon.FilePath.Length == 0 &&
            TryParseSeIcon(item.Icon.SeIconChar, out var seIcon))
        {
            return DrawStyledButton(seIcon.ToIconString(), iconSize, opacity);
        }

        return DrawIconButton(item.Icon, iconSize, opacity);
    }

    private bool DrawIconButton(MultiDockIconConfig icon, float iconSize, float opacity)
    {
        var wrap = icon.GameIconId != 0
            ? ImageHelper.GetGameIcon(icon.GameIconId)
            : (GetFileTexture(icon.FilePath) ?? GetFallbackTexture())?.GetWrapOrDefault();
        if (wrap is null || wrap.Handle == IntPtr.Zero)
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
        }

        if (item.ContextMenuMacros.Count == 0)
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
                OpenPlugin(item);
                break;
            case MultiDockItemType.ImGuiWindow:
                ImGui.SetWindowFocus(item.WindowName);
                break;
            case MultiDockItemType.Command:
                ExecuteCommand(item.Command);
                break;
        }
    }

    private static void OpenPlugin(MultiDockItemConfig item)
    {
        foreach (var plugin in DalamudServices.PluginInterface.InstalledPlugins)
        {
            if (!plugin.IsLoaded ||
                !plugin.InternalName.Equals(item.PluginInternalName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (item.PreferConfigUi && plugin.HasConfigUi)
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
                DalamudServices.PluginLog.Error(ex, "MultiDock failed to open plugin {PluginName}.", item.PluginInternalName);
            }

            return;
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
            ChatManager.Instance().SendMessage(normalized);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiDock command execution failed: {Command}", normalized);
        }
    }

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
        fallbackTexture = GetFileTexture(Path.Combine("Resources", "图标.png"));
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
        for (var i = 0; i < config.Items.Count;)
        {
            if (!NormalizeItem(config.Items[i], out var key) || !keys.Add(key))
            {
                config.Items.RemoveAt(i);
                continue;
            }

            i++;
        }
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
        dock.AutoDelaySeconds = Math.Clamp(dock.AutoDelaySeconds, 0f, 120f);
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
                key = $"window:{item.WindowName}";
                return true;
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
