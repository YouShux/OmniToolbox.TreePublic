using Dalamud.Interface;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.General;

internal sealed class MitigationMonitorOverlay
{
    private readonly MitigationMonitorConfig config;
    private readonly MitigationHistoryMenu historyMenu;
    private readonly MitigationRecordTable recordTable;
    private readonly Action saveConfig;
    private Action? openSettingsAction;
    private float lastScale;
    private bool restoreLayoutNextDraw;
    private bool geometryDirty;
    private bool collapsedIconDragged;

    public MitigationMonitorOverlay(
        MitigationMonitorConfig config,
        MitigationCombatLog combatLog,
        MitigationReplayStore replayStore,
        Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        historyMenu = new(combatLog, replayStore);
        recordTable = new(config, combatLog, saveConfig);
    }

    public void SetOpenSettingsAction(Action action) => openSettingsAction = action;

    public void Draw()
    {
        if (!config.Visible || GameState.IsInPVPArea)
        {
            return;
        }

        if (config.Collapsed)
        {
            DrawCollapsedIcon();
            return;
        }

        var scaleChanged = MathF.Abs(lastScale - OmniTheme.ScaleValue) > 0.001f;
        ImGui.SetNextWindowPos(config.Position, restoreLayoutNextDraw ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(
            OmniTheme.Scale(config.Size),
            restoreLayoutNextDraw || scaleChanged ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
        restoreLayoutNextDraw = false;
        lastScale = OmniTheme.ScaleValue;

        var flags = ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.NoScrollbar;
        if (config.Locked)
        {
            flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        }

        var tokens = OmniTheme.Tokens;
        using var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, OmniTheme.Scale(new Vector2(6f)))
            .Push(ImGuiStyleVar.WindowRounding, OmniTheme.Scale(8f))
            .Push(ImGuiStyleVar.WindowBorderSize, OmniTheme.BorderThickness());
        using var colors = ImRaii.PushColor(ImGuiCol.WindowBg, tokens.Background with { W = config.Opacity })
            .Push(ImGuiCol.Border, tokens.Border)
            .Push(ImGuiCol.TableHeaderBg, tokens.Primary with { W = config.Opacity });

        if (ImGui.Begin($"{OmniLoc.Get("Feature.MitigationMonitor.Title")}###OmniMitigationMonitor", flags))
        {
            DrawToolbar();
            recordTable.Draw(historyMenu.ActiveHistoryKey);
            historyMenu.Draw();
            UpdateWindowGeometry();
        }

        ImGui.End();
    }

    public void ClearSelection()
    {
        historyMenu.ClearSelection();
        recordTable.ResetRuntime();
    }

    public void ResetRuntime()
    {
        historyMenu.ResetRuntime();
        recordTable.ResetRuntime();
        geometryDirty = false;
        collapsedIconDragged = false;
    }

    public void RequestLayoutRestore()
    {
        restoreLayoutNextDraw = true;
        lastScale = 0f;
    }

    private void DrawToolbar()
    {
        var start = ImGui.GetCursorScreenPos();
        var height = OmniTheme.Scale(24f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.Title"));
        var buttonSize = new Vector2(OmniTheme.Scale(22f), height);
        var spacing = OmniTheme.Scale(3f);
        var controlsWidth = buttonSize.X * 4f + spacing * 3f;
        ImGui.SetCursorScreenPos(new(
            MathF.Max(start.X, start.X + ImGui.GetContentRegionAvail().X - controlsWidth),
            start.Y));

        if (ImGuiOm.ButtonIcon(
                "##MitigationHistory",
                FontAwesomeIcon.History,
                buttonSize,
                OmniLoc.Get("Feature.MitigationMonitor.History.Title")))
        {
            historyMenu.Open();
        }

        ImGui.SameLine(0f, spacing);
        if (ImGuiOm.ButtonIcon(
                "##MitigationCollapse",
                FontAwesomeIcon.ChevronCircleUp,
                buttonSize,
                OmniLoc.Get("Feature.MitigationMonitor.Collapse")))
        {
            config.CollapsedPosition = ImGui.GetWindowPos() + new Vector2(
                MathF.Max(0f, ImGui.GetWindowSize().X - OmniTheme.Scale(108f)),
                OmniTheme.Scale(6f));
            config.Collapsed = true;
            saveConfig();
        }

        ImGui.SameLine(0f, spacing);
        if (ImGuiOm.ButtonIcon(
                "##MitigationLock",
                config.Locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                buttonSize,
                OmniLoc.Get(config.Locked
                    ? "Feature.MitigationMonitor.Unlock"
                    : "Feature.MitigationMonitor.Lock")))
        {
            config.Locked = !config.Locked;
            saveConfig();
        }

        ImGui.SameLine(0f, spacing);
        if (ImGuiOm.ButtonIcon(
                "##MitigationSettings",
                FontAwesomeIcon.Cog,
                buttonSize,
                OmniLoc.Get("Feature.MitigationMonitor.Settings")))
        {
            openSettingsAction?.Invoke();
        }

        ImGui.SetCursorScreenPos(new(start.X, start.Y + height + OmniTheme.Scale(3f)));
    }

    private void DrawCollapsedIcon()
    {
        var flags = ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoResize;
        if (config.Locked)
        {
            flags |= ImGuiWindowFlags.NoMove;
        }

        ImGui.SetNextWindowPos(config.CollapsedPosition, ImGuiCond.Always);
        ImGui.SetNextWindowSize(OmniTheme.Scale(new Vector2(34f)), ImGuiCond.Always);
        using var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, OmniTheme.Scale(new Vector2(4f)))
            .Push(ImGuiStyleVar.WindowRounding, OmniTheme.Scale(4f))
            .Push(ImGuiStyleVar.WindowBorderSize, OmniTheme.BorderThickness());
        using var colors = ImRaii.PushColor(
                ImGuiCol.WindowBg,
                OmniTheme.Tokens.Background with { W = config.Opacity })
            .Push(ImGuiCol.Border, OmniTheme.Tokens.Border);

        if (ImGui.Begin("###OmniMitigationMonitorCollapsed", flags))
        {
            var min = ImGui.GetCursorScreenPos();
            var size = OmniTheme.Scale(new Vector2(26f));
            ImGui.InvisibleButton("##MitigationCollapsedIcon", size);
            if (!config.Locked && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                collapsedIconDragged = true;
                config.CollapsedPosition = ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta;
                ImGui.SetWindowPos(config.CollapsedPosition);
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            }

            if (ImGui.IsItemDeactivated())
            {
                if (collapsedIconDragged)
                {
                    saveConfig();
                }
                else if (ImGui.IsItemHovered())
                {
                    config.Collapsed = false;
                    restoreLayoutNextDraw = true;
                    saveConfig();
                }

                collapsedIconDragged = false;
            }

            DrawCenteredIcon(FontAwesomeIcon.ChevronCircleDown, min, size);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(OmniLoc.Get("Feature.MitigationMonitor.Expand"));
            }
        }

        ImGui.End();
    }

    private static void DrawCenteredIcon(FontAwesomeIcon icon, Vector2 min, Vector2 size)
    {
        using var font = DalamudServices.PluginInterface.UiBuilder.IconFontHandle.Push();
        var text = icon.ToIconString();
        var textSize = ImGui.CalcTextSize(text);
        ImGui.GetWindowDrawList().AddText(
            min + new Vector2(
                MathF.Max(0f, (size.X - textSize.X) * 0.5f),
                MathF.Max(0f, (size.Y - textSize.Y) * 0.5f)),
            OmniTheme.Color(OmniTheme.Tokens.Text),
            text);
    }

    private void UpdateWindowGeometry()
    {
        if (config.Locked)
        {
            return;
        }

        var position = ImGui.GetWindowPos();
        var size = OmniTheme.Unscale(ImGui.GetWindowSize());
        if (Vector2.DistanceSquared(position, config.Position) > 0.25f)
        {
            config.Position = position;
            geometryDirty = true;
        }

        if (Vector2.DistanceSquared(size, config.Size) > 0.25f)
        {
            config.Size = new(
                Math.Clamp(size.X, 320f, 900f),
                Math.Clamp(size.Y, 160f, 720f));
            geometryDirty = true;
        }

        if (geometryDirty && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            geometryDirty = false;
            saveConfig();
        }
    }
}
