using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Verification;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;
using OmenTools.Threading.TaskHelper.Enums;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

public sealed class AutoGcSealFeature : TreeHouseFeatureBase
{
    private const uint UpdateIntervalMs = 100;

    private readonly AutoGcSealConfig config;
    private readonly Action saveConfig;
    private readonly FeatureGate featureGate;
    private readonly AutoGcSealInterop interop = new();
    private readonly AutoGcSealDriver driver;
    private FeatureLifetime? runtimeLifetime;
    private TaskHelper? taskHelper;
    private AutoGcSealNativeUi? window;

    public AutoGcSealFeature(
        AutoGcSealConfig config,
        Action saveConfig,
        FeatureGate featureGate)
        : base(new(
            "auto-gc-seal",
            "Feature.AutoGcSeal.Title",
            "Feature.AutoGcSeal.Description",
            TreeHouseCategory.InterfaceOperation,
            true))
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.featureGate = featureGate;
        driver = new(config, interop);
        if (AutoGcSealPanel.NormalizeConfig(config))
        {
            saveConfig();
        }
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => AutoGcSealPanel.Draw(
        config,
        IsEnabled,
        OpenSupply,
        OpenExchange);

    protected override void OnEnable()
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var lifetime = new FeatureLifetime();
        try
        {
            var nativeWindow = new AutoGcSealNativeUi(config, saveConfig);
            window = nativeWindow;
            lifetime.Add(nativeWindow.Dispose);

            var helper = new TaskHelper
            {
                RetryIntervalMS = (int)UpdateIntervalMs,
                TimeoutMS = int.MaxValue
            };
            taskHelper = helper;
            lifetime.Add(() =>
            {
                driver.Stop();
                interop.RestoreProtectedRows();
                helper.Abort();
                helper.Dispose();
            });

            if (!FrameworkManager.Instance().Reg(OnUpdate, UpdateIntervalMs))
            {
                throw new InvalidOperationException("Auto GC seal framework registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnUpdate));
            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PostSetup, "GrandCompanySupplyList", OnSupplyListAddon);
            addonEvents.Register(AddonEvent.PostUpdate, "GrandCompanySupplyList", OnSupplyListAddon);
            addonEvents.Register(AddonEvent.PostDraw, "GrandCompanySupplyList", OnSupplyListAddon);
            addonEvents.Register(AddonEvent.PostReceiveEvent, "GrandCompanySupplyList", OnSupplyListAddon);
            addonEvents.Register(AddonEvent.PreFinalize, "GrandCompanySupplyList", OnSupplyListAddon);
            addonEvents.Register(AddonEvent.PostSetup, "GrandCompanySupplyReward", OnSupplyRewardAddon);
            addonEvents.Register(AddonEvent.PostUpdate, "GrandCompanySupplyReward", OnSupplyRewardAddon);
            addonEvents.Register(AddonEvent.PostReceiveEvent, "GrandCompanySupplyReward", OnSupplyRewardAddon);
            addonEvents.Register(AddonEvent.PostSetup, "SelectYesno", OnSelectYesNoAddon);
            addonEvents.Register(AddonEvent.PostSetup, "SelectString", OnInteractionAddon);
            addonEvents.Register(AddonEvent.PostSetup, "SelectIconString", OnInteractionAddon);
            addonEvents.Register(AddonEvent.PostSetup, "GrandCompanyExchange", OnInteractionAddon);
            runtimeLifetime = lifetime;
            OnUpdate(DalamudServices.Framework);
        }
        catch
        {
            lifetime.Dispose();
            taskHelper = null;
            window = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            taskHelper = null;
            window = null;
        }
    }

    private void OnUpdate(IFramework _)
    {
        interop.RefreshProtectedRows(config);
        driver.HandleSupplyReward();
        RefreshWindow();
    }

    private void OnSupplyListAddon(AddonEvent type, AddonArgs args)
    {
        if (type == AddonEvent.PreFinalize)
        {
            interop.ClearProtectedRowCache();
            return;
        }

        interop.RefreshProtectedRows(config);
        if (type == AddonEvent.PostReceiveEvent &&
            interop.TryCaptureProtectedSelection(config, args, out var item))
        {
            driver.RecordProtectedSelection(item);
        }
    }

    private void OnSupplyRewardAddon(AddonEvent _, AddonArgs __) => driver.HandleSupplyReward();

    private void OnSelectYesNoAddon(AddonEvent _, AddonArgs __) => driver.HandleSelectYesNo();

    private void OnInteractionAddon(AddonEvent _, AddonArgs __)
    {
        if (driver.IsActive)
        {
            driver.Tick();
        }
    }

    private void StartAutomatic()
    {
        if (taskHelper is null)
        {
            return;
        }

        taskHelper.Abort();
        driver.BeginAutomatic();
        EnqueueDriver();
    }

    private void StopAutomatic()
    {
        driver.Stop();
        taskHelper?.Abort();
    }

    public void OpenSupply()
    {
        if (taskHelper is null)
        {
            return;
        }

        if (driver.IsRunning)
        {
            driver.RequestSupply();
            return;
        }

        taskHelper.Abort();
        driver.BeginOpenSupply();
        EnqueueDriver();
    }

    public void OpenExchange()
    {
        if (taskHelper is null)
        {
            return;
        }

        taskHelper.Abort();
        driver.BeginOpenExchange();
        EnqueueDriver();
    }

    private void EnqueueDriver() => taskHelper!.Enqueue(
        driver.Tick,
        "Auto GC seal operation",
        timeoutMS: int.MaxValue,
        timeoutBehaviour: TaskAbortBehaviour.AbortCurrent,
        exceptionBehaviour: TaskAbortBehaviour.AbortCurrent,
        timeoutAction: driver.Stop,
        exceptionAction: driver.Stop);

    private void RefreshWindow()
    {
        if (window is null || !interop.TryGetWindowContext(out var currentSeals))
        {
            window?.Close();
            return;
        }

        window.UpdateState(
            new(
                currentSeals,
                config.ExchangeThreshold,
                config.ReservedSeals,
                config.ExchangeItemId,
                LuminaWrapper.GetItemName(config.ExchangeItemId),
                config.CustomExcludedItemIds.Count,
                driver.IsRunning),
            StartAutomatic,
            StopAutomatic,
            OpenSupply,
            OpenExchange);
        if (!window.IsOpen)
        {
            window.Open();
        }
    }
}
