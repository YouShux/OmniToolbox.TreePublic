using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using TreeTools.Threading;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class AutoMount : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Automation
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "AutoMountTitle",
            ["Description"] = "AutoMountDescription",
            ["AutoMountTriggers"] = "Feature.AutoMount.Triggers",
            ["RidingBehavior"] = "Feature.AutoMount.RidingBehavior",
            ["SummonSettings"] = "Feature.AutoMount.SummonSettings",
            ["Mount.Help"] = "Feature.AutoMount.Mount.Help",
            ["Roulette"] = "Feature.AutoMount.Roulette",
            ["SelectedCount"] = "Feature.AutoMount.SelectedCount",
            ["SelectedOnly"] = "Feature.AutoMount.SelectedOnly",
            ["NoMatches"] = "Feature.AutoMount.NoMatches",
            ["ZoneChange"] = "Feature.AutoMount.ZoneChange",
            ["CraftEnd"] = "Feature.AutoMount.CraftEnd",
            ["GatherEnd"] = "Feature.AutoMount.GatherEnd",
            ["CombatEnd"] = "Feature.AutoMount.CombatEnd",
            ["Delay"] = "Feature.AutoMount.Delay",
            ["JumpAfterMount"] = "Feature.AutoMount.JumpAfterMount",
            ["JumpAfterMount.Help"] = "Feature.AutoMount.JumpAfterMount.Help",
            ["WorkMode"] = "Feature.AutoMount.WorkMode",
            ["Blacklist"] = "Feature.AutoMount.Blacklist",
            ["Whitelist"] = "Feature.AutoMount.Whitelist",
            ["WorkMode.Help"] = "Feature.AutoMount.WorkMode.Help",
            ["AutoDismount"] = "Feature.AutoMount.AutoDismount",
            ["AutoDismount.Help"] = "Feature.AutoMount.AutoDismount.Help"
        });

    private readonly Settings config;
    private TaskHelper?       mountTasks;
    private TaskHelper?       dismountTasks;
    private bool              issuingAutomaticAction;

    public AutoMount(Settings config)
    {
        this.config = config;
        config.Delay = Math.Max(0, config.Delay);
        if (config.SelectedMount != 0)
        {
            config.SelectedMounts.Add(config.SelectedMount);
            config.SelectedMount = 0;
        }
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        mountTasks = new() { RetryIntervalMS = 100 };
        activation.Add(mountTasks);
        dismountTasks = new() { RetryIntervalMS = 50, TimeoutMS = 1500 };
        activation.Add(dismountTasks);
        IsActionUsable = IsActionUsableSig.GetDelegate<IsActionUsableDelegate>();

        var services = DService.Instance();
        activation.OnStopping(() => services.ClientState.TerritoryChanged -= OnTerritoryChanged);
        activation.OnStopping(() => services.ClientState.Logout -= OnLogout);
        activation.OnStopping(() => services.Condition.ConditionChange -= OnConditionChanged);
        activation.OnStopping(() => UseActionManager.Instance().Unreg(OnPreUseAction));
        services.ClientState.TerritoryChanged += OnTerritoryChanged;
        services.ClientState.Logout += OnLogout;
        services.Condition.ConditionChange += OnConditionChanged;
        if (!UseActionManager.Instance().RegPreUseAction(OnPreUseAction))
            throw new InvalidOperationException("Automatic mount action registration failed.");
    }

    protected override void OnStop()
    {
        mountTasks = null;
        dismountTasks = null;
    }

    public override bool HasSettings => true;

    public override bool ResetSettings()
    {
        if (config.SelectedMount == 0 && config.SelectedMounts.Count == 0 && config.Delay == 1000 &&
            config.MountWhenZoneChange && config.MountWhenCraftEnd &&
            config.MountWhenGatherEnd && config.MountWhenCombatEnd && config.JumpAfterMount &&
            config.AutoDismount && !config.UseWhitelist && config.BlacklistZones.Count == 0 &&
            config.WhitelistZones.Count == 0)
            return false;

        config.SelectedMount = 0;
        config.SelectedMounts.Clear();
        config.Delay = 1000;
        config.MountWhenZoneChange = true;
        config.MountWhenCraftEnd = true;
        config.MountWhenGatherEnd = true;
        config.MountWhenCombatEnd = true;
        config.JumpAfterMount = true;
        config.AutoDismount = true;
        config.UseWhitelist = false;
        config.BlacklistZones.Clear();
        config.WhitelistZones.Clear();
        mountTasks?.Abort();
        dismountTasks?.Abort();
        return true;
    }

    protected override bool OnInterruptAutomation()
    {
        var busy = mountTasks is { IsBusy: true } || dismountTasks is { IsBusy: true };
        mountTasks?.Abort();
        dismountTasks?.Abort();
        return busy;
    }

    private void OnTerritoryChanged(uint territoryID)
    {
        mountTasks?.Abort();
        dismountTasks?.Abort();
        QueueMount(MountTrigger.ZoneChange, territoryID);
    }

    private void OnLogout(int type, int code)
    {
        mountTasks?.Abort();
        dismountTasks?.Abort();
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (value && flag is ConditionFlag.InCombat or ConditionFlag.Crafting or ConditionFlag.Gathering)
        {
            mountTasks?.Abort();
            return;
        }
        if (value)
            return;

        if (flag == ConditionFlag.Gathering)
            QueueMount(MountTrigger.GatherEnd);
        else if (flag == ConditionFlag.Crafting)
            QueueMount(MountTrigger.CraftEnd);
        else if (flag == ConditionFlag.InCombat)
            QueueMount(MountTrigger.CombatEnd);
    }

    private void QueueMount(MountTrigger trigger, uint? territoryID = null)
    {
        if (mountTasks is null || trigger != MountTrigger.ZoneChange && dismountTasks is { IsBusy: true })
            return;

        var request = new MountRequest(DalamudServices.PlayerState.ContentId,
            territoryID ?? DService.Instance().ClientState.TerritoryType, trigger);
        if (!IsMountRequestValid(request))
            return;

        mountTasks.Abort();
        mountTasks.TimeoutMS = (int)Math.Min(int.MaxValue, 10_000L + config.Delay);
        if (trigger == MountTrigger.ZoneChange)
        {
            mountTasks.Enqueue(() =>
            {
                if (!IsMountRequestValid(request))
                {
                    mountTasks.Abort();
                    return true;
                }
                return dismountTasks is not { IsBusy: true } && IsMountSceneReady(request.TerritoryID);
            }, "Wait for territory", 60_000);
        }
        mountTasks.DelayNext(config.Delay);
        mountTasks.Enqueue(() => TryMount(request), "Summon mount");
        if (!config.JumpAfterMount)
            return;

        mountTasks.Enqueue(() =>
        {
            if (!IsMountSceneReady(request.TerritoryID))
                return false;
            if (!IsMountRequestValid(request) || LocalPlayerState.Instance().IsMoving)
            {
                mountTasks.Abort();
                return true;
            }
            return DService.Instance().Condition[ConditionFlag.Mounted];
        }, "Wait for mount", 5000);
        mountTasks.Enqueue(() => TryTakeOff(request), "Jump after mounting");
        mountTasks.DelayNext(50);
        mountTasks.Enqueue(() => TryTakeOff(request), "Take off after mounting");
    }

    private bool TryMount(MountRequest request)
    {
        var condition = DService.Instance().Condition;
        if (!IsMountSceneReady(request.TerritoryID))
            return false;
        if (!IsMountRequestValid(request) || condition.IsOnMount ||
            condition[ConditionFlag.InCombat] || LocalPlayerState.Instance().IsMoving)
        {
            mountTasks?.Abort();
            return true;
        }
        if (condition.IsOccupiedInEvent || condition.IsCasting || condition[ConditionFlag.Jumping] ||
            condition[ConditionFlag.ExecutingCraftingAction] || condition[ConditionFlag.ExecutingGatheringAction])
            return false;

        var manager = ActionManager.Instance();
        var playerState = PlayerState.Instance();
        if (manager == null || playerState == null)
            return false;
        var selectedMount = 0u;
        var unlockedCount = 0;
        foreach (var mountID in config.SelectedMounts)
        {
            if (mountID == 0 || !playerState->IsMountUnlocked(mountID))
                continue;

            unlockedCount++;
            if (Random.Shared.Next(unlockedCount) == 0)
                selectedMount = mountID;
        }

        var type = selectedMount == 0 ? ActionType.GeneralAction : ActionType.Mount;
        var actionID = selectedMount == 0 ? MOUNT_ROULETTE_ACTION_ID : selectedMount;
        return manager->GetActionStatus(type, actionID) == 0 &&
               UseAutomaticAction(type, actionID);
    }

    private static bool IsMountSceneReady(uint territoryID)
    {
        var services = DService.Instance();
        return GameState.TerritoryType == territoryID && GameState.IsTerritoryLoaded && !services.Condition.IsBetweenAreas &&
               UIModule.IsScreenReady() && services.ObjectTable.LocalPlayer is not null;
    }

    private bool IsMountRequestValid(MountRequest request)
    {
        var services = DService.Instance();
        if (Activation.IsStopping || !services.ClientState.IsLoggedIn || request.ContentID == 0 ||
            request.ContentID != DalamudServices.PlayerState.ContentId ||
            request.TerritoryID != services.ClientState.TerritoryType || services.ClientState.IsPvP ||
            !LuminaGetter.TryGetRow<TerritoryType>(request.TerritoryID, out var territory) || !territory.Mount ||
            (config.UseWhitelist ? !config.WhitelistZones.Contains(request.TerritoryID) :
                config.BlacklistZones.Contains(request.TerritoryID)))
            return false;

        if (request.Trigger == MountTrigger.CombatEnd)
        {
            var fateManager = FateManager.Instance();
            if (fateManager != null && fateManager->CurrentFate != null && fateManager->CurrentFate->Progress < 100)
                return false;
        }

        return request.Trigger switch
        {
            MountTrigger.ZoneChange => config.MountWhenZoneChange,
            MountTrigger.CraftEnd => config.MountWhenCraftEnd,
            MountTrigger.GatherEnd => config.MountWhenGatherEnd,
            MountTrigger.CombatEnd => config.MountWhenCombatEnd,
            _ => false
        };
    }

    private void TryTakeOff(MountRequest request)
    {
        var condition = DService.Instance().Condition;
        var playerState = PlayerState.Instance();
        if (config.JumpAfterMount && IsMountRequestValid(request) && IsMountSceneReady(request.TerritoryID) &&
            playerState != null && playerState->CanFly &&
            condition[ConditionFlag.Mounted] && !condition[ConditionFlag.InFlight] &&
            !condition[ConditionFlag.InCombat] && !condition.IsBetweenAreas && !LocalPlayerState.Instance().IsMoving)
            UseAutomaticAction(ActionType.GeneralAction, JUMP_ACTION_ID);
    }

    private bool UseAutomaticAction(ActionType type, uint actionID)
    {
        issuingAutomaticAction = true;
        try
        {
            return UseActionManager.Instance().UseAction(type, actionID);
        }
        finally
        {
            issuingAutomaticAction = false;
        }
    }

    private enum MountTrigger
    {
        ZoneChange,
        CraftEnd,
        GatherEnd,
        CombatEnd
    }

    private readonly record struct MountRequest(ulong ContentID, uint TerritoryID, MountTrigger Trigger);

    public sealed class Settings
    {
        public uint          SelectedMount;
        public HashSet<uint> SelectedMounts = [];
        public int           Delay = 1000;
        public bool          MountWhenZoneChange = true;
        public bool          MountWhenCraftEnd = true;
        public bool          MountWhenGatherEnd = true;
        public bool          MountWhenCombatEnd = true;
        public bool          JumpAfterMount = true;
        public bool          AutoDismount = true;
        public bool          UseWhitelist;
        public HashSet<uint> BlacklistZones = [];
        public HashSet<uint> WhitelistZones = [];
    }

    #region 常量

    private const uint MOUNT_ROULETTE_ACTION_ID = 9;
    private const uint JUMP_ACTION_ID = 2;

    #endregion
}
