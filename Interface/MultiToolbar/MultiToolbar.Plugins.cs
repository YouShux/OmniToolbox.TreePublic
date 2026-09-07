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
        ImGui.SetNextWindowSize(Vector2.Min(OmniTheme.Scale(new Vector2(560f, 540f)), ImGui.GetMainViewport().WorkSize), ImGuiCond.Appearing);
        using var popup = ImRaii.Popup("##multiToolbarPluginsEditor", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
        if (!popup)
        {
            return;
        }
        ImGui.SetWindowFontScale(1f);
        OmniControls.DrawWindowBackground(ImGui.GetWindowPos(), ImGui.GetWindowSize(), false);
        ImGui.TextUnformatted(title);
        ImGui.Separator();
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
        using var child = ImRaii.Child("##pluginEditorRows", Vector2.Zero, true, ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!child)
        {
            return;
        }
        var height = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
        using var table = ImRaii.Table("##plugins", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
        if (!table)
        {
            return;
        }
        ImGui.TableSetupColumn("##move", ImGuiTableColumnFlags.WidthFixed, height);
        ImGui.TableSetupColumn("##selected", ImGuiTableColumnFlags.WidthFixed, height);
        ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, height);
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);
        var changed = false;
        var reorderFrom = -1;
        var reorderTo = -1;
        for (var index = 0; index < pluginEditorRows.Count; index++)
        {
            var name = pluginEditorRows[index];
            installedPlugins.TryGetValue(name, out var plugin);
            if (!MatchesPluginSearch(name, plugin?.Name, pluginEditorSearch))
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
            using var alpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * (unloaded ? 0.6f : 1f));
            if (plugin != null)
            {
                DrawPluginIcon(plugin, ImGui.GetCursorScreenPos(), height);
            }
            ImGui.Dummy(new Vector2(height));
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted((plugin?.Name ?? name) + (unloaded ? OmniLoc.Get("Feature.MultiToolbar.UnloadedSuffix") : string.Empty));
            OmniControls.HelpTooltip(name);
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
}
