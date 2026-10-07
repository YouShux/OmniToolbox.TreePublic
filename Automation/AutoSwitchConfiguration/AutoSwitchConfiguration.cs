using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Notifications;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoSwitchConfiguration : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AutoSwitchConfigurationTitle"),
        Description = OmniLoc.Get("AutoSwitchConfigurationDescription"),
        Category = ModuleCategory.Automation
    };

    public override bool HasSettings => true;

    private readonly Config config;
    private readonly System.Action saveConfig;
    private readonly IAutoSwitchConfigurationSource source;
    private TaskHelper? taskHelper;
    private CancellationTokenSource? flowCancellation;
    private uint triggeredTerritory;

    public AutoSwitchConfiguration(Config config, System.Action saveConfig, IAutoSwitchConfigurationSource source)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.source = source;
        if (config.MigrateLegacy())
            saveConfig();
    }

    protected override void OnEnable()
    {
        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = 30_000,
            TimeoutAction = () => Fail("Feature.AutoSwitchConfiguration.Timeout"),
            ExceptionAction = () => Fail("Feature.AutoSwitchConfiguration.Failed")
        };
        var clientState = DService.Instance().ClientState;
        clientState.TerritoryChanged += OnTerritoryChanged;
        clientState.Login += OnLogin;
        clientState.Logout += OnLogout;
        QueueMatchingRule(clientState.TerritoryType);
    }

    protected override void OnDisable()
    {
        var clientState = DService.Instance().ClientState;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        clientState.Login -= OnLogin;
        clientState.Logout -= OnLogout;
        CancelFlow();
        taskHelper?.Dispose();
        taskHelper = null;
        triggeredTerritory = 0;
    }

    protected override bool OnInterruptAutomation()
    {
        if (taskHelper is not { IsBusy: true })
            return false;

        CancelFlow();
        return true;
    }

    private void OnTerritoryChanged(uint territory)
    {
        if (territory != triggeredTerritory)
            QueueMatchingRule(territory);
    }

    private void OnLogin() => QueueMatchingRule(DService.Instance().ClientState.TerritoryType);

    private void OnLogout(int logoutType, int logoutCode)
    {
        CancelFlow();
        triggeredTerritory = 0;
    }

    private void QueueMatchingRule(uint territory)
    {
        CancelFlow();
        triggeredTerritory = territory;
        if (taskHelper is null || !DService.Instance().ClientState.IsLoggedIn || territory == 0)
            return;

        flowCancellation = new();
        var token = flowCancellation.Token;
        taskHelper.Enqueue(() =>
        {
            if (!IsCurrentFlow(token, territory))
                return true;
            if (!IsReady())
                return false;

            var rule = config.Rules.Find(entry => entry.Enabled &&
                (entry.TerritoryID == 0 || entry.TerritoryID == territory));
            if (rule is not null)
                QueueRule(rule.Copy(), territory, token);
            return true;
        });
    }

    private void QueueRule(Rule rule, uint territory, CancellationToken token)
    {
        var tasks = taskHelper!;
        var gearsetID = rule.GearsetID;
        if (rule.ClassJobID != 0)
        {
            tasks.Enqueue(() =>
            {
                if (!IsCurrentFlow(token, territory))
                    return true;
                if (!IsReady())
                    return false;
                if (!TryResolveGearset(rule.ClassJobID, gearsetID, out var resolvedGearset))
                {
                    Fail("Feature.AutoSwitchConfiguration.InvalidGearset");
                    return true;
                }

                gearsetID = resolvedGearset;
                if (IsGearsetEquipped(gearsetID, rule.ClassJobID))
                    return true;
                if (!LocalPlayerState.SwitchGearset(resolvedGearset))
                    Fail("Feature.AutoSwitchConfiguration.InvalidGearset");
                return true;
            });
            tasks.Enqueue(() => !IsCurrentFlow(token, territory) ||
                IsReady() && IsGearsetEquipped(gearsetID, rule.ClassJobID));
        }

        foreach (var step in rule.Actions)
        {
            if (step.Type == ActionKind.SendCommand)
            {
                foreach (var command in step.Command.Split(['\r', '\n'],
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    tasks.DelayNext(500);
                    tasks.Enqueue(() =>
                    {
                        if (!IsCurrentFlow(token, territory))
                            return true;
                        if (!IsReady())
                            return false;

                        ChatManager.Instance().SendMessage(command);
                        return true;
                    });
                }
                continue;
            }

            Task<bool>? collectionTask = null;
            tasks.Enqueue(() =>
            {
                if (!IsCurrentFlow(token, territory))
                    return true;
                if (!IsReady())
                    return false;

                try
                {
                    if (collectionTask is null)
                    {
                        collectionTask = source.SetPluginCollectionEnabledAsync(
                            step.CollectionID, step.Type == ActionKind.EnableCollection, token);
                        // 已取消的流程不再轮询，仍须观察提供方稍后返回的异常。
                        _ = collectionTask.ContinueWith(static task =>
{
    _ = task.Exception;
},
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                    if (!collectionTask.IsCompleted)
                        return false;
                    if (!collectionTask.GetAwaiter().GetResult())
                        Fail("Feature.AutoSwitchConfiguration.CollectionFailed");
                    return true;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return true;
                }
                catch (Exception exception)
                {
                    DService.Instance().Log.Warning(exception, "Auto switch configuration failed to change a plugin collection.");
                    Fail("Feature.AutoSwitchConfiguration.CollectionFailed");
                    return true;
                }
            });
        }
    }

    private void SaveChanges()
    {
        CancelFlow();
        saveConfig();
    }

    private void CancelFlow()
    {
        flowCancellation?.Cancel();
        flowCancellation?.Dispose();
        flowCancellation = null;
        taskHelper?.Abort();
    }

    private void Fail(string key)
    {
        CancelFlow();
        OmniNotifier.Chat(OmniLoc.Get(key));
    }

    private bool IsCurrentFlow(CancellationToken token, uint territory) => !token.IsCancellationRequested &&
        IsEnabled && !DalamudServices.IsUnloading && DService.Instance().ClientState.IsLoggedIn &&
        DService.Instance().ClientState.TerritoryType == territory;

    private static bool IsReady() => !DalamudServices.IsUnloading && GameState.IsLoggedIn && LocalPlayerState.Object is not null &&
        GameState.IsTerritoryLoaded && !DService.Instance().Condition.IsBetweenAreas && UIModule.IsScreenReady();

    private static unsafe bool TryResolveGearset(uint classJobID, int selectedGearset, out byte gearsetID)
    {
        gearsetID = 0;
        if (selectedGearset < 0)
            return LocalPlayerState.TryFindClassJobGearset(classJobID, out gearsetID);

        var gearset = GetGearset(selectedGearset);
        if (gearset == null || gearset->ClassJob != classJobID)
            return false;

        gearsetID = gearset->Id;
        return true;
    }

    private static unsafe bool IsGearsetEquipped(int gearsetID, uint classJobID)
    {
        var module = RaptureGearsetModule.Instance();
        return module != null && module->CurrentGearsetIndex == gearsetID && LocalPlayerState.ClassJob == classJobID;
    }

    private static unsafe RaptureGearsetModule.GearsetEntry* GetGearset(int gearsetID)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null || gearsetID < 0 || gearsetID >= module->Entries.Length)
            return null;

        var gearset = module->GetGearset(gearsetID);
        return gearset == null || gearset->Id != gearsetID ||
            !gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) ||
            gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.MainHandMissing)
            ? null : gearset;
    }
}
