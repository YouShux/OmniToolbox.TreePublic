using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;
using OmniToolbox.Market;
using OmniToolbox.PublicBridge;
using OmniToolbox.Teleport;
using OmniToolbox.Tooltips;
using OmniToolbox.TreeHouse.Actions;
using OmniToolbox.TreeHouse.General;
using OmniToolbox.TreeHouse.InterfaceOptimization;
using OmniToolbox.TreeHouse.InterfaceOptimization.ItemPreview;
using OmniToolbox.TreeHouse.InterfaceOptimization.ItemPriceQuery;
using OmniToolbox.TreeHouse.InterfaceOptimization.ItemQuery;
using OmniToolbox.TreeHouse.InterfaceOperation;
using OmniToolbox.TreeHouse.Script;
using OmniToolbox.TreeHouse.Utility;
using OmniToolbox.Verification;
using Dalamud.Plugin.Services;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse;

public sealed class TreeHouseCatalog : IDisposable
{
    private readonly TreeHouseConfig config;
    private readonly FeatureGate featureGate;
    private readonly FeatureLifetime lifetime = new();
    private readonly List<ITreeHouseFeature> pendingFeatureRestores = [];
    private bool restoreUpdateRegistered;

    private TreeHouseCatalog(
        TreeHouseConfig config,
        FeatureGate featureGate,
        IInnEntryService innEntryService,
        IAutoStoreService autoStoreService,
        IMarketPurchaseService marketPurchaseService,
        INpcShopPurchaseService npcShopPurchaseService,
        FreeCompanyActionFeature freeCompanyActionFeature,
        ITreasureMapService treasureMapService,
        IArmoireRecordService armoireRecordService,
        ItemTooltipRemarkFeature itemTooltipRemarkService,
        MitigationMonitorFeature mitigationMonitorFeature,
        FoodReminderFeature foodReminderFeature,
        IconBrowser iconBrowser,
        BetterUserMacroFeature betterUserMacroFeature,
        ChatFrameOptimizationFeature chatFrameOptimizationFeature,
        AutoGcSealFeature autoGcSealFeature,
        IItemPriceQueryService itemPriceQueryService,
        ItemQueryFeature itemQueryFeature,
        IItemPreviewService itemPreviewService,
        ItemTooltipInventoryContext inventoryContext,
        NonEntityTargetVisibility nonEntityTargetVisibility,
        AetheryteRouteResolver aetheryteRouteResolver,
        IReadOnlyList<ITreeHouseFeature> features)
    {
        this.config = config;
        this.featureGate = featureGate;
        InnEntryService = innEntryService;
        AutoStoreService = autoStoreService;
        MarketPurchaseService = marketPurchaseService;
        NpcShopPurchaseService = npcShopPurchaseService;
        FreeCompanyActionFeature = freeCompanyActionFeature;
        TreasureMapService = treasureMapService;
        ArmoireRecordService = armoireRecordService;
        ItemTooltipRemarkService = itemTooltipRemarkService;
        MitigationMonitorFeature = mitigationMonitorFeature;
        FoodReminderFeature = foodReminderFeature;
        IconBrowser = iconBrowser;
        BetterUserMacroFeature = betterUserMacroFeature;
        ChatFrameOptimizationFeature = chatFrameOptimizationFeature;
        AutoGcSealFeature = autoGcSealFeature;
        ItemPriceQueryService = itemPriceQueryService;
        ItemQueryFeature = itemQueryFeature;
        ItemPreviewService = itemPreviewService;
        AetheryteRouteResolver = aetheryteRouteResolver;
        Features = features;
        lifetime.Add(inventoryContext.Dispose);
        lifetime.Add(nonEntityTargetVisibility.Dispose);
        foreach (var feature in features)
        {
            lifetime.Add(feature.Dispose);
            if (config.IsEnabled(feature.Info.Id) && !TryRestoreEnabled(feature))
            {
                pendingFeatureRestores.Add(feature);
            }
        }

        if (pendingFeatureRestores.Count > 0)
        {
            restoreUpdateRegistered = FrameworkManager.Instance().Reg(RestorePendingFeatures, 1_000);
            if (restoreUpdateRegistered)
            {
                lifetime.Add(StopPendingFeatureRestore);
            }
            else
            {
                DalamudServices.PluginLog.Error("TreeHouse pending feature restore registration failed.");
            }
        }
    }

    public IReadOnlyList<ITreeHouseFeature> Features { get; }

    public IInnEntryService InnEntryService { get; }

    public IAutoStoreService AutoStoreService { get; }

    public IMarketPurchaseService MarketPurchaseService { get; }

    public INpcShopPurchaseService NpcShopPurchaseService { get; }

    public FreeCompanyActionFeature FreeCompanyActionFeature { get; }

    public ITreasureMapService TreasureMapService { get; }

    public IArmoireRecordService ArmoireRecordService { get; }

    public ItemTooltipRemarkFeature ItemTooltipRemarkService { get; }

    public MitigationMonitorFeature MitigationMonitorFeature { get; }

    public FoodReminderFeature FoodReminderFeature { get; }

    public IconBrowser IconBrowser { get; }

    public BetterUserMacroFeature BetterUserMacroFeature { get; }

    public ChatFrameOptimizationFeature ChatFrameOptimizationFeature { get; }

    public AutoGcSealFeature AutoGcSealFeature { get; }

    public IItemPriceQueryService ItemPriceQueryService { get; }

    public ItemQueryFeature ItemQueryFeature { get; }

    public IItemPreviewService ItemPreviewService { get; }

    public AetheryteRouteResolver AetheryteRouteResolver { get; }

    internal static TreeHouseCatalog CreateDefault(
        TreeHouseConfig config,
        Action saveConfig,
        HookRegistry hookRegistry,
        FeatureGate featureGate,
        ItemDataService itemDataService,
        ItemConfig itemConfig,
        InventoryConfig inventoryConfig,
        MarketConfig marketConfig,
        PlayerInventoryService inventoryService,
        MarketPriceService marketPriceService,
        ItemOwnedCountTooltip itemOwnedCountTooltip,
        ItemSupplementTooltip itemSupplementTooltip,
        ItemPreviewService itemPreviewService,
        TeleportService teleportService,
        AutoPathService autoPathService,
        IPrivateTreeModule privateTreeModule)
    {
        var innEntry = new InnEntryFeature(featureGate);
        var autoStore = new AutoStoreFeature(
            config.AutoStore,
            inventoryService,
            featureGate,
            innEntry,
            autoPathService,
            teleportService,
            saveConfig);
        var marketPurchase = new MarketPurchaseFeature(
            inventoryService,
            featureGate,
            marketPriceService,
            itemDataService,
            itemConfig,
            saveConfig);
        var npcShopPurchase = new NpcShopPurchaseFeature(
            config.AutoPath,
            inventoryService,
            featureGate,
            teleportService,
            autoPathService);
        var voidPurchase = new VoidPurchaseFeature(marketPurchase, npcShopPurchase);
        var privateFeatures = privateTreeModule.CreateFeatures(new(
            config,
            saveConfig,
            hookRegistry,
            featureGate));
        var freeCompanyAction = new FreeCompanyActionFeature(
            privateTreeModule.AgentEventService,
            featureGate);
        var itemTooltipInventoryContext = new ItemTooltipInventoryContext(hookRegistry);
        var itemTooltipRemark = new ItemTooltipRemarkFeature(config.ItemTooltipRemark);
        var mitigationMonitor = new MitigationMonitorFeature(config.MitigationMonitor, saveConfig);
        var foodReminder = new FoodReminderFeature(config.FoodReminder, saveConfig);
        var iconBrowser = new IconBrowser(config.IconBrowser, saveConfig);
        var betterUserMacro = new BetterUserMacroFeature(config.BetterUserMacro);
        var chatFrameOptimization = new ChatFrameOptimizationFeature(config.ChatFrameOptimization);
        var autoGcSeal = new AutoGcSealFeature(config.AutoGcSeal, saveConfig, featureGate);
        var itemPriceQuery = new ItemPriceQueryFeature(marketConfig, marketPriceService, itemSupplementTooltip);
        var itemQuery = new ItemQueryFeature(
            itemConfig,
            inventoryConfig,
            inventoryService,
            itemOwnedCountTooltip);
        var itemPreview = new ItemPreviewFeature(itemConfig, itemPreviewService, itemSupplementTooltip);
        var nonEntityTargetVisibility = new NonEntityTargetVisibility(hookRegistry);
        var treasureMap = new TreasureMapFeature(
            config.TreasureMap,
            inventoryService,
            marketPurchase,
            autoStore,
            teleportService,
            privateTreeModule.FeatureProvider,
            nonEntityTargetVisibility,
            featureGate);
        var aetheryteRouteResolver = new AetheryteRouteResolver();
        var armoireRecord = new ArmoireRecordFeature(
            config.ArmoireRecord,
            inventoryService,
            autoStore,
            featureGate);
        var features = new List<ITreeHouseFeature>
        {
            voidPurchase,
            freeCompanyAction,
            new DisplayIdInformationFeature(config.DisplayIdInformation, iconBrowser),
            itemPriceQuery,
            itemQuery,
            itemPreview,
            new FloatingInfoOverlayFeature(
                config.FloatingInfoOverlay,
                nonEntityTargetVisibility),
            new DuplicateStatusPreventionFeature(config.DuplicateStatusPrevention),
            new DirectionalActionReleaseFeature(config.DirectionalActionRelease, featureGate),
            new CollectableValuePreviewFeature(itemTooltipInventoryContext),
            new AutoPartyInviteFeature(config.AutoPartyInvite, featureGate),
            innEntry,
            new ReadyCheckFeature(config.ReadyCheck),
            foodReminder,
            new HouseExpirationReminderFeature(config.HouseExpirationReminder, saveConfig),
            new BetterCharacterPanelFeature(config.BetterCharacterPanel),
            new RealItemIconFeature(config.RealItemIcon, inventoryService, featureGate),
            betterUserMacro,
            new AutoHideModelFeature(config.AutoHideModel),
            mitigationMonitor,
            new FastSearchResultsFeature(config.FastSearchResults, saveConfig),
            new CopyItemNameFeature(),
            new AltF4ExitFeature(),
            new BackgroundChatReminderFeature(config.BackgroundChatReminder),
            new BackgroundFpsLimitFeature(config),
            new ChatHotbarLockFeature(config.ChatHotbarLock, saveConfig),
            chatFrameOptimization,
            new WidescreenCutsceneFeature(hookRegistry),
            new CoordinateNearestAetheryteFeature(
                config.CoordinateNearestAetheryte,
                teleportService,
                aetheryteRouteResolver),
            new AntiCensorshipFeature(config.AntiCensorship),
            new DesynthesisEnhancementFeature(config.DesynthesisEnhancement, hookRegistry),
            treasureMap,
            new CosmicLotteryFeature(config.CosmicLottery, featureGate),
            new InfiniteSprintFeature(config.InfiniteSprint, featureGate),
            autoGcSeal,
            new OneClickLowerQualityFeature(),
            new LockedDutyPreviewFeature(config.LockedDutyPreview, saveConfig),
            autoStore,
            armoireRecord,
            new GlamourNameFeature(),
            new MateriaTotalFeature(config.MateriaTotal, itemTooltipInventoryContext),
            itemTooltipRemark,
            new ShieldOnHpFeature(config.ShieldOnHp),
            new RaiseDispelEnhancementFeature(config.RaiseDispelEnhancement),
            new MultiDockFeature(config.MultiDock, saveConfig, iconBrowser),
            new TimelineConverterFeature(config)
        };
        features.AddRange(privateFeatures);
        return new(
            config,
            featureGate,
            innEntry,
            autoStore,
            marketPurchase,
            npcShopPurchase,
            freeCompanyAction,
            treasureMap,
            armoireRecord,
            itemTooltipRemark,
            mitigationMonitor,
            foodReminder,
            iconBrowser,
            betterUserMacro,
            chatFrameOptimization,
            autoGcSeal,
            itemPriceQuery,
            itemQuery,
            itemPreview,
            itemTooltipInventoryContext,
            nonEntityTargetVisibility,
            aetheryteRouteResolver,
            features);
    }

    public bool IsFavorite(ITreeHouseFeature feature) => config.Favorites.Contains(feature.Info.Id);

    public bool IsConfiguredEnabled(ITreeHouseFeature feature) => config.IsEnabled(feature.Info.Id);

    public void SetFavorite(ITreeHouseFeature feature, bool favorite)
    {
        if (favorite)
        {
            config.Favorites.Add(feature.Info.Id);
        }
        else
        {
            config.Favorites.Remove(feature.Info.Id);
        }
    }

    public bool CanEnable(ITreeHouseFeature feature, out string reasonKey)
    {
        if (feature.Info.RequiresTestQualification)
        {
            return featureGate.CanUseTestFeature(out reasonKey);
        }

        if (feature.Info.RequiresPrivateProvider)
        {
            return featureGate.CanUsePrivateFeature(out reasonKey);
        }

        reasonKey = string.Empty;
        return true;
    }

    public bool SetEnabled(ITreeHouseFeature feature, bool enabled)
    {
        if (enabled && !CanEnable(feature, out _))
        {
            config.SetEnabled(feature.Info.Id, false);
            return false;
        }

        try
        {
            feature.SetEnabled(enabled);
            config.SetEnabled(feature.Info.Id, enabled);
            return true;
        }
        catch (Exception ex)
        {
            config.SetEnabled(feature.Info.Id, feature.IsEnabled);
            DalamudServices.PluginLog.Error(ex, "TreeHouse feature {FeatureId} failed to change state.", feature.Info.Id);
            return false;
        }
    }

    private bool TryRestoreEnabled(ITreeHouseFeature feature)
    {
        if (!config.IsEnabled(feature.Info.Id) || feature.IsEnabled)
        {
            return true;
        }

        if (!CanEnable(feature, out _))
        {
            return false;
        }

        try
        {
            feature.SetEnabled(true);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(
                ex,
                "TreeHouse feature {FeatureId} failed to restore its enabled state.",
                feature.Info.Id);
        }

        return true;
    }

    private void RestorePendingFeatures(IFramework _)
    {
        for (var index = pendingFeatureRestores.Count - 1; index >= 0; index--)
        {
            if (TryRestoreEnabled(pendingFeatureRestores[index]))
            {
                pendingFeatureRestores.RemoveAt(index);
            }
        }

        if (pendingFeatureRestores.Count == 0)
        {
            StopPendingFeatureRestore();
        }
    }

    private void StopPendingFeatureRestore()
    {
        if (!restoreUpdateRegistered)
        {
            return;
        }

        restoreUpdateRegistered = false;
        FrameworkManager.Instance().Unreg(RestorePendingFeatures);
    }

    public void Dispose() => lifetime.Dispose();
}
