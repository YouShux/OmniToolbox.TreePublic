using System.Text.Json;
using Dalamud.Interface;
using Dalamud.Plugin;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed class MultiDockPanel
{
    private const int ClipboardVersion = 1;
    private const float SourcePickerHeight = 320f;
    private const float CommandSectionHeight = 520f;
    private static readonly MultiDockAutoMode[] AutoModes = Enum.GetValues<MultiDockAutoMode>();
    private static readonly JsonSerializerOptions ClipboardJsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly MultiDockConfig config;
    private readonly MultiDockFeature feature;
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
    private string editingToggleIconId = string.Empty;

    public MultiDockPanel(
        MultiDockConfig config,
        MultiDockFeature feature,
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
                ClipboardJsonOptions));
            SetClipboardStatus("Feature.MultiDock.ExportSuccess", false);
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.MultiDock.Import"), false))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<MultiDockClipboardSnapshot>(
                    ImGui.GetClipboardText(),
                    ClipboardJsonOptions);
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
                editingToggleIconId = string.Empty;
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
                        editingToggleIconId = string.Empty;
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
                        editingToggleIconId = string.Empty;
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
            var key = MultiDockFeature.NormalizeWindowKey(candidate.WindowName);
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
                editingToggleIconId = string.Empty;
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
        var canAdd = MultiDockFeature.TryNormalizeCommand(commandText, out var normalized) &&
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
            if (!MultiDockFeature.TryNormalizeCommand(pair.Key, out var command))
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
                    editingToggleIconId = string.Empty;
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
                    item.PreferConfigUi,
                    value => item.PreferConfigUi = value);
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
                    item.PreferConfigUi,
                    value => item.PreferConfigUi = value);
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
            item.Command = MultiDockFeature.TryNormalizeCommand(item.Command, out var command)
                ? command
                : string.Empty;
            item.ClickCommand = MultiDockFeature.TryNormalizeCommand(item.ClickCommand, out var clickCommand)
                ? clickCommand
                : string.Empty;
            for (var i = 0; i < item.ContextMenuMacros.Count; i++)
            {
                item.ContextMenuMacros[i].Name = item.ContextMenuMacros[i].Name.Trim();
                if (MultiDockFeature.TryNormalizeCommand(item.ContextMenuMacros[i].Command, out var macroCommand))
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
                        editingToggleIconId == id,
                        OmniLoc.Get("Feature.MultiDock.Edit")))
                {
                    editingItemIndex = -1;
                    editingToggleIconId = editingToggleIconId == id ? string.Empty : id;
                }
            }
        }

        if (editingToggleIconId == id)
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
        var gameIconId = icon.GameIconId > int.MaxValue ? int.MaxValue : (int)icon.GameIconId;
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiDock.GameIcon"));
        ImGui.SetNextItemWidth(MathF.Max(
            OmniTheme.Scale(100f),
            ImGui.GetContentRegionAvail().X - OmniTheme.Scale(36f) - ImGui.GetStyle().ItemSpacing.X));
        if (OmniControls.InputInt("##gameIcon", ref gameIconId))
        {
            icon.GameIconId = (uint)Math.Max(0, gameIconId);
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
                    icon.GameIconId = value;
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
                    icon.GameIconId = 0;
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
        var key = MultiDockFeature.NormalizeWindowKey(name);
        for (var i = 0; i < config.Items.Count; i++)
        {
            if (config.Items[i].Type == MultiDockItemType.ImGuiWindow &&
                MultiDockFeature.NormalizeWindowKey(config.Items[i].WindowName)
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

    private sealed class MultiDockClipboardSnapshot
    {
        public int Version { get; set; } = ClipboardVersion;

        public MultiDockConfig? Config { get; set; }
    }
}
