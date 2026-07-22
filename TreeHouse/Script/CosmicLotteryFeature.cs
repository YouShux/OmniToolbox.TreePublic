using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Network;
using FFXIVClientStructs.FFXIV.Client.Network;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Verification;
using OmenTools;
using OmenTools.Info.Game.Enums;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;

namespace OmniToolbox.TreeHouse.Script;

public sealed unsafe class CosmicLotteryFeature : TreeHouseFeatureBase
{
    private const int RuntimeTickIntervalMs = 100;

    private readonly CosmicLotteryConfig config;
    private readonly FeatureGate featureGate;
    private FeatureLifetime? runtimeLifetime;
    private TaskHelper? taskHelper;
    private CosmicLotteryDriver? driver;
    private Hook<PacketDispatcher.Delegates.HandleInventoryItemUpdatePacket>? inventoryUpdateHook;

    public CosmicLotteryFeature(CosmicLotteryConfig config, FeatureGate featureGate) : base(new(
        "cosmic-lottery",
        "Feature.CosmicLottery.Title",
        "Feature.CosmicLottery.Description",
        TreeHouseCategory.Script,
        true,
        true))
    {
        this.config = config;
        this.featureGate = featureGate;
        CosmicLotteryPanel.Normalize(config);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = CosmicLotteryPanel.Draw(config);
        if (changed)
        {
            driver?.OnConfigChanged();
        }

        return changed;
    }

    protected override void OnEnable()
    {
        if (!featureGate.CanUseTestFeature(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var lifetime = new FeatureLifetime();
        try
        {
            taskHelper = new() { TimeoutMS = 5_000 };
            driver = new(config, taskHelper, CanRun);
            inventoryUpdateHook = DService.Instance().Hook.HookFromAddress<
                PacketDispatcher.Delegates.HandleInventoryItemUpdatePacket>(
                PacketDispatcher.MemberFunctionPointers.HandleInventoryItemUpdatePacket,
                OnInventoryItemUpdate);
            lifetime.Add(inventoryUpdateHook.Dispose);
            inventoryUpdateHook.Enable();

            if (!ExecuteCommandManager.Instance().RegPost(OnPostExecuteCommand))
            {
                throw new InvalidOperationException("Cosmic lottery inventory event registration failed.");
            }

            lifetime.Add(() => ExecuteCommandManager.Instance().Unreg(OnPostExecuteCommand));
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, RuntimeTickIntervalMs))
            {
                throw new InvalidOperationException("Cosmic lottery update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            DalamudServices.PluginInterface.UiBuilder.Draw += DrawOverlay;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawOverlay);
            runtimeLifetime = lifetime;
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
        DService.Instance().ClientState.IsLoggedIn &&
        CosmicLotteryNativeUi.IsLotteryHudReady();

    private void OnFrameworkUpdate(IFramework _) => driver?.Tick();

    private void OnPostExecuteCommand(
        ExecuteCommandFlag command,
        uint param1,
        uint param2,
        uint param3,
        uint param4)
    {
        if (command == ExecuteCommandFlag.RefreshInventory)
        {
            driver?.OnInventoryRefresh();
        }
    }

    private void OnInventoryItemUpdate(uint targetId, InventoryItemPacket* packet)
    {
        inventoryUpdateHook!.Original(targetId, packet);
        if (packet == null)
        {
            return;
        }

        try
        {
            driver?.OnInventoryItemUpdate((InventoryType)packet->InventoryType, packet->ItemId);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Cosmic lottery inventory update processing failed.");
        }
    }

    private void DrawOverlay()
    {
        if (CanRun() && driver is not null)
        {
            CosmicLotteryNativeUi.Draw(driver);
        }
    }

    private void ReleaseRuntime(FeatureLifetime? lifetime)
    {
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            driver?.Reset();
            taskHelper?.Dispose();
            inventoryUpdateHook = null;
            driver = null;
            taskHelper = null;
        }
    }
}
