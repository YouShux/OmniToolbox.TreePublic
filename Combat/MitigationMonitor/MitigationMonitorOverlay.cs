using System.Drawing;
using Dalamud.Interface;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

internal sealed class MitigationMonitorOverlay
{
    internal const float MinimumWidth = 420f;

    private readonly MitigationMonitorConfig config;
    private readonly MitigationHistoryMenu historyMenu;
    private readonly MitigationRecordTable recordTable;
    private readonly Action saveConfig;
    private Action? openSettingsAction;
    private float lastScale;
    private bool restoreLayoutNextDraw;
    private bool geometryDirty;
    private bool collapsedIconDragged;
    private DateTime? expansionStartUTC;
    private Vector2 expansionFromPosition;
    private Vector2 expansionFromSize;

    public MitigationMonitorOverlay(
        MitigationMonitorConfig config,
        MitigationCombatLog combatLog,
        MitigationReplayStore replayStore,
        Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        historyMenu = new(config, combatLog, replayStore);
        recordTable = new(config, combatLog, saveConfig);
    }

    public void SetOpenSettingsAction(Action action) => openSettingsAction = action;

    public void Draw()
    {
        if (!config.Visible || GameState.IsInPVPArea)
        {
            return;
        }

        using var font = FontManager.Instance().UIFont.Push();
        if (config.Collapsed)
        {
            DrawCollapsedIcon();
            return;
        }

        var scaleChanged = MathF.Abs(lastScale - config.ScaleValue) > 0.001f;
        var targetPosition = config.Position;
        var targetSize = config.Scale(config.Size);
        var animatingExpansion = false;
        var positionCondition = restoreLayoutNextDraw ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
        var sizeCondition = restoreLayoutNextDraw || scaleChanged ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
        if (expansionStartUTC.HasValue)
        {
            var progress = Math.Clamp((float)(DateTime.UtcNow - expansionStartUTC.Value).TotalSeconds / 0.15f, 0f, 1f);
            if (progress < 1f)
            {
                var eased = progress * progress * (3f - 2f * progress);
                targetPosition = Vector2.Lerp(expansionFromPosition, targetPosition, eased);
                targetSize = Vector2.Lerp(expansionFromSize, targetSize, eased);
                animatingExpansion = true;
            }
            else
            {
                expansionStartUTC = null;
            }

            positionCondition = ImGuiCond.Always;
            sizeCondition = ImGuiCond.Always;
        }

        ImGui.SetNextWindowPos(targetPosition, positionCondition);
        ImGui.SetNextWindowSize(
            targetSize,
            sizeCondition);
        ImGui.SetNextWindowSizeConstraints(
            config.Scale(new Vector2(MinimumWidth, 160f)),
            new Vector2(float.MaxValue));
        restoreLayoutNextDraw = false;
        lastScale = config.ScaleValue;

        var flags = ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.NoScrollbar;
        if (config.Locked)
        {
            flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        }

        using var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, config.Scale(new Vector2(6f)))
            .Push(ImGuiStyleVar.WindowRounding, config.Scale(8f))
            .Push(ImGuiStyleVar.WindowBorderSize, OmniTheme.BorderThickness() * config.EffectiveScale);
        using var colors = ImRaii.PushColor(
                ImGuiCol.WindowBg,
                new Vector4(0.045f, 0.047f, 0.052f, config.Opacity))
            .Push(ImGuiCol.Border, KnownColor.LightSlateGray.ToVector4() with { W = 0.42f });

        if (ImGui.Begin($"{OmniLoc.Get("Feature.MitigationMonitor.Title")}###OmniMitigationMonitor", flags))
        {
            ImGui.SetWindowFontScale(config.EffectiveScale);
            recordTable.Draw(
                historyMenu.ActiveHistoryKey,
                historyMenu.Open,
                Collapse,
                ToggleLock,
                () => openSettingsAction?.Invoke());
            historyMenu.Draw();
            if (!animatingExpansion)
            {
                UpdateWindowGeometry();
            }
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
        expansionStartUTC = null;
    }

    public void RequestLayoutRestore()
    {
        restoreLayoutNextDraw = true;
        lastScale = 0f;
    }

    private void Collapse(Vector2 buttonPosition)
    {
        config.CollapsedPosition = buttonPosition;
        expansionStartUTC = null;
        config.Collapsed = true;
        saveConfig();
    }

    private void ToggleLock()
    {
        config.Locked = !config.Locked;
        saveConfig();
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
        ImGui.SetNextWindowSize(config.Scale(new Vector2(34f)), ImGuiCond.Always);
        using var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, config.Scale(new Vector2(4f)))
            .Push(ImGuiStyleVar.WindowRounding, config.Scale(4f))
            .Push(ImGuiStyleVar.WindowBorderSize, OmniTheme.BorderThickness() * config.EffectiveScale);
        using var colors = ImRaii.PushColor(
                ImGuiCol.WindowBg,
                new Vector4(0.045f, 0.047f, 0.052f, config.Opacity))
            .Push(ImGuiCol.Border, KnownColor.LightSlateGray.ToVector4() with { W = 0.42f });

        if (ImGui.Begin("###OmniMitigationMonitorCollapsed", flags))
        {
            ImGui.SetWindowFontScale(config.EffectiveScale);
            var min = ImGui.GetCursorScreenPos();
            var size = config.Scale(new Vector2(26f));
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
                    expansionFromPosition = ImGui.GetWindowPos();
                    expansionFromSize = config.Scale(new Vector2(34f));
                    expansionStartUTC = DateTime.UtcNow;
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
        var text = icon.ToIconString();
        var textSize = ImGui.CalcTextSize(text);
        ImGui.GetWindowDrawList().AddText(
            min + new Vector2(
                MathF.Max(0f, (size.X - textSize.X) * 0.5f),
                MathF.Max(0f, (size.Y - textSize.Y) * 0.5f)),
            OmniTheme.Color(KnownColor.White.ToVector4()),
            text);
    }

    private void UpdateWindowGeometry()
    {
        if (config.Locked)
        {
            return;
        }

        var position = ImGui.GetWindowPos();
        var size = config.Unscale(ImGui.GetWindowSize());
        if (Vector2.DistanceSquared(position, config.Position) > 0.25f)
        {
            config.Position = position;
            geometryDirty = true;
        }

        if (Vector2.DistanceSquared(size, config.Size) > 0.25f)
        {
            config.Size = new(
                MathF.Max(size.X, MinimumWidth),
                MathF.Max(size.Y, 160f));
            geometryDirty = true;
        }

        if (geometryDirty && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            geometryDirty = false;
            saveConfig();
        }
    }
}
