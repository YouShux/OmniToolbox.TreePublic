using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using TreeTools.Threading;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class AutoSwitchConfiguration : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Automation
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "AutoSwitchConfigurationTitle",
            ["Description"] = "AutoSwitchConfigurationDescription",
            ["Feature.AutoSwitchConfiguration.ActionContent"] = "Feature.AutoSwitchConfiguration.ActionContent",
            ["Feature.AutoSwitchConfiguration.ActionType"] = "Feature.AutoSwitchConfiguration.ActionType",
            ["Feature.AutoSwitchConfiguration.Actions"] = "Feature.AutoSwitchConfiguration.Actions",
            ["Feature.AutoSwitchConfiguration.Add"] = "Feature.AutoSwitchConfiguration.Add",
            ["Feature.AutoSwitchConfiguration.AddAction"] = "Feature.AutoSwitchConfiguration.AddAction",
            ["Feature.AutoSwitchConfiguration.AnyTerritory"] = "Feature.AutoSwitchConfiguration.AnyTerritory",
            ["Feature.AutoSwitchConfiguration.AutoGearset"] = "Feature.AutoSwitchConfiguration.AutoGearset",
            ["Feature.AutoSwitchConfiguration.Cancel"] = "Feature.AutoSwitchConfiguration.Cancel",
            ["Feature.AutoSwitchConfiguration.CollectionUnavailable"] = "Feature.AutoSwitchConfiguration.CollectionUnavailable",
            ["Feature.AutoSwitchConfiguration.CommandsHelp"] = "Feature.AutoSwitchConfiguration.CommandsHelp",
            ["Feature.AutoSwitchConfiguration.Delete"] = "Feature.AutoSwitchConfiguration.Delete",
            ["Feature.AutoSwitchConfiguration.DisableCollection"] = "Feature.AutoSwitchConfiguration.DisableCollection",
            ["Feature.AutoSwitchConfiguration.Edit"] = "Feature.AutoSwitchConfiguration.Edit",
            ["Feature.AutoSwitchConfiguration.EnableAll"] = "Feature.AutoSwitchConfiguration.EnableAll",
            ["Feature.AutoSwitchConfiguration.EnableCollection"] = "Feature.AutoSwitchConfiguration.EnableCollection",
            ["Feature.AutoSwitchConfiguration.Enabled"] = "Feature.AutoSwitchConfiguration.Enabled",
            ["Feature.AutoSwitchConfiguration.Gearset"] = "Feature.AutoSwitchConfiguration.Gearset",
            ["Feature.AutoSwitchConfiguration.GearsetFallback"] = "Feature.AutoSwitchConfiguration.GearsetFallback",
            ["Feature.AutoSwitchConfiguration.MoveDown"] = "Feature.AutoSwitchConfiguration.MoveDown",
            ["Feature.AutoSwitchConfiguration.MoveUp"] = "Feature.AutoSwitchConfiguration.MoveUp",
            ["Feature.AutoSwitchConfiguration.Name"] = "Feature.AutoSwitchConfiguration.Name",
            ["Feature.AutoSwitchConfiguration.NoClassJob"] = "Feature.AutoSwitchConfiguration.NoClassJob",
            ["Feature.AutoSwitchConfiguration.NoCollections"] = "Feature.AutoSwitchConfiguration.NoCollections",
            ["Feature.AutoSwitchConfiguration.NoMatches"] = "Feature.AutoSwitchConfiguration.NoMatches",
            ["Feature.AutoSwitchConfiguration.NoToolbarCommands"] = "Feature.AutoSwitchConfiguration.NoToolbarCommands",
            ["Feature.AutoSwitchConfiguration.Rule"] = "Feature.AutoSwitchConfiguration.Rule",
            ["Feature.AutoSwitchConfiguration.RuleNameDefault"] = "Feature.AutoSwitchConfiguration.RuleNameDefault",
            ["Feature.AutoSwitchConfiguration.Save"] = "Feature.AutoSwitchConfiguration.Save",
            ["Feature.AutoSwitchConfiguration.Search"] = "Feature.AutoSwitchConfiguration.Search",
            ["Feature.AutoSwitchConfiguration.SelectCollection"] = "Feature.AutoSwitchConfiguration.SelectCollection",
            ["Feature.AutoSwitchConfiguration.SelectJob"] = "Feature.AutoSwitchConfiguration.SelectJob",
            ["Feature.AutoSwitchConfiguration.SelectTerritory"] = "Feature.AutoSwitchConfiguration.SelectTerritory",
            ["Feature.AutoSwitchConfiguration.SelectToolbarCommand"] = "Feature.AutoSwitchConfiguration.SelectToolbarCommand",
            ["Feature.AutoSwitchConfiguration.SendCommand"] = "Feature.AutoSwitchConfiguration.SendCommand"
        });

    private readonly Config                         config;
    private readonly System.Action                  saveConfig;
    private readonly IAutoSwitchConfigurationSource source;
    private TaskHelper?                             taskHelper;
    private CancellationTokenSource?                flowCancellation;
    private uint                                    triggeredTerritory;

    public AutoSwitchConfiguration
    (
        Config                         config,
        System.Action                  saveConfig,
        IAutoSwitchConfigurationSource source
    )
    {
        this.config     = config;
        this.saveConfig = saveConfig;
        this.source     = source;
        if (config.MigrateLegacy())
            saveConfig();
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = 30_000
        };
        activation.Add(taskHelper);
        var tasks = taskHelper;
        Activation.Add(() =>
        {
            taskHelper = null;
        });
        Activation.OnStopping(CancelFlow);
        taskHelper.TaskTimeout += _ => CancelFlow();
        taskHelper.TaskGetException += _ => CancelFlow();
        var clientState = DService.Instance().ClientState;
        Activation.OnStopping(() => clientState.TerritoryChanged -= OnTerritoryChanged);
        clientState.TerritoryChanged += OnTerritoryChanged;
        Activation.OnStopping(() => clientState.Login -= OnLogin);
        clientState.Login += OnLogin;
        Activation.OnStopping(() => clientState.Logout -= OnLogout);
        clientState.Logout += OnLogout;
        QueueMatchingRule(clientState.TerritoryType);
    }

    protected override void OnStop() => triggeredTerritory = 0;

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

    private void OnLogout
    (
        int logoutType,
        int logoutCode
    )
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

        flowCancellation = CancellationTokenSource.CreateLinkedTokenSource(Activation.CancellationToken);
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

    private void QueueRule
    (
        Rule              rule,
        uint              territory,
        CancellationToken token
    )
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
                    CancelFlow();
                    return true;
                }

                gearsetID = resolvedGearset;
                if (IsGearsetEquipped(gearsetID, rule.ClassJobID))
                    return true;
                if (!LocalPlayerState.SwitchGearset(resolvedGearset))
                    CancelFlow();
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
                        Activation.Track(collectionTask);
                    }
                    if (!collectionTask.IsCompleted)
                        return false;
                    if (!collectionTask.GetAwaiter().GetResult())
                        CancelFlow();
                    return true;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return true;
                }
                catch (Exception)
                {
                    CancelFlow();
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

    private bool IsCurrentFlow
    (
        CancellationToken token,
        uint              territory
    ) => !token.IsCancellationRequested &&
        IsEnabled && !DalamudServices.IsUnloading && DService.Instance().ClientState.IsLoggedIn &&
        DService.Instance().ClientState.TerritoryType == territory;

    private static bool IsReady() => !DalamudServices.IsUnloading && GameState.IsLoggedIn && LocalPlayerState.Object is not null &&
        GameState.IsTerritoryLoaded && !DService.Instance().Condition.IsBetweenAreas && UIModule.IsScreenReady();

    private static unsafe bool TryResolveGearset
    (
        uint     classJobID,
        int      selectedGearset,
        out byte gearsetID
    )
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

    private static unsafe bool IsGearsetEquipped
    (
        int  gearsetID,
        uint classJobID
    )
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
