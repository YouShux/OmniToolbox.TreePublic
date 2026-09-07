using System.Globalization;
using System.Linq;
using Dalamud.Interface;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.OmenService;

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
        using var settingsPadding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding,
            new Vector2(ImGui.GetStyle().CellPadding.X, rowSpacing * 0.5f));
        ImGui.SetCursorScreenPos(origin + new Vector2(0f, rowStride - rowSpacing * 0.5f));
        changed |= DrawAppearanceSettings(rowHeight);
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
                OmniControls.DrawWindowBackground(ImGui.GetWindowPos(), ImGui.GetWindowSize(), false);
                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.EditWidgets"));
                ImGui.Separator();
                using (var columns = ImRaii.Table("##multiToolbarEditorColumns", 4, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
                {
                    if (columns)
                    {
                        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.WidgetSideLeft"));
                        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.WidgetSideCenter"));
                        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.WidgetSideRight"));
                        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.ServerInfoBar"));
                        var headerHeight = ImGui.GetTextLineHeight();
                        OmniControls.BeginTableHeaderRow(headerHeight);
                        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.WidgetSideLeft"), headerHeight);
                        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.WidgetSideCenter"), headerHeight);
                        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.WidgetSideRight"), headerHeight);
                        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.ServerInfoBar"), headerHeight);
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
                            if (column == 3)
                            {
                                changed |= DrawDtrSelection();
                            }
                            else
                            {
                                changed |= DrawWidgetEditor(column switch
                                {
                                    0 => MultiToolbarWidgetSide.Left,
                                    1 => MultiToolbarWidgetSide.Center,
                                    _ => MultiToolbarWidgetSide.Right,
                                });
                            }
                        }
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
        using var table = ImRaii.Table("##multiToolbarGeneralSettings", 4,
            ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return changed;
        }

        for (var column = 0; column < 4; column++)
        {
            ImGui.TableSetupColumn($"##setting{column}", ImGuiTableColumnFlags.WidthStretch, 1f);
        }
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var offset = config.BarVerticalOffset;
        if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.BarOffset"), "##multiToolbarBarOffset", ref offset,
                0f, MathF.Max(0f, ImGui.GetMainViewport().WorkSize.Y / ScaleToolbar(1f) - 40f), "%.0f"))
        {
            config.BarVerticalOffset = offset;
            changed = true;
        }
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
        var scale = config.ToolbarScale;
        if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.ToolbarScale"), "##multiToolbarScale", ref scale, 0.1f, 3f, "%.2f"))
        {
            config.ToolbarScale = scale;
            changed = true;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var iconSize = config.RowIconSize;
        if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.RowIconSize"), "##multiToolbarRowIconSize", ref iconSize, 8f, 72f, "%.0f"))
        {
            config.RowIconSize = iconSize;
            changed = true;
        }
        ImGui.TableNextColumn();
        var componentSpacing = config.ComponentSpacing;
        if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.ComponentSpacing"), "##multiToolbarComponentSpacing", ref componentSpacing, 0f, 40f, "%.0f"))
        {
            config.ComponentSpacing = componentSpacing;
            changed = true;
        }
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.Alignment"));
        ImGui.SameLine();
        var alignment = (int)config.Alignment;
        string[] alignmentLabels = [OmniLoc.Get("Feature.MultiToolbar.AlignmentTop"), OmniLoc.Get("Feature.MultiToolbar.AlignmentBottom")];
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##multiToolbarAlignment", ref alignment, alignmentLabels, alignmentLabels.Length))
        {
            config.Alignment = (MultiToolbarAlignment)alignment;
            changed = true;
        }
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.Font"));
        ImGui.SameLine();
        changed |= DrawToolbarFontSelector();
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
            var index = config.DtrOrder.IndexOf(entry.Title);
            using var id = ImRaii.PushId(index);
            var source = DrawReorderHandle("OMNI_TOOLBAR_DTR", index, ImGui.GetFrameHeight());
            if (source >= 0)
            {
                moveFrom = source;
                moveTo = index;
            }
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
        var moveFrom = -1;
        var moveTo = -1;
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
                    var source = DrawReorderHandle($"OMNI_TOOLBAR_WIDGET_{columnSide}", index, rowContentHeight);
                    if (source >= 0)
                    {
                        moveFrom = source;
                        moveTo = index;
                    }
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

                    ImGui.SetNextWindowSizeConstraints(
                        new Vector2(OmniTheme.Scale(360f), 0f),
                        ImGui.GetMainViewport().WorkSize);
                    using (var detail = ImRaii.Popup($"##multiToolbarWidgetDetail{index}"))
                    {
                        if (detail)
                        {
                            ImGui.TextUnformatted(WidgetTypeLabel(widget.Type));
                            ImGui.Separator();
                            ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.WidgetPosition"));
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
                            else
                            {
                                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.CommandName"));
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

                                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.CommandIcon"));
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
        else
        {
            changed |= MoveEntry(config.Widgets, moveFrom, moveTo);
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
            : type is MultiToolbarWidgetType.PluginList or MultiToolbarWidgetType.CommandList
                ? MultiToolbarWidgetSide.Center
                : MultiToolbarWidgetSide.Left,
        Name = type == MultiToolbarWidgetType.CustomButton ? OmniLoc.Get("Feature.MultiToolbar.NewCommand") : string.Empty,
        Command = type == MultiToolbarWidgetType.CustomButton ? "/" : MultiToolbarWidgetCommand(type),
        ShowIcon = DefaultWidgetShowIcon(type),
        DisplayName = type is MultiToolbarWidgetType.BattleEffects or MultiToolbarWidgetType.Societies or MultiToolbarWidgetType.OnlineStatus ? "" : null,
        GameIconID = type == MultiToolbarWidgetType.BattleEffects ? 516u : 0u,
    };

    private static bool DefaultWidgetShowIcon(MultiToolbarWidgetType type) => type is not
        (MultiToolbarWidgetType.PluginList or MultiToolbarWidgetType.CommandList or MultiToolbarWidgetType.DtrList or MultiToolbarWidgetType.DtrSingle or MultiToolbarWidgetType.StackedClock);

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
        _ => WidgetTypeLabel(type)
    };
}

public enum MultiToolbarWidgetSide
{
    Left,
    Center,
    Right,
}

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
}

public enum MultiToolbarAlignment
{
    Top,
    Bottom,
}

[Serializable]
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

    public string Command { get; set; } = string.Empty;
    public string RightCommand { get; set; } = string.Empty;

    public uint GameIconID { get; set; }
    public string IconFilePath { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = string.Empty;
}

[Serializable]
public sealed class MultiToolbarConfig : MultiToolbarBarConfig
{
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<MultiToolbarAuxiliaryBarConfig> AuxiliaryBars { get; set; } = [];
}

[Serializable]
public sealed class MultiToolbarAuxiliaryBarConfig : MultiToolbarBarConfig
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
}

[Serializable]
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

    public MultiToolbarAlignment Alignment { get; set; } = MultiToolbarAlignment.Top;

    public bool AutoExpandOnHover { get; set; } = true;

    public float BarVerticalOffset { get; set; } = 0f;

    // 工具栏自身的尺寸倍率，叠加 Omni 全局主题缩放。
    public float ToolbarScale { get; set; } = 0.75f;
    public string FontFileName { get; set; } = string.Empty;

    public float ComponentSpacing { get; set; } = 6f;

    public bool IsLocked { get; set; } = true;

    public float BarBackgroundOpacity { get; set; }

    public float ButtonBackgroundOpacity { get; set; }

    public float RowIconSize { get; set; } = 40f;

    public uint TrackedSocietyID { get; set; }

    public uint TrackedCurrencyID { get; set; } = 1;

    public bool ShowWorldMarkerOverlay { get; set; } = true;

    public HashSet<MultiToolbarWorldMarkerType> EnabledWorldMarkers { get; set; } = [..Enum.GetValues<MultiToolbarWorldMarkerType>()];

    public List<MultiToolbarVolumePreset> VolumePresets { get; set; } = [];

    public int ActiveVolumePreset { get; set; } = -1;

    public int ClockTimeSource { get; set; }



    public Vector4? BackgroundColor { get; set; }

    public Vector4? TextColor { get; set; }

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
