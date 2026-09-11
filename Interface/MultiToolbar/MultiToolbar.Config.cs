using System.Globalization;
using System.Linq;
using System.Reflection;
using Dalamud.Interface;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private string widgetSearchText = string.Empty;
    private static readonly MultiToolbarWidgetType[] SelectableWidgetTypes = Enum.GetValues<MultiToolbarWidgetType>()
        .Where(static type => type is not MultiToolbarWidgetType.ExperienceBar and not MultiToolbarWidgetType.SanctuaryIndicator)
        .OrderBy(static type => type switch
        {
            MultiToolbarWidgetType.ToolbarPin => (double)MultiToolbarWidgetType.CustomButton + 0.5,
            MultiToolbarWidgetType.DtrSingle => (double)MultiToolbarWidgetType.DtrList + 0.5,
            MultiToolbarWidgetType.MailIndicator => (double)MultiToolbarWidgetType.Flag + 0.5,
            MultiToolbarWidgetType.QuickCommands => (double)MultiToolbarWidgetType.Weather + 0.1,
            MultiToolbarWidgetType.Separator => (double)MultiToolbarWidgetType.Weather + 0.2,
            _ => (double)type
        })
        .ToArray();

    private bool DrawBarSettings(float rowHeight, float rowSpacing)
    {
        var changed = false;
        var origin = ImGui.GetCursorScreenPos();
        var rowStride = rowHeight + rowSpacing;
        var editWidgetsLabel = OmniLoc.Get("Feature.MultiToolbar.EditWidgets");
        var openWidgetEditor = OmniControls.SmallButton(editWidgetsLabel + "##multiToolbarWidgetSettings", false,
            OmniControls.CompactButtonSize(editWidgetsLabel));
        ImGui.SameLine();
        DrawPluginEditorButton();
        ImGui.SameLine();
        DrawCommandEditorButton();
        ImGui.SameLine();
        changed |= DrawColorSettingsButton();
        using var settingsPadding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding,
            new Vector2(ImGui.GetStyle().CellPadding.X, rowSpacing * 0.5f));
        ImGui.SetCursorScreenPos(origin + new Vector2(0f, rowStride - rowSpacing * 0.5f));
        var autoExpandOnHover = config.AutoExpandOnHover;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.MultiToolbar.AutoExpandOnHover"), ref autoExpandOnHover, rowHeight))
        {
            config.AutoExpandOnHover = autoExpandOnHover;
            changed = true;
        }
        if (barID == "main")
        {
            ImGui.SameLine();
            var hideNativeInfoBar = config.HideNativeInfoBar;
            if (OmniControls.Checkbox(OmniLoc.Get("Feature.MultiToolbar.HideNativeInfoBar"), ref hideNativeInfoBar, rowHeight))
            {
                config.HideNativeInfoBar = hideNativeInfoBar;
                changed = true;
            }
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0f, rowStride * 2f - rowSpacing * 0.5f));
        changed |= DrawGeneralSettings();

        if (openWidgetEditor)
        {
            ImGui.OpenPopup("##multiToolbarWidgetSettingsPopup");
        }

        var viewportSize = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(Vector2.Min(new Vector2(OmniTheme.Scale(1400f), OmniTheme.Scale(680f)), viewportSize), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(
            Vector2.Min(new Vector2(OmniTheme.Scale(1200f), OmniTheme.Scale(260f)), viewportSize),
            Vector2.Min(new Vector2(OmniTheme.Scale(1600f), OmniTheme.Scale(760f)), viewportSize));
        using (var popup = ImRaii.Popup("##multiToolbarWidgetSettingsPopup", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar))
        {
            if (popup)
            {
                DrawEditorBackground(OmniLoc.Get("Feature.MultiToolbar.EditWidgets"));
                using var childBackground = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
                using (var columns = ImRaii.Table("##multiToolbarEditorColumns", 4, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
                {
                    if (columns)
                    {
                        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.WidgetSideLeft"));
                        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.WidgetSideCenter"));
                        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.WidgetSideRight"));
                        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.ServerInfoBar"));
                        var editorOrder = GetWidgetEditorColumnOrder();
                        var headerMoveFrom = -1;
                        var headerMoveTo = -1;
                        var headerHeight = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
                        OmniControls.BeginTableHeaderRow(headerHeight);
                        for (var column = 0; column < 4; column++)
                        {
                            ImGui.TableNextColumn();
                            using var headerID = ImRaii.PushId($"header{column}");
                            var source = DrawReorderHandle("OMNI_TOOLBAR_COLUMN", column, headerHeight);
                            if (source >= 0)
                            {
                                headerMoveFrom = source;
                                headerMoveTo = column;
                            }
                            ImGui.SameLine();
                            ImGui.AlignTextToFramePadding();
                            ImGui.TextUnformatted(editorOrder[column] == 3
                                ? OmniLoc.Get("Feature.MultiToolbar.ServerInfoBar")
                                : OmniLoc.Get(WidgetEditorSideKey(GetWidgetEditorSide(editorOrder, column))));
                        }
                        ImGui.TableNextRow();
                        var columnHeight = MathF.Max(ImGui.GetFrameHeight(), ImGui.GetContentRegionAvail().Y - ImGui.GetStyle().CellPadding.Y * 2f);
                        for (var column = 0; column < 4; column++)
                        {
                            ImGui.TableNextColumn();
                            using var id = ImRaii.PushId(column);
                            using var child = ImRaii.Child("##multiToolbarEditorColumn", new Vector2(0f, columnHeight), true,
                                ImGuiWindowFlags.AlwaysUseWindowPadding);
                            if (!child)
                            {
                                continue;
                            }
                            if (editorOrder[column] == 3)
                            {
                                changed |= DrawDtrSelection();
                                DrawWidgetEditorDropArea(null);
                            }
                            else
                            {
                                var side = GetWidgetEditorSide(editorOrder, column);
                                changed |= DrawWidgetEditor(side);
                                DrawWidgetEditorDropArea(side);
                            }
                        }
                        changed |= ApplyWidgetEditorDrop();
                        changed |= SwapWidgetEditorColumns(editorOrder, headerMoveFrom, headerMoveTo);
                    }
                }
            }
        }

        return changed;
    }

    private bool DrawGeneralSettings()
    {
        var changed = false;
        var rowContentHeight = MathF.Max(OmniTheme.CheckboxSize(), MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight()));
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, MathF.Max(0f, (rowContentHeight - ImGui.GetTextLineHeight()) * 0.5f)));
        const ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX;
        using (var table = ImRaii.Table("##multiToolbarOffsets", 4, flags))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var offset = config.BarVerticalOffset;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.BarOffset"), "##multiToolbarBarOffset", ref offset,
                        0f, MathF.Max(0f, ImGui.GetMainViewport().WorkSize.Y / ScaleToolbar(1f) - 40f), "%.0f"))
                {
                    config.BarVerticalOffset = offset;
                    changed = true;
                }
                var maximum = MathF.Max(0f, ImGui.GetMainViewport().WorkSize.X / ScaleToolbar(1f) - 40f);
                ImGui.TableNextColumn();
                var horizontalOffset = config.BarHorizontalOffset;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.BarHorizontalOffset"), "##multiToolbarBarHorizontalOffset", ref horizontalOffset,
                        0f, maximum, "%.0f"))
                {
                    config.BarHorizontalOffset = horizontalOffset;
                    changed = true;
                }
                ImGui.TableNextColumn();
                var leftMargin = config.BarLeftMargin;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.BarLeftMargin"), "##multiToolbarBarLeftMargin", ref leftMargin,
                        0f, maximum, "%.0f"))
                {
                    config.BarLeftMargin = leftMargin;
                    changed = true;
                }
                ImGui.TableNextColumn();
                var rightMargin = config.BarRightMargin;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.BarRightMargin"), "##multiToolbarBarRightMargin", ref rightMargin,
                        0f, maximum, "%.0f"))
                {
                    config.BarRightMargin = rightMargin;
                    changed = true;
                }
            }
        }

        using (var table = ImRaii.Table("##multiToolbarOpacity", 3, flags))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var barOpacity = config.BarBackgroundOpacity;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.BarOpacity"), "##multiToolbarBarOpacity", ref barOpacity, 0f, 1f, "%.2f"))
                {
                    config.BarBackgroundOpacity = barOpacity;
                    changed = true;
                }
                ImGui.TableNextColumn();
                var buttonOpacity = config.ButtonBackgroundOpacity;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.ButtonOpacity"), "##multiToolbarButtonOpacity", ref buttonOpacity, 0f, 1f, "%.2f"))
                {
                    config.ButtonBackgroundOpacity = buttonOpacity;
                    changed = true;
                }
                ImGui.TableNextColumn();
                var iconSize = config.RowIconSize;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.RowIconSize"), "##multiToolbarRowIconSize", ref iconSize, 8f, 72f, "%.0f"))
                {
                    config.RowIconSize = iconSize;
                    changed = true;
                }
            }
        }
        using (var table = ImRaii.Table("##multiToolbarLayout", 4, flags))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var componentSpacing = config.ComponentSpacing;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.ComponentSpacing"), "##multiToolbarComponentSpacing", ref componentSpacing, 0f, 40f, "%.0f"))
                {
                    config.ComponentSpacing = componentSpacing;
                    changed = true;
                }
                ImGui.TableNextColumn();
                var buttonPadding = config.ButtonPadding;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.ButtonPadding"), "##multiToolbarButtonPadding", ref buttonPadding, 0f, 40f, "%.0f"))
                {
                    config.ButtonPadding = buttonPadding;
                    changed = true;
                }
                ImGui.TableNextColumn();
                var buttonRadius = config.ButtonCornerRadius;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.ButtonCornerRadius"), "##multiToolbarButtonCornerRadius", ref buttonRadius, 0f, 20f, "%.0f"))
                {
                    config.ButtonCornerRadius = buttonRadius;
                    changed = true;
                }
                ImGui.TableNextColumn();
                var scale = config.ToolbarScale;
                if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.ToolbarScale"), "##multiToolbarScale", ref scale, 0.1f, 3f, "%.2f"))
                {
                    config.ToolbarScale = scale;
                    changed = true;
                }
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.Alignment"));
                ImGui.SameLine();
                var alignment = (int)config.Alignment;
                string[] alignmentLabels = [OmniLoc.Get("Feature.MultiToolbar.AlignmentTop"), OmniLoc.Get("Feature.MultiToolbar.AlignmentBottom")];
                var alignmentLabel = alignmentLabels[Math.Clamp(alignment, 0, alignmentLabels.Length - 1)];
                if (OmniControls.BeginCombo("##multiToolbarAlignment", alignmentLabel, ImGui.GetContentRegionAvail().X))
                {
                    for (var index = 0; index < alignmentLabels.Length; index++)
                    {
                        if (ImGui.Selectable(alignmentLabels[index], index == alignment))
                        {
                            config.Alignment = (MultiToolbarAlignment)index;
                            changed = true;
                        }
                    }
                    ImGui.EndCombo();
                }
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.Font"));
                ImGui.SameLine();
                changed |= DrawToolbarFontSelector();
            }
        }
        return changed;
    }

    private bool DrawDtrSelection()
    {
        var changed = false;
        ImGui.Spacing();
        OmniControls.SectionLabel(OmniLoc.Get("Feature.MultiToolbar.DtrEntries"));
        var entries = DService.Instance().DTRBar.Entries;
        foreach (var entry in entries)
        {
            if (!config.DtrOrder.Contains(entry.Title))
            {
                config.DtrOrder.Add(entry.Title);
                changed = true;
            }
        }
        var moveFrom = -1;
        var moveTo = -1;
        foreach (var entry in entries.OrderBy(entry => config.DtrOrder.IndexOf(entry.Title)).Reverse())
        {
            if (config.Widgets.Any(widget => widget.Type == MultiToolbarWidgetType.DtrSingle && widget.DtrTitle == entry.Title))
            {
                continue;
            }
            var index = config.DtrOrder.IndexOf(entry.Title);
            using var id = ImRaii.PushId(index);
            var source = DrawReorderHandle("OMNI_TOOLBAR_DTR", index, ImGui.GetFrameHeight());
            if (source >= 0)
            {
                moveFrom = source;
                moveTo = index;
            }
            AcceptReturnedDtrDrop(index);
            ImGui.SameLine();
            var selected = config.CollapsedDtrTitles.Contains(entry.Title);
            if (OmniControls.Checkbox($"{entry.Title}##multiToolbarDtr_{entry.Title}", ref selected))
            {
                if (selected)
                {
                    config.CollapsedDtrTitles.Add(entry.Title);
                }
                else
                {
                    config.CollapsedDtrTitles.Remove(entry.Title);
                }

                changed = true;
            }
        }

        changed |= MoveEntry(config.DtrOrder, moveFrom, moveTo);
        return changed;
    }

    private static bool DrawFloatSlider(string label, string id, ref float value, float minimum, float maximum, string format)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X);
        ImGui.SetNextItemWidth(width);
        return OmniControls.SliderFloat(id, ref value, minimum, maximum, format, width);
    }

    private bool DrawWidgetEditor(MultiToolbarWidgetSide columnSide)
    {
        var changed = false;
        var removeIndex = -1;
        var rowContentHeight = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
        using var padding = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, MathF.Max(0f, (rowContentHeight - ImGui.GetTextLineHeight()) * 0.5f)));
        var sideLabels = (string[])[
            OmniLoc.Get("Feature.MultiToolbar.WidgetSideLeft"),
            OmniLoc.Get("Feature.MultiToolbar.WidgetSideCenter"),
            OmniLoc.Get("Feature.MultiToolbar.WidgetSideRight"),
        ];
        using (var table = ImRaii.Table(
                   "##multiToolbarWidgetEditor",
                   5,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##multiToolbarWidgetColDrag", ImGuiTableColumnFlags.WidthFixed, rowContentHeight);
                ImGui.TableSetupColumn("##multiToolbarWidgetColEnabled", ImGuiTableColumnFlags.WidthFixed, rowContentHeight);
                ImGui.TableSetupColumn("##multiToolbarWidgetColType", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##multiToolbarWidgetColSettings", ImGuiTableColumnFlags.WidthFixed, rowContentHeight);
                ImGui.TableSetupColumn("##multiToolbarWidgetColRemove", ImGuiTableColumnFlags.WidthFixed, rowContentHeight);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                for (var column = 0; column < 2; column++)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(OmniLoc.Get(
                        column switch
                        {
                            0 => "Feature.MultiToolbar.WidgetEnabled",
                            _ => "Feature.MultiToolbar.WidgetType"
                        }));
                }

                for (var index = 0; index < config.Widgets.Count; index++)
                {
                    var widget = config.Widgets[index];
                    if (widget.Side != columnSide)
                    {
                        continue;
                    }
                    using var widgetID = ImRaii.PushId(index);
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
                    ImGui.TableNextColumn();
                    var source = DrawReorderHandle("OMNI_TOOLBAR_WIDGET", index, rowContentHeight);
                    if (source >= 0)
                    {
                        pendingWidgetDrop = (source, columnSide, index, false);
                    }
                    AcceptDtrWidgetDrop(columnSide, index);
                    ImGui.TableNextColumn();
                    var enabled = widget.Enabled;
                    if (OmniControls.Checkbox($"##multiToolbarWidgetEnabled{index}", ref enabled, rowContentHeight))
                    {
                        widget.Enabled = enabled;
                        changed = true;
                    }

                    ImGui.TableNextColumn();
                    var previewPosition = ImGui.GetCursorScreenPos() + ImGui.GetStyle().FramePadding;
                    var previewWidth = ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.X * 2f;
                    ImGui.SetNextItemWidth(-1f);
                    ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue,
                        10f * ImGui.GetTextLineHeight() + 9f * ImGui.GetStyle().ItemSpacing.Y + 2f * ImGui.GetStyle().WindowPadding.Y));
                    using (var combo = ImRaii.Combo($"##multiToolbarWidgetType{index}", string.Empty))
                    {
                        if (combo)
                        {
                            var type = widget.Type;
                            if (DrawWidgetTypeOptions(ref type, string.Empty, widget))
                            {
                                widget.Type = type;
                                changed = true;
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }
                    DrawWidgetOption(widget, previewPosition, previewWidth);

                    ImGui.TableNextColumn();
                    if (OmniControls.IconButton(
                            $"multiToolbarWidgetSettings{index}",
                            FontAwesomeIcon.Cog,
                            false,
                            new Vector2(rowContentHeight),
                            OmniLoc.Get("Feature.MultiToolbar.EditWidgets")))
                    {
                        ImGui.OpenPopup($"##multiToolbarWidgetDetail{index}");
                    }

                    if (widget.Type == MultiToolbarWidgetType.DynamicMenu)
                    {
                        ImGui.SetNextWindowSize(
                            Vector2.Min(OmniTheme.Scale(new Vector2(640f)), ImGui.GetMainViewport().WorkSize),
                            ImGuiCond.Always);
                    }
                    ImGui.SetNextWindowSizeConstraints(
                        new Vector2(OmniTheme.Scale(360f), 0f),
                        ImGui.GetMainViewport().WorkSize);
                    using (var detail = ImRaii.Popup($"##multiToolbarWidgetDetail{index}", ImGuiWindowFlags.NoBackground))
                    {
                        if (detail)
                        {
                            DrawEditorBackground(WidgetTypeLabel(widget.Type));
                            ImGui.AlignTextToFramePadding();
                            ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.WidgetPosition"));
                            ImGui.SameLine();
                            var side = (int)widget.Side;
                            ImGui.SetNextItemWidth(-1f);
                            if (ImGui.Combo($"##multiToolbarWidgetSide{index}", ref side, sideLabels, sideLabels.Length))
                            {
                                widget.Side = (MultiToolbarWidgetSide)side;
                                changed = true;
                                ImGui.CloseCurrentPopup();
                            }
                            if (widget.Type == MultiToolbarWidgetType.DtrSingle)
                            {
                                changed |= DrawSingleDtrSettings(widget);
                            }
                            ImGui.AlignTextToFramePadding();
                            ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.CommandName"));
                            ImGui.SameLine();
                            var name = widget.DisplayName ?? WidgetLabel(widget, index);
                            ImGui.SetNextItemWidth(-1f);
                            if (ImGui.InputText($"##multiToolbarWidgetName{index}", ref name, 128))
                            {
                                widget.DisplayName = name;
                                changed = true;
                                if (widget.Type == MultiToolbarWidgetType.CustomButton)
                                {
                                    widget.Name = name;
                                }
                            }
                            if (widget.Type == MultiToolbarWidgetType.DynamicMenu)
                            {
                                changed |= DrawWidgetIconEditor(widget, index);
                                changed |= DrawDynamicMenuEditor(widget);
                            }
                            else
                            {
                                if (widget.Type == MultiToolbarWidgetType.CustomButton)
                                {
                                    for (var button = 0; button < 2; button++)
                                    {
                                        ImGui.TextUnformatted(OmniLoc.Get(button == 0
                                            ? "Feature.MultiToolbar.LeftCommand" : "Feature.MultiToolbar.RightCommand"));
                                        var command = button == 0 ? widget.Command : widget.RightCommand;
                                        ImGui.SetNextItemWidth(-1f);
                                        var edited = ImGui.InputText($"##multiToolbarWidgetCommand{index}_{button}", ref command, 256);
                                        var committed = ImGui.IsItemDeactivatedAfterEdit();
                                        if (committed && TryNormalizeCommand(command, out var normalized))
                                        {
                                            command = normalized;
                                        }
                                        if (edited || committed)
                                        {
                                            if (button == 0)
                                            {
                                                widget.Command = command;
                                            }
                                            else
                                            {
                                                widget.RightCommand = command;
                                            }
                                        }
                                        changed |= committed;
                                    }
                                }

                                changed |= DrawWidgetIconEditor(widget, index);
                            }
                        }
                    }

                    ImGui.TableNextColumn();
                    if (OmniControls.IconButton(
                            $"multiToolbarWidgetRemove{index}",
                            FontAwesomeIcon.Trash,
                            false,
                            new Vector2(rowContentHeight),
                            OmniLoc.Get("Feature.MultiToolbar.RemoveWidget")))
                    {
                        removeIndex = index;
                    }
                }
            }
        }

        if (removeIndex >= 0)
        {
            config.Widgets.RemoveAt(removeIndex);
            changed = true;
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        var addLabel = OmniLoc.Get("Feature.MultiToolbar.AddWidget");
        if (OmniControls.SmallButton(
                $"{addLabel}##multiToolbarWidgetAdd",
                false,
                OmniControls.CompactButtonSize(addLabel)))
        {
            ImGui.OpenPopup("##multiToolbarAddWidget");
        }

        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue,
            11f * ImGui.GetTextLineHeight() + ImGui.GetFrameHeight() + 13f * ImGui.GetStyle().ItemSpacing.Y + 2f * ImGui.GetStyle().WindowPadding.Y));
        using (var popup = ImRaii.Popup("##multiToolbarAddWidget"))
        {
            if (popup)
            {
                ImGui.TextUnformatted(addLabel);
                ImGui.SetNextItemWidth(MathF.Max(1f, OmniTheme.Scale(220f)));
                ImGui.InputTextWithHint(
                    "##multiToolbarWidgetSearch",
                    OmniLoc.Get("Feature.MultiToolbar.SearchWidget"),
                    ref widgetSearchText,
                    64);
                ImGui.Separator();
                var type = MultiToolbarWidgetType.CustomButton;
                if (DrawWidgetTypeOptions(ref type, widgetSearchText))
                {
                    var widget = CreateDefaultWidget(type);
                    widget.Side = columnSide;
                    config.Widgets.Add(widget);
                    changed = true;
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        return changed;
    }

    private void DrawWidgetOption(MultiToolbarWidgetConfig widget, Vector2 position, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var size = ImGui.GetTextLineHeight();
        drawList.PushClipRect(position, position + new Vector2(MathF.Max(0f, width), size), true);
        var showIcon = widget.Type != MultiToolbarWidgetType.CustomButton && ShouldShowWidgetIcon(widget);
        var gameIconID = showIcon ? widget.GameIconID > 0 ? widget.GameIconID : WidgetGameIcon(widget.Type) : 0;
        var icon = showIcon ? WidgetIcon(widget.Type) : null;
        if (gameIconID > 0 && ImageHelper.GetGameIcon(gameIconID) is { } texture)
        {
            drawList.AddImage(texture.Handle, position, position + new Vector2(size),
                gameIconID == 60560 ? new Vector2(0.25f) : Vector2.Zero,
                gameIconID == 60560 ? new Vector2(0.75f) : Vector2.One);
        }
        else if (icon is { } fontIcon)
        {
            using var iconFont = ImRaii.PushFont(UiBuilder.IconFont);
            drawList.AddText(ImGui.GetFont(), size, position, ImGui.GetColorU32(ImGuiCol.Text), fontIcon.ToIconString());
        }

        if (gameIconID > 0 || icon is not null)
        {
            var offset = size + ImGui.GetStyle().ItemSpacing.X;
            position.X += offset;
            width -= offset;
        }
        drawList.AddText(position, ImGui.GetColorU32(ImGuiCol.Text), EllipsizeToolbarText(WidgetTypeLabel(widget.Type), width));
        drawList.PopClipRect();
    }

    private bool DrawWidgetTypeOptions(ref MultiToolbarWidgetType selected, string filter, MultiToolbarWidgetConfig? editing = null)
    {
        var types = SelectableWidgetTypes.Where(type => string.IsNullOrWhiteSpace(filter) ||
            WidgetTypeLabel(type).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        var rowHeight = ImGui.GetTextLineHeight();
        foreach (var type in types)
        {
            var widget = editing?.Type == type ? editing : config.Widgets.FirstOrDefault(item => item.Type == type) ?? CreateDefaultWidget(type);
            var position = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var clicked = ImGui.Selectable($"##option{type}", selected == type,
                ImGuiSelectableFlags.None, new Vector2(0f, rowHeight));
            DrawWidgetOption(widget, position, width);
            if (clicked)
            {
                selected = type;
                return true;
            }
        }

        return false;
    }

    private bool DrawWidgetIconEditor(MultiToolbarWidgetConfig widget, int index)
    {
        var changed = false;
        var showIcon = widget.ShowIcon;
        if (OmniControls.Checkbox($"##multiToolbarWidgetShowIcon{index}", ref showIcon))
        {
            widget.ShowIcon = showIcon;
            changed = true;
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.CommandIcon"));
        ImGui.SameLine();
        var iconText = widget.GameIconID.ToString(CultureInfo.InvariantCulture);
        ImGui.SetNextItemWidth(MathF.Max(1f, ImGui.GetContentRegionAvail().X - OmniTheme.CheckboxSize() - ImGui.GetStyle().ItemSpacing.X));
        if (ImGui.InputText($"##multiToolbarWidgetIcon{index}", ref iconText, 16) &&
            uint.TryParse(iconText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iconID))
        {
            widget.GameIconID = iconID;
        }
        changed |= ImGui.IsItemDeactivatedAfterEdit();

        ImGui.SameLine();
        if (OmniControls.IconButton(
                $"iconBrowser{index}",
                FontAwesomeIcon.Image,
                false,
                OmniLoc.Get("Feature.MultiToolbar.IconBrowser")))
        {
            iconBrowser.OpenForSelection(
                false,
                value =>
                {
                    widget.GameIconID = value;
                    saveConfig();
                },
                null);
        }

        return changed;
    }

    private static MultiToolbarWidgetConfig CreateDefaultWidget(MultiToolbarWidgetType type) => new()
    {
        Type = type,
        Side = type is MultiToolbarWidgetType.DtrList or MultiToolbarWidgetType.DtrSingle or MultiToolbarWidgetType.Volume or MultiToolbarWidgetType.MailIndicator or MultiToolbarWidgetType.MarkerControl or MultiToolbarWidgetType.WalkingIndicator or MultiToolbarWidgetType.StackedClock or MultiToolbarWidgetType.ToolbarPin
            ? MultiToolbarWidgetSide.Right
            : type is MultiToolbarWidgetType.PluginList or MultiToolbarWidgetType.CommandList or MultiToolbarWidgetType.QuickCommands
                ? MultiToolbarWidgetSide.Center
                : MultiToolbarWidgetSide.Left,
        Name = type == MultiToolbarWidgetType.CustomButton ? OmniLoc.Get("Feature.MultiToolbar.NewCommand") : string.Empty,
        Command = type == MultiToolbarWidgetType.CustomButton ? "/" : MultiToolbarWidgetCommand(type),
        ShowIcon = DefaultWidgetShowIcon(type),
        DisplayName = type is MultiToolbarWidgetType.BattleEffects or MultiToolbarWidgetType.Societies or MultiToolbarWidgetType.OnlineStatus ? "" : null,
        GameIconID = type == MultiToolbarWidgetType.BattleEffects ? 516u : type == MultiToolbarWidgetType.QuickCommands ? 14u : 0u,
    };

    private static bool DefaultWidgetShowIcon(MultiToolbarWidgetType type) => type is not
        (MultiToolbarWidgetType.PluginList or MultiToolbarWidgetType.CommandList or MultiToolbarWidgetType.DtrList or MultiToolbarWidgetType.DtrSingle or MultiToolbarWidgetType.StackedClock or MultiToolbarWidgetType.Separator or MultiToolbarWidgetType.DynamicMenu);

    private static string MultiToolbarWidgetCommand(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.GearsetSwitcher => "/character",
        MultiToolbarWidgetType.Currencies => "/currency",
        MultiToolbarWidgetType.RetainerList => "/omni 一键打开雇员铃",
        MultiToolbarWidgetType.BattleEffects => "/battleeffects",
        _ => string.Empty,
    };

    private static string WidgetTypeLabel(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.PluginList => OmniLoc.Get("Feature.MultiToolbar.WidgetPluginList"),
        MultiToolbarWidgetType.CommandList => OmniLoc.Get("Feature.MultiToolbar.WidgetCommandList"),
        MultiToolbarWidgetType.DtrList => OmniLoc.Get("Feature.MultiToolbar.WidgetDtrList"),
        MultiToolbarWidgetType.DtrSingle => OmniLoc.Get("Feature.MultiToolbar.WidgetDtrSingle"),
        MultiToolbarWidgetType.BattleEffects => OmniLoc.Get("Feature.MultiToolbar.WidgetBattleEffects"),
        MultiToolbarWidgetType.Societies => OmniLoc.Get("Feature.MultiToolbar.WidgetSocieties"),
        MultiToolbarWidgetType.OnlineStatus => OmniLoc.Get("Feature.MultiToolbar.WidgetOnlineStatus"),
        MultiToolbarWidgetType.GearsetSwitcher => OmniLoc.Get("Feature.MultiToolbar.WidgetGearsetSwitcher"),
        MultiToolbarWidgetType.Durability => OmniLoc.Get("Feature.MultiToolbar.WidgetDurability"),
        MultiToolbarWidgetType.RetainerList => OmniLoc.Get("Feature.MultiToolbar.WidgetRetainerList"),
        MultiToolbarWidgetType.Currencies => OmniLoc.Get("Feature.MultiToolbar.WidgetCurrencies"),
        MultiToolbarWidgetType.Flag => OmniLoc.Get("Feature.MultiToolbar.WidgetFlag"),
        MultiToolbarWidgetType.Volume => OmniLoc.Get("Feature.MultiToolbar.WidgetVolume"),
        MultiToolbarWidgetType.MailIndicator => OmniLoc.Get("Feature.MultiToolbar.WidgetMailIndicator"),
        MultiToolbarWidgetType.MarkerControl => OmniLoc.Get("Feature.MultiToolbar.WidgetMarkerControl"),
        MultiToolbarWidgetType.WalkingIndicator => OmniLoc.Get("Feature.MultiToolbar.WidgetWalkingIndicator"),
        MultiToolbarWidgetType.StackedClock => OmniLoc.Get("Feature.MultiToolbar.WidgetStackedClock"),
        MultiToolbarWidgetType.ToolbarPin => OmniLoc.Get("Feature.MultiToolbar.WidgetToolbarPin"),
        MultiToolbarWidgetType.CustomButton => OmniLoc.Get("Feature.MultiToolbar.WidgetCustomButton"),
        MultiToolbarWidgetType.QuickCommands => OmniLoc.Get("Feature.MultiToolbar.WidgetQuickCommands"),
        MultiToolbarWidgetType.Separator => OmniLoc.Get("Feature.MultiToolbar.WidgetSeparator"),
        _ => OmniLoc.Get($"Feature.MultiToolbar.Widget{type}")
    };

    private static string WidgetTypeDescription(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.PluginList => OmniLoc.Get("Feature.MultiToolbar.WidgetPluginListHelp"),
        MultiToolbarWidgetType.CommandList => OmniLoc.Get("Feature.MultiToolbar.WidgetCommandListHelp"),
        MultiToolbarWidgetType.DtrList => OmniLoc.Get("Feature.MultiToolbar.WidgetDtrListHelp"),
        MultiToolbarWidgetType.DtrSingle => OmniLoc.Get("Feature.MultiToolbar.WidgetDtrSingleHelp"),
        MultiToolbarWidgetType.BattleEffects => OmniLoc.Get("Feature.MultiToolbar.WidgetBattleEffectsHelp"),
        MultiToolbarWidgetType.Societies => OmniLoc.Get("Feature.MultiToolbar.WidgetSocietiesHelp"),
        MultiToolbarWidgetType.OnlineStatus => OmniLoc.Get("Feature.MultiToolbar.WidgetOnlineStatusHelp"),
        MultiToolbarWidgetType.GearsetSwitcher => OmniLoc.Get("Feature.MultiToolbar.WidgetGearsetSwitcherHelp"),
        MultiToolbarWidgetType.Durability => OmniLoc.Get("Feature.MultiToolbar.WidgetDurabilityHelp"),
        MultiToolbarWidgetType.RetainerList => OmniLoc.Get("Feature.MultiToolbar.WidgetRetainerListHelp"),
        MultiToolbarWidgetType.Currencies => OmniLoc.Get("Feature.MultiToolbar.WidgetCurrenciesHelp"),
        MultiToolbarWidgetType.Flag => OmniLoc.Get("Feature.MultiToolbar.WidgetFlagHelp"),
        MultiToolbarWidgetType.Volume => OmniLoc.Get("Feature.MultiToolbar.WidgetVolumeHelp"),
        MultiToolbarWidgetType.MailIndicator => OmniLoc.Get("Feature.MultiToolbar.WidgetMailIndicatorHelp"),
        MultiToolbarWidgetType.MarkerControl => OmniLoc.Get("Feature.MultiToolbar.WidgetMarkerControlHelp"),
        MultiToolbarWidgetType.WalkingIndicator => OmniLoc.Get("Feature.MultiToolbar.WidgetWalkingIndicatorHelp"),
        MultiToolbarWidgetType.StackedClock => OmniLoc.Get("Feature.MultiToolbar.WidgetStackedClockHelp"),
        MultiToolbarWidgetType.ToolbarPin => OmniLoc.Get("Feature.MultiToolbar.WidgetToolbarPinHelp"),
        MultiToolbarWidgetType.CustomButton => OmniLoc.Get("Feature.MultiToolbar.WidgetCustomButtonHelp"),
        MultiToolbarWidgetType.QuickCommands => OmniLoc.Get("Feature.MultiToolbar.WidgetQuickCommandsHelp"),
        MultiToolbarWidgetType.Separator => OmniLoc.Get("Feature.MultiToolbar.WidgetSeparatorHelp"),
        MultiToolbarWidgetType.DynamicMenu => OmniLoc.Get("Feature.MultiToolbar.WidgetDynamicMenuHelp"),
        _ => WidgetTypeLabel(type)
    };
}

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public enum MultiToolbarWidgetSide
{
    Left,
    Center,
    Right,
}

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public enum MultiToolbarWidgetType
{
    PluginList,
    CommandList,
    CustomButton,
    DtrList,
    BattleEffects,
    Societies,
    OnlineStatus,
    GearsetSwitcher,
    Durability,
    RetainerList,
    Currencies,
    Flag,
    Volume,
    MailIndicator,
    MarkerControl,
    WalkingIndicator,
    StackedClock,
    ToolbarPin,
    Clock,
    FpsCounter,
    Coordinates,
    Location,
    WorldName,
    InventorySpace,
    ExperienceBar,
    Weather,
    SanctuaryIndicator,
    DtrSingle,
    QuickCommands,
    Separator,
    DynamicMenu,
}

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public enum MultiToolbarAlignment
{
    Top,
    Bottom,
}

[Serializable]
[Obfuscation(Exclude = true, ApplyToMembers = true)]
public sealed class MultiToolbarWidgetConfig
{
    public MultiToolbarWidgetSide Side { get; set; } = MultiToolbarWidgetSide.Left;

    public MultiToolbarWidgetType Type { get; set; } = MultiToolbarWidgetType.CustomButton;

    public bool Enabled { get; set; } = true;

    public bool ShowIcon { get; set; } = true;

    // null 保留组件动态名称，空字符串仅显示图标。
    public string? DisplayName { get; set; }

    public string Name { get; set; } = string.Empty;
    public string DtrTitle { get; set; } = string.Empty;
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<MultiToolbarMenuEntry> MenuEntries { get; set; } = [];

    public string Command { get; set; } = string.Empty;
    public string RightCommand { get; set; } = string.Empty;

    public uint GameIconID { get; set; }
    public string IconFilePath { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = string.Empty;
}

[Serializable]
[Obfuscation(Exclude = true, ApplyToMembers = true)]
public sealed class MultiToolbarConfig : MultiToolbarBarConfig
{
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<MultiToolbarAuxiliaryBarConfig> AuxiliaryBars { get; set; } = [];
}

[Serializable]
[Obfuscation(Exclude = true, ApplyToMembers = true)]
public sealed class MultiToolbarAuxiliaryBarConfig : MultiToolbarBarConfig
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
}

[Serializable]
[Obfuscation(Exclude = true, ApplyToMembers = true)]
public class MultiToolbarBarConfig
{

    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<MultiToolbarWidgetConfig> Commands { get; set; } =
    [
        new() { Type = MultiToolbarWidgetType.CustomButton, Name = "卫月插件", Command = "/xlplugins" },
        new() { Type = MultiToolbarWidgetType.CustomButton, Name = "虚空市场板", Command = "/omni 虚空市场板", GameIconID = 60570 },
    ];

    public bool TextEntryDefaultsApplied { get; set; }
    public bool MultiDockMigrated { get; set; }
    public bool MultiDockPluginsMigrated { get; set; }
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<string> SelectedPlugins { get; set; } = [];
    public HashSet<string> HiddenWindows { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<MultiToolbarWidgetConfig> Widgets { get; set; } =
    [
        new() { Side = MultiToolbarWidgetSide.Center, Type = MultiToolbarWidgetType.Location },
        new() { Side = MultiToolbarWidgetSide.Center, Type = MultiToolbarWidgetType.PluginList, ShowIcon = false },
        new() { Side = MultiToolbarWidgetSide.Center, Type = MultiToolbarWidgetType.CommandList, ShowIcon = false },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.BattleEffects, Command = "/battleeffects", DisplayName = "", GameIconID = 516 },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.Societies, DisplayName = "" },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.OnlineStatus, DisplayName = "" },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.GearsetSwitcher, Command = "/character" },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.Durability },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.RetainerList, Command = "/omni 一键打开雇员铃" },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.Currencies, Command = "/currency" },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.WalkingIndicator },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.Flag },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.MailIndicator },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.DtrList },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.Volume },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.MarkerControl },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.StackedClock, ShowIcon = false },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.ToolbarPin },
    ];

    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<int> WidgetEditorColumnOrder { get; set; } = [0, 1, 2, 3];

    public MultiToolbarAlignment Alignment { get; set; } = MultiToolbarAlignment.Top;

    public bool AutoExpandOnHover { get; set; } = true;

    // 仅主工具栏读取；使用服务器信息栏组件时隐藏原生 _DTR 信息栏。
    public bool HideNativeInfoBar { get; set; } = true;

    public float BarVerticalOffset { get; set; } = 0f;

    public float BarHorizontalOffset { get; set; } = 0f;

    public float BarLeftMargin { get; set; } = 0f;

    public float BarRightMargin { get; set; } = 0f;

    // 工具栏自身的尺寸倍率，叠加 Omni 全局主题缩放。
    public float ToolbarScale { get; set; } = 0.75f;
    public string FontFileName { get; set; } = string.Empty;

    public float ComponentSpacing { get; set; } = 6f;

    public float ButtonPadding { get; set; } = 2f;

    public float ButtonCornerRadius { get; set; } = 10f;

    public bool IsLocked { get; set; } = true;

    public float BarBackgroundOpacity { get; set; }

    public float ButtonBackgroundOpacity { get; set; }

    public float RowIconSize { get; set; } = 40f;

    public uint TrackedSocietyID { get; set; }

    public uint TrackedCurrencyID { get; set; } = 1;

    public bool ShowWorldMarkerOverlay { get; set; } = true;

    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public HashSet<MultiToolbarWorldMarkerType> EnabledWorldMarkers { get; set; } = [..Enum.GetValues<MultiToolbarWorldMarkerType>()];

    public List<MultiToolbarVolumePreset> VolumePresets { get; set; } = [];

    public int ActiveVolumePreset { get; set; } = -1;

    public int ClockTimeSource { get; set; }



    public Vector4? BackgroundColor { get; set; }

    public Vector4? TextColor { get; set; }

    public Vector4 ToolbarTextColor { get; set; } = Vector4.One;

    public Vector4? AccentColor { get; set; }

    public Vector4? BorderColor { get; set; }

    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<string> CollapsedDtrTitles { get; set; } =
    [
        "OmniToolbox.Toolbar.HiddenWindows", "DailyRoutines-GameTimeLeft"
    ];
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<string> DtrOrder { get; set; } =
    [
        "YesAlready", "DailyRoutines-AutoCountPlayers", "DailyRoutines-FastInstanceZoneChange",
        "DailyRoutines-BetterFPSLimitation", "DailyRoutines-GameTimeLeft", "LazyLoot",
        "DailyRoutines-FastWorldTravel", "vnavmesh", "DailyRoutines-AutoDisplayNetworkLatency",
        "bmr-autorotation", "bmr-ai", "DailyRoutines-AutoDisplayMitigationInfo",
        "AEAssist-CombatRoutine", "AEAssist-TargetSelector", "LifestreamInstance",
        "OmniToolbox.Toolbar.HiddenWindows"
    ];
}
