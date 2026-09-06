using System.Linq;
using System.Reflection;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private const string LegacyDtrPrefix = "OmniToolbox-MultiToolbar-";
    private PopupKind activePopup;
    private MultiToolbarWidgetType activeWidgetPopupType;
    private Vector2 popupPosition;
    private bool popupOpenedThisFrame;
    private float autoHideOffset;
    private bool toolbarVisible = true;
    private float toolbarTop;
    private bool legacyDtrCleanupDone;
    private bool? nativeDtrVisibility;
    private readonly List<IReadOnlyDtrBarEntry> visibleDtrEntries = [];
    private readonly List<IReadOnlyDtrBarEntry> collapsedDtrEntries = [];
    private readonly List<(Vector2 Min, Vector2 Max)> interactiveRects = [];
    private float toolbarButtonHeight;
    private bool showLoadedTab = true;
    private string pluginSearchText = string.Empty;
    private readonly List<IExposedPlugin> loadedPlugins = [];
    private readonly List<IExposedPlugin> unloadedPlugins = [];
    private double nextPluginCacheRefreshAt;
    private bool pluginCacheValid;

    private void Draw()
    {
        // 打开弹窗的鼠标按下事件属于工具栏按钮，不应在同一帧被外部点击逻辑再次关闭。
        popupOpenedThisFrame = false;
        using var font = FontManager.Instance().UIFont.Push();
        using var selectedFont = GetToolbarFont().Push();
        using var style = new ComicStyleScope();
        var theme = ToolbarTheme;
        using var toolbarColors = ImRaii.PushColor(ImGuiCol.Text, theme.Text)
            .Push(ImGuiCol.Button, theme.Surface)
            .Push(ImGuiCol.ButtonHovered, OmniTheme.UsesDarkPalette ? OmniTheme.HoverBackground : theme.Primary)
            .Push(ImGuiCol.ButtonActive, OmniTheme.UsesDarkPalette ? OmniTheme.ActiveBackground : theme.Accent)
            .Push(ImGuiCol.Header, OmniTheme.UsesDarkPalette ? OmniTheme.ActiveBackground : theme.Surface)
            .Push(ImGuiCol.HeaderHovered, OmniTheme.UsesDarkPalette ? OmniTheme.HoverBackground : theme.Primary)
            .Push(ImGuiCol.HeaderActive, OmniTheme.UsesDarkPalette ? OmniTheme.ActiveBackground : theme.Accent)
            .Push(ImGuiCol.CheckMark, theme.Accent);
        UpdateNativeDtrVisibility(config.Widgets.Any(widget => widget.Enabled && widget.Type == MultiToolbarWidgetType.DtrList));
        RefreshDtrEntries();
        DrawWorldMarkerOverlay();
        DrawToolbar();
        DrawHiddenWindowManager();

        if (activePopup != PopupKind.None)
        {
            DrawPopup();
        }
    }

    // Umbra 式全宽顶部工具栏：左/中/右三区排布小工具，背景默认全透明
    private void DrawToolbar()
    {
        var viewport = ImGui.GetMainViewport();
        var toolbarScale = GetToolbarScale();
        var rowHeight = MathF.Ceiling(MathF.Max(ImGui.GetFrameHeight() * toolbarScale, ScaleToolbar(36f)));
        toolbarButtonHeight = rowHeight;
        var offset = ScaleToolbar(config.BarVerticalOffset);
        var baseY = config.Alignment == MultiToolbarAlignment.Top
            ? viewport.WorkPos.Y + offset
            : viewport.WorkPos.Y + viewport.WorkSize.Y - rowHeight - offset;
        var deltaTime = MathF.Min(ImGui.GetIO().DeltaTime, 0.05f);
        var mousePosition = ImGui.GetMousePos();
        // baseY 在顶部和底部布局中均为未隐藏时的顶边；命中区域不随滑动动画移动。
        var hoverMin = new Vector2(
            viewport.WorkPos.X - rowHeight * 0.5f,
            baseY);
        var hoverMax = new Vector2(
            viewport.WorkPos.X + viewport.WorkSize.X + rowHeight * 0.5f,
            baseY + rowHeight);
        var cursorNearToolbar = config.IsLocked ||
                                activePopup != PopupKind.None ||
                                (mousePosition.X >= hoverMin.X && mousePosition.X <= hoverMax.X &&
                                 mousePosition.Y >= hoverMin.Y && mousePosition.Y <= hoverMax.Y);
        toolbarVisible = cursorNearToolbar;

        var targetOffset = toolbarVisible
            ? 0f
            : config.Alignment == MultiToolbarAlignment.Top ? -rowHeight - ScaleToolbar(2f) : rowHeight + ScaleToolbar(2f);
        autoHideOffset += (targetOffset - autoHideOffset) * MathF.Min(1f, deltaTime * 10f);
        if (MathF.Abs(autoHideOffset - targetOffset) < 0.5f)
        {
            autoHideOffset = targetOffset;
        }
        toolbarTop = MathF.Round(baseY + autoHideOffset);
        ImGui.SetNextWindowPos(new Vector2(viewport.WorkPos.X, toolbarTop), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, rowHeight), ImGuiCond.Always);
        using var colors = ImRaii.PushColor(ImGuiCol.WindowBg, Vector4.Zero);
        using var border = colors.Push(ImGuiCol.Border, Vector4.Zero);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero)
            .Push(ImGuiStyleVar.WindowBorderSize, 0f)
            .Push(ImGuiStyleVar.WindowRounding, 0f)
            .Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        var flags = ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoNav |
                    ImGuiWindowFlags.NoDocking |
                    ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoDecoration;
        if (activePopup == PopupKind.None && !IsMouseOverInteractiveArea())
        {
            flags |= ImGuiWindowFlags.NoInputs;
        }
        ImGui.SetNextWindowViewport(viewport.ID);
        if (!ImGui.Begin("##multiToolbarBar", flags))
        {
            ImGui.End();
            return;
        }

        ImGui.SetWindowFontScale(toolbarScale);
        var drawList = ImGui.GetWindowDrawList();
        var barMin = new Vector2(viewport.WorkPos.X, toolbarTop);
        var barMax = barMin + new Vector2(viewport.WorkSize.X, rowHeight);
        ImGui.PushClipRect(viewport.WorkPos, viewport.WorkPos + viewport.WorkSize, true);
        DrawToolbarBackground(drawList, barMin, barMax);
        DrawWidgetRow();
        ImGui.PopClipRect();
        ImGui.End();
    }

    private void DrawToolbarBackground(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        var theme = ToolbarTheme;
        var radius = ScaleToolbar(theme.BorderRadius);
        var opacity = Math.Clamp(config.BarBackgroundOpacity, 0f, 1f);
        var top = config.Alignment == MultiToolbarAlignment.Top;
        var background = WithAlpha(theme.Background, theme.Background.W * opacity);
        var rounding = top
            ? ImDrawFlags.RoundCornersBottomLeft | ImDrawFlags.RoundCornersBottomRight
            : ImDrawFlags.RoundCornersTopLeft | ImDrawFlags.RoundCornersTopRight;

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(background), radius, rounding);
        drawList.AddRect(
            min,
            max,
            ImGui.GetColorU32(WithAlpha(theme.Border, theme.Border.W * opacity)),
            radius,
            rounding,
            OmniTheme.BorderThickness());
    }

    private void DrawWidgetRow()
    {
        interactiveRects.Clear();
        List<MultiToolbarWidgetConfig> left = [];
        List<MultiToolbarWidgetConfig> center = [];
        List<MultiToolbarWidgetConfig> right = [];
        foreach (var widget in config.Widgets)
        {
            if (!widget.Enabled || !IsRightWidgetVisible(widget.Type) || !IsInformationWidgetVisible(widget.Type))
            {
                continue;
            }

            switch (widget.Side)
            {
                case MultiToolbarWidgetSide.Left:
                    left.Add(widget);
                    break;
                case MultiToolbarWidgetSide.Center:
                    center.Add(widget);
                    break;
                default:
                    right.Add(widget);
                    break;
            }
        }

        var viewport = ImGui.GetMainViewport();
        var contentStart = viewport.WorkPos.X;
        // 工具栏窗口本身就是全工作区宽度，直接使用 viewport 几何，避免 ImGui 当前光标状态影响三区定位。
        var available = viewport.WorkSize.X;
        var leftWidth = MeasureGroupWidth(left);
        var centerWidth = MeasureGroupWidth(center);
        var rightWidth = MeasureGroupWidth(right);
        var lockWidth = MeasureLockWidth();
        var widgetAvailable = MathF.Max(0f, available - lockWidth);
        if (right.Any(static widget => widget.Type == MultiToolbarWidgetType.DtrList))
        {
            var rightOtherWidth = MeasureGroupWidth(right, excludeDtr: true);
            var dtrBudget = MathF.Max(0f, widgetAvailable - leftWidth - centerWidth - rightOtherWidth);
            LimitDtrEntries(dtrBudget);
            rightWidth = MeasureGroupWidth(right);
        }

        var baseY = toolbarTop;
        var rightStart = contentStart + MathF.Max(0f, widgetAvailable - rightWidth);
        var centerStart = contentStart + MathF.Max((widgetAvailable - centerWidth) * 0.5f, 0f);
        var centerMinimum = contentStart + leftWidth;
        var centerMaximum = rightStart - centerWidth;
        if (centerMaximum >= centerMinimum)
        {
            centerStart = Math.Clamp(centerStart, centerMinimum, centerMaximum);
        }
        else
        {
            // 窄窗口时优先保持左右两组位置，中心组从左组末端开始并由窗口裁剪。
            centerStart = centerMinimum;
        }

        if (left.Count > 0)
        {
            DrawWidgetGroup(left, 0, contentStart, baseY);
        }

        if (center.Count > 0)
        {
            DrawWidgetGroup(center, left.Count, centerStart, baseY);
        }

        if (right.Count > 0)
        {
            DrawWidgetGroup(right, left.Count + center.Count, rightStart, baseY);
        }

        var lockPosition = new Vector2(contentStart + available - lockWidth, baseY);
        interactiveRects.Add((lockPosition, lockPosition + new Vector2(lockWidth, toolbarButtonHeight)));
        ImGui.SetCursorScreenPos(lockPosition);
        DrawLockButton();
    }

    private float GetToolbarScale() => Math.Clamp(config.ToolbarScale, 0.1f, 3f);

    private float ScaleToolbar(float value) => OmniTheme.Scale(value * GetToolbarScale());

    // 保留上下留白与描边空间，锁图标大小独立于文字字号。
    private float GetLockFontSize() => ScaleToolbar(Math.Clamp(toolbarButtonHeight / ScaleToolbar(1f) - 10f, 6f, 32f));

    private float MeasureLockWidth()
    {
        var fontSize = GetLockFontSize();
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        var width = MathF.Max(ImGui.CalcTextSize(FontAwesomeIcon.Lock.ToIconString()).X,
            ImGui.CalcTextSize(FontAwesomeIcon.LockOpen.ToIconString()).X);
        return width * fontSize / ImGui.GetFontSize() + ScaleToolbar(8f);
    }

    private void DrawLockButton()
    {
        var size = new Vector2(MeasureLockWidth(), toolbarButtonHeight);
        ImGui.InvisibleButton("##multiToolbarLock", size);
        DrawWidgetVisual(string.Empty, size);
        var fontSize = GetLockFontSize();
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            var text = (config.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen).ToIconString();
            var textSize = ImGui.CalcTextSize(text) * (fontSize / ImGui.GetFontSize());
            var position = Vector2.Round(ImGui.GetItemRectMin() + (size - textSize) * 0.5f + new Vector2(0f, ScaleToolbar(1f)));
            var drawList = ImGui.GetWindowDrawList();
            DrawToolbarTextOutline(drawList, text, position, fontSize);
            drawList.AddText(ImGui.GetFont(), fontSize, position, ImGui.GetColorU32(ToolbarTheme.Text), text);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (ImGui.IsItemClicked())
        {
            config.IsLocked = !config.IsLocked;
            autoHideOffset = 0f;
            toolbarVisible = true;
            saveConfig();
        }

        OmniControls.HelpTooltip(config.IsLocked
            ? OmniLoc.Get("Feature.MultiToolbar.Unlock")
            : OmniLoc.Get("Feature.MultiToolbar.Lock"));
    }

    private float MeasureDtrWidth()
    {
        var width = 0f;
        foreach (var entry in visibleDtrEntries)
        {
            width += MeasureDtrText(entry).X + ScaleToolbar(16f);
        }

        if (collapsedDtrEntries.Count > 0)
        {
            width += ImGui.CalcTextSize(dtrEntryLabel).X + ScaleToolbar(38f);
        }

        return width;
    }

    private void DrawDtrWidget(int index)
    {
        var x = ImGui.GetCursorScreenPos().X;
        var y = toolbarTop;
        foreach (var entry in visibleDtrEntries)
        {
            var size = new Vector2(
                MeasureDtrText(entry).X + ScaleToolbar(16f),
                toolbarButtonHeight);
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            ImGui.InvisibleButton($"##multiToolbarDtrVisible{index}_{entry.Title}", size);
            DrawWidgetVisual(string.Empty, size);
            DrawDtrText(entry, ImGui.GetItemRectMin() + new Vector2(ScaleToolbar(8f), 0f), size.X - ScaleToolbar(16f));
            if (entry.HasClickAction && (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right)))
            {
                entry.OnClick?.Invoke(new DtrInteractionEvent
                {
                    ClickType = ImGui.IsItemClicked(ImGuiMouseButton.Right) ? MouseClickType.Right : MouseClickType.Left,
                    ModifierKeys = GetDtrModifierKeys(),
                    Position = ImGui.GetMousePos(),
                });
            }

            if (ImGui.IsItemHovered() && entry.Tooltip is { } tooltip)
            {
                ImGui.BeginTooltip();
                ImGuiHelpers.SeStringWrapped(
                    new ReadOnlySeString(tooltip.Encode()),
                    new SeStringDrawParams { WrapWidth = ScaleToolbar(420f) });
                ImGui.EndTooltip();
            }

            x += size.X;
        }

        if (collapsedDtrEntries.Count > 0)
        {
            var label = dtrEntryLabel;
            var size = new Vector2(
                ImGui.CalcTextSize(label).X + ScaleToolbar(38f),
                toolbarButtonHeight);
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            ImGui.InvisibleButton($"##multiToolbarDtrCollapsed{index}", size);
            DrawWidgetVisual(label, size, FontAwesomeIcon.List);
            if (ImGui.IsItemClicked())
            {
                TogglePopup(PopupKind.Dtr);
            }
        }
    }

    private float MeasureGroupWidth(List<MultiToolbarWidgetConfig> widgets, bool excludeDtr = false)
    {
        if (widgets.Count == 0)
        {
            return 0f;
        }

        var width = 0f;
        var horizontalPadding = ScaleToolbar(4f) * 2f;
        for (var index = 0; index < widgets.Count; index++)
        {
            if (widgets[index].Type == MultiToolbarWidgetType.DtrList)
            {
                if (excludeDtr)
                {
                    continue;
                }

                width += MeasureDtrWidth();
                continue;
            }

            width += MeasureWidgetWidth(widgets[index], index, horizontalPadding);
        }

        var count = widgets.Count(widget => !excludeDtr || widget.Type != MultiToolbarWidgetType.DtrList);
        return width + MathF.Max(0, count - 1) * ScaleToolbar(config.ComponentSpacing);
    }

    private void LimitDtrEntries(float maxWidth)
    {
        if (visibleDtrEntries.Count == 0)
        {
            return;
        }

        var visibleWidth = visibleDtrEntries.Sum(entry => MeasureDtrText(entry).X + ScaleToolbar(16f));
        var collapsedButtonWidth = ImGui.CalcTextSize(dtrEntryLabel).X + ScaleToolbar(38f);
        var budget = visibleWidth + (collapsedDtrEntries.Count > 0 ? collapsedButtonWidth : 0f) > maxWidth
            ? MathF.Max(0f, maxWidth - collapsedButtonWidth)
            : maxWidth;
        var used = 0f;
        var keep = new List<IReadOnlyDtrBarEntry>(visibleDtrEntries.Count);
        foreach (var entry in visibleDtrEntries)
        {
            var width = MeasureDtrText(entry).X + ScaleToolbar(16f);
            if (used + width > budget && keep.Count > 0)
            {
                collapsedDtrEntries.Add(entry);
                continue;
            }

            keep.Add(entry);
            used += width;
        }

        visibleDtrEntries.Clear();
        visibleDtrEntries.AddRange(keep);
    }

    private void DrawWidgetGroup(List<MultiToolbarWidgetConfig> widgets, int idOffset, float startX, float startY)
    {
        var x = startX;
        for (var index = 0; index < widgets.Count; index++)
        {
            var width = MeasureWidgetEntryWidth(widgets[index], index + idOffset);
            interactiveRects.Add((new Vector2(x, startY), new Vector2(x + width, startY + toolbarButtonHeight)));
            ImGui.SetCursorScreenPos(new Vector2(x, startY));
            DrawWidgetButton(widgets[index], index + idOffset);
            x += width + ScaleToolbar(config.ComponentSpacing);
        }
    }

    private bool IsMouseOverInteractiveArea()
    {
        var mousePosition = ImGui.GetMousePos();
        return interactiveRects.Any(rect =>
            mousePosition.X >= rect.Min.X && mousePosition.X <= rect.Max.X &&
            mousePosition.Y >= rect.Min.Y && mousePosition.Y <= rect.Max.Y);
    }

    private float MeasureWidgetEntryWidth(MultiToolbarWidgetConfig widget, int index) =>
        widget.Type == MultiToolbarWidgetType.DtrList
            ? MeasureDtrWidth()
            : MeasureWidgetWidth(widget, index, ScaleToolbar(4f) * 2f);

    private void DrawWidgetButton(MultiToolbarWidgetConfig widget, int index)
    {
        if (widget.Type == MultiToolbarWidgetType.DtrList)
        {
            DrawDtrWidget(index);
            return;
        }

        var label = WidgetLabel(widget, index);
        var padding = new Vector2(ScaleToolbar(4f), ScaleToolbar(3f));
        var showIcon = ShouldShowWidgetIcon(widget);
        var icon = showIcon ? WidgetIcon(widget.Type) : null;
        var gameIconID = showIcon
            ? widget.GameIconID > 0 ? widget.GameIconID : WidgetGameIcon(widget.Type)
            : 0;
        var size = new Vector2(MeasureWidgetWidth(widget, index, padding.X * 2f), toolbarButtonHeight);
        var id = $"##multiToolbarWidget{index}";
        ImGui.InvisibleButton(id, size);
        DrawWidgetVisual(label, size, icon, gameIconID,
            widget.Type is MultiToolbarWidgetType.StackedClock or MultiToolbarWidgetType.Weather,
            widget.Type is MultiToolbarWidgetType.StackedClock or MultiToolbarWidgetType.Clock,
            widget.Type == MultiToolbarWidgetType.GearsetSwitcher,
            widget.GameIconID > 0 && IconBrowser.IsActionOrItemIcon(widget.GameIconID));

        if (TryHandleRightWidgetInteraction(widget.Type) || TryHandleInformationWidgetInteraction(widget.Type))
        {
            return;
        }

        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (activePopup != PopupKind.None &&
                (int)widget.Type >= (int)MultiToolbarWidgetType.BattleEffects &&
                widget.Type is not MultiToolbarWidgetType.ToolbarPin and not MultiToolbarWidgetType.WalkingIndicator &&
                (activePopup != PopupKind.Widget || activeWidgetPopupType != widget.Type) &&
                !ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Right) &&
                ImGui.GetIO().MouseDelta.LengthSquared() > 0.01f)
            {
                ToggleWidgetPopup(widget.Type);
                return;
            }
        }

        switch (widget.Type)
        {
            case MultiToolbarWidgetType.PluginList:
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    ExecuteCommand("/xlplugins");
                }
                else if (ImGui.IsItemClicked())
                {
                    TogglePopup(PopupKind.Plugins);
                }
                else if (hovered && activePopup != PopupKind.None && activePopup != PopupKind.Plugins)
                {
                    OpenPopup(PopupKind.Plugins, CalculatePopupPosition(PopupKind.Plugins));
                }
                break;
            case MultiToolbarWidgetType.CommandList:
                if (ImGui.IsItemClicked())
                {
                    TogglePopup(PopupKind.Commands);
                }
                else if (hovered && activePopup != PopupKind.None && activePopup != PopupKind.Commands)
                {
                    OpenPopup(PopupKind.Commands, CalculatePopupPosition(PopupKind.Commands));
                }
                break;
            case MultiToolbarWidgetType.DtrList:
                if (ImGui.IsItemClicked())
                {
                    TogglePopup(PopupKind.Dtr);
                }

                break;
            case MultiToolbarWidgetType.ToolbarPin:
                if (ImGui.IsItemClicked())
                {
                    config.IsLocked = !config.IsLocked;
                    autoHideOffset = 0f;
                    toolbarVisible = true;
                    saveConfig();
                }

                break;
            case MultiToolbarWidgetType.WalkingIndicator:
                if (ImGui.IsItemClicked())
                {
                    ExecuteBuiltInWidgetAction(widget);
                }

                break;
            case MultiToolbarWidgetType.OnlineStatus:
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    OpenOnlineStatusDetail();
                }
                else if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ToggleWidgetPopup(widget.Type);
                }

                break;
            case MultiToolbarWidgetType.Societies:
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    TeleportToSociety(config.TrackedSocietyID);
                }
                else if (ImGui.IsItemClicked())
                {
                    ToggleWidgetPopup(widget.Type);
                }

                break;
            case MultiToolbarWidgetType.Currencies:
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    unsafe
                    {
                        var ui = FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance();
                        if (ui != null)
                        {
                            ui->ExecuteMainCommand(66);
                        }
                    }
                }
                else if (ImGui.IsItemClicked())
                {
                    ToggleWidgetPopup(widget.Type);
                }

                break;
            case MultiToolbarWidgetType.CustomButton:
                if (ImGui.IsItemClicked())
                {
                    ExecuteBuiltInWidgetAction(widget);
                }

                break;
            default:
                if (ImGui.IsItemClicked())
                {
                    ToggleWidgetPopup(widget.Type);
                }
                break;
        }
    }

    private float MeasureWidgetWidth(MultiToolbarWidgetConfig widget, int index, float horizontalPadding)
    {
        var label = WidgetLabel(widget, index);
        var textScale = widget.Type == MultiToolbarWidgetType.StackedClock
            ? GetStackedClockTextScale()
            : widget.Type == MultiToolbarWidgetType.Weather ? 0.65f : 1f;
        var width = ImGui.CalcTextSize(label).X * textScale + horizontalPadding;
        if (ShouldShowWidgetIcon(widget) &&
            (WidgetIcon(widget.Type) is not null || widget.GameIconID > 0 || WidgetGameIcon(widget.Type) > 0))
        {
            width += GetWidgetIconSize(widget.GameIconID > 0 ? widget.GameIconID : WidgetGameIcon(widget.Type),
                widget.Type == MultiToolbarWidgetType.GearsetSwitcher);
            if (!string.IsNullOrEmpty(label))
            {
                width += ScaleToolbar(6f);
            }
        }

        return MathF.Min(width, ScaleToolbar(180f));
    }

    private FontAwesomeIcon? WidgetIcon(MultiToolbarWidgetType type) =>
        InformationWidgetIcon(type) ?? RightWidgetIcon(type) ?? type switch
    {
        MultiToolbarWidgetType.PluginList => FontAwesomeIcon.Plug,
        MultiToolbarWidgetType.CommandList => FontAwesomeIcon.Terminal,
        MultiToolbarWidgetType.DtrList => FontAwesomeIcon.List,
        // 自定义按钮没有游戏图标时沿用 Umbra 的可识别默认终端图标。
        MultiToolbarWidgetType.CustomButton => FontAwesomeIcon.Terminal,
        MultiToolbarWidgetType.BattleEffects => FontAwesomeIcon.WandMagicSparkles,
        MultiToolbarWidgetType.Societies => null,
        // 这些组件在 Umbra 中使用动态游戏图标；未配置图标时不显示误导性的字体图标。
        MultiToolbarWidgetType.OnlineStatus => null,
        MultiToolbarWidgetType.GearsetSwitcher => null,
        MultiToolbarWidgetType.Durability => null,
        MultiToolbarWidgetType.RetainerList => null,
        MultiToolbarWidgetType.Currencies => null,
        MultiToolbarWidgetType.Flag => null,
        MultiToolbarWidgetType.Volume => FontAwesomeIcon.VolumeUp,
        MultiToolbarWidgetType.MailIndicator => FontAwesomeIcon.Envelope,
        MultiToolbarWidgetType.MarkerControl => FontAwesomeIcon.MapSigns,
        MultiToolbarWidgetType.WalkingIndicator => FontAwesomeIcon.Running,
        MultiToolbarWidgetType.StackedClock => null,
        MultiToolbarWidgetType.ToolbarPin => FontAwesomeIcon.Lock,
        _ => null,
    };

    private uint WidgetGameIcon(MultiToolbarWidgetType type) =>
        type == MultiToolbarWidgetType.BattleEffects ? 516u :
        SocialWidgetGameIcon(type) is var socialIcon && socialIcon > 0
            ? socialIcon
            : InventoryWidgetGameIcon(type) is var inventoryIcon && inventoryIcon > 0
                ? inventoryIcon
                : InformationWidgetGameIcon(type) is var informationIcon && informationIcon > 0
                    ? informationIcon
                    : RightWidgetGameIcon(type);

    private static bool ShouldShowWidgetIcon(MultiToolbarWidgetConfig widget) =>
        widget.ShowIcon && widget.Type is not MultiToolbarWidgetType.DtrList and not MultiToolbarWidgetType.ToolbarPin;

    private float GetWidgetIconSize(uint gameIconID = 0, bool largeIcon = false) => gameIconID == 60560
        ? ImGui.GetFontSize()
        : largeIcon ? toolbarButtonHeight - ScaleToolbar(2f)
        : MathF.Min(ImGui.GetFontSize() * 1.3f, toolbarButtonHeight - ScaleToolbar(4f));

    private float GetStackedClockTextScale() => MathF.Min(0.8f,
        (toolbarButtonHeight - ScaleToolbar(2f)) / (ImGui.GetFontSize() * 2f));

    private float GetPopupRowIconSize() => Math.Clamp(
        ScaleToolbar(config.RowIconSize),
        ScaleToolbar(18f),
        ScaleToolbar(32f));

    private static void ExecuteBuiltInWidgetAction(MultiToolbarWidgetConfig widget)
    {
        if (widget.Type == MultiToolbarWidgetType.WalkingIndicator)
        {
            unsafe
            {
                var control = FFXIVClientStructs.FFXIV.Client.Game.Control.Control.Instance();
                if (control != null)
                {
                    control->IsWalking = !control->IsWalking;
                }
            }

            return;
        }

        ExecuteCommand(widget.Command);
    }

    private void DrawWidgetVisual(
        string label,
        Vector2 size,
        FontAwesomeIcon? icon = null,
        uint gameIconID = 0,
        bool stacked = false,
        bool bold = false,
        bool largeIcon = false,
        bool frameGameIcon = false)
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var theme = ToolbarTheme;
        var radius = ScaleToolbar(theme.ButtonRadius);
        var opacity = hovered || active ? 1f : Math.Clamp(config.ButtonBackgroundOpacity, 0f, 1f);
        if (opacity > 0f)
        {
            var shadowOffset = new Vector2(ScaleToolbar(MathF.Max(0f, theme.ShadowOffset)));
            var backgroundMin = min + new Vector2(0f, ScaleToolbar(2f));
            var backgroundMax = max - new Vector2(0f, MathF.Max(ScaleToolbar(2f), shadowOffset.Y));
            drawList.AddRectFilled(backgroundMin + shadowOffset, backgroundMax + shadowOffset,
                OmniTheme.Color(WithAlpha(theme.Shadow, theme.Shadow.W * opacity)), radius);
            var background = active
                ? OmniTheme.UsesDarkPalette ? OmniTheme.ActiveBackground : theme.Accent
                : hovered
                    ? OmniTheme.UsesDarkPalette ? OmniTheme.HoverBackground : theme.Primary
                    : theme.Surface;
            drawList.AddRectFilled(backgroundMin, backgroundMax, OmniTheme.Color(WithAlpha(background, background.W * opacity)), radius);
            var border = OmniTheme.BorderThickness();
            drawList.AddRect(backgroundMin + new Vector2(border * 0.5f), backgroundMax - new Vector2(border * 0.5f),
                OmniTheme.Color(WithAlpha(theme.Border, theme.Border.W * opacity)),
                MathF.Max(0f, radius - border * 0.5f), ImDrawFlags.RoundCornersAll, border);
        }
        drawList.PushClipRect(min, max, true);
        var hasIcon = icon is not null || gameIconID > 0;
        var textScale = stacked ? bold ? GetStackedClockTextScale() : 0.65f : 1f;
        var textSize = ImGui.CalcTextSize(label) * textScale;
        var iconSize = hasIcon ? GetWidgetIconSize(gameIconID, largeIcon) : 0f;
        var iconGap = string.IsNullOrEmpty(label) ? 0f : ScaleToolbar(6f);
        var contentWidth = (hasIcon ? iconSize + iconGap : 0f) + textSize.X;
        var contentStart = min.X + MathF.Max(0f, (size.X - contentWidth) * 0.5f);
        var contentCenterY = min.Y + size.Y * 0.5f;

        if (gameIconID > 0 && frameGameIcon)
        {
            FramedGameIcon.Draw(drawList, gameIconID,
                new Vector2(contentStart, contentCenterY - iconSize * 0.5f), new Vector2(iconSize));
        }
        else if (gameIconID > 0 && ImageHelper.GetGameIcon(gameIconID) is { } texture)
        {
            drawList.AddImage(
                texture.Handle,
                new Vector2(contentStart, contentCenterY - iconSize * 0.5f),
                new Vector2(contentStart + iconSize, contentCenterY + iconSize * 0.5f),
                gameIconID == 60560 ? new Vector2(0.25f) : Vector2.Zero,
                gameIconID == 60560 ? new Vector2(0.75f) : Vector2.One);
        }
        else if (icon is { } fontIcon)
        {
            var iconFontSize = iconSize;
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            var iconText = fontIcon.ToIconString();
            var iconTextSize = ImGui.CalcTextSize(iconText) * (iconFontSize / MathF.Max(1f, ImGui.GetFontSize()));
            DrawToolbarTextOutline(drawList, iconText,
                new Vector2(contentStart, contentCenterY - iconTextSize.Y * 0.5f), iconFontSize);
            drawList.AddText(
                ImGui.GetFont(),
                iconFontSize,
                new Vector2(contentStart, contentCenterY - iconTextSize.Y * 0.5f),
                ImGui.GetColorU32(theme.Text),
                iconText);
        }

        if (!string.IsNullOrEmpty(label))
        {
            var textStart = contentStart + (hasIcon ? iconSize + iconGap : 0f);
            if (bold)
            {
                DrawClockText(drawList, label,
                    new Vector2(textStart, contentCenterY - textSize.Y * 0.5f), textScale, stacked);
                drawList.PopClipRect();
                return;
            }
            DrawToolbarTextOutline(drawList, label,
                new Vector2(textStart, contentCenterY - textSize.Y * 0.5f), ImGui.GetFontSize() * textScale);
            drawList.AddText(
                ImGui.GetFont(),
                ImGui.GetFontSize() * textScale,
                new Vector2(textStart, contentCenterY - textSize.Y * 0.5f),
                ImGui.GetColorU32(theme.Text),
                label);
        }

        drawList.PopClipRect();
    }

    // Umbra 导出主题 Widget.TextOutline: 0xFF313131。
    private static readonly Vector4 ToolbarTextOutlineColor = new(49f / 255f, 49f / 255f, 49f / 255f, 1f);

    private void DrawToolbarTextOutline(ImDrawListPtr drawList, string text, Vector2 position, float fontSize)
    {
        var radius = ScaleToolbar(2f);
        var color = ImGui.GetColorU32(ToolbarTextOutlineColor);
        for (var index = 0; index < 8; index++)
        {
            var angle = index * MathF.PI / 4f;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            drawList.AddText(ImGui.GetFont(), fontSize, position + offset, color, text);
        }
    }

    private void DrawClockText(ImDrawListPtr drawList, string label, Vector2 position, float scale, bool stacked)
    {
        var theme = ToolbarTheme;
        var fontSize = ImGui.GetFontSize() * scale;
        var lines = label.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var origin = Vector2.Round(position + new Vector2(0f, index * fontSize));
            var color = theme.Text;
            if (stacked && index > 0)
            {
                color = new Vector4(color.X * 0.75f, color.Y * 0.75f, color.Z * 0.75f, color.W);
            }
            DrawToolbarTextOutline(drawList, line, origin, fontSize);
            drawList.AddText(ImGui.GetFont(), fontSize, origin, ImGui.GetColorU32(color), line);

            // 仅加粗时间数字，保留 LT/ST/ET 标签内部的字母空隙。
            var timeStart = 0;
            while (timeStart < line.Length && !char.IsAsciiDigit(line[timeStart]))
            {
                timeStart++;
            }
            if (timeStart < line.Length)
            {
                var offset = ImGui.CalcTextSize(line[..timeStart]).X * scale;
                drawList.AddText(ImGui.GetFont(), fontSize, origin + new Vector2(offset + ScaleToolbar(0.6f), 0f),
                    ImGui.GetColorU32(color), line[timeStart..]);
            }
        }
    }

    private string WidgetLabel(MultiToolbarWidgetConfig widget, int index)
    {
        if (widget.DisplayName is { } displayName)
        {
            return string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName;
        }

        if (widget.Type is MultiToolbarWidgetType.MailIndicator or
            MultiToolbarWidgetType.MarkerControl or
            MultiToolbarWidgetType.WalkingIndicator or
            MultiToolbarWidgetType.ToolbarPin)
        {
            return string.Empty;
        }

        var dynamicLabel = SocialWidgetLabel(widget.Type) ??
                           InventoryWidgetLabel(widget.Type) ??
                           RightWidgetLabel(widget.Type) ??
                           InformationWidgetLabel(widget.Type);
        if (dynamicLabel is not null)
        {
            return dynamicLabel;
        }

        return widget.Type switch
        {
            MultiToolbarWidgetType.PluginList => pluginEntryLabel,
            MultiToolbarWidgetType.CommandList => commandEntryLabel,
            MultiToolbarWidgetType.DtrList => dtrEntryLabel,
            MultiToolbarWidgetType.BattleEffects => OmniLoc.Get("Feature.MultiToolbar.WidgetBattleEffects"),
            MultiToolbarWidgetType.Societies => OmniLoc.Get("Feature.MultiToolbar.WidgetSocieties"),
            MultiToolbarWidgetType.OnlineStatus => OmniLoc.Get("Feature.MultiToolbar.WidgetOnlineStatus"),
            MultiToolbarWidgetType.GearsetSwitcher => OmniLoc.Get("Feature.MultiToolbar.WidgetGearsetSwitcher"),
            MultiToolbarWidgetType.Durability => OmniLoc.Get("Feature.MultiToolbar.WidgetDurability"),
            MultiToolbarWidgetType.RetainerList => OmniLoc.Get("Feature.MultiToolbar.WidgetRetainerList"),
            MultiToolbarWidgetType.Currencies => OmniLoc.Get("Feature.MultiToolbar.WidgetCurrencies"),
            MultiToolbarWidgetType.Flag => OmniLoc.Get("Feature.MultiToolbar.WidgetFlag"),
            MultiToolbarWidgetType.Volume => OmniLoc.Get("Feature.MultiToolbar.WidgetVolume"),
            MultiToolbarWidgetType.StackedClock => DateTime.Now.ToString("HH:mm"),
            _ => string.IsNullOrWhiteSpace(widget.Name) ? widget.Command : widget.Name,
        };
    }

    private void DrawPopup()
    {
        if (popupOpenedThisFrame)
        {
            ImGui.SetNextWindowFocus();
        }

        ImGui.SetNextWindowPos(
            popupPosition,
            ImGuiCond.Always,
            config.Alignment == MultiToolbarAlignment.Bottom ? new Vector2(0f, 1f) : Vector2.Zero);
        var viewport = ImGui.GetMainViewport();
        var popupWidth = MathF.Min(GetPopupWidth(activePopup), viewport.WorkSize.X);
        var isListPopup = activePopup is PopupKind.Plugins or PopupKind.Commands;
        ImGui.SetNextWindowSize(new Vector2(popupWidth, isListPopup ? ScaleToolbar(PopupListHeight + 70f) : 0f), ImGuiCond.Always);
        var availableHeight = config.Alignment == MultiToolbarAlignment.Bottom
            ? popupPosition.Y - viewport.WorkPos.Y
            : viewport.WorkPos.Y + viewport.WorkSize.Y - popupPosition.Y;
        ImGui.SetNextWindowSizeConstraints(new Vector2(popupWidth, 0f), new Vector2(popupWidth, MathF.Max(1f, availableHeight)));
        using var colors = ImRaii.PushColor(
                ImGuiCol.WindowBg,
                ToolbarTheme.Background)
            .Push(ImGuiCol.ChildBg, ToolbarTheme.Surface)
            .Push(ImGuiCol.Border, ToolbarTheme.Border)
            .Push(ImGuiCol.PopupBg, ToolbarTheme.Background);
        using var popupStyle = ImRaii.PushStyle(
                ImGuiStyleVar.WindowPadding,
                OmniTheme.Scale(OmniTheme.Tokens.WindowPadding))
            .Push(ImGuiStyleVar.ItemSpacing, OmniTheme.Scale(OmniTheme.Tokens.ItemSpacing))
            .Push(ImGuiStyleVar.WindowBorderSize, OmniTheme.BorderThickness())
            .Push(ImGuiStyleVar.WindowRounding, OmniTheme.Scale(OmniTheme.Tokens.BorderRadius))
            .Push(ImGuiStyleVar.ChildRounding, OmniTheme.Scale(OmniTheme.Tokens.BorderRadius))
            .Push(ImGuiStyleVar.ChildBorderSize, OmniTheme.BorderThickness());
        var flags = ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.AlwaysUseWindowPadding |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoNav |
                    ImGuiWindowFlags.NoDocking;
        var visible = ImGui.Begin("##multiToolbarPopup", flags);
        var hasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if ((!popupOpenedThisFrame && !hasFocus) || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ImGui.End();
            ClosePopup();
            return;
        }

        if (!visible)
        {
            ImGui.End();
            return;
        }

        ImGui.SetWindowFontScale(GetToolbarScale());
        switch (activePopup)
        {
            case PopupKind.Plugins:
                DrawPluginPopup();
                break;
            case PopupKind.Commands:
                DrawCommandPopup();
                break;
            case PopupKind.Dtr:
                DrawDtrPopup();
                break;
            case PopupKind.Widget:
                if (!DrawSocialWidgetPopup(activeWidgetPopupType) &&
                    !DrawInventoryWidgetPopup(activeWidgetPopupType) &&
                    !DrawInformationWidgetPopup(activeWidgetPopupType))
                {
                    DrawRightWidgetPopup(activeWidgetPopupType);
                }

                break;
        }

        ImGui.End();
    }

    private void TogglePopup(PopupKind kind)
    {
        if (activePopup == kind)
        {
            ClosePopup();
            return;
        }

        OpenPopup(kind, CalculatePopupPosition(kind));
    }

    private Vector2 CalculatePopupPosition(PopupKind kind)
    {
        var min = ImGui.GetItemRectMin();
        var isTop = config.Alignment == MultiToolbarAlignment.Top;
        var y = isTop ? min.Y + ImGui.GetItemRectSize().Y : min.Y;
        var viewport = ImGui.GetMainViewport();
        var popupWidth = MathF.Min(GetPopupWidth(kind), viewport.WorkSize.X);
        var maxX = MathF.Max(viewport.WorkPos.X, viewport.WorkPos.X + viewport.WorkSize.X - popupWidth);
        var x = Math.Clamp(min.X, viewport.WorkPos.X, maxX);
        var popupHeight = MathF.Min(PopupHeightEstimate(kind), viewport.WorkSize.Y);
        var workMinY = viewport.WorkPos.Y;
        var workMaxY = viewport.WorkPos.Y + viewport.WorkSize.Y;
        y = isTop
            ? Math.Clamp(y, workMinY, MathF.Max(workMinY, workMaxY - popupHeight))
            : Math.Clamp(y, MathF.Min(workMaxY, workMinY + popupHeight), workMaxY);
        return new Vector2(x, y);
    }

    private float GetPopupWidth(PopupKind kind) =>
        ScaleToolbar(kind == PopupKind.Widget
            ? activeWidgetPopupType switch
            {
                MultiToolbarWidgetType.Societies => GetSocietiesPopupWidth(),
                MultiToolbarWidgetType.Volume => 560f,
                MultiToolbarWidgetType.StackedClock or MultiToolbarWidgetType.Clock => 150f,
                MultiToolbarWidgetType.MarkerControl => 220f,
                MultiToolbarWidgetType.RetainerList => 620f,
                MultiToolbarWidgetType.Durability => 440f,
                MultiToolbarWidgetType.Currencies => 440f,
                MultiToolbarWidgetType.GearsetSwitcher => 600f,
                MultiToolbarWidgetType.OnlineStatus => 260f,
                MultiToolbarWidgetType.BattleEffects => 380f,
                _ => PopupWidth,
            }
            : PopupWidth);

    private static string EllipsizeToolbarText(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
        {
            return text;
        }

        const string suffix = "...";
        var available = width - ImGui.CalcTextSize(suffix).X;
        if (available < 0f)
        {
            return string.Empty;
        }

        var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = boundaries.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var end = middle < boundaries.Length ? boundaries[middle] : text.Length;
            if (ImGui.CalcTextSize(text[..end]).X <= available)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return text[..(low < boundaries.Length ? boundaries[low] : text.Length)] + suffix;
    }

    private void ToggleWidgetPopup(MultiToolbarWidgetType type)
    {
        if (activePopup == PopupKind.Widget && activeWidgetPopupType == type)
        {
            ClosePopup();
            return;
        }

        activeWidgetPopupType = type;
        OnInventoryWidgetPopupOpened(type);
        OpenPopup(PopupKind.Widget, CalculatePopupPosition(PopupKind.Widget));
    }

    private float PopupHeightEstimate(PopupKind kind)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        return kind switch
        {
            PopupKind.Plugins or PopupKind.Commands => ScaleToolbar(PopupListHeight + 70f),
            PopupKind.Dtr => ScaleToolbar(50f) + Math.Min(
                ScaleToolbar(300f),
                collapsedDtrEntries.Count * MathF.Max(ImGui.GetFrameHeight(), ScaleToolbar(24f)) +
                Math.Max(0, collapsedDtrEntries.Count - 1) * spacing),
            PopupKind.Widget => ScaleToolbar(activeWidgetPopupType == MultiToolbarWidgetType.Societies ? 520f : 360f),
            _ => ScaleToolbar(100f),
        };
    }

    private unsafe void OpenPopup(PopupKind kind, Vector2 position)
    {
        if (activePopup != PopupKind.None)
        {
            FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlaySoundEffect(1);
        }
        activePopup = kind;
        popupPosition = position;
        popupOpenedThisFrame = true;
        pluginCacheValid = false;
        FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlaySoundEffect(1);
    }

    private unsafe void ClosePopup()
    {
        if (activePopup != PopupKind.None)
        {
            FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlaySoundEffect(1);
        }
        activePopup = PopupKind.None;
        activeWidgetPopupType = default;
        pluginCacheValid = false;
    }

    private void DrawPluginPopup()
    {
        var loadedLabel = OmniLoc.Get("Feature.MultiToolbar.LoadedTab");
        var unloadedLabel = OmniLoc.Get("Feature.MultiToolbar.UnloadedTab");
        var interactionHelp = OmniLoc.Get("Feature.MultiToolbar.PluginInteractionHelp");
        var toggleWidth = MathF.Max(
            ImGui.CalcTextSize(loadedLabel).X,
            ImGui.CalcTextSize(unloadedLabel).X) + ImGui.GetStyle().FramePadding.X * 2f;
        var toggleSpacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(MathF.Max(80f, ImGui.GetContentRegionAvail().X - toggleWidth - toggleSpacing));
        OmniControls.InputTextWithHint(
            "##multiToolbarPluginSearch",
            OmniLoc.Get("Feature.MultiToolbar.SearchPlugin"),
            ref pluginSearchText,
            64);
        ImGui.SameLine();
        if (ImGui.Button($"{(showLoadedTab ? loadedLabel : unloadedLabel)}###multiToolbarPluginFilter", new Vector2(toggleWidth, 0f)))
        {
            showLoadedTab = !showLoadedTab;
        }
        OmniControls.HelpTooltip(interactionHelp);
        ImGui.Spacing();

        RefreshPluginCache();
        var entries = FilterPlugins(showLoadedTab ? loadedPlugins : unloadedPlugins);
        var iconSize = MathF.Round(ScaleToolbar(config.RowIconSize));
        var rowHeight = MathF.Max(ImGui.GetFrameHeight(), iconSize + ScaleToolbar(4f));
        using var listPadding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(ScaleToolbar(8f)));
        using var child = ImRaii.Child("##multiToolbarPluginList", new Vector2(0f, MathF.Max(1f, ImGui.GetContentRegionAvail().Y)), false,
            ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!child)
        {
            return;
        }

        if (entries.Count == 0)
        {
            ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.NoPlugins"));
            return;
        }

        var listOrigin = ImGui.GetCursorScreenPos();
        var rowWidth = ImGui.GetContentRegionAvail().X;
        var rowStride = rowHeight + ImGui.GetStyle().ItemSpacing.Y;
        for (var index = 0; index < entries.Count; index++)
        {
            ImGui.SetCursorScreenPos(listOrigin + new Vector2(0f, index * rowStride));
            DrawPluginRow(entries[index], index, iconSize, rowHeight, rowWidth, !showLoadedTab);
        }
    }

    private void DrawPluginRow(IExposedPlugin plugin, int index, float iconSize, float rowHeight, float rowWidth, bool unloaded)
    {
        var rowPos = ImGui.GetCursorScreenPos();
        var contentInset = ScaleToolbar(8f);
        var showSettings = !unloaded && plugin.HasMainUi && plugin.HasConfigUi;
        var contentWidth = MathF.Max(1f, rowWidth - rowHeight);
        if (ImGui.InvisibleButton($"##multiToolbarPlugin{index}", new Vector2(contentWidth, rowHeight)))
        {
            if (unloaded)
            {
                TryEnablePlugin(plugin);
            }
            else
            {
                OpenPlugin(plugin);
            }
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (unloaded)
            {
                TryEnablePlugin(plugin);
            }
            else
            {
                TryDisablePlugin(plugin);
            }

            pluginCacheValid = false;
        }

        var drawList = ImGui.GetWindowDrawList();
        if (ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(
                rowPos,
                rowPos + new Vector2(rowWidth, rowHeight),
                ImGui.GetColorU32(OmniTheme.HoverBackground),
                ScaleToolbar(OmniTheme.Tokens.ButtonRadius));
        }

        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, unloaded ? 0.6f : 1f))
        {
            DrawPluginIcon(plugin, rowPos + new Vector2(contentInset, (rowHeight - iconSize) * 0.5f), iconSize);
        }

        var displayName = unloaded
            ? $"{plugin.Name}{OmniLoc.Get("Feature.MultiToolbar.UnloadedSuffix")}"
            : plugin.Name;
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);
        drawList.PushClipRect(rowPos, rowPos + new Vector2(contentWidth, rowHeight), true);
        drawList.AddText(
            rowPos +
            new Vector2(
                contentInset + iconSize + ScaleToolbar(6f),
                (rowHeight - ImGui.GetTextLineHeight()) * 0.5f),
            unloaded ? WithAlpha(textColor, 0.6f) : textColor,
            displayName);
        drawList.PopClipRect();
        if (showSettings)
        {
            var cursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(rowPos + new Vector2(contentWidth, MathF.Max(0f, (rowHeight - ImGui.GetFrameHeight()) * 0.5f)));
            if (OmniControls.IconButton($"multiToolbarPluginSettings{index}", FontAwesomeIcon.Cog, false, plugin.Name))
            {
                plugin.OpenConfigUi();
            }

            ImGui.SetCursorScreenPos(cursor);
        }
    }

    private void DrawPluginIcon(IExposedPlugin plugin, Vector2 position, float iconSize)
    {
        var drawList = ImGui.GetWindowDrawList();
        var texture = PluginIconResolver.GetIcon(plugin, iconSize);
        if (PluginIconResolver.IsUsableTexture(texture))
        {
            var size = new Vector2(texture.Width, texture.Height) * (iconSize / MathF.Max(texture.Width, texture.Height));
            var min = Vector2.Round(position + (new Vector2(iconSize) - size) * 0.5f);
            drawList.AddImage(texture.Handle, min, min + size, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One));
        }
        else
        {
            // 无图标时退化为首字母色块
            drawList.AddRectFilled(
                position,
                position + new Vector2(iconSize),
                IconFallbackColor(plugin.InternalName),
                ScaleToolbar(4f));
            var letter = string.IsNullOrEmpty(plugin.Name) ? "?" : plugin.Name[..1].ToUpperInvariant();
            drawList.AddText(
                position + (new Vector2(iconSize) - ImGui.CalcTextSize(letter)) * 0.5f,
                ImGui.GetColorU32(OmniTheme.Tokens.Text),
                letter);
        }
    }

    private void DrawCommandPopup()
    {
        var headerPosition = ImGui.GetCursorScreenPos();
        var headerWidth = ImGui.GetContentRegionAvail().X;
        var buttonSize = OmniControls.CompactButtonSize(OmniLoc.Get("Feature.MultiToolbar.EditCommands"));
        ImGui.BeginGroup();
        ImGui.SetCursorScreenPos(headerPosition + new Vector2(0f, MathF.Max(0f, (buttonSize.Y - ImGui.GetTextLineHeight()) * 0.5f)));
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.CommandList"));
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(
            MathF.Max(ImGui.GetCursorScreenPos().X, headerPosition.X + headerWidth - buttonSize.X),
            headerPosition.Y));
        DrawCommandEditorButton();
        ImGui.EndGroup();
        ImGui.Spacing();
        List<MultiToolbarWidgetConfig> widgets = [..config.Commands.Where(command => command.Enabled)];
        foreach (var widget in config.Widgets)
        {
            if (widget.Enabled && widget.Type == MultiToolbarWidgetType.CustomButton &&
                !widgets.Any(command => string.Equals(command.Command, widget.Command, StringComparison.OrdinalIgnoreCase)))
            {
                widgets.Add(widget);
            }
        }

        if (widgets.Count == 0)
        {
            ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.NoCommands"));
            return;
        }

        var iconSize = MathF.Round(ScaleToolbar(config.RowIconSize));
        var rowHeight = MathF.Max(ImGui.GetFrameHeight(), iconSize + ScaleToolbar(4f));
        using var child = ImRaii.Child("##multiToolbarCommandList", new Vector2(0f, MathF.Max(1f, ImGui.GetContentRegionAvail().Y)));
        if (!child)
        {
            return;
        }

        for (var index = 0; index < widgets.Count; index++)
        {
            DrawCommandRow(widgets[index], index, iconSize, rowHeight);
        }
    }

    private void DrawCommandRow(MultiToolbarWidgetConfig widget, int index, float iconSize, float rowHeight)
    {
        var rowPos = ImGui.GetCursorScreenPos();
        var rowWidth = ImGui.GetContentRegionAvail().X;
        if (ImGui.InvisibleButton($"##multiToolbarCommandEntry{index}", new Vector2(rowWidth, rowHeight)))
        {
            ExecuteCommand(widget.Command);
        }

        var drawList = ImGui.GetWindowDrawList();
        if (ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(
                rowPos,
                rowPos + new Vector2(rowWidth, rowHeight),
                ImGui.GetColorU32(OmniTheme.HoverBackground),
                ScaleToolbar(OmniTheme.Tokens.ButtonRadius));
        }

        var iconPos = rowPos + new Vector2(ScaleToolbar(4f), (rowHeight - iconSize) * 0.5f);
        if (widget.ShowIcon && widget.GameIconID > 0 && IconBrowser.IsActionOrItemIcon(widget.GameIconID))
        {
            FramedGameIcon.Draw(drawList, widget.GameIconID, iconPos, new Vector2(iconSize));
        }
        else if (widget.ShowIcon && widget.GameIconID > 0 && ImageHelper.GetGameIcon(widget.GameIconID) is { } texture)
        {
            drawList.AddImage(texture.Handle, iconPos, iconPos + new Vector2(iconSize));
        }
        else if (widget.ShowIcon && !string.IsNullOrWhiteSpace(widget.IconFilePath) &&
                 DalamudServices.TextureProvider.GetFromFile(widget.IconFilePath).GetWrapOrDefault() is { } fileIcon)
        {
            var imageSize = new Vector2(fileIcon.Width, fileIcon.Height) * (iconSize / MathF.Max(fileIcon.Width, fileIcon.Height));
            var imagePosition = iconPos + (new Vector2(iconSize) - imageSize) * 0.5f;
            drawList.AddImage(fileIcon.Handle, imagePosition, imagePosition + imageSize);
        }
        else if (widget.ShowIcon && !string.IsNullOrWhiteSpace(widget.IconGlyph) &&
                 TryGetCommandGlyph(widget.IconGlyph, out var glyph))
        {
            var glyphText = glyph.ToIconString();
            drawList.AddText(iconPos + (new Vector2(iconSize) - ImGui.CalcTextSize(glyphText)) * 0.5f,
                ImGui.GetColorU32(ImGuiCol.Text), glyphText);
        }
        else if (widget.ShowIcon && widget.Command.Trim().Equals("/xlplugins", StringComparison.OrdinalIgnoreCase) &&
                 DalamudServices.TextureProvider.GetFromFile(System.IO.Path.Combine(
                     DalamudServices.PluginInterface.AssemblyLocation.DirectoryName!, "Resources", "XIVLauncherCN.png"))
                     .GetWrapOrDefault() is { } launcherIcon)
        {
            drawList.AddImage(launcherIcon.Handle, iconPos, iconPos + new Vector2(iconSize));
        }
        else if (widget.ShowIcon)
        {
            var iconText = FontAwesomeIcon.Terminal.ToIconString();
            drawList.AddText(
                iconPos + (new Vector2(iconSize) - ImGui.CalcTextSize(iconText)) * 0.5f,
                ImGui.GetColorU32(OmniTheme.Tokens.Text),
                iconText);
        }

        drawList.AddText(
            rowPos +
            new Vector2(
                ScaleToolbar(4f) + (widget.ShowIcon ? iconSize + ScaleToolbar(6f) : 0f),
                (rowHeight - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(ImGuiCol.Text),
            string.IsNullOrWhiteSpace(widget.Name) ? widget.Command : widget.Name);
    }

    private void DrawDtrPopup()
    {
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.DtrList"));
        ImGui.Spacing();
        if (collapsedDtrEntries.Count == 0)
        {
            ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.NoDtr"));
            return;
        }

        var rowHeight = MathF.Max(ImGui.GetFrameHeight(), ScaleToolbar(24f));
        var childHeight = Math.Min(
            ScaleToolbar(300f),
            collapsedDtrEntries.Count * rowHeight + Math.Max(0, collapsedDtrEntries.Count - 1) * ImGui.GetStyle().ItemSpacing.Y);
        using var child = ImRaii.Child("##multiToolbarDtrList", new Vector2(0f, childHeight));
        if (!child)
        {
            return;
        }

        for (var index = 0; index < collapsedDtrEntries.Count; index++)
        {
            var entry = collapsedDtrEntries[index];
            var rowPos = ImGui.GetCursorScreenPos();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            ImGui.InvisibleButton($"##multiToolbarDtrEntry{index}", new Vector2(rowWidth, rowHeight));
            var drawList = ImGui.GetWindowDrawList();
            if (ImGui.IsItemHovered())
            {
                drawList.AddRectFilled(
                    rowPos,
                    rowPos + new Vector2(rowWidth, rowHeight),
                    ImGui.GetColorU32(OmniTheme.HoverBackground),
                    ScaleToolbar(OmniTheme.Tokens.ButtonRadius));
            }

            var iconOffset = ScaleToolbar(4f);
            DrawDtrText(entry, rowPos + new Vector2(iconOffset, 0f), rowWidth - iconOffset * 2f);
            if (entry.HasClickAction && (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right)))
            {
                entry.OnClick?.Invoke(new DtrInteractionEvent
                {
                    ClickType = ImGui.IsItemClicked(ImGuiMouseButton.Right) ? MouseClickType.Right : MouseClickType.Left,
                    ModifierKeys = GetDtrModifierKeys(),
                    Position = ImGui.GetMousePos(),
                });
            }

            if (entry.Tooltip is { } tooltip && ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGuiHelpers.SeStringWrapped(
                    new ReadOnlySeString(tooltip.Encode()),
                    new SeStringDrawParams { WrapWidth = ScaleToolbar(420f) });
                ImGui.EndTooltip();
            }
        }
    }

    private void RefreshDtrEntries()
    {
        visibleDtrEntries.Clear();
        collapsedDtrEntries.Clear();
        RemoveLegacyDtrEntries();
        if (!config.Widgets.Any(widget => widget.Enabled && widget.Type == MultiToolbarWidgetType.DtrList))
        {
            return;
        }

        // 只隐藏原生栏，不改写其他插件拥有的条目可见状态。
        foreach (var entry in DService.Instance().DTRBar.Entries.OrderBy(entry =>
                     config.DtrOrder.IndexOf(entry.Title) is var order && order >= 0 ? order : int.MaxValue).Reverse())
        {
            if (!entry.Shown || entry.UserHidden || string.IsNullOrWhiteSpace(DtrText(entry)))
            {
                continue;
            }

            if (config.CollapsedDtrTitles.Any(title =>
                    string.Equals(title, entry.Title, StringComparison.OrdinalIgnoreCase)))
            {
                collapsedDtrEntries.Add(entry);
            }
            else
            {
                visibleDtrEntries.Add(entry);
            }
        }
    }

    private void RemoveLegacyDtrEntries()
    {
        if (legacyDtrCleanupDone)
        {
            return;
        }

        legacyDtrCleanupDone = true;
        foreach (var entry in DService.Instance().DTRBar.Entries)
        {
            if (!entry.Title.StartsWith(LegacyDtrPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry is IDtrBarEntry mutableEntry)
            {
                mutableEntry.Remove();
                continue;
            }

            try
            {
                entry.GetType().GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(entry, null);
            }
            catch (Exception ex)
            {
                DalamudServices.PluginLog.Debug(ex, "MultiToolbar could not remove legacy DTR entry {Title}.", entry.Title);
            }
        }
    }

    private static string DtrText(IReadOnlyDtrBarEntry entry) => entry.Text?.ToString() ?? string.Empty;


    private static ReadOnlySeString DtrSeString(IReadOnlyDtrBarEntry entry) =>
        new(entry.Text?.Encode() ?? []);

    private static Vector2 MeasureDtrText(IReadOnlyDtrBarEntry entry)
    {
        var drawParams = new SeStringDrawParams
        {
            // 显式使用空绘制列表：只测量 SeString，不能把测量结果绘制到当前窗口。
            TargetDrawList = default(ImDrawListPtr),
            ScreenOffset = Vector2.Zero,
            Font = ImGui.GetFont(),
            FontSize = ImGui.GetFontSize(),
            WrapWidth = float.PositiveInfinity
        };
        var renderedSize = ImGuiHelpers.SeStringWrapped(DtrSeString(entry), drawParams).Size;
        // TextValue 对带图标/颜色 payload 的 DTR 可能只返回局部文本，使用最终显示字符串兜底完整宽度。
        var plainSize = ImGui.CalcTextSize(DtrText(entry));
        return new(MathF.Max(renderedSize.X, plainSize.X), MathF.Max(renderedSize.Y, plainSize.Y));
    }

    private void DrawDtrText(IReadOnlyDtrBarEntry entry, Vector2 position, float width)
    {
        var drawParams = new SeStringDrawParams
        {
            TargetDrawList = ImGui.GetWindowDrawList(),
            ScreenOffset = position + new Vector2(0f, MathF.Max(0f, (ImGui.GetItemRectSize().Y - ImGui.GetTextLineHeight()) * 0.5f)),
            Font = ImGui.GetFont(),
            FontSize = ImGui.GetFontSize(),
            // 工具栏条目必须保持单行，禁止 SeString 渲染器按可用区域自动换行。
            WrapWidth = float.PositiveInfinity,
            Color = ImGui.GetColorU32(ImGuiCol.Text),
            Shadow = false,
            Edge = true,
            EdgeColor = ImGui.GetColorU32(ToolbarTextOutlineColor),
            EdgeStrength = ScaleToolbar(2f)
        };
        ImGuiHelpers.SeStringWrapped(DtrSeString(entry), drawParams);
    }

    private void RestoreDtrEntries()
    {
        visibleDtrEntries.Clear();
        collapsedDtrEntries.Clear();
    }

    private unsafe void UpdateNativeDtrVisibility(bool hide)
    {
        if (!AddonHelper.TryGetByName("_DTR", out AtkUnitBase* addon) || addon is null)
        {
            return;
        }

        if (hide)
        {
            nativeDtrVisibility ??= addon->IsVisible;
            addon->IsVisible = false;
            return;
        }

        if (nativeDtrVisibility is { } originalVisibility)
        {
            addon->IsVisible = originalVisibility;
            nativeDtrVisibility = null;
        }
    }

    private static ClickModifierKeys GetDtrModifierKeys()
    {
        var io = ImGui.GetIO();
        var modifiers = ClickModifierKeys.None;
        if (io.KeyCtrl)
        {
            modifiers |= ClickModifierKeys.Ctrl;
        }

        if (io.KeyAlt)
        {
            modifiers |= ClickModifierKeys.Alt;
        }

        if (io.KeyShift)
        {
            modifiers |= ClickModifierKeys.Shift;
        }

        return modifiers;
    }

    // 弹窗打开期间每秒刷新一次插件缓存，避免每帧枚举 InstalledPlugins
    private void RefreshPluginCache()
    {
        var now = ImGui.GetTime();
        if (pluginCacheValid && now < nextPluginCacheRefreshAt)
        {
            return;
        }

        nextPluginCacheRefreshAt = now + PluginCacheRefreshIntervalSeconds;
        pluginCacheValid = true;
        loadedPlugins.Clear();
        unloadedPlugins.Clear();
        try
        {
            foreach (var plugin in DalamudServices.PluginInterface.InstalledPlugins)
            {
                if (plugin.IsLoaded)
                {
                    if (plugin.HasMainUi || plugin.HasConfigUi)
                    {
                        loadedPlugins.Add(plugin);
                    }
                }
                else
                {
                    unloadedPlugins.Add(plugin);
                }
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to enumerate installed plugins.");
        }

        loadedPlugins.Sort(ComparePluginNames);
        unloadedPlugins.Sort(ComparePluginNames);
    }

    private List<IExposedPlugin> FilterPlugins(List<IExposedPlugin> source)
    {
        if (string.IsNullOrWhiteSpace(pluginSearchText))
        {
            return source;
        }

        var result = new List<IExposedPlugin>();
        foreach (var plugin in source)
        {
            if (plugin.Name?.Contains(pluginSearchText, StringComparison.OrdinalIgnoreCase) == true ||
                plugin.InternalName?.Contains(pluginSearchText, StringComparison.OrdinalIgnoreCase) == true)
            {
                result.Add(plugin);
            }
        }

        return result;
    }

    private static void OpenPlugin(IExposedPlugin plugin)
    {
        try
        {
            if ((ImGui.GetIO().KeyShift || ImGui.GetIO().KeyCtrl) && plugin.HasConfigUi)
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
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to open plugin {PluginName}.", plugin.InternalName);
        }
    }

    private static int ComparePluginNames(IExposedPlugin? left, IExposedPlugin? right) =>
        string.Compare(left?.Name, right?.Name, StringComparison.OrdinalIgnoreCase);

    private static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, Math.Clamp(alpha, 0f, 1f));

    private static uint WithAlpha(uint color, float alpha) =>
        (color & 0x00FFFFFFu) | ((uint)(Math.Clamp(alpha, 0f, 1f) * 255f) << 24);

    private static uint IconFallbackColor(string? name)
    {
        // ImGui.NET 当前版本未提供 out 参数重载，色相由插件名哈希决定，饱和度/亮度取固定值
        var hash = (uint)(name?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0);
        var hue = hash % 360 / 360f;
        var sector = (int)(hue * 6f) % 6;
        var fraction = hue * 6f - (int)(hue * 6f);
        const float saturation = 0.45f;
        const float value = 0.55f;
        var offset = saturation * fraction;
        var low = value * (1f - saturation);
        var (red, green, blue) = sector switch
        {
            0 => (value, offset + low, low),
            1 => (value - offset, value, low),
            2 => (low, value, offset + low),
            3 => (low, value - offset, value),
            4 => (offset + low, low, value),
            _ => (value, low, value - offset),
        };
        var fallback = Vector4.Lerp(
            OmniTheme.Tokens.Primary,
            new Vector4(red, green, blue, 1f),
            0.45f);
        return ImGui.GetColorU32(fallback);
    }

    private enum PopupKind
    {
        None,
        Plugins,
        Commands,
        Dtr,
        Widget
    }
}
