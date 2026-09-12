using Dalamud.Plugin;
using OmniToolbox.Host;
using OmniToolbox.UI;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Interface;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private const float CollectionPopupHeight = 620f;
    private readonly MultiToolbarPluginBridge pluginBridge = new();
    private readonly HashSet<string> pendingPluginToggles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MultiToolbarCollectionConfig> visibleCollections = [];
    private string collectionSearchText = string.Empty;
    private string newCollectionName = string.Empty;
    private string collectionAssignmentPlugin = string.Empty;
    private string collectionNameBuffer = string.Empty;
    private string editingCollectionContent = string.Empty;
    private string collectionPluginSearch = string.Empty;
    private bool copyingCollection;
    private bool collectionCopyFailed;

    private void DrawCollectionsButton()
    {
        var title = OmniLoc.Get("Feature.MultiToolbar.Collections");
        if (OmniControls.IconButton("multiToolbarCollections", FontAwesomeIcon.Box, false,
                new Vector2(ImGui.GetFrameHeight()), title))
        {
            OpenPopup(PopupKind.Collections, popupPosition);
        }
        OmniControls.HelpTooltip(OmniLoc.Get("Feature.MultiToolbar.Collections.Help"));
    }

    private void DrawCollectionsPopup()
    {
        RefreshCollections();
        if (collectionCopyFailed)
        {
            ImGui.TextWrapped(OmniLoc.Get("Feature.MultiToolbar.CollectionCopyFailed"));
        }

        ImGui.SetNextItemWidth(MathF.Max(1f, ImGui.GetContentRegionAvail().X -
            ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X));
        OmniControls.InputTextWithHint("##multiToolbarCollectionSearch",
            OmniLoc.Get("Feature.MultiToolbar.SearchCollection"), ref collectionSearchText, 64);
        ImGui.SameLine();
        if (OmniControls.IconButton("addCollection", FontAwesomeIcon.Plus, false,
                new Vector2(ImGui.GetFrameHeight()), OmniLoc.Get("Feature.MultiToolbar.AddCollection")))
        {
            newCollectionName = string.Empty;
            ImGui.OpenPopup("##multiToolbarAddCollection");
        }

        ImGui.SetNextWindowSize(new Vector2(
            MathF.Min(OmniTheme.Scale(460f), ImGui.GetMainViewport().WorkSize.X), 0f), ImGuiCond.Always);
        using (var addRounding = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding,
                   OmniTheme.Scale(OmniTheme.Tokens.BorderRadius)))
        using (var addPopup = ImRaii.Popup("##multiToolbarAddCollection",
                   ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            if (addPopup)
            {
                OmniControls.DrawWindowBackground(ImGui.GetWindowPos(), ImGui.GetWindowSize(), true);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.CollectionName"));
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1f);
                if (OmniControls.InputText("##newCollectionName", ref newCollectionName, 64,
                        ImGuiInputTextFlags.EnterReturnsTrue) &&
                    TryAddCollection(newCollectionName))
                {
                    newCollectionName = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.Spacing();
        RefreshPluginCache();
        var openEditor = false;
        using (var child = ImRaii.Child("##multiToolbarCollectionsList", Vector2.Zero, true,
                   ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child)
            {
                var removed = -1;
                var copied = -1;
                var reorderFrom = -1;
                var reorderTo = -1;
                for (var index = 0; index < visibleCollections.Count; index++)
                {
                    var collection = visibleCollections[index];
                    if (!MatchesCollectionSearch(collection.Name, collectionSearchText))
                    {
                        continue;
                    }

                    using var id = ImRaii.PushId($"collection_{index}");
                    var rowPosition = ImGui.GetCursorPos();
                    var rowWidth = ImGui.GetContentRegionAvail().X;
                    var editLabel = OmniLoc.Get("Common.Edit");
                    var copyLabel = OmniLoc.Get("Feature.MultiToolbar.CopyCollection");
                    var deleteLabel = OmniLoc.Get("Common.Delete");
                    var height = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
                    var buttonSize = new Vector2(height);
                    var actionsX = rowPosition.X + rowWidth - height * 3f - ImGui.GetStyle().ItemSpacing.X * 2f;
                    var sourceIndex = DrawReorderHandle("OMNI_TOOLBAR_COLLECTION", index, height);
                    if (sourceIndex >= 0)
                    {
                        reorderFrom = sourceIndex;
                        reorderTo = index;
                    }
                    ImGui.SameLine();
                    var enabled = collection.Enabled;
                    if (OmniControls.Checkbox("##enabled", ref enabled, height))
                    {
                        collection.Enabled = enabled;
                        if (collection.DalamudProfileID is { } profileID)
                        {
                            _ = pluginBridge.SetCollectionEnabledAsync(profileID, enabled);
                        }
                        else
                        {
                            saveConfig();
                        }
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted(EllipsizeToolbarText(
                        $"{collection.Name} ({collection.PluginInternalNames.Count})",
                        MathF.Max(1f, actionsX - ImGui.GetCursorPosX() - ImGui.GetStyle().ItemSpacing.X)));
                    ImGui.SameLine(actionsX);
                    if (OmniControls.IconButton("editCollection", FontAwesomeIcon.Edit, false, buttonSize, editLabel))
                    {
                        collectionNameBuffer = collection.Name;
                        editingCollectionContent = collection.Name;
                        collectionPluginSearch = string.Empty;
                        openEditor = true;
                    }
                    ImGui.SameLine();
                    using (ImRaii.Disabled(copyingCollection))
                    {
                        if (OmniControls.IconButton("copyCollection", FontAwesomeIcon.Copy, false, buttonSize, copyLabel))
                        {
                            copied = index;
                        }
                    }
                    ImGui.SameLine();
                    if (OmniControls.IconButton("deleteCollection", FontAwesomeIcon.Trash, false, buttonSize, deleteLabel))
                    {
                        removed = index;
                    }
                    ImGui.Separator();
                }

                if (copied >= 0)
                {
                    _ = CopyCollectionAsync(visibleCollections[copied]);
                }

                if (removed >= 0)
                {
                    var collection = visibleCollections[removed];
                    if (collection.DalamudProfileID is { } profileID)
                    {
                        _ = pluginBridge.DeleteCollectionAsync(profileID);
                    }
                    else
                    {
                        config.PluginCollections.Remove(collection);
                        saveConfig();
                    }

                    visibleCollections.RemoveAt(removed);
                }
                else if (MoveEntry(visibleCollections, reorderFrom, reorderTo))
                {
                    var nativeOrder = visibleCollections
                        .Where(collection => collection.DalamudProfileID is not null)
                        .Select(collection => collection.DalamudProfileID!.Value)
                        .ToArray();
                    if (nativeOrder.Length > 0)
                    {
                        pluginBridge.ReorderCollections(nativeOrder);
                    }
                    config.PluginCollectionOrder = visibleCollections.Select(CollectionOrderKey).ToList();
                    saveConfig();
                }

                if (visibleCollections.Count == 0)
                {
                    ImGui.TextDisabled(OmniLoc.Get("Feature.MultiToolbar.NoCollections"));
                }
            }
        }

        if (openEditor)
        {
            ImGui.OpenPopup("##multiToolbarEditCollectionName");
        }
        DrawCollectionEditorPopup();
    }

    private void DrawCollectionAssignmentPopup()
    {
        if (string.IsNullOrEmpty(collectionAssignmentPlugin))
        {
            return;
        }

        RefreshCollections();
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding,
            OmniTheme.Scale(OmniTheme.Tokens.BorderRadius));
        using var popup = ImRaii.Popup("##multiToolbarCollectionAssignment",
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings);
        if (!popup)
        {
            return;
        }

        DrawEditorBackground(OmniLoc.Get("Feature.MultiToolbar.SelectCollections"));
        foreach (var collection in visibleCollections)
        {
            var selected = collection.PluginInternalNames.Contains(collectionAssignmentPlugin,
                StringComparer.OrdinalIgnoreCase);
            if (OmniControls.Checkbox(collection.Name, ref selected))
            {
                UpdateCollectionPlugin(collection, collectionAssignmentPlugin, selected);
            }
        }

        ImGui.Separator();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.MultiToolbar.RemoveFromAllCollections"), false))
        {
            foreach (var collection in visibleCollections)
            {
                UpdateCollectionPlugin(collection, collectionAssignmentPlugin, false);
            }

            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawCollectionEditorPopup()
    {
        ImGui.SetNextWindowSize(Vector2.Min(OmniTheme.Scale(new Vector2(520f, 520f)),
            ImGui.GetMainViewport().WorkSize), ImGuiCond.Always);
        using var popup = ImRaii.Popup("##multiToolbarEditCollectionName",
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
        if (!popup)
        {
            return;
        }

        DrawEditorBackground(OmniLoc.Get("Common.Edit"));
        ImGui.SetNextItemWidth(-1f);
        var rename = OmniControls.InputText(OmniLoc.Get("Feature.MultiToolbar.CollectionName"),
            ref collectionNameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        var collection = visibleCollections.FirstOrDefault(item =>
            string.Equals(item.Name, editingCollectionContent, StringComparison.Ordinal));
        if (collection is null)
        {
            return;
        }

        if (rename && TryRenameCollection(collection.Name, collectionNameBuffer))
        {
            editingCollectionContent = collection.Name;
        }
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + OmniTheme.Scale(2f));
        ImGui.SetNextItemWidth(-1f);
        OmniControls.InputTextWithHint("##collectionPluginSearch",
            OmniLoc.Get("Feature.MultiToolbar.SearchPlugin"), ref collectionPluginSearch, 64);
        using var child = ImRaii.Child("##collectionEditorPlugins", Vector2.Zero);
        if (!child)
        {
            return;
        }
        foreach (var plugin in installedPlugins.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!MatchesPluginSearch(plugin.InternalName, plugin.Name, collectionPluginSearch))
            {
                continue;
            }
            var rowPosition = ImGui.GetCursorScreenPos();
            var rowHeight = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
            var rowRight = rowPosition.X + ImGui.GetContentRegionAvail().X;
            var selected = collection.PluginInternalNames.Contains(plugin.InternalName, StringComparer.OrdinalIgnoreCase);
            var buttonSize = new Vector2(rowHeight);
            var iconSize = MathF.Min(OmniTheme.TableItemIconSize(), rowHeight);
            if (OmniControls.Checkbox($"##collectionPluginEnabled_{plugin.InternalName}",
                    ref selected, rowHeight))
            {
                UpdateCollectionPlugin(collection, plugin.InternalName, selected);
            }
            ImGui.SameLine();
            var iconPosition = ImGui.GetCursorScreenPos();
            DrawPluginIcon(plugin, iconPosition + new Vector2(
                0f, (rowHeight - iconSize) * 0.5f), iconSize);
            ImGui.Dummy(new Vector2(iconSize, rowHeight));
            ImGui.SameLine();
            var nameWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - buttonSize.X -
                ImGui.GetStyle().ItemSpacing.X * 2f);
            ImGui.TextUnformatted(EllipsizeToolbarText(plugin.Name, nameWidth));
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(rowRight - buttonSize.X, rowPosition.Y));
            if (OmniControls.IconButton($"collectionAssignment_{plugin.InternalName}", FontAwesomeIcon.Box,
                    false, buttonSize, OmniLoc.Get("Feature.MultiToolbar.SelectCollections")))
            {
                collectionAssignmentPlugin = plugin.InternalName;
                ImGui.OpenPopup("##multiToolbarCollectionAssignment");
            }
        }
        DrawCollectionAssignmentPopup();
    }

    private void RequestPluginState(IExposedPlugin plugin, bool targetState)
    {
        if (!pendingPluginToggles.Add(plugin.InternalName))
        {
            return;
        }

        _ = RequestPluginStateAsync(plugin, targetState);
    }

    private async Task RequestPluginStateAsync(IExposedPlugin plugin, bool targetState)
    {
        try
        {
            if (await pluginBridge.TogglePluginAsync(plugin, targetState).ConfigureAwait(false))
            {
                pluginCacheValid = false;
            }
        }
        finally
        {
            pendingPluginToggles.Remove(plugin.InternalName);
        }
    }

    private bool TryAddCollection(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || visibleCollections.Any(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (pluginBridge.TryCreateCollection(name, out _))
        {
            return true;
        }

        config.PluginCollections.Add(new MultiToolbarCollectionConfig { Name = name });
        saveConfig();
        return true;
    }

    private bool TryRenameCollection(string oldName, string newName)
    {
        newName = newName.Trim();
        var collection = visibleCollections.FirstOrDefault(item =>
            string.Equals(item.Name, oldName, StringComparison.Ordinal));
        if (collection is null || newName.Length == 0 || visibleCollections.Any(item =>
                !ReferenceEquals(item, collection) &&
                string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (collection.DalamudProfileID is { } profileID)
        {
            if (!pluginBridge.TryRenameCollection(profileID, newName))
            {
                return false;
            }
        }

        var orderIndex = config.PluginCollectionOrder.IndexOf(CollectionOrderKey(collection));
        collection.Name = newName;
        if (orderIndex >= 0)
        {
            config.PluginCollectionOrder[orderIndex] = CollectionOrderKey(collection);
        }
        saveConfig();
        return true;
    }

    private void RefreshCollections()
    {
        var nativeCollections = pluginBridge.ReadCollections();
        visibleCollections.Clear();
        visibleCollections.AddRange(nativeCollections);
        var nativeNames = nativeCollections
            .Select(collection => collection.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        visibleCollections.AddRange(config.PluginCollections.Where(collection =>
            !nativeNames.Contains(collection.Name)));
        if (config.PluginCollectionOrder.Count > 0)
        {
            var order = config.PluginCollectionOrder
                .Select((key, index) => (key, index))
                .ToDictionary(item => item.key, item => item.index);
            var sorted = visibleCollections.OrderBy(collection =>
                order.GetValueOrDefault(CollectionOrderKey(collection), int.MaxValue)).ToArray();
            visibleCollections.Clear();
            visibleCollections.AddRange(sorted);
        }
    }

    private static string CollectionOrderKey(MultiToolbarCollectionConfig collection) =>
        collection.DalamudProfileID is { } profileID ? $"profile:{profileID:N}" : $"local:{collection.Name}";

    private async Task CopyCollectionAsync(MultiToolbarCollectionConfig source)
    {
        collectionCopyFailed = false;
        var baseName = string.Format(OmniLoc.Get("Feature.MultiToolbar.CollectionCopyName"), source.Name);
        var name = baseName;
        var suffix = 2;
        while (visibleCollections.Any(collection => string.Equals(collection.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName} {suffix++}";
        }

        if (source.DalamudProfileID is not null)
        {
            copyingCollection = true;
            try
            {
                collectionCopyFailed = !await pluginBridge.CopyCollectionAsync(source.PluginInternalNames.ToArray(), name);
            }
            finally
            {
                copyingCollection = false;
            }
        }
        else
        {
            config.PluginCollections.Add(new MultiToolbarCollectionConfig
            {
                Name = name,
                Enabled = false,
                PluginInternalNames = [..source.PluginInternalNames]
            });
            saveConfig();
        }
    }

    private void UpdateCollectionPlugin(MultiToolbarCollectionConfig collection, string internalName, bool selected)
    {
        if (selected)
        {
            AddPluginToCollection(collection, internalName);
        }
        else
        {
            RemovePluginFromCollection(collection, internalName);
        }

        if (collection.DalamudProfileID is { } profileID &&
            installedPlugins.TryGetValue(internalName, out var plugin))
        {
            _ = pluginBridge.UpdateCollectionPluginAsync(profileID, plugin, selected);
        }
        else
        {
            saveConfig();
        }
    }

    private static bool MatchesCollectionSearch(string name, string search) =>
        string.IsNullOrWhiteSpace(search) || name.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static void AddPluginToCollection(MultiToolbarCollectionConfig collection, string internalName)
    {
        if (!collection.PluginInternalNames.Contains(internalName, StringComparer.OrdinalIgnoreCase))
        {
            collection.PluginInternalNames.Add(internalName);
        }
    }

    private static void RemovePluginFromCollection(MultiToolbarCollectionConfig collection, string internalName) =>
        collection.PluginInternalNames.RemoveAll(name =>
            string.Equals(name, internalName, StringComparison.OrdinalIgnoreCase));
}
