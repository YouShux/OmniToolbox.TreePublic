using System.Text.Json;
using Dalamud.Interface;
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
    private static readonly JsonSerializerOptions ClipboardJsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly MultiDockConfig config;
    private readonly IconBrowser iconBrowser;
    private readonly Action saveConfig;
    private readonly Action itemsChanged;
    private readonly Action iconsChanged;
    private string pluginFilter = string.Empty;
    private string windowName = string.Empty;
    private string windowDisplayName = string.Empty;
    private string commandName = string.Empty;
    private string commandText = string.Empty;
    private string commandFilter = string.Empty;
    private string clipboardStatusKey = string.Empty;
    private double clipboardStatusUntil;
    private bool clipboardStatusIsError;
    private int editingItemIndex = -1;

    public MultiDockPanel(
        MultiDockConfig config,
        IconBrowser iconBrowser,
        Action saveConfig,
        Action itemsChanged,
        Action iconsChanged)
    {
        this.config = config;
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
                    "Feature.MultiDock.Visible",
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

        using var settingsTable = ImRaii.Table(
            $"##multiDockSettings{id}",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!settingsTable)
        {
            return changed;
        }

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        changed |= DrawFloatSliderRow(
            "Feature.MultiDock.IconSize",
            $"iconSize{id}",
            dock.IconSize,
            value => dock.IconSize = value,
            24f,
            96f,
            "%.0f");
        changed |= DrawFloatSliderRow(
            "Feature.MultiDock.BackgroundOpacity",
            $"opacity{id}",
            dock.BackgroundOpacity,
            value => dock.BackgroundOpacity = value,
            0f,
            1f,
            "%.2f");
        changed |= DrawIntSliderRow(
            "Feature.MultiDock.ItemsPerRow",
            $"itemsPerRow{id}",
            dock.ItemsPerRow,
            value => dock.ItemsPerRow = value,
            1,
            20);
        changed |= DrawFloatSliderRow(
            "Feature.MultiDock.IconSpacing",
            $"spacing{id}",
            dock.IconSpacing,
            value => dock.IconSpacing = value,
            0f,
            40f,
            "%.0f");
        changed |= DrawAutoModeRow(dock, id);
        using (ImRaii.Disabled(dock.AutoMode == MultiDockAutoMode.None))
        {
            changed |= DrawFloatSliderRow(
                "Feature.MultiDock.AutoDelay",
                $"autoDelay{id}",
                dock.AutoDelaySeconds,
                value => dock.AutoDelaySeconds = value,
                0f,
                120f,
                "%.0f");
        }

        changed |= DrawBoolComboRow(
            "Feature.MultiDock.TogglePosition",
            $"togglePosition{id}",
            dock.ToggleAtEnd,
            value => dock.ToggleAtEnd = value,
            "Feature.MultiDock.TogglePosition.Start",
            "Feature.MultiDock.TogglePosition.End");
        changed |= DrawBoolComboRow(
            "Feature.MultiDock.ExpandDirection",
            $"expandDirection{id}",
            dock.ReverseExpand,
            value => dock.ReverseExpand = value,
            "Feature.MultiDock.ExpandDirection.Forward",
            "Feature.MultiDock.ExpandDirection.Reverse");
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiDock.ToggleIcon"));
        ImGui.TableNextColumn();
        changed |= DrawIconEditor(dock.ToggleIcon, $"toggle{id}");
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
                ImGui.TableHeadersRow();
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawPluginPicker();
                ImGui.TableNextColumn();
                changed |= DrawWindowCreator();
            }
        }

        ImGui.Spacing();
        OmniControls.SectionLabel(OmniLoc.Get("Feature.MultiDock.CurrentItems"));
        changed |= DrawManagedItems(false);
        return changed;
    }

    private bool DrawPluginPicker()
    {
        ImGui.SetNextItemWidth(-1f);
        OmniControls.InputTextWithHint(
            "##multiDockPluginFilter",
            OmniLoc.Get("Feature.MultiDock.Filter"),
            ref pluginFilter,
            128);
        if (!ImGui.BeginChild(
                "##multiDockPluginPicker",
                new Vector2(0f, OmniTheme.Scale(210f)),
                true))
        {
            ImGui.EndChild();
            return false;
        }

        var changed = false;
        foreach (var plugin in DalamudServices.PluginInterface.InstalledPlugins)
        {
            if (!plugin.IsLoaded || (!plugin.HasMainUi && !plugin.HasConfigUi) ||
                (pluginFilter.Length > 0 &&
                 !plugin.Name.Contains(pluginFilter, StringComparison.OrdinalIgnoreCase) &&
                 !plugin.InternalName.Contains(pluginFilter, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var index = FindPlugin(plugin.InternalName);
            var selected = index >= 0;
            ImGui.PushID(plugin.InternalName);
            if (OmniControls.Checkbox(plugin.Name, ref selected))
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
                itemsChanged();
                changed = true;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(plugin.InternalName);
            }

            ImGui.PopID();
        }

        ImGui.EndChild();
        return changed;
    }

    private bool DrawWindowCreator()
    {
        var changed = false;
        ImGui.SetNextItemWidth(-1f);
        OmniControls.InputTextWithHint(
            "##multiDockWindowName",
            OmniLoc.Get("Feature.MultiDock.WindowName"),
            ref windowName,
            256);
        ImGui.SetNextItemWidth(-1f);
        OmniControls.InputTextWithHint(
            "##multiDockWindowDisplayName",
            OmniLoc.Get("Feature.MultiDock.DisplayName"),
            ref windowDisplayName,
            128);
        using (ImRaii.Disabled(windowName.Trim().Length == 0 || FindWindow(windowName.Trim()) >= 0))
        {
            if (OmniControls.SmallButton(
                    OmniLoc.Get("Feature.MultiDock.AddWindow"),
                    false,
                    new Vector2(ImGui.GetContentRegionAvail().X, OmniTheme.SmallButtonSize().Y)))
            {
                config.Items.Add(new()
                {
                    Type = MultiDockItemType.ImGuiWindow,
                    Name = windowDisplayName.Trim(),
                    WindowName = windowName.Trim()
                });
                windowName = string.Empty;
                windowDisplayName = string.Empty;
                itemsChanged();
                changed = true;
            }
        }

        return changed;
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
                ImGui.TableHeadersRow();
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawCommandCreator();
                ImGui.TableNextColumn();
                changed |= DrawCommandBrowser();
            }
        }

        ImGui.Spacing();
        OmniControls.SectionLabel(OmniLoc.Get("Feature.MultiDock.CurrentCommands"));
        changed |= DrawManagedItems(true);
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
        if (!ImGui.BeginChild(
                "##multiDockCommandBrowser",
                new Vector2(0f, OmniTheme.Scale(210f)),
                true))
        {
            ImGui.EndChild();
            return false;
        }

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
            if (OmniControls.Checkbox(command, ref selected))
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

            if (help.Length > 0 && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(help);
            }

            ImGui.PopID();
        }

        ImGui.EndChild();
        return changed;
    }

    private bool DrawManagedItems(bool commandsOnly)
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   commandsOnly ? "##multiDockManagedCommands" : "##multiDockManagedPluginWindows",
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
                if ((config.Items[i].Type == MultiDockItemType.Command) != commandsOnly)
                {
                    continue;
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(GetItemName(config.Items[i]));
                ImGui.TableNextColumn();
                using (ImRaii.Disabled(FindAdjacent(i, commandsOnly, -1) < 0))
                {
                    if (OmniControls.IconButton(
                            $"moveUp{i}",
                            FontAwesomeIcon.ChevronCircleUp,
                            false,
                            OmniLoc.Get("Feature.MultiDock.MoveUp")))
                    {
                        MoveItem(i, FindAdjacent(i, commandsOnly, -1));
                        changed = true;
                    }
                }

                ImGui.TableNextColumn();
                using (ImRaii.Disabled(FindAdjacent(i, commandsOnly, 1) < 0))
                {
                    if (OmniControls.IconButton(
                            $"moveDown{i}",
                            FontAwesomeIcon.ChevronCircleDown,
                            false,
                            OmniLoc.Get("Feature.MultiDock.MoveDown")))
                    {
                        MoveItem(i, FindAdjacent(i, commandsOnly, 1));
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
            (config.Items[editingItemIndex].Type == MultiDockItemType.Command) == commandsOnly)
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

    private static bool DrawFloatSliderRow(
        string labelKey,
        string id,
        float current,
        Action<float> setter,
        float min,
        float max,
        string format)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        var value = current;
        OmniControls.SliderFloat($"##{id}", ref value, min, max, format, ImGui.GetContentRegionAvail().X);
        if (!ImGui.IsItemDeactivatedAfterEdit())
        {
            setter(value);
            return false;
        }

        setter(value);
        return true;
    }

    private static bool DrawIntSliderRow(
        string labelKey,
        string id,
        int current,
        Action<int> setter,
        int min,
        int max)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        var value = current;
        OmniControls.SliderInt($"##{id}", ref value, min, max, "%d", ImGui.GetContentRegionAvail().X);
        if (!ImGui.IsItemDeactivatedAfterEdit())
        {
            setter(value);
            return false;
        }

        setter(value);
        return true;
    }

    private static bool DrawAutoModeRow(MultiDockBarConfig dock, string id)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiDock.AutoMode"));
        ImGui.TableNextColumn();
        if (!OmniControls.BeginCombo(
                $"##autoMode{id}",
                OmniLoc.Get(dock.AutoMode switch
                {
                    MultiDockAutoMode.Hide => "Feature.MultiDock.AutoMode.Hide",
                    MultiDockAutoMode.Collapse => "Feature.MultiDock.AutoMode.Collapse",
                    _ => "Feature.MultiDock.AutoMode.None"
                }),
                ImGui.GetContentRegionAvail().X))
        {
            return false;
        }

        var changed = false;
        foreach (var mode in Enum.GetValues<MultiDockAutoMode>())
        {
            if (ImGui.Selectable(
                    OmniLoc.Get(mode switch
                    {
                        MultiDockAutoMode.Hide => "Feature.MultiDock.AutoMode.Hide",
                        MultiDockAutoMode.Collapse => "Feature.MultiDock.AutoMode.Collapse",
                        _ => "Feature.MultiDock.AutoMode.None"
                    }),
                    dock.AutoMode == mode))
            {
                dock.AutoMode = mode;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static bool DrawBoolComboRow(
        string labelKey,
        string id,
        bool current,
        Action<bool> setter,
        string falseLabelKey,
        string trueLabelKey)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        if (!OmniControls.BeginCombo(
                $"##{id}",
                OmniLoc.Get(current ? trueLabelKey : falseLabelKey),
                ImGui.GetContentRegionAvail().X))
        {
            return false;
        }

        var changed = false;
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
        return changed;
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

    private int FindAdjacent(int index, bool commandsOnly, int step)
    {
        for (var i = index + step; i >= 0 && i < config.Items.Count; i += step)
        {
            if ((config.Items[i].Type == MultiDockItemType.Command) == commandsOnly)
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
