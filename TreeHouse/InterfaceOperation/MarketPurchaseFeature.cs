using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;
using OmniToolbox.Market;
using OmniToolbox.Verification;
using OmenTools;
using OmenTools.Threading.TaskHelper;
using OmenTools.Threading.TaskHelper.Enums;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed class MarketPurchaseFeature : TreeHouseFeatureBase, IMarketPurchaseService
{
    private const int OperationIntervalMs = 100;
    private const int OperationTimeoutMs = 120_000;

    private static readonly HashSet<string> MarketBoardNames = new(StringComparer.Ordinal)
    {
        "市场布告板",
        "Market Board",
        "マーケットボード"
    };

    private readonly PlayerInventoryService inventoryService;
    private readonly FeatureGate featureGate;
    private readonly MarketBrowserWindow browser;
    private MarketBoardDriver? driver;
    private MarketAutoBuyer? buyer;
    private TaskHelper? taskHelper;
    private FeatureLifetime? runtimeLifetime;

    public MarketPurchaseFeature(
        PlayerInventoryService inventoryService,
        FeatureGate featureGate,
        MarketPriceService marketPriceService,
        ItemDataService itemDataService,
        ItemConfig itemConfig,
        Action saveConfig)
        : base(new(
            "market-purchase",
            "Feature.MarketPurchase.Title",
            "Feature.MarketPurchase.Description",
            TreeHouseCategory.InterfaceOperation,
            true))
    {
        this.inventoryService = inventoryService;
        this.featureGate = featureGate;
        browser = new(marketPriceService, itemDataService, itemConfig, saveConfig);
    }

    public bool IsAvailable => IsEnabled && buyer is not null && taskHelper is not null;

    public bool CanPurchaseInCurrentArea
    {
        get
        {
            if (!IsAvailable)
            {
                return false;
            }

            foreach (var gameObject in DService.Instance().ObjectTable)
            {
                if (MarketBoardNames.Contains(gameObject.Name.ToString()))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool IsBusy => buyer?.IsActive == true || taskHelper?.IsBusy == true;

    public uint SearchItemId => buyer?.SearchItemId ?? 0;

    public IReadOnlyList<MarketListing> Listings => buyer?.Listings ?? [];

    public IReadOnlyList<MarketHistoryEntry> History => driver?.GetHistory(SearchItemId) ?? [];

    public string StatusKey => buyer?.StatusKey ?? "Feature.MarketPurchase.Status.Unavailable";

    public override bool HasSettings => true;

    public override bool DrawSettings() => MarketPurchasePanel.Draw(this, IsEnabled);

    public bool OpenBrowser(uint itemId = 0)
    {
        if (!IsAvailable)
        {
            return false;
        }

        browser.Open(itemId);
        return true;
    }

    public bool RequestSearch(uint itemId)
    {
        if (!CanStart(itemId))
        {
            return false;
        }

        buyer!.BeginSearch(itemId);
        Enqueue($"Search market listings for item {itemId}");
        return true;
    }

    public bool RequestPurchaseListings(uint itemId, IReadOnlyList<MarketListing> listings)
    {
        if (listings.Count == 0)
        {
            buyer?.SetStatus("Feature.MarketPurchase.Status.InvalidRequest");
            return false;
        }

        var listingIds = new HashSet<ulong>();
        for (var index = 0; index < listings.Count; index++)
        {
            var listing = listings[index];
            if (listing.ItemId != itemId ||
                listing.UnitPrice == 0 ||
                listing.Quantity == 0 ||
                !listingIds.Add(listing.ListingId))
            {
                buyer?.SetStatus("Feature.MarketPurchase.Status.InvalidRequest");
                return false;
            }
        }

        if (!CanStart(itemId))
        {
            return false;
        }

        buyer!.BeginPurchase(itemId, listings);
        Enqueue($"Purchase selected listings for item {itemId}");
        return true;
    }

    public bool RequestPurchase(uint itemId, bool isHq, uint quantity, int maxUnitPrice)
    {
        if (quantity == 0 || maxUnitPrice < 0)
        {
            buyer?.SetStatus("Feature.MarketPurchase.Status.InvalidRequest");
            return false;
        }

        if (!CanStart(itemId))
        {
            return false;
        }

        buyer!.BeginPurchase(itemId, isHq, quantity, maxUnitPrice);
        Enqueue($"Purchase item {itemId} from market board");
        return true;
    }

    public void Cancel()
    {
        if (buyer?.IsActive != true)
        {
            return;
        }

        buyer.Cancel();
        taskHelper?.Abort();
    }

    protected override void OnEnable()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        driver = new();
        buyer = new(driver, inventoryService);
        taskHelper = new()
        {
            RetryIntervalMS = OperationIntervalMs
        };
        buyer.SetStatus("Feature.MarketPurchase.Status.Ready");

        var lifetime = new FeatureLifetime();
        try
        {
            DalamudServices.PluginInterface.UiBuilder.Draw += browser.Draw;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= browser.Draw);
            runtimeLifetime = lifetime;
        }
        catch
        {
            lifetime.Dispose();
            driver.Dispose();
            driver = null;
            buyer.Reset();
            buyer = null;
            taskHelper.Dispose();
            taskHelper = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        runtimeLifetime?.Dispose();
        runtimeLifetime = null;
        browser.Close();
        driver?.Dispose();
        driver = null;
        buyer?.Reset();
        buyer = null;
        taskHelper?.Abort();
        taskHelper?.Dispose();
        taskHelper = null;
    }

    private bool CanStart(uint itemId)
    {
        if (IsBusy)
        {
            return false;
        }

        if (itemId == 0)
        {
            buyer?.SetStatus("Feature.MarketPurchase.Status.InvalidRequest");
            return false;
        }

        if (!IsAvailable)
        {
            buyer?.SetStatus("Feature.MarketPurchase.Status.Unavailable");
            return false;
        }

        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            buyer!.SetStatus(reason);
            return false;
        }

        return true;
    }

    private void Enqueue(string name) => taskHelper!.Enqueue(
        Tick,
        name,
        timeoutMS: OperationTimeoutMs,
        timeoutBehaviour: TaskAbortBehaviour.AbortCurrent,
        exceptionBehaviour: TaskAbortBehaviour.AbortCurrent,
        timeoutAction: () => buyer?.Fail("Feature.MarketPurchase.Status.Timeout"),
        exceptionAction: () => buyer?.Fail("Feature.MarketPurchase.Status.Failed"));

    private bool Tick()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            buyer?.Fail(reason);
            return true;
        }

        return buyer?.Tick() ?? true;
    }
}
