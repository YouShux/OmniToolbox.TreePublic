using Dalamud.Plugin.Services;
using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;
using OmniToolbox.PublicBridge;
using OmniToolbox.Teleport;
using OmniToolbox.TreeHouse.InterfaceOperation;
using OmniToolbox.Verification;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.Script;

internal sealed class TreasureMapFeature : TreeHouseFeatureBase, ITreasureMapService
{
    private const int RuntimeTickIntervalMs = 500;

    private readonly TreasureMapConfig config;
    private readonly PlayerInventoryService inventoryService;
    private readonly IMarketPurchaseService marketPurchaseService;
    private readonly IAutoStoreService autoStoreService;
    private readonly TeleportService teleportService;
    private readonly IPrivateFeatureProvider privateFeatureProvider;
    private readonly NonEntityTargetVisibility targetVisibility;
    private readonly FeatureGate featureGate;
    private FeatureLifetime? runtimeLifetime;
    private TreasureMapDriver? driver;

    public TreasureMapFeature(
        TreasureMapConfig config,
        PlayerInventoryService inventoryService,
        IMarketPurchaseService marketPurchaseService,
        IAutoStoreService autoStoreService,
        TeleportService teleportService,
        IPrivateFeatureProvider privateFeatureProvider,
        NonEntityTargetVisibility targetVisibility,
        FeatureGate featureGate)
        : base(new(
            "treasure-map",
            "Feature.TreasureMap.Title",
            "Feature.TreasureMap.Description",
            TreeHouseCategory.Script,
            true,
            true))
    {
        this.config = config;
        this.inventoryService = inventoryService;
        this.marketPurchaseService = marketPurchaseService;
        this.autoStoreService = autoStoreService;
        this.teleportService = teleportService;
        this.privateFeatureProvider = privateFeatureProvider;
        this.targetVisibility = targetVisibility;
        this.featureGate = featureGate;
        TreasureMapPanel.Normalize(config);
    }

    public bool IsAvailable => IsEnabled && driver is not null;

    public bool IsBusy => driver?.IsBusy == true;

    public string StatusKey => driver?.StatusKey ?? "Feature.TreasureMap.Status.Disabled";

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = TreasureMapPanel.Draw(
            config,
            this,
            IsEnabled,
            privateFeatureProvider.IsAvailable,
            privateFeatureProvider.UnavailableReasonKey);
        if (changed)
        {
            ApplyTargetPolicy();
            driver?.OnConfigChanged();
        }

        return changed;
    }

    public bool RequestStart(uint itemId, int maxUnitPrice) =>
        driver?.RequestStart(itemId, maxUnitPrice) == true;

    public void Cancel() => driver?.Cancel();

    protected override void OnEnable()
    {
        if (!featureGate.CanUseTestFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var lifetime = new FeatureLifetime();
        try
        {
            driver = new(
                config,
                inventoryService,
                marketPurchaseService,
                autoStoreService,
                teleportService,
                privateFeatureProvider,
                CanRun);
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, RuntimeTickIntervalMs))
            {
                throw new InvalidOperationException("Treasure map update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            runtimeLifetime = lifetime;
            ApplyTargetPolicy();
            driver.Tick();
        }
        catch
        {
            runtimeLifetime = null;
            ReleaseRuntime(lifetime);
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        ReleaseRuntime(lifetime);
    }

    private bool CanRun() =>
        featureGate.CanUseTestFeature(out _) &&
        DService.Instance().ClientState.IsLoggedIn;

    private void OnFrameworkUpdate(IFramework _)
    {
        ApplyTargetPolicy();
        driver?.Tick();
    }

    private void ApplyTargetPolicy() =>
        targetVisibility.SetTreasureMapPolicy(CanRun() && config.BlockOtherTreasureTargets);

    private void ReleaseRuntime(FeatureLifetime? lifetime)
    {
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            targetVisibility.SetTreasureMapPolicy(false);
            driver?.Reset();
            driver = null;
        }
    }
}
