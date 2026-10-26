using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using TreeTools.Threading;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using Aetheryte = Lumina.Excel.Sheets.Aetheryte;
using EventFramework = FFXIVClientStructs.FFXIV.Client.Game.Event.EventFramework;
using EventHandlerModule = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandlerModule;
using NativeEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class VoidMateriaTransmutation(
    Func<bool> canUseFeature,
    Func<uint, bool> tryTeleport,
    Func<bool> isTeleportBusy,
    Action cancelTeleport) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        RequiresPrivateProvider = true,
        Commands =
        [
            new("Feature.VoidMateriaTransmutation.Command", OmniCommandCatalog.Format("materiatransmute"))
        ]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "VoidMateriaTransmutationTitle",
            ["Description"] = "VoidMateriaTransmutationDescription",
            ["Feature.VoidMateriaTransmutation.AlreadyOpen"] = "Feature.VoidMateriaTransmutation.AlreadyOpen",
            ["Feature.VoidMateriaTransmutation.BatchFailed"] = "Feature.VoidMateriaTransmutation.BatchFailed",
            ["Feature.VoidMateriaTransmutation.BatchFinished"] = "Feature.VoidMateriaTransmutation.BatchFinished",
            ["Feature.VoidMateriaTransmutation.BatchStart"] = "Feature.VoidMateriaTransmutation.BatchStart",
            ["Feature.VoidMateriaTransmutation.BatchStop"] = "Feature.VoidMateriaTransmutation.BatchStop",
            ["Feature.VoidMateriaTransmutation.BatchTimedOut"] = "Feature.VoidMateriaTransmutation.BatchTimedOut",
            ["Feature.VoidMateriaTransmutation.Busy"] = "Feature.VoidMateriaTransmutation.Busy",
            ["Feature.VoidMateriaTransmutation.Command"] = "Feature.VoidMateriaTransmutation.Command",
            ["Feature.VoidMateriaTransmutation.NotLoggedIn"] = "Feature.VoidMateriaTransmutation.NotLoggedIn",
            ["Feature.VoidMateriaTransmutation.Open"] = "Feature.VoidMateriaTransmutation.Open",
            ["Feature.VoidMateriaTransmutation.OpenFailed"] = "Feature.VoidMateriaTransmutation.OpenFailed",
            ["Feature.VoidMateriaTransmutation.OpenTimedOut"] = "Feature.VoidMateriaTransmutation.OpenTimedOut",
            ["Feature.VoidMateriaTransmutation.Opening"] = "Feature.VoidMateriaTransmutation.Opening",
            ["Feature.VoidMateriaTransmutation.QuestRequired"] = "Feature.VoidMateriaTransmutation.QuestRequired",
            ["Feature.VoidMateriaTransmutation.TeleportFailed"] = "Feature.VoidMateriaTransmutation.TeleportFailed",
            ["Feature.VoidMateriaTransmutation.Unavailable"] = "Feature.VoidMateriaTransmutation.Unavailable",
            ["TreeHouse.Status.VerificationRequired"] = "TreeHouse.Status.VerificationRequired"
        });

    public override bool HasSettings => true;

    private TaskHelper? taskHelper;
    private uint destinationTerritoryID;
    private bool ownsTeleport;
    private bool ownsWindow;
    private bool hasSceneOverrides;
    private Hook<NativeEventHandler.Delegates.EndEvent>? EndEventHook;
    private static readonly CompSig CreateEventHandlerSig = new("48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 48 89 7C 24 20 41 54 41 56 41 57 48 83 EC 50 8B EA");
    private delegate NativeEventHandler* CreateEventHandlerDelegate(EventHandlerModule* module, uint eventID, GameObject* eventObject);
    private CreateEventHandlerDelegate? CreateEventHandler;
    private static readonly CompSig IsEventHandlerReadySig = new("48 89 5C 24 08 57 48 83 EC 20 F6 41 24 01 48 8B F9 74 ?? 48 8B 99 98 01 00 00");
    private delegate byte IsEventHandlerReadyDelegate(NativeEventHandler* handler);
    private IsEventHandlerReadyDelegate? IsEventHandlerReady;

    protected override void OnStart(FeatureLifetime activation)
    {
        if (!LuminaGetter.TryGetRow<Aetheryte>(DESTINATION_AETHERYTE_ID, out var destination))
            throw new InvalidOperationException("Materia transmutation destination is unavailable.");

        destinationTerritoryID = destination.Territory.RowId;
        var services = DService.Instance();
        var lifetime = Activation;
        lifetime.Add(() =>
        {
            try
            {
                Stop();
            }
            finally
            {
                EndEventHook?.Dispose();
                EndEventHook = null;
                taskHelper = null;
                DisposeBatch();
            }
        });
        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = 10_000
        };
        lifetime.Add(taskHelper);
        taskHelper.TaskTimeout += _ => Fail("Feature.VoidMateriaTransmutation.OpenTimedOut");
        taskHelper.TaskGetException += _ => Fail("Feature.VoidMateriaTransmutation.OpenFailed");
        InitializeBatch();
        lifetime.OnStopping(() => services.AddonLifecycle.UnregisterListener(OnWindowEvent));
        services.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "TradeMultiple", OnWindowEvent);
        services.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "TradeMultiple", OnWindowEvent);
        services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "TradeMultiple", OnWindowEvent);
        lifetime.OnStopping(() => services.ClientState.TerritoryChanged -= OnTerritoryChanged);
        services.ClientState.TerritoryChanged += OnTerritoryChanged;
        lifetime.OnStopping(() => services.ClientState.Logout -= OnLogout);
        services.ClientState.Logout += OnLogout;
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 500))
            throw new InvalidOperationException("Materia transmutation update registration failed.");

        lifetime.OnStopping(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
    }

    public override bool DrawSettings()
    {
        var reason = GetUnavailableReason();
        using (ImRaii.Disabled(reason is not null))
        {
            if (OmniControls.SmallButton(OmniLoc.Get(this, "Feature.VoidMateriaTransmutation.Open"), false))
                _ = DalamudServices.Framework.RunOnFrameworkThread(RequestOpen);
        }

        return false;
    }

    public override bool TryHandleCommand(string command, string arguments)
    {
        if (!string.Equals(command, "materiatransmute", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(arguments))
            return false;

        _ = DalamudServices.Framework.RunOnFrameworkThread(RequestOpen);
        return true;
    }

    protected override bool OnInterruptAutomation()
    {
        if (batchTasks?.IsBusy == true)
        {
            batchTasks.Abort();
            return true;
        }

        if (taskHelper?.IsBusy != true)
            return false;

        Stop();
        return true;
    }

    private void RequestOpen()
    {
        if (GetUnavailableReason() is { } reason)
        {
            NotifyHelper.ToastError(reason);
            return;
        }

        var helper = taskHelper;
        if (helper is null)
            return;

        if (DService.Instance().ClientState.TerritoryType != destinationTerritoryID)
        {
            if (!tryTeleport(DESTINATION_AETHERYTE_ID))
            {
                Fail("Feature.VoidMateriaTransmutation.TeleportFailed");
                return;
            }

            ownsTeleport = true;
        }

        helper.Enqueue(WaitForDestination, timeoutMS: 90_000);
        helper.Enqueue(PrepareEventHandler);
        helper.Enqueue(RequestWindow);
        helper.Enqueue(() => ownsWindow);
    }

    private bool WaitForDestination()
    {
        if (ownsTeleport && isTeleportBusy())
            return false;

        ownsTeleport = false;
        return CanOpenInCurrentTerritory();
    }

    private bool CanOpenInCurrentTerritory()
    {
        if (!IsEnabled || !canUseFeature())
        {
            Fail("TreeHouse.Status.VerificationRequired");
            return false;
        }

        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn)
        {
            Stop();
            return false;
        }

        if (services.Condition.IsBetweenAreas || !UIModule.IsScreenReady() || !DalamudServices.PlayerState.IsLoaded)
            return false;

        if (services.ClientState.TerritoryType != destinationTerritoryID)
        {
            Fail("Feature.VoidMateriaTransmutation.TeleportFailed");
            return false;
        }

        if (services.Condition[ConditionFlag.InCombat])
        {
            Fail("Feature.VoidMateriaTransmutation.Busy");
            return false;
        }

        return !services.Condition.IsOccupiedInEvent && !services.Condition.IsCasting;
    }

    private bool PrepareEventHandler()
    {
        if (!CanOpenInCurrentTerritory())
            return false;

        var events = EventFramework.Instance();
        if (events == null)
            return false;

        var handler = events->GetEventHandlerById(TRANSMUTATION_EVENT_ID);
        if (handler == null)
        {
            CreateEventHandler ??= CreateEventHandlerSig.GetDelegate<CreateEventHandlerDelegate>();
            handler = CreateEventHandler(&events->EventHandlerModule, TRANSMUTATION_EVENT_ID, null);
            if (handler == null)
                throw new InvalidOperationException("Native materia event handler creation failed.");
        }

        IsEventHandlerReady ??= IsEventHandlerReadySig.GetDelegate<IsEventHandlerReadyDelegate>();
        if (IsEventHandlerReady(handler) == 0)
            return false;

        if (!hasSceneOverrides)
        {
            if (events->LuaState == null)
                return false;

            EndEventHook ??= handler->VirtualTable->HookVFuncFromName(
                nameof(NativeEventHandler.EndEvent), (NativeEventHandler.Delegates.EndEvent)EndEventDetour);
            Activation.OnStopping(EndEventHook.Disable);
            EndEventHook.Enable();
            events->LuaState->DoString(APPLY_SCENE_OVERRIDES, nameof(VoidMateriaTransmutation));
            hasSceneOverrides = true;
        }

        return true;
    }

    private bool RequestWindow()
    {
        if (!CanOpenInCurrentTerritory())
            return false;

        new EventStartPackt(LocalPlayerState.EntityID, TRANSMUTATION_EVENT_ID).Send();
        return true;
    }

    private void OnWindowEvent(AddonEvent type, AddonArgs args)
    {
        if (type == AddonEvent.PostSetup && hasSceneOverrides)
        {
            ownsWindow = true;
        }
        else if (type == AddonEvent.PostDraw)
        {
            UpdateBatchButton((AtkUnitBase*)args.Addon.Address);
        }
        else if (type == AddonEvent.PreFinalize)
        {
            ownsWindow = false;
            batchButton = null;
        }
    }

    private void EndEventDetour(NativeEventHandler* handler, bool a2, uint a3, uint exitReason)
    {
        var restore = hasSceneOverrides && handler != null && handler->Info.EventId.Id == TRANSMUTATION_EVENT_ID;
        var hook = EndEventHook ?? throw new InvalidOperationException("Materia event completion hook is unavailable.");
        hook.Original(handler, a2, a3, exitReason);
        if (restore)
        {
            batchTasks?.Abort();
            ReleaseBatchButton();
            RestoreSceneOverrides();
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if ((taskHelper?.IsBusy == true || hasSceneOverrides) &&
            (!canUseFeature() || !DService.Instance().ClientState.IsLoggedIn))
            Stop();
    }

    private void OnTerritoryChanged(uint _)
    {
        if (hasSceneOverrides)
            Stop();
    }

    private void OnLogout(int logoutType, int logoutCode) => Stop();

    private string? GetUnavailableReason()
    {
        if (!IsEnabled || !canUseFeature())
            return OmniLoc.Get("TreeHouse.Status.VerificationRequired");

        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn || !DalamudServices.PlayerState.IsLoaded)
            return OmniLoc.Get(this, "Feature.VoidMateriaTransmutation.NotLoggedIn");

        if (!QuestManager.IsQuestComplete(UNLOCK_QUEST_ID))
            return string.Format(OmniLoc.Get(this, "Feature.VoidMateriaTransmutation.QuestRequired"),
                LuminaWrapper.GetQuestName(UNLOCK_QUEST_ID));

        if (taskHelper?.IsBusy == true)
            return OmniLoc.Get(this, "Feature.VoidMateriaTransmutation.Opening");

        var condition = services.Condition;
        if (isTeleportBusy() || !UIModule.IsScreenReady() || condition.IsBetweenAreas ||
            condition.IsOccupiedInEvent || condition.IsOnMount || condition.IsCasting || condition[ConditionFlag.InCombat])
            return OmniLoc.Get(this, "Feature.VoidMateriaTransmutation.Busy");

        var agent = AgentTradeMultiple.Instance();
        if (agent == null || UpstreamOpcode.EventStartOpcode == 0)
            return OmniLoc.Get(this, "Feature.VoidMateriaTransmutation.Unavailable");

        return agent->IsAgentActive()
            ? OmniLoc.Get(this, "Feature.VoidMateriaTransmutation.AlreadyOpen")
            : null;
    }

    private void Stop()
    {
        batchTasks?.Abort();
        ReleaseBatchButton();
        taskHelper?.Abort();
        if (ownsTeleport)
        {
            ownsTeleport = false;
            if (isTeleportBusy())
                cancelTeleport();
        }

        try
        {
            if (ownsWindow)
            {
                ownsWindow = false;
                var agent = AgentTradeMultiple.Instance();
                if (agent != null && agent->IsAgentActive())
                    agent->Hide();
            }
        }
        finally
        {
            RestoreSceneOverrides();
        }
    }

    private void RestoreSceneOverrides()
    {
        if (!hasSceneOverrides)
            return;

        hasSceneOverrides = false;
        var events = EventFramework.Instance();
        if (events == null || events->LuaState == null)
            return;

        try
        {
            events->LuaState->DoString(RESTORE_SCENE_OVERRIDES, nameof(VoidMateriaTransmutation));
        }
        catch (Exception exception)
        {
            DalamudServices.PluginLog.Warning(exception, "Failed to restore materia transmutation scenes.");
        }
    }

    private void Fail(string reasonKey)
    {
        Stop();
        NotifyHelper.ToastError(OmniLoc.Get(reasonKey));
    }

    #region 常量

    private const uint UNLOCK_QUEST_ID = 66999;
    private const uint TRANSMUTATION_EVENT_ID = 0x000B00AE;
    private const uint DESTINATION_AETHERYTE_ID = 53;
    private const string APPLY_SCENE_OVERRIDES = """
        local cls = CmnDefMateriaSlot
        assert(type(cls) == "table", "Materia scene class is unavailable")
        assert(rawget(cls, "__OmniMateriaScenes") == nil, "Materia scene overrides are already active")
        local names = { "OnScene00000", "OnScene00001", "OnScene00002", "OnScene00003", "OnScene00006", "OnScene00007" }
        local saved = { originals = {}, replacements = {} }
        for _, name in ipairs(names) do
            local original = rawget(cls, name)
            assert(type(original) == "function", "Materia scene function is unavailable: " .. name)
            saved.originals[name] = original
        end
        local function selection(self, player, npc)
            return self:MateriaTrade()
        end
        local function result(self, player, npc, ...)
            return ...
        end
        for _, name in ipairs(names) do
            local replacement = name == "OnScene00000" and selection or result
            saved.replacements[name] = replacement
            cls[name] = replacement
        end
        cls.__OmniMateriaScenes = saved
        """;
    private const string RESTORE_SCENE_OVERRIDES = """
        local cls = CmnDefMateriaSlot
        local saved = type(cls) == "table" and rawget(cls, "__OmniMateriaScenes")
        if type(saved) == "table" then
            for name, original in pairs(saved.originals) do
                if rawget(cls, name) == saved.replacements[name] then
                    cls[name] = original
                end
            end
            cls.__OmniMateriaScenes = nil
        end
        """;

    #endregion
}
