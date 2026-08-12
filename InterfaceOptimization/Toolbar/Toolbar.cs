using Dalamud.Game.Config;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class Toolbar : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ToolbarTitle"),
        Description = OmniLoc.Get("ToolbarDescription"),
        Category = ModuleCategory.InterfaceOptimization
    };

    private readonly ToolbarConfig config;
    private readonly ToolbarSettings settings;
    private readonly System.Action saveConfig;
    private readonly List<DtrItem> dtrItems = [];
    private FeatureLifetime? runtimeLifetime;
    private ToolbarSnapshot snapshot = new();
    private bool? nativeDtrOriginalVisibility;
    private bool autoHideVisible = true;
    private long nextDtrRefreshTick;
    private long nextTimeRefreshTick;

    public Toolbar(ToolbarConfig config, IconBrowser iconBrowser, System.Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        settings = new(config, iconBrowser, saveConfig);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => settings.Draw();

    protected override void OnEnable()
    {
        config.Normalize();
        autoHideVisible = config.Locked;
        var lifetime = new FeatureLifetime();
        try
        {
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 100))
            {
                throw new InvalidOperationException("Toolbar update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            DalamudServices.PluginInterface.UiBuilder.Draw += DrawToolbar;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawToolbar);
            runtimeLifetime = lifetime;
            OnFrameworkUpdate(DService.Instance().Framework);
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            RestoreNativeDtr();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            RestoreNativeDtr();
            dtrItems.Clear();
            snapshot = new();
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        UpdateNativeDtrVisibility();
        UpdateSnapshot();
        if (Environment.TickCount64 >= nextDtrRefreshTick)
        {
            nextDtrRefreshTick = Environment.TickCount64 + 500;
            UpdateDtrItems();
        }
    }

    private void UpdateSnapshot()
    {
        var player = DService.Instance().ObjectTable.LocalPlayer;
        if (player is null)
        {
            snapshot = new();
            return;
        }

        snapshot.Player = string.Format(
            OmniLoc.Get("Feature.Toolbar.Format.Player"),
            player.ClassJob.Value.Abbreviation,
            player.Level,
            player.Name);
        snapshot.Health = string.Format(
            OmniLoc.Get("Feature.Toolbar.Format.Health"),
            player.CurrentHp,
            player.MaxHp);
        snapshot.Magic = string.Format(
            OmniLoc.Get("Feature.Toolbar.Format.Magic"),
            player.CurrentMp,
            player.MaxMp);
        var inventory = InventoryManager.Instance();
        snapshot.Gil = string.Format(
            OmniLoc.Get("Feature.Toolbar.Format.Gil"),
            inventory == null ? 0 : inventory->GetGil());
        snapshot.Location = GetLocationText(player.Position);
        snapshot.Fps = string.Format(
            OmniLoc.Get("Feature.Toolbar.Format.Fps"),
            Math.Clamp((int)MathF.Round(ImGui.GetIO().Framerate), 0, 999));
        snapshot.Volume = GetVolumeText();

        if (Environment.TickCount64 < nextTimeRefreshTick)
        {
            return;
        }

        nextTimeRefreshTick = Environment.TickCount64 + 1_000;
        snapshot.LocalTime = string.Format(
            OmniLoc.Get("Feature.Toolbar.Format.LocalTime"),
            DateTime.Now.ToString("HH:mm:ss"));
        var eorzeaSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 144L / 7L;
        snapshot.EorzeaTime = string.Format(
            OmniLoc.Get("Feature.Toolbar.Format.EorzeaTime"),
            TimeSpan.FromSeconds(eorzeaSeconds % 86_400L).ToString(@"hh\:mm"));
    }

    private static string GetLocationText(Vector3 position)
    {
        var services = DService.Instance();
        var territoryName = LuminaGetter.TryGetRow<TerritoryType>(services.ClientState.TerritoryType, out var territory)
            ? territory.ExtractPlaceName()
            : string.Empty;
        if (!LuminaGetter.TryGetRow<Map>(services.ClientState.MapId, out var map))
        {
            return territoryName;
        }

        var coordinates = PositionHelper.WorldToMap(new(position.X, position.Z), map);
        return string.Format(
            OmniLoc.Get("Feature.Toolbar.Format.Location"),
            territoryName,
            coordinates.X,
            coordinates.Y);
    }

    private static string GetVolumeText()
    {
        var gameConfig = DService.Instance().GameConfig;
        gameConfig.TryGet(SystemConfigOption.SoundMaster, out uint volume);
        var muted = gameConfig.TryGet(SystemConfigOption.IsSndMaster, out uint mutedValue) && mutedValue != 0;
        return string.Format(
            OmniLoc.Get(muted ? "Feature.Toolbar.Format.VolumeMuted" : "Feature.Toolbar.Format.Volume"),
            Math.Clamp(volume, 0u, 100u));
    }

    private void UpdateDtrItems()
    {
        dtrItems.Clear();
        foreach (var entry in DService.Instance().DTRBar.Entries)
        {
            if (!entry.Shown || entry.UserHidden || string.IsNullOrWhiteSpace(entry.Text?.ToString()))
            {
                continue;
            }

            dtrItems.Add(new(
                entry.Text!.ToString(),
                entry.Tooltip?.ToString() ?? string.Empty,
                entry.HasClickAction ? entry.OnClick : null));
        }
    }

    private void UpdateNativeDtrVisibility()
    {
        if (!config.HideNativeDtr)
        {
            RestoreNativeDtr();
            return;
        }

        var addon = AddonHelper.GetByName("_DTR");
        if (addon is null)
        {
            return;
        }

        nativeDtrOriginalVisibility ??= addon->IsVisible;
        addon->IsVisible = false;
    }

    private void RestoreNativeDtr()
    {
        if (nativeDtrOriginalVisibility is not { } originalVisibility)
        {
            return;
        }

        var addon = AddonHelper.GetByName("_DTR");
        if (addon is not null)
        {
            addon->IsVisible = originalVisibility;
        }

        nativeDtrOriginalVisibility = null;
    }

    private void DrawToolbar()
    {
        var scale = Math.Clamp(config.Scale, 0.75f, 2f);
        using var font = FontManager.Instance().GetUIFont(scale).Push();
        var height = OmniTheme.Scale(Math.Clamp(config.Height, 24f, 64f)) * scale;
        var spacing = OmniTheme.Scale(Math.Clamp(config.Spacing, 0f, 32f)) * scale;
        var padding = OmniTheme.Scale(10f) * scale;
        var viewport = ImGui.GetMainViewport();
        var leftWidth = MeasureSection(config.Left, spacing);
        var centerWidth = MeasureSection(config.Center, spacing);
        var rightContentWidth = MeasureSection(config.Right, spacing);
        var lockSize = ImGui.GetTextLineHeight();
        var rightWidth = rightContentWidth + (rightContentWidth > 0f ? spacing : 0f) + lockSize;
        var width = config.Stretch
            ? viewport.WorkSize.X
            : MathF.Min(viewport.WorkSize.X, leftWidth + centerWidth + rightWidth + padding * 4f);
        var expandedPosition = new Vector2(
            viewport.WorkPos.X + MathF.Max(0f, (viewport.WorkSize.X - width) * 0.5f),
            config.Edge == ToolbarEdge.Top
                ? viewport.WorkPos.Y
                : viewport.WorkPos.Y + viewport.WorkSize.Y - height);
        UpdateAutoHideVisibility(expandedPosition, width, height, viewport);
        var position = expandedPosition;
        if (!config.Locked && !autoHideVisible)
        {
            position.Y += config.Edge == ToolbarEdge.Top
                ? -height + OmniTheme.Scale(3f)
                : height - OmniTheme.Scale(3f);
        }

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new(width, height), ImGuiCond.Always);
        var background = *ImGui.GetStyleColorVec4(ImGuiCol.WindowBg);
        background.W *= Math.Clamp(config.Opacity, 0.15f, 1f);
        using var backgroundColor = ImRaii.PushColor(ImGuiCol.WindowBg, background);
        using var windowPadding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (!ImGui.Begin(
                "##OmniToolbar",
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoNavFocus |
                ImGuiWindowFlags.NoBringToFrontOnFocus))
        {
            ImGui.End();
            return;
        }

        var y = position.Y + MathF.Max(0f, (height - ImGui.GetTextLineHeight()) * 0.5f);
        if (config.Stretch)
        {
            DrawSection(config.Left, new(position.X + padding, y), spacing);
            DrawSection(
                config.Center,
                new(position.X + MathF.Max(padding, (width - centerWidth) * 0.5f), y),
                spacing);
            DrawSection(
                config.Right,
                new(position.X + width - padding - rightWidth, y),
                spacing);
            DrawLockButton(new(position.X + width - padding - lockSize, y), lockSize);
        }
        else
        {
            var x = position.X + padding;
            DrawSection(config.Left, new(x, y), spacing);
            x += leftWidth + padding;
            DrawSection(config.Center, new(x, y), spacing);
            x += centerWidth + padding;
            DrawSection(config.Right, new(x, y), spacing);
            x += rightContentWidth + (rightContentWidth > 0f ? spacing : 0f);
            DrawLockButton(new(x, y), lockSize);
        }

        ImGui.End();
    }

    private void UpdateAutoHideVisibility(
        Vector2 expandedPosition,
        float width,
        float height,
        ImGuiViewportPtr viewport)
    {
        if (config.Locked)
        {
            autoHideVisible = true;
            return;
        }

        var triggerHeight = OmniTheme.Scale(3f);
        var triggerPosition = new Vector2(
            expandedPosition.X,
            config.Edge == ToolbarEdge.Top
                ? viewport.WorkPos.Y
                : viewport.WorkPos.Y + viewport.WorkSize.Y - triggerHeight);
        var edgeHovered = ImGui.IsMouseHoveringRect(
            triggerPosition,
            triggerPosition + new Vector2(width, triggerHeight),
            true);
        autoHideVisible = autoHideVisible
            ? edgeHovered || ImGui.IsMouseHoveringRect(
                expandedPosition,
                expandedPosition + new Vector2(width, height),
                true)
            : edgeHovered;
    }

    private float MeasureSection(IReadOnlyList<ToolbarComponentType> components, float spacing)
    {
        var width = 0f;
        var count = 0;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] == ToolbarComponentType.Dtr)
            {
                for (var dtrIndex = 0; dtrIndex < dtrItems.Count; dtrIndex++)
                {
                    width += ImGui.CalcTextSize(dtrItems[dtrIndex].Text).X;
                    if (dtrIndex == 0 && config.GetIconID(ToolbarComponentType.Dtr) != 0)
                    {
                        width += ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
                    }

                    count++;
                }

                continue;
            }

            width += components[i] == ToolbarComponentType.Separator
                ? OmniTheme.Scale(1f)
                : MeasureComponent(components[i]);
            count++;
        }

        return width + Math.Max(0, count - 1) * spacing;
    }

    private void DrawSection(
        IReadOnlyList<ToolbarComponentType> components,
        Vector2 position,
        float spacing)
    {
        ImGui.SetCursorScreenPos(position);
        var drawn = 0;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] == ToolbarComponentType.Dtr)
            {
                for (var dtrIndex = 0; dtrIndex < dtrItems.Count; dtrIndex++)
                {
                    DrawSameLineSpacing(ref drawn, spacing);
                    DrawDtrItem(
                        dtrItems[dtrIndex],
                        dtrIndex == 0 ? config.GetIconID(ToolbarComponentType.Dtr) : 0);
                }

                continue;
            }

            DrawSameLineSpacing(ref drawn, spacing);
            if (components[i] == ToolbarComponentType.Separator)
            {
                DrawSeparator();
            }
            else
            {
                DrawComponent(components[i]);
            }
        }
    }

    private static void DrawSameLineSpacing(ref int drawn, float spacing)
    {
        if (drawn > 0)
        {
            ImGui.SameLine(0f, spacing);
        }

        drawn++;
    }

    private static void DrawSeparator()
    {
        var position = ImGui.GetCursorScreenPos();
        var height = ImGui.GetTextLineHeight();
        ImGui.Dummy(new(OmniTheme.Scale(1f), height));
        ImGui.GetWindowDrawList().AddLine(
            position,
            position + new Vector2(0f, height),
            ImGui.GetColorU32(ImGuiCol.Separator),
            OmniTheme.BorderThickness());
    }

    private void DrawComponent(ToolbarComponentType component)
    {
        ImGui.BeginGroup();
        DrawIconAndText(config.GetIconID(component), GetComponentText(component));
        ImGui.EndGroup();
        if (component == ToolbarComponentType.Omni && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            DService.Instance().Command.ProcessCommand("/omni");
        }
        else if (component == ToolbarComponentType.Volume && ImGui.IsItemHovered())
        {
            if (Math.Abs(ImGui.GetIO().MouseWheel) > float.Epsilon)
            {
                AdjustVolume(ImGui.GetIO().MouseWheel > 0f ? 5 : -5);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                ToggleMute();
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(OmniLoc.Get($"Feature.Toolbar.Tooltip.{component}"));
        }
    }

    private static void DrawDtrItem(DtrItem item, uint iconID)
    {
        ImGui.BeginGroup();
        DrawIconAndText(iconID, item.Text);
        ImGui.EndGroup();
        if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(item.Tooltip))
        {
            ImGui.SetTooltip(item.Tooltip);
        }

        if (item.OnClick is null)
        {
            return;
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            item.OnClick(new() { ClickType = MouseClickType.Left });
        }
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            item.OnClick(new() { ClickType = MouseClickType.Right });
        }
    }

    private float MeasureComponent(ToolbarComponentType component)
    {
        var width = ImGui.CalcTextSize(GetComponentText(component)).X;
        return config.GetIconID(component) == 0
            ? width
            : width + ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
    }

    private static void DrawIconAndText(uint iconID, string text)
    {
        if (iconID != 0)
        {
            var size = new Vector2(ImGui.GetTextLineHeight());
            var position = ImGui.GetCursorScreenPos();
            ImGui.Dummy(size);
            FramedGameIcon.Draw(
                iconID,
                position,
                size,
                drawFrame: false,
                preserveAspectRatio: true);
            ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
        }

        ImGui.TextUnformatted(text);
    }

    private void DrawLockButton(Vector2 position, float size)
    {
        ImGui.SetCursorScreenPos(position);
        using var buttonColor = ImRaii.PushColor(ImGuiCol.Button, 0u);
        if (!ImGuiOm.ButtonIcon(
                "##toolbarLock",
                config.Locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                new Vector2(size),
                OmniLoc.Get(config.Locked
                    ? "Feature.Toolbar.Tooltip.Unlock"
                    : "Feature.Toolbar.Tooltip.Lock")))
        {
            return;
        }

        config.Locked = !config.Locked;
        autoHideVisible = true;
        saveConfig();
    }

    private string GetComponentText(ToolbarComponentType component) => component switch
    {
        ToolbarComponentType.Player => snapshot.Player,
        ToolbarComponentType.Health => snapshot.Health,
        ToolbarComponentType.Magic => snapshot.Magic,
        ToolbarComponentType.Gil => snapshot.Gil,
        ToolbarComponentType.Location => snapshot.Location,
        ToolbarComponentType.LocalTime => snapshot.LocalTime,
        ToolbarComponentType.EorzeaTime => snapshot.EorzeaTime,
        ToolbarComponentType.Fps => snapshot.Fps,
        ToolbarComponentType.Volume => snapshot.Volume,
        ToolbarComponentType.Omni => OmniLoc.Get("Feature.Toolbar.Component.Omni"),
        _ => string.Empty
    };

    private static void AdjustVolume(int delta)
    {
        var gameConfig = DService.Instance().GameConfig;
        if (gameConfig.TryGet(SystemConfigOption.SoundMaster, out uint volume))
        {
            gameConfig.Set(SystemConfigOption.SoundMaster, (uint)Math.Clamp((int)volume + delta, 0, 100));
        }
    }

    private static void ToggleMute()
    {
        var gameConfig = DService.Instance().GameConfig;
        if (gameConfig.TryGet(SystemConfigOption.IsSndMaster, out uint muted))
        {
            gameConfig.Set(SystemConfigOption.IsSndMaster, muted == 0 ? 1u : 0u);
        }
    }

    private sealed class ToolbarSnapshot
    {
        public string Player { get; set; } = string.Empty;
        public string Health { get; set; } = string.Empty;
        public string Magic { get; set; } = string.Empty;
        public string Gil { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string LocalTime { get; set; } = string.Empty;
        public string EorzeaTime { get; set; } = string.Empty;
        public string Fps { get; set; } = string.Empty;
        public string Volume { get; set; } = string.Empty;
    }

    private readonly record struct DtrItem(
        string Text,
        string Tooltip,
        Action<DtrInteractionEvent>? OnClick);
}
