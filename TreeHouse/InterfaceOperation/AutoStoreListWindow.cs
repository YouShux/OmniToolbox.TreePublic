using System.Globalization;
using Dalamud.Interface.Textures;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal sealed class AutoStoreListWindow(
    AutoStoreConfig config,
    PlayerInventoryService inventoryService,
    IAutoStoreService autoStoreService,
    Action saveConfig)
{
    private readonly List<AutoStoreListItem> sourceItems = [];
    private readonly List<AutoStoreListItem> allItems = [];
    private readonly List<AutoStoreListItem> visibleItems = [];
    private readonly Dictionary<uint, AutoStoreListItem> sourceItemsById = [];
    private readonly Dictionary<uint, int> itemQuantities = [];
    private readonly HashSet<uint> checkedItemIds = [];
    private readonly HashSet<uint> visibleItemIds = [];
    private readonly HashSet<uint> presetItemIds = [];
    private readonly List<uint> staleQuantityIds = [];
    private AutoStorePreset? selectedPreset;
    private AutoStoreListSource source = AutoStoreListSource.All;
    private string presetName = string.Empty;
    private string searchText = string.Empty;
    private uint selectedItemId;
    private long lastSnapshotRevision = -1;
    private float lastScale = float.NaN;
    private Vector2 lastConfiguredSize = new(float.NaN, float.NaN);
    private bool isOpen;

    public void Open()
    {
        isOpen = true;
        Refresh(true);
    }

    public void Toggle()
    {
        if (isOpen)
        {
            isOpen = false;
            return;
        }

        Open();
    }

    public void Close()
    {
        isOpen = false;
        checkedItemIds.Clear();
        selectedItemId = 0;
    }

    public void Draw()
    {
        if (!isOpen)
        {
            return;
        }

        Refresh(false);
        var scale = OmniTheme.ScaleValue;
        var configuredSize = ClampWindowSize(config.ListWindowSize);
        var sizeChanged = MathF.Abs(lastScale - scale) > 0.001f ||
                          configuredSize != lastConfiguredSize;
        lastScale = scale;
        lastConfiguredSize = configuredSize;
        ImGui.SetNextWindowSize(
            OmniTheme.Scale(configuredSize),
            sizeChanged ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(
            OmniTheme.Scale(new Vector2(500f, 360f)),
            OmniTheme.Scale(new Vector2(1000f, 900f)));
        if (!ImGui.Begin($"{OmniLoc.Get("Feature.AutoStore.List.Title")}##autoStoreList", ref isOpen))
        {
            ImGui.End();
            return;
        }

        using (var body = ImRaii.Child(
                   "##autoStoreListBody",
                   new Vector2(0f, -(ImGui.GetFrameHeightWithSpacing() * 2f)),
                   false))
        {
            if (body)
            {
                DrawBody();
            }
        }

        DrawFooter();
        ImGui.End();
    }

    private void DrawBody()
    {
        using var table = ImRaii.Table(
            "##autoStoreListLayout",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            ImGui.GetContentRegionAvail());
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("##autoStoreListMain", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            "##autoStoreListTargets",
            ImGuiTableColumnFlags.WidthFixed,
            Math.Clamp(ImGui.GetContentRegionAvail().X * 0.28f, OmniTheme.Scale(128f), OmniTheme.Scale(180f)));
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawMainPanel();
        ImGui.TableNextColumn();
        DrawTargetPanel();
    }

    private void DrawMainPanel()
    {
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputTextWithHint(
                "##autoStoreListSearch",
                OmniLoc.Get("Feature.AutoStore.List.Search"),
                ref searchText,
                128))
        {
            ApplyFilter();
        }

        DrawSourcePresetCombo();
        if (selectedPreset is null)
        {
            ImGui.Dummy(new Vector2(0f, ImGui.GetFrameHeight()));
        }
        else
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint(
                "##autoStorePresetName",
                OmniLoc.Get("Feature.AutoStore.List.PresetName"),
                ref presetName,
                64);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                SavePresetName();
            }
        }

        DrawItemTable();
    }

    private void DrawSourcePresetCombo()
    {
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (!ImGui.BeginCombo(
                "##autoStoreSourcePreset",
                selectedPreset is null
                    ? AutoStoreListOptions.GetSourceLabel(source)
                    : GetPresetDisplayName(selectedPreset)))
        {
            return;
        }

        foreach (var option in AutoStoreListOptions.Sources)
        {
            var selected = selectedPreset is null && source == option;
            if (ImGui.Selectable(
                    $"{AutoStoreListOptions.GetSourceLabel(option)}##autoStoreSource{option}",
                    selected))
            {
                SelectSource(option);
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        string? deletePresetId = null;
        if (config.Presets.Count > 0)
        {
            ImGui.Separator();
        }

        for (var index = 0; index < config.Presets.Count; index++)
        {
            var preset = config.Presets[index];
            var selected = string.Equals(selectedPreset?.Id, preset.Id, StringComparison.Ordinal);
            if (ImGui.Selectable(
                    $"{GetPresetDisplayName(preset)}##autoStorePreset{preset.Id}",
                    selected))
            {
                SelectPreset(preset);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(string.Format(
                    CultureInfo.CurrentCulture,
                    OmniLoc.Get("Feature.AutoStore.List.PresetTooltip"),
                    AutoStoreListOptions.GetSourceLabel(preset.Source),
                    preset.Items.Count));
            }

            if (ImGui.BeginPopupContextItem($"##autoStorePresetContext{preset.Id}"))
            {
                if (ImGui.MenuItem(OmniLoc.Get("Feature.AutoStore.List.DeletePreset")))
                {
                    deletePresetId = preset.Id;
                }

                ImGui.EndPopup();
            }
        }

        ImGui.EndCombo();
        if (deletePresetId is not null)
        {
            DeletePreset(deletePresetId);
        }
    }

    private void DrawItemTable()
    {
        using var table = ImRaii.Table(
            "##autoStoreItems",
            5,
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp,
            ImGui.GetContentRegionAvail());
        if (!table)
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##autoStoreChecked", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(28f));
        ImGui.TableSetupColumn("##autoStoreItemName", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##autoStoreLevel", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(66f));
        ImGui.TableSetupColumn("##autoStoreItemLevel", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(78f));
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoStore.List.Quantity"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(100f));
        ImGui.TableHeadersRow();

        ImGuiListClipperPtr clipper;
        unsafe
        {
            clipper = new(ImGuiNative.ImGuiListClipper());
        }

        clipper.Begin(visibleItems.Count, OmniTheme.Scale(34f));
        while (clipper.Step())
        {
            for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
            {
                DrawItemRow(visibleItems[index]);
            }
        }

        clipper.Destroy();
    }

    private void DrawItemRow(AutoStoreListItem item)
    {
        var available = IsItemAvailable(item);
        ImGui.PushID((int)item.ItemId);
        ImGui.TableNextRow(ImGuiTableRowFlags.None, OmniTheme.Scale(32f));
        using (ImRaii.Disabled(!available))
        {
            ImGui.TableNextColumn();
            var isChecked = checkedItemIds.Contains(item.ItemId);
            if (ImGui.Checkbox("##autoStoreChecked", ref isChecked))
            {
                if (isChecked)
                {
                    checkedItemIds.Add(item.ItemId);
                }
                else
                {
                    checkedItemIds.Remove(item.ItemId);
                }
            }

            ImGui.TableNextColumn();
            DrawItemIcon(item.IconId);
            ImGui.SameLine();
            if (ImGui.Selectable(
                    $"{item.Name}##autoStoreSelected",
                    selectedItemId == item.ItemId,
                    ImGuiSelectableFlags.None,
                    new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
            {
                selectedItemId = item.ItemId;
            }

            if (!string.IsNullOrWhiteSpace(item.Locations) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(item.Locations);
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.Level > 1 ? $"Lv. {item.Level}" : string.Empty);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.ItemLevel > 1 ? $"iLvl. {item.ItemLevel}" : string.Empty);
            ImGui.TableNextColumn();
            var quantity = GetItemQuantity(item);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputInt("##autoStoreQuantity", ref quantity, 1, 0))
            {
                itemQuantities[item.ItemId] = selectedPreset is null
                    ? Math.Clamp(quantity, 1, Math.Max(1, item.Quantity))
                    : Math.Max(1, quantity);
                if (selectedPreset is not null)
                {
                    if (IsItemAvailable(item))
                    {
                        checkedItemIds.Add(item.ItemId);
                    }
                    else
                    {
                        checkedItemIds.Remove(item.ItemId);
                    }
                }
            }
        }

        ImGui.PopID();
    }

    private static void DrawItemIcon(uint iconId)
    {
        if (iconId != 0 &&
            DalamudServices.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault() is { } texture)
        {
            ImGui.Image(texture.Handle, OmniTheme.Scale(new Vector2(24f, 24f)));
            return;
        }

        ImGui.Dummy(OmniTheme.Scale(new Vector2(24f, 24f)));
    }

    private void DrawTargetPanel()
    {
        ImGui.TextUnformatted(OmniLoc.Get("Feature.AutoStore.List.StoreTarget"));
        ImGui.Separator();
        foreach (var target in AutoStoreListOptions.Targets)
        {
            if (!ImGui.Selectable(
                    $"{AutoStoreListOptions.GetTargetLabel(target)}##autoStoreTarget{target}",
                    target == config.DefaultStoreTarget))
            {
                continue;
            }

            config.DefaultStoreTarget = target;
            saveConfig();
        }
    }

    private void DrawFooter()
    {
        var saveLabel = OmniLoc.Get(selectedPreset is null
            ? "Feature.AutoStore.List.SavePreset"
            : "Feature.AutoStore.List.UpdatePreset");
        var selectAllLabel = OmniLoc.Get("Feature.AutoStore.List.SelectAll");
        var clearLabel = OmniLoc.Get("Feature.AutoStore.List.Clear");
        var retrieveLabel = OmniLoc.Get("Feature.AutoStore.List.Retrieve");
        var storeLabel = OmniLoc.Get("Feature.AutoStore.List.Store");
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(GetSelectionSummary());

        using var table = ImRaii.Table(
            "##autoStoreFooterActions",
            5,
            ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        using (ImRaii.Disabled(!HasPresetSelection()))
        {
            if (ImGui.Button(saveLabel, new Vector2(-1f, 0f)))
            {
                SavePreset();
            }
        }

        ImGui.TableNextColumn();
        using (ImRaii.Disabled(!CanSelectAll()))
        {
            if (ImGui.Button(selectAllLabel, new Vector2(-1f, 0f)))
            {
                SelectAll();
            }
        }

        ImGui.TableNextColumn();
        using (ImRaii.Disabled(checkedItemIds.Count == 0))
        {
            if (ImGui.Button(clearLabel, new Vector2(-1f, 0f)))
            {
                checkedItemIds.Clear();
            }
        }

        var canRun = autoStoreService.IsAvailable && !autoStoreService.IsBusy;
        ImGui.TableNextColumn();
        using (ImRaii.Disabled(!canRun || !HasOperationSelection(false)))
        {
            if (ImGui.Button(retrieveLabel, new Vector2(-1f, 0f)))
            {
                QueueRetrieve();
            }
        }

        ImGui.TableNextColumn();
        using (ImRaii.Disabled(!canRun || !HasOperationSelection(true)))
        {
            if (ImGui.Button(storeLabel, new Vector2(-1f, 0f)))
            {
                QueueStore();
            }
        }
    }

    private void Refresh(bool force)
    {
        var revision = inventoryService.GetSnapshotRevision();
        if (!force && revision == lastSnapshotRevision)
        {
            return;
        }

        lastSnapshotRevision = revision;
        AutoStoreListProjection.Build(inventoryService.GetItemsSnapshot(), source, sourceItems);
        sourceItemsById.Clear();
        foreach (var item in sourceItems)
        {
            sourceItemsById[item.ItemId] = item;
        }

        allItems.Clear();
        if (selectedPreset is null)
        {
            allItems.AddRange(sourceItems);
            SyncSourceQuantities();
        }
        else
        {
            BuildPresetItems();
        }

        ApplyFilter();
    }

    private void BuildPresetItems()
    {
        presetItemIds.Clear();
        foreach (var presetItem in selectedPreset!.Items)
        {
            if (presetItem.ItemId == 0 || !presetItemIds.Add(presetItem.ItemId))
            {
                continue;
            }

            allItems.Add(sourceItemsById.TryGetValue(presetItem.ItemId, out var item)
                ? item
                : AutoStoreListProjection.CreatePlaceholder(presetItem.ItemId));
            itemQuantities.TryAdd(presetItem.ItemId, Math.Max(1, presetItem.Quantity));
        }

        RemoveStaleQuantities(presetItemIds);
    }

    private void SyncSourceQuantities()
    {
        presetItemIds.Clear();
        foreach (var item in allItems)
        {
            presetItemIds.Add(item.ItemId);
            itemQuantities[item.ItemId] = itemQuantities.TryGetValue(item.ItemId, out var quantity)
                ? Math.Clamp(quantity, 1, Math.Max(1, item.Quantity))
                : Math.Max(1, item.Quantity);
        }

        RemoveStaleQuantities(presetItemIds);
    }

    private void RemoveStaleQuantities(IReadOnlySet<uint> validItemIds)
    {
        staleQuantityIds.Clear();
        foreach (var itemId in itemQuantities.Keys)
        {
            if (!validItemIds.Contains(itemId))
            {
                staleQuantityIds.Add(itemId);
            }
        }

        foreach (var itemId in staleQuantityIds)
        {
            itemQuantities.Remove(itemId);
        }
    }

    private void ApplyFilter()
    {
        visibleItems.Clear();
        visibleItemIds.Clear();
        foreach (var item in allItems)
        {
            if (!item.Matches(searchText))
            {
                continue;
            }

            visibleItems.Add(item);
            visibleItemIds.Add(item.ItemId);
        }

        checkedItemIds.IntersectWith(visibleItemIds);
        if (selectedPreset is not null)
        {
            foreach (var item in visibleItems)
            {
                if (IsItemAvailable(item))
                {
                    checkedItemIds.Add(item.ItemId);
                }
                else
                {
                    checkedItemIds.Remove(item.ItemId);
                }
            }
        }

        if (!visibleItemIds.Contains(selectedItemId))
        {
            selectedItemId = 0;
        }
    }

    private void SelectSource(AutoStoreListSource value)
    {
        source = value;
        selectedPreset = null;
        presetName = string.Empty;
        checkedItemIds.Clear();
        itemQuantities.Clear();
        Refresh(true);
    }

    private void SelectPreset(AutoStorePreset preset)
    {
        selectedPreset = preset;
        source = preset.Source;
        presetName = preset.Name;
        searchText = string.Empty;
        checkedItemIds.Clear();
        itemQuantities.Clear();
        foreach (var item in preset.Items)
        {
            if (item.ItemId != 0)
            {
                itemQuantities[item.ItemId] = Math.Max(1, item.Quantity);
            }
        }

        Refresh(true);
    }

    private void SavePresetName()
    {
        if (selectedPreset is null)
        {
            return;
        }

        var name = presetName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            presetName = selectedPreset.Name;
            return;
        }

        if (string.Equals(selectedPreset.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        selectedPreset.Name = name;
        presetName = name;
        saveConfig();
    }

    private void SavePreset()
    {
        var items = BuildPresetSaveItems();
        if (items.Count == 0)
        {
            return;
        }

        if (selectedPreset is null)
        {
            selectedPreset = new()
            {
                Name = string.Format(
                    CultureInfo.CurrentCulture,
                    OmniLoc.Get("Feature.AutoStore.List.DefaultPresetName"),
                    config.Presets.Count + 1),
                Source = source,
                Items = items
            };
            config.Presets.Add(selectedPreset);
            searchText = string.Empty;
        }
        else
        {
            selectedPreset.Source = source;
            selectedPreset.Items = items;
        }

        presetName = selectedPreset.Name;
        saveConfig();
        Refresh(true);
    }

    private List<AutoStorePresetItem> BuildPresetSaveItems()
    {
        var items = new List<AutoStorePresetItem>();
        if (checkedItemIds.Count > 0)
        {
            foreach (var item in visibleItems)
            {
                if (checkedItemIds.Contains(item.ItemId) && IsItemAvailable(item))
                {
                    items.Add(new() { ItemId = item.ItemId, Quantity = GetItemQuantity(item) });
                }
            }

            return items;
        }

        if (TryGetSelectedItem(out var selected) && IsItemAvailable(selected))
        {
            items.Add(new() { ItemId = selected.ItemId, Quantity = GetItemQuantity(selected) });
        }

        return items;
    }

    private void DeletePreset(string presetId)
    {
        for (var index = 0; index < config.Presets.Count; index++)
        {
            if (!string.Equals(config.Presets[index].Id, presetId, StringComparison.Ordinal))
            {
                continue;
            }

            var removed = config.Presets[index];
            config.Presets.RemoveAt(index);
            if (string.Equals(selectedPreset?.Id, removed.Id, StringComparison.Ordinal))
            {
                source = removed.Source;
                selectedPreset = null;
                presetName = string.Empty;
                checkedItemIds.Clear();
                itemQuantities.Clear();
                Refresh(true);
            }

            saveConfig();
            return;
        }
    }

    private void SelectAll()
    {
        foreach (var item in visibleItems)
        {
            if (IsItemAvailable(item))
            {
                checkedItemIds.Add(item.ItemId);
            }
        }
    }

    private bool CanSelectAll()
    {
        foreach (var item in visibleItems)
        {
            if (IsItemAvailable(item) && !checkedItemIds.Contains(item.ItemId))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasPresetSelection() => checkedItemIds.Count > 0 ||
                                         TryGetSelectedItem(out var item) && IsItemAvailable(item);

    private bool HasOperationSelection(bool isStore)
    {
        if (selectedPreset is not null || !isStore && checkedItemIds.Count > 0)
        {
            return checkedItemIds.Count > 0;
        }

        return TryGetSelectedItem(out var item) && IsItemAvailable(item);
    }

    private void QueueRetrieve()
    {
        var requests = BuildRetrieveRequests();
        if (!autoStoreService.RequestRetrieve(requests))
        {
            return;
        }

        if (requests.Count == 1)
        {
            checkedItemIds.Remove(requests[0].ItemId);
        }
        else
        {
            checkedItemIds.Clear();
        }
    }

    private void QueueStore()
    {
        var requests = BuildStoreRequests();
        if (requests.Count > 0)
        {
            autoStoreService.RequestStore(requests, config.DefaultStoreTarget);
        }
    }

    private List<AutoStoreItemRequest> BuildRetrieveRequests()
    {
        if (selectedPreset is not null)
        {
            return BuildCheckedRequests();
        }

        if (checkedItemIds.Count == 0)
        {
            return BuildSelectedRequest();
        }

        var entries = new List<RequestEntry>();
        for (var index = 0; index < visibleItems.Count; index++)
        {
            var item = visibleItems[index];
            if (checkedItemIds.Contains(item.ItemId) && IsItemAvailable(item))
            {
                entries.Add(new(
                    new(item.ItemId, GetItemQuantity(item)),
                    item.RetrievePriority,
                    index));
            }
        }

        entries.Sort(static (left, right) =>
        {
            var result = left.Priority.CompareTo(right.Priority);
            return result != 0 ? result : left.Index.CompareTo(right.Index);
        });
        var requests = new List<AutoStoreItemRequest>(entries.Count);
        foreach (var entry in entries)
        {
            requests.Add(entry.Request);
        }

        return requests;
    }

    private List<AutoStoreItemRequest> BuildStoreRequests() => selectedPreset is null
        ? BuildSelectedRequest()
        : BuildCheckedRequests();

    private List<AutoStoreItemRequest> BuildCheckedRequests()
    {
        var requests = new List<AutoStoreItemRequest>();
        foreach (var item in visibleItems)
        {
            if (checkedItemIds.Contains(item.ItemId) && IsItemAvailable(item))
            {
                requests.Add(new(item.ItemId, GetItemQuantity(item)));
            }
        }

        return requests;
    }

    private List<AutoStoreItemRequest> BuildSelectedRequest()
    {
        if (!TryGetSelectedItem(out var item) || !IsItemAvailable(item))
        {
            return [];
        }

        return [new(item.ItemId, GetItemQuantity(item))];
    }

    private bool TryGetSelectedItem(out AutoStoreListItem item)
    {
        foreach (var candidate in visibleItems)
        {
            if (candidate.ItemId == selectedItemId)
            {
                item = candidate;
                return true;
            }
        }

        item = null!;
        return false;
    }

    private int GetItemQuantity(AutoStoreListItem item)
    {
        if (!itemQuantities.TryGetValue(item.ItemId, out var quantity))
        {
            quantity = selectedPreset is null ? Math.Max(1, item.Quantity) : 1;
            itemQuantities[item.ItemId] = quantity;
        }

        return selectedPreset is null
            ? Math.Clamp(quantity, 1, Math.Max(1, item.Quantity))
            : Math.Max(1, quantity);
    }

    private bool IsItemAvailable(AutoStoreListItem item) => selectedPreset is null ||
                                                             GetItemQuantity(item) <= item.Quantity;

    private string GetSelectionSummary()
    {
        if (checkedItemIds.Count > 0)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.AutoStore.List.CheckedCount"),
                checkedItemIds.Count);
        }

        if (!TryGetSelectedItem(out var item))
        {
            return OmniLoc.Get("Feature.AutoStore.List.NoSelection");
        }

        if (string.IsNullOrWhiteSpace(item.Locations))
        {
            return item.Name;
        }

        var separator = item.Locations.IndexOf(',');
        return string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get("Feature.AutoStore.List.SelectedItem"),
            item.Name,
            separator < 0 ? item.Locations : item.Locations[..separator]);
    }

    private string GetPresetDisplayName(AutoStorePreset preset)
    {
        var name = string.IsNullOrWhiteSpace(preset.Name)
            ? OmniLoc.Get("Feature.AutoStore.List.UnnamedPreset")
            : preset.Name;
        var occurrence = 0;
        foreach (var candidate in config.Presets)
        {
            if (string.Equals(candidate.Name, preset.Name, StringComparison.Ordinal))
            {
                occurrence++;
            }

            if (ReferenceEquals(candidate, preset))
            {
                break;
            }
        }

        return occurrence > 1 ? $"{name} ({occurrence})" : name;
    }

    private static Vector2 ClampWindowSize(Vector2 value) => new(
        Math.Clamp(value.X, 500f, 1000f),
        Math.Clamp(value.Y, 360f, 900f));

    private readonly record struct RequestEntry(
        AutoStoreItemRequest Request,
        int Priority,
        int Index);
}
