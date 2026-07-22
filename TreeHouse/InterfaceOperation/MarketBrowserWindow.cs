using Dalamud.Interface;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Market;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.Extensions;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal sealed class MarketBrowserWindow(
    MarketPriceService marketPriceService,
    ItemDataService itemDataService,
    ItemConfig itemConfig,
    Action saveConfig)
{
    private static readonly string[] RegionScopes =
    [
        "Japan",
        "North-America",
        "Europe",
        "Oceania",
        "China"
    ];

    private readonly List<MarketScopeOption> scopes = [];
    private readonly List<ItemSearchResult> searchResults = [];
    private readonly List<OnlineMarketListing> visibleListings = [];
    private readonly List<OnlineMarketHistoryEntry> visibleHistory = [];
    private ItemSearchResult selectedItem;
    private MarketDetails? preparedDetails;
    private string searchText = string.Empty;
    private int selectedScopeIndex;
    private int marketPriceThreshold = 100;
    private bool preparedHqOnly;
    private bool hqOnly;
    private bool isOpen;

    public void Open(uint itemId = 0)
    {
        RefreshScopes();
        isOpen = true;
        if (itemId != 0 && itemDataService.TryGet(itemId, out var item) && item.IsTradable)
        {
            SelectItem(item);
        }
        else
        {
            RefreshSearch();
        }
    }

    public void Close()
    {
        isOpen = false;
        preparedDetails = null;
        visibleListings.Clear();
        visibleHistory.Clear();
    }

    public void Draw()
    {
        if (!isOpen)
        {
            return;
        }

        using var style = new ComicStyleScope();
        ImGui.SetNextWindowSize(OmniTheme.Scale(new Vector2(940f, 680f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(
            OmniTheme.Scale(new Vector2(700f, 500f)),
            ImGuiHelpers.MainViewport.Size);
        if (!ImGui.Begin($"{OmniLoc.Get("Feature.MarketPurchase.Browser.Title")}##marketBrowser", ref isOpen))
        {
            ImGui.End();
            return;
        }

        using var layout = ImRaii.Table(
            "##marketBrowserLayout",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            ImGui.GetContentRegionAvail());
        if (layout)
        {
            ImGui.TableSetupColumn("##marketBrowserItems", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(238f));
            ImGui.TableSetupColumn("##marketBrowserData", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawItemSelector();
            ImGui.TableNextColumn();
            DrawMarketData();
        }

        ImGui.End();
    }

    private void DrawItemSelector()
    {
        if (OmniControls.InputTextWithHint(
                "##marketBrowserSearch",
                OmniLoc.Get("ItemSearch.SearchHint"),
                ref searchText,
                128,
                ImGui.GetContentRegionAvail().X))
        {
            RefreshSearch();
        }

        using var child = ImRaii.Child("##marketBrowserSearchResults", ImGui.GetContentRegionAvail(), true);
        if (!child)
        {
            return;
        }

        for (var index = 0; index < searchResults.Count; index++)
        {
            var item = searchResults[index];
            if (ImGui.Selectable(
                    $"{item.Name}##marketBrowserItem{item.Id}",
                    selectedItem.Id == item.Id))
            {
                SelectItem(item);
            }

            ImGui.TextDisabled($"{OmniLoc.Get("ItemSearch.ItemId")}: {item.Id}");
        }
    }

    private void DrawMarketData()
    {
        if (selectedItem.Id == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MarketPurchase.Browser.SelectItem"));
            return;
        }

        DrawToolbar();
        if (scopes.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MarketPurchase.Browser.ScopeUnavailable"));
            return;
        }

        marketPriceService.RequestDetails(selectedItem.Id, scopes[selectedScopeIndex].Value);
        var details = marketPriceService.GetDetails(selectedItem.Id, scopes[selectedScopeIndex].Value);
        if (details is null)
        {
            ImGui.TextDisabled(OmniLoc.Get(marketPriceService.IsDetailsPending(
                selectedItem.Id,
                scopes[selectedScopeIndex].Value)
                    ? "ItemSearch.Price.Loading"
                    : "ItemSearch.Price.Unavailable"));
            return;
        }

        PrepareVisibleRows(details);
        ImGui.TextDisabled(string.Format(
            OmniLoc.Get("Feature.MarketPurchase.Browser.Status"),
            details.Listings.Count,
            details.History.Count,
            details.UpdatedAt?.ToLocalTime().ToString("g") ?? "-"));
        if (!ImGui.BeginTabBar("##marketBrowserTabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem(OmniLoc.Get("Feature.MarketPurchase.Browser.Tab.Worlds")))
        {
            DrawWorldPrices(details.Worlds);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(OmniLoc.Get("Feature.MarketPurchase.Tab.Listings")))
        {
            DrawListings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(OmniLoc.Get("Feature.MarketPurchase.Tab.History")))
        {
            DrawHistory();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawToolbar()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(selectedItem.Name);
        ImGui.SameLine();
        ImGui.TextDisabled($"{OmniLoc.Get("ItemSearch.ItemId")}: {selectedItem.Id}");

        using var table = ImRaii.Table(
            "##marketBrowserToolbar",
            6,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("##marketScope", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##marketQuality", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(48f));
        ImGui.TableSetupColumn("##marketThreshold", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(126f));
        ImGui.TableSetupColumn("##marketFavorite", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(32f));
        ImGui.TableSetupColumn("##marketMonitor", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(32f));
        ImGui.TableSetupColumn("##marketRefresh", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(32f));
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (OmniControls.BeginCombo(
                "##marketBrowserScope",
                scopes.Count == 0 ? "-" : scopes[selectedScopeIndex].Label,
                ImGui.GetContentRegionAvail().X))
        {
            for (var index = 0; index < scopes.Count; index++)
            {
                if (ImGui.Selectable(scopes[index].Label, selectedScopeIndex == index))
                {
                    selectedScopeIndex = index;
                    preparedDetails = null;
                    RequestDetails();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TableNextColumn();
        if (OmniControls.Checkbox("HQ##marketBrowserHq", ref hqOnly))
        {
            LoadMonitorThreshold();
        }

        ImGui.TableNextColumn();
        if (OmniControls.InputInt(
                "##marketBrowserThreshold",
                ref marketPriceThreshold,
                ImGui.GetContentRegionAvail().X,
                100,
                1_000))
        {
            marketPriceThreshold = Math.Max(1, marketPriceThreshold);
        }

        if (ImGui.IsItemDeactivatedAfterEdit() &&
            itemConfig.Monitor.FindItem(selectedItem.Id, hqOnly)?.MarketPriceWarning == true &&
            itemConfig.Monitor.SetMarketPriceMonitoring(
                selectedItem.Id,
                hqOnly,
                true,
                marketPriceThreshold))
        {
            saveConfig();
        }

        OmniControls.HelpTooltip(OmniLoc.Get("Monitor.MaxUnitPrice"));
        ImGui.TableNextColumn();
        var favorite = itemConfig.Favorites.Contains(selectedItem.Id);
        if (OmniControls.IconButton(
                "##marketBrowserFavorite",
                FontAwesomeIcon.Star,
                favorite,
                OmniLoc.Get(favorite ? "ItemSearch.Unfavorite" : "ItemSearch.Context.AddFavorite")))
        {
            if (!itemConfig.Favorites.Add(selectedItem.Id))
            {
                itemConfig.Favorites.Remove(selectedItem.Id);
            }

            saveConfig();
        }

        ImGui.TableNextColumn();
        var monitored = itemConfig.Monitor.FindItem(selectedItem.Id, hqOnly)?.MarketPriceWarning == true;
        if (OmniControls.IconButton(
                "##marketBrowserMonitor",
                FontAwesomeIcon.Bell,
                monitored,
                OmniLoc.Get(monitored
                    ? "Feature.MarketPurchase.Browser.Unmonitor"
                    : "Feature.MarketPurchase.Browser.Monitor")) &&
            itemConfig.Monitor.SetMarketPriceMonitoring(
                selectedItem.Id,
                hqOnly,
                !monitored,
                marketPriceThreshold))
        {
            saveConfig();
        }

        ImGui.TableNextColumn();
        if (OmniControls.IconButton(
                "##marketBrowserRefresh",
                FontAwesomeIcon.SyncAlt,
                false,
                OmniLoc.Get("Feature.MarketPurchase.Refresh")))
        {
            RequestDetails(true);
        }
    }

    private void DrawWorldPrices(IReadOnlyList<MarketWorldPrice> worlds)
    {
        if (worlds.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MarketPurchase.Status.NoListings"));
            return;
        }

        using var table = ImRaii.Table(
            "##marketBrowserWorlds",
            3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
            ImGui.GetContentRegionAvail());
        if (!table)
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Browser.Column.World"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("NQ", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        for (var index = 0; index < worlds.Count; index++)
        {
            var world = worlds[index];
            ImGui.PushID(index);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.Selectable(world.WorldName))
            {
                SelectScope(world.WorldName);
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatPrice(world.NqPrice));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatPrice(world.HqPrice));
            ImGui.PopID();
        }
    }

    private void DrawListings()
    {
        if (visibleListings.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MarketPurchase.Status.NoListings"));
            return;
        }

        using var table = ImRaii.Table(
            "##marketBrowserListings",
            6,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
            ImGui.GetContentRegionAvail());
        if (!table)
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Browser.Column.World"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Column.Quality"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(54f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Column.UnitPrice"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Column.Quantity"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(68f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Column.Total"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Browser.Column.Retainer"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        DrawClippedRows(visibleListings.Count, DrawListingRow);
    }

    private void DrawListingRow(int index)
    {
        var listing = visibleListings[index];
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(listing.WorldName);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(listing.IsHq ? "HQ" : "NQ");
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(listing.UnitPrice.ToString("N0"));
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(listing.Quantity.ToString("N0"));
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(listing.Total.ToString("N0"));
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(listing.RetainerName);
        if (listing.OnMannequin)
        {
            OmniControls.HelpTooltip(OmniLoc.Get("Feature.MarketPurchase.Browser.Mannequin"));
        }
    }

    private void DrawHistory()
    {
        if (visibleHistory.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MarketPurchase.History.Empty"));
            return;
        }

        using var table = ImRaii.Table(
            "##marketBrowserHistory",
            5,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
            ImGui.GetContentRegionAvail());
        if (!table)
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Browser.Column.World"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Column.Quality"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(54f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Column.UnitPrice"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Column.Quantity"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(68f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MarketPurchase.Column.Time"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        DrawClippedRows(visibleHistory.Count, DrawHistoryRow);
    }

    private void DrawHistoryRow(int index)
    {
        var entry = visibleHistory[index];
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(entry.WorldName);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(entry.IsHq ? "HQ" : "NQ");
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(entry.UnitPrice.ToString("N0"));
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(entry.Quantity.ToString("N0"));
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(entry.PurchaseTime.ToLocalTime().ToString("g"));
    }

    private static void DrawClippedRows(int count, Action<int> drawRow)
    {
        ImGuiListClipperPtr clipper;
        unsafe
        {
            clipper = new(ImGuiNative.ImGuiListClipper());
        }

        clipper.Begin(count, OmniTheme.Scale(28f));
        while (clipper.Step())
        {
            for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
            {
                drawRow(index);
            }
        }

        clipper.Destroy();
    }

    private void PrepareVisibleRows(MarketDetails details)
    {
        if (ReferenceEquals(preparedDetails, details) && preparedHqOnly == hqOnly)
        {
            return;
        }

        preparedDetails = details;
        preparedHqOnly = hqOnly;
        visibleListings.Clear();
        for (var index = 0; index < details.Listings.Count; index++)
        {
            if (!hqOnly || details.Listings[index].IsHq)
            {
                visibleListings.Add(details.Listings[index]);
            }
        }

        visibleHistory.Clear();
        for (var index = 0; index < details.History.Count; index++)
        {
            if (!details.History[index].OnMannequin && (!hqOnly || details.History[index].IsHq))
            {
                visibleHistory.Add(details.History[index]);
            }
        }
    }

    private void RefreshSearch()
    {
        searchResults.Clear();
        if (string.IsNullOrWhiteSpace(searchText))
        {
            foreach (var itemId in itemConfig.Favorites)
            {
                if (itemDataService.TryGet(itemId, out var item) && item.IsTradable)
                {
                    searchResults.Add(item);
                }
            }

            searchResults.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            return;
        }

        searchResults.AddRange(itemDataService.SearchTradable(searchText));
    }

    private void SelectItem(ItemSearchResult item)
    {
        selectedItem = item;
        searchText = item.Name;
        preparedDetails = null;
        LoadMonitorThreshold();
        RequestDetails();
    }

    private void LoadMonitorThreshold() =>
        marketPriceThreshold = Math.Max(
            1,
            itemConfig.Monitor.FindItem(selectedItem.Id, hqOnly)?.MaxUnitPrice ?? 100);

    private void RequestDetails(bool refresh = false)
    {
        if (selectedItem.Id != 0 && scopes.Count != 0)
        {
            marketPriceService.RequestDetails(
                selectedItem.Id,
                scopes[selectedScopeIndex].Value,
                refresh);
        }
    }

    private void SelectScope(string scope)
    {
        for (var index = 0; index < scopes.Count; index++)
        {
            if (!string.Equals(scopes[index].Value, scope, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            selectedScopeIndex = index;
            preparedDetails = null;
            RequestDetails();
            return;
        }

        scopes.Add(new(scope, string.Format(OmniLoc.Get("Feature.MarketPurchase.Browser.Scope.World"), scope)));
        selectedScopeIndex = scopes.Count - 1;
        preparedDetails = null;
        RequestDetails();
    }

    private void RefreshScopes()
    {
        var previous = scopes.Count == 0 ? string.Empty : scopes[selectedScopeIndex].Value;
        scopes.Clear();
        var defaultScope = string.Empty;
        if (DalamudServices.PlayerState.CurrentWorld.ValueNullable is { } world)
        {
            var worldName = world.Name.ExtractText();
            if (worldName.Length != 0)
            {
                scopes.Add(new(
                    worldName,
                    string.Format(OmniLoc.Get("Feature.MarketPurchase.Browser.Scope.World"), worldName)));
            }

            var dataCenterName = world.DataCenter.ValueNullable?.Name.ExtractText() ?? string.Empty;
            if (dataCenterName.Length != 0 && dataCenterName != worldName)
            {
                defaultScope = dataCenterName;
                scopes.Add(new(
                    dataCenterName,
                    string.Format(OmniLoc.Get("Feature.MarketPurchase.Browser.Scope.DataCenter"), dataCenterName)));
            }
        }

        for (var index = 0; index < RegionScopes.Length; index++)
        {
            scopes.Add(new(
                RegionScopes[index],
                string.Format(OmniLoc.Get("Feature.MarketPurchase.Browser.Scope.Region"), RegionScopes[index])));
        }

        selectedScopeIndex = 0;
        var target = previous.Length == 0 ? defaultScope : previous;
        for (var index = 0; index < scopes.Count; index++)
        {
            if (string.Equals(scopes[index].Value, target, StringComparison.OrdinalIgnoreCase))
            {
                selectedScopeIndex = index;
                break;
            }
        }
    }

    private static string FormatPrice(uint price) => price == 0 ? "-" : price.ToString("N0");

    private readonly record struct MarketScopeOption(string Value, string Label);
}
