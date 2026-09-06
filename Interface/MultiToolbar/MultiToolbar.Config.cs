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

    public override bool DrawSettings()
    {
        var changed = DrawGeneralSettings();
        changed |= DrawAppearanceSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (OmniControls.IconButton(
                "multiToolbarWidgetSettings",
                FontAwesomeIcon.Cog,
                false,
                OmniLoc.Get("Feature.MultiToolbar.EditWidgets")))
        {
            ImGui.OpenPopup("##multiToolbarWidgetSettingsPopup");
        }

        var viewportSize = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSizeConstraints(
            Vector2.Min(new Vector2(OmniTheme.Scale(620f), OmniTheme.Scale(260f)), viewportSize),
            Vector2.Min(new Vector2(OmniTheme.Scale(1100f), OmniTheme.Scale(760f)), viewportSize));
        using (var popup = ImRaii.Popup("##multiToolbarWidgetSettingsPopup"))
        {
            if (popup)
            {
                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.EditWidgets"));
                ImGui.Separator();
                changed |= DrawWidgetEditor();
                ImGui.Spacing();
                changed |= DrawDtrSelection();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.FlagChatPrefix"));
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1f);
                var flagChatPrefix = config.FlagChatPrefix;
                if (ImGui.InputText("##multiToolbarFlagChatPrefix", ref flagChatPrefix, 128))
                {
                    config.FlagChatPrefix = flagChatPrefix;
                }
                changed |= ImGui.IsItemDeactivatedAfterEdit();
            }
        }

        return changed;
    }

    private bool DrawGeneralSettings()
    {
        var changed = false;
        var rowContentHeight = MathF.Max(OmniTheme.CheckboxSize(), MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight()));
        using var padding = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, MathF.Max(0f, (rowContentHeight - ImGui.GetTextLineHeight()) * 0.5f)));
        var alignmentLabels = (string[])[
            OmniLoc.Get("Feature.MultiToolbar.AlignmentTop"),
            OmniLoc.Get("Feature.MultiToolbar.AlignmentBottom"),
        ];
        using var table = ImRaii.Table(
            "##multiToolbarGeneralSettings",
            4,
            ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##multiToolbarOffset", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##multiToolbarOpacity", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##multiToolbarScale", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##multiToolbarIconSize", ImGuiTableColumnFlags.WidthStretch, 1f);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var barVerticalOffset = config.BarVerticalOffset;
        if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.BarOffset"), "##multiToolbarBarOffset", ref barVerticalOffset, 0f, 60f, "%.0f"))
        {
            config.BarVerticalOffset = barVerticalOffset;
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
        var toolbarScale = config.ToolbarScale;
        if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.ToolbarScale"), "##multiToolbarScale", ref toolbarScale, 0.75f, 1.75f, "%.2f"))
        {
            config.ToolbarScale = toolbarScale;
            changed = true;
        }

        ImGui.TableNextColumn();
        var rowIconSize = config.RowIconSize;
        if (DrawFloatSlider(OmniLoc.Get("Feature.MultiToolbar.RowIconSize"), "##multiToolbarRowIconSize", ref rowIconSize, 12f, 40f, "%.0f"))
        {
            config.RowIconSize = rowIconSize;
            changed = true;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.Alignment"));
        ImGui.SameLine();
        var alignment = (int)config.Alignment;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##multiToolbarAlignment", ref alignment, alignmentLabels, alignmentLabels.Length))
        {
            config.Alignment = (MultiToolbarAlignment)alignment;
            changed = true;
        }

        ImGui.TableNextColumn();
        var locked = config.IsLocked;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.MultiToolbar.Locked"), ref locked, rowContentHeight))
        {
            config.IsLocked = locked;
            changed = true;
        }

        return changed;
    }

    private bool DrawDtrSelection()
    {
        var changed = false;
        ImGui.Spacing();
        OmniControls.SectionLabel(OmniLoc.Get("Feature.MultiToolbar.DtrEntries"));
        foreach (var entry in DService.Instance().DTRBar.Entries)
        {
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

    private bool DrawWidgetEditor()
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
        var typeLabels = (string[])[
            OmniLoc.Get("Feature.MultiToolbar.WidgetPluginList"),
            OmniLoc.Get("Feature.MultiToolbar.WidgetCommandList"),
            OmniLoc.Get("Feature.MultiToolbar.WidgetCustomButton"),
            OmniLoc.Get("Feature.MultiToolbar.WidgetDtrList"),
            ..Enum.GetValues<MultiToolbarWidgetType>().Where(static type => (int)type >= 4).Select(WidgetTypeLabel),
        ];
        using (var table = ImRaii.Table(
                   "##multiToolbarWidgetEditor",
                   5,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##multiToolbarWidgetColEnabled", ImGuiTableColumnFlags.WidthFixed, rowContentHeight);
                ImGui.TableSetupColumn("##multiToolbarWidgetColSide", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(96f));
                ImGui.TableSetupColumn("##multiToolbarWidgetColType", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##multiToolbarWidgetColSettings", ImGuiTableColumnFlags.WidthFixed, rowContentHeight);
                ImGui.TableSetupColumn("##multiToolbarWidgetColRemove", ImGuiTableColumnFlags.WidthFixed, rowContentHeight);
                ImGui.TableNextRow();
                for (var column = 0; column < 3; column++)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(OmniLoc.Get(
                        column switch
                        {
                            0 => "Feature.MultiToolbar.WidgetEnabled",
                            1 => "Feature.MultiToolbar.WidgetPosition",
                            _ => "Feature.MultiToolbar.WidgetType"
                        }));
                }

                for (var index = 0; index < config.Widgets.Count; index++)
                {
                    var widget = config.Widgets[index];
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
                    ImGui.TableNextColumn();
                    var enabled = widget.Enabled;
                    if (OmniControls.Checkbox($"##multiToolbarWidgetEnabled{index}", ref enabled, rowContentHeight))
                    {
                        widget.Enabled = enabled;
                        changed = true;
                    }

                    ImGui.TableNextColumn();
                    var side = (int)widget.Side;
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.Combo($"##multiToolbarWidgetSide{index}", ref side, sideLabels, sideLabels.Length))
                    {
                        widget.Side = (MultiToolbarWidgetSide)side;
                        changed = true;
                    }

                    ImGui.TableNextColumn();
                    var type = (int)widget.Type;
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.Combo($"##multiToolbarWidgetType{index}", ref type, typeLabels, typeLabels.Length))
                    {
                        widget.Type = (MultiToolbarWidgetType)type;
                        changed = true;
                    }

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
                            if (widget.Type == MultiToolbarWidgetType.CustomButton)
                            {
                                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.CommandName"));
                                var name = widget.Name;
                                ImGui.SetNextItemWidth(-1f);
                                if (ImGui.InputText($"##multiToolbarWidgetName{index}", ref name, 64))
                                {
                                    widget.Name = name;
                                }
                                changed |= ImGui.IsItemDeactivatedAfterEdit();

                                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.CommandBody"));
                                var command = widget.Command;
                                ImGui.SetNextItemWidth(-1f);
                                if (ImGui.InputText($"##multiToolbarWidgetCommand{index}", ref command, 256))
                                {
                                    widget.Command = command;
                                }
                                if (ImGui.IsItemDeactivatedAfterEdit())
                                {
                                    if (TryNormalizeCommand(command, out var normalized))
                                    {
                                        widget.Command = normalized;
                                    }
                                    changed = true;
                                }
                            }

                            ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.CommandIcon"));
                            changed |= DrawWidgetIconEditor(widget, index);
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
                foreach (var type in Enum.GetValues<MultiToolbarWidgetType>())
                {
                    var label = WidgetTypeLabel(type);
                    if (!string.IsNullOrWhiteSpace(widgetSearchText) &&
                        !label.Contains(widgetSearchText, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var icon = (WidgetIcon(type) ?? FontAwesomeIcon.Terminal).ToIconString();
                    if (!ImGui.Selectable($"{icon}  {label}##multiToolbarWidgetOption{type}"))
                    {
                        ImGui.TextDisabled(WidgetTypeDescription(type));
                        continue;
                    }

                    config.Widgets.Add(CreateDefaultWidget(type));
                    changed = true;
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        return changed;
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
        Side = type is MultiToolbarWidgetType.DtrList or MultiToolbarWidgetType.Volume or MultiToolbarWidgetType.MailIndicator or MultiToolbarWidgetType.MarkerControl or MultiToolbarWidgetType.WalkingIndicator or MultiToolbarWidgetType.StackedClock or MultiToolbarWidgetType.ToolbarPin
            ? MultiToolbarWidgetSide.Right
            : type is MultiToolbarWidgetType.PluginList or MultiToolbarWidgetType.CommandList
                ? MultiToolbarWidgetSide.Center
                : MultiToolbarWidgetSide.Left,
        Name = type == MultiToolbarWidgetType.CustomButton ? OmniLoc.Get("Feature.MultiToolbar.NewCommand") : string.Empty,
        Command = type == MultiToolbarWidgetType.CustomButton ? "/" : MultiToolbarWidgetCommand(type),
        ShowIcon = DefaultWidgetShowIcon(type),
    };

    private static bool DefaultWidgetShowIcon(MultiToolbarWidgetType type) => type is not MultiToolbarWidgetType.DtrList and not MultiToolbarWidgetType.StackedClock;

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

    // 仅 CustomButton 使用：按钮与指令列表里显示的名称
    public string Name { get; set; } = string.Empty;

    // 仅 CustomButton 使用：点击后执行的指令
    public string Command { get; set; } = string.Empty;

    // 仅 CustomButton 使用：指令列表弹窗里的游戏图标
    public uint GameIconID { get; set; }
}

[Serializable]
public sealed class MultiToolbarConfig
{
    public List<MultiToolbarWidgetConfig> Widgets { get; set; } =
    [
        new() { Side = MultiToolbarWidgetSide.Center, Type = MultiToolbarWidgetType.PluginList },
        new() { Side = MultiToolbarWidgetSide.Center, Type = MultiToolbarWidgetType.CommandList },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.DtrList },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.BattleEffects, Command = "/battleeffects" },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.Societies },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.OnlineStatus },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.GearsetSwitcher, Command = "/character" },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.Durability },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.RetainerList, Command = "/omni 一键打开雇员铃" },
        new() { Side = MultiToolbarWidgetSide.Left, Type = MultiToolbarWidgetType.Currencies, Command = "/currency" },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.Flag },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.Volume },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.MailIndicator },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.MarkerControl },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.WalkingIndicator },
        new() { Side = MultiToolbarWidgetSide.Right, Type = MultiToolbarWidgetType.StackedClock, ShowIcon = false },
    ];

    public MultiToolbarAlignment Alignment { get; set; } = MultiToolbarAlignment.Top;

    // 距屏幕工作区边缘的偏移
    public float BarVerticalOffset { get; set; } = 0f;

    // 工具栏自身的尺寸倍率，叠加 Omni 全局主题缩放。
    public float ToolbarScale { get; set; } = 1f;

    // 锁定时保持显示，解锁后离开工具栏自动收起
    public bool IsLocked { get; set; } = true;

    // 工具栏背景不透明度，默认全透明
    public float BarBackgroundOpacity { get; set; }

    // 弹窗行图标大小
    public float RowIconSize { get; set; } = 22f;

    public uint TrackedSocietyID { get; set; }

    public uint TrackedCurrencyID { get; set; } = 1;

    public bool ShowWorldMarkerOverlay { get; set; } = true;

    public List<MultiToolbarVolumePreset> VolumePresets { get; set; } = [];

    public int ActiveVolumePreset { get; set; } = -1;

    public int ClockTimeSource { get; set; }

    public string FlagChatPrefix { get; set; } = string.Empty;

    public bool UseCustomColors { get; set; }

    public Vector4? BackgroundColor { get; set; }

    public Vector4? TextColor { get; set; }

    public Vector4? AccentColor { get; set; }

    public Vector4? BorderColor { get; set; }

    public List<string> CollapsedDtrTitles { get; set; } = [];
}
