using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Teleport;
using OmniToolbox.Verification;
using OmenTools.Threading.TaskHelper;
using OmenTools.Threading.TaskHelper.Enums;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed class NpcShopPurchaseFeature : TreeHouseFeatureBase, INpcShopPurchaseService
{
    private const int OperationIntervalMs = 100;

    private readonly AutoPathConfig autoPathConfig;
    private readonly NpcShopPurchaseDriver driver;
    private readonly FeatureGate featureGate;
    private TaskHelper? taskHelper;

    internal NpcShopPurchaseFeature(
        AutoPathConfig autoPathConfig,
        PlayerInventoryService inventoryService,
        FeatureGate featureGate,
        TeleportService teleportService,
        AutoPathService autoPathService)
        : base(new(
            "npc-shop-purchase",
            "Feature.NpcShopPurchase.Title",
            "Feature.NpcShopPurchase.Description",
            TreeHouseCategory.InterfaceOperation,
            true))
    {
        this.autoPathConfig = autoPathConfig;
        driver = new(inventoryService, autoPathConfig, teleportService, autoPathService);
        this.featureGate = featureGate;
    }

    public bool IsAvailable => IsEnabled && taskHelper is not null;

    public bool IsBusy => driver.IsActive || taskHelper?.IsBusy == true;

    public string StatusKey => driver.StatusKey;

    public override bool HasSettings => true;

    public override bool DrawSettings() => NpcShopPurchasePanel.Draw(this, autoPathConfig, IsEnabled);

    public bool RequestPurchase(uint itemId, uint quantity)
    {
        if (IsBusy)
        {
            return false;
        }

        if (itemId == 0 || quantity == 0)
        {
            driver.SetStatus("Feature.NpcShopPurchase.Status.InvalidRequest");
            return false;
        }

        if (!IsAvailable)
        {
            driver.SetStatus("Feature.NpcShopPurchase.Status.Unavailable");
            return false;
        }

        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            driver.SetStatus(reason);
            return false;
        }

        if (!driver.Begin(itemId, quantity))
        {
            return false;
        }

        taskHelper!.Enqueue(
            Tick,
            $"Purchase item {itemId} from NPC shop",
            timeoutMS: GetTimeoutMs(quantity, autoPathConfig.Enabled),
            timeoutBehaviour: TaskAbortBehaviour.AbortCurrent,
            exceptionBehaviour: TaskAbortBehaviour.AbortCurrent,
            timeoutAction: () => driver.Fail("Feature.NpcShopPurchase.Status.Timeout"),
            exceptionAction: () => driver.Fail("Feature.NpcShopPurchase.Status.Failed"));
        return true;
    }

    public void Cancel()
    {
        if (!driver.IsActive)
        {
            return;
        }

        driver.Cancel();
        taskHelper?.Abort();
    }

    protected override void OnEnable()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        taskHelper = new()
        {
            RetryIntervalMS = OperationIntervalMs
        };
        driver.SetStatus("Feature.NpcShopPurchase.Status.Ready");
    }

    protected override void OnDisable()
    {
        driver.Reset();
        taskHelper?.Abort();
        taskHelper?.Dispose();
        taskHelper = null;
    }

    private bool Tick()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            driver.Fail(reason);
            return true;
        }

        return driver.Tick();
    }

    private static int GetTimeoutMs(uint quantity, bool autoPathEnabled)
    {
        var rounds = (int)Math.Min(((ulong)quantity + 98) / 99, 19UL);
        return Math.Clamp(10 + rounds * 6, 12, 120) * 1_000 + (autoPathEnabled ? 105_000 : 0);
    }
}
