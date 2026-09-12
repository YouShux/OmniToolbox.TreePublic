using System.Linq;
using Dalamud.Interface;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private string pluginEditorSearch = string.Empty;
    private readonly List<string> pluginEditorRows = [];
    private readonly HashSet<string> pluginEditorNames = new(StringComparer.OrdinalIgnoreCase);
    private string? pluginDisplaySettingsName;
    private string pluginDisplayNameBuffer = string.Empty;
    private int pluginIconIDBuffer;

    private void DrawPluginEditorButton(bool iconOnly = false)
    {
        var title = OmniLoc.Get("Feature.MultiToolbar.EditPlugins");
        var clicked = iconOnly
            ? OmniControls.IconButton("multiToolbarEditPlugins", FontAwesomeIcon.Cog, false, new Vector2(ImGui.GetFrameHeight()), title)
            : OmniControls.SmallButton(title + "##multiToolbarEditPlugins", false, OmniControls.CompactButtonSize(title));
        if (clicked)
        {
            pluginCacheValid = false;
            ImGui.OpenPopup("##multiToolbarPluginsEditor");
        }
        ImGui.SetNextWindowSize(Vector2.Min(OmniTheme.Scale(new Vector2(560f, 540f)), ImGui.GetMainViewport().WorkSize), ImGuiCond.Always);
        using var popup = ImRaii.Popup("##multiToolbarPluginsEditor", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
        if (!popup)
        {
            return;
        }
        ImGui.SetWindowFontScale(1f);
        DrawEditorBackground(title);
        using var childBackground = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.SetNextItemWidth(-1f);
        OmniControls.InputTextWithHint("##pluginEditorSearch", OmniLoc.Get("Feature.MultiToolbar.SearchPlugin"), ref pluginEditorSearch, 64);
        RefreshPluginCache();
        pluginEditorRows.Clear();
        pluginEditorNames.Clear();
        foreach (var name in config.SelectedPlugins)
        {
            pluginEditorRows.Add(name);
            pluginEditorNames.Add(name);
        }
        foreach (var plugin in loadedPlugins)
        {
            if (pluginEditorNames.Add(plugin.InternalName))
            {
                pluginEditorRows.Add(plugin.InternalName);
            }
        }
        var openDisplaySettings = false;
        using (var child = ImRaii.Child("##pluginEditorRows", Vector2.Zero, true, ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (!child)
            {
                return;
            }
            var height = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
            using var table = ImRaii.Table("##plugins", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
            if (!table)
            {
                return;
            }
            ImGui.TableSetupColumn("##move", ImGuiTableColumnFlags.WidthFixed, height);
            ImGui.TableSetupColumn("##selected", ImGuiTableColumnFlags.WidthFixed, height);
            ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, height);
            ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##settings", ImGuiTableColumnFlags.WidthFixed, height);
            var changed = false;
            var reorderFrom = -1;
            var reorderTo = -1;
            for (var index = 0; index < pluginEditorRows.Count; index++)
            {
                var name = pluginEditorRows[index];
                installedPlugins.TryGetValue(name, out var plugin);
                if (!MatchesPluginSearch(name, GetPluginDisplayName(name, plugin?.Name), pluginEditorSearch) &&
                    !MatchesPluginSearch(name, plugin?.Name, pluginEditorSearch))
                {
                    continue;
                }
                using var id = ImRaii.PushId(name);
                var selected = index < config.SelectedPlugins.Count;
                ImGui.TableNextRow(ImGuiTableRowFlags.None, height);
                ImGui.TableNextColumn();
                using (ImRaii.Disabled(!selected))
                {
                    var from = DrawReorderHandle("OMNI_TOOLBAR_PLUGIN", index, height);
                    if (selected && from >= 0)
                    {
                        reorderFrom = from;
                        reorderTo = index;
                    }
                }
                ImGui.TableNextColumn();
                if (OmniControls.Checkbox("##selected", ref selected, height))
                {
                    if (selected)
                    {
                        config.SelectedPlugins.Add(name);
                    }
                    else
                    {
                        config.SelectedPlugins.RemoveAt(index);
                    }
                    changed = true;
                    break;
                }
                ImGui.TableNextColumn();
                var unloaded = plugin == null || !plugin.IsLoaded;
                using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * (unloaded ? 0.6f : 1f)))
                {
                    if (plugin != null)
                    {
                        DrawPluginIcon(plugin, ImGui.GetCursorScreenPos(), height);
                    }
                    ImGui.Dummy(new Vector2(height));
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    var displayName = GetPluginDisplayName(name, plugin?.Name) +
                        (unloaded ? OmniLoc.Get("Feature.MultiToolbar.UnloadedSuffix") : string.Empty);
                    ImGui.TextUnformatted(EllipsizeToolbarText(displayName, ImGui.GetContentRegionAvail().X));
                    OmniControls.HelpTooltip(name);
                }
                ImGui.TableNextColumn();
                if (OmniControls.IconButton("pluginDisplaySettings", FontAwesomeIcon.Cog, false,
                        new Vector2(height), OmniLoc.Get("Feature.MultiToolbar.PluginDisplaySettings")))
                {
                    var settings = config.PluginConfigs.FirstOrDefault(item =>
                        item.InternalName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    pluginDisplaySettingsName = name;
                    pluginDisplayNameBuffer = settings?.DisplayName ?? plugin?.Name ?? name;
                    pluginIconIDBuffer = unchecked((int)(settings?.IconID ?? 0));
                    openDisplaySettings = true;
                }
            }
            if (!changed)
            {
                changed = MoveEntry(config.SelectedPlugins, reorderFrom, reorderTo);
            }
            if (changed)
            {
                config.MultiDockPluginsMigrated = true;
                saveConfig();
            }
        }

        if (openDisplaySettings)
        {
            ImGui.OpenPopup("##multiToolbarPluginDisplaySettings");
        }
        ImGui.SetNextWindowSize(Vector2.Min(OmniTheme.Scale(new Vector2(420f, 145f)),
            ImGui.GetMainViewport().WorkSize), ImGuiCond.Always);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding,
            OmniTheme.Scale(OmniTheme.Tokens.BorderRadius));
        using var settingsPopup = ImRaii.Popup("##multiToolbarPluginDisplaySettings",
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
        if (!settingsPopup || pluginDisplaySettingsName is null)
        {
            return;
        }
        DrawEditorBackground(OmniLoc.Get("Feature.MultiToolbar.PluginDisplaySettings"));
        ImGui.SetNextItemWidth(-1f);
        var save = OmniControls.InputText(OmniLoc.Get("Feature.MultiToolbar.PluginDisplayName"),
            ref pluginDisplayNameBuffer, 128, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.PluginIconID"));
        ImGui.SameLine();
        var iconButtonSize = MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight());
        ImGui.SetNextItemWidth(MathF.Max(1f,
            ImGui.GetContentRegionAvail().X - iconButtonSize - OmniTheme.Scale(4f)));
        OmniControls.InputInt("##pluginIconID", ref pluginIconIDBuffer, 0, 0);
        save |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine(0f, OmniTheme.Scale(4f));
        if (OmniControls.IconButton("pluginIconBrowser", FontAwesomeIcon.Image, false, new Vector2(iconButtonSize),
                OmniLoc.Get("Feature.MultiToolbar.IconBrowser")))
        {
            var internalName = pluginDisplaySettingsName;
            var displayName = pluginDisplayNameBuffer;
            iconBrowser.OpenForSelection(false, value =>
            {
                if (pluginDisplaySettingsName == internalName)
                {
                    pluginIconIDBuffer = checked((int)value);
                }
                SavePluginDisplaySettings(internalName, displayName, value);
            }, null);
        }
        if (save)
        {
            SavePluginDisplaySettings(pluginDisplaySettingsName, pluginDisplayNameBuffer,
                (uint)Math.Max(0, pluginIconIDBuffer));
        }
    }

    private string GetPluginDisplayName(string internalName, string? originalName)
    {
        var displayName = config.PluginConfigs.FirstOrDefault(item =>
            item.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase))?.DisplayName;
        return string.IsNullOrWhiteSpace(displayName) ? originalName ?? internalName : displayName;
    }

    private void SavePluginDisplaySettings(string internalName, string displayName, uint iconID)
    {
        var settings = config.PluginConfigs.FirstOrDefault(item =>
            item.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
        if (settings is null)
        {
            settings = new MultiToolbarPluginConfig { InternalName = internalName };
            config.PluginConfigs.Add(settings);
        }
        settings.DisplayName = displayName.Trim();
        settings.IconID = iconID;
        saveConfig();
    }
}
