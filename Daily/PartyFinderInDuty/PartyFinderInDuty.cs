using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Agent;
using Dalamud.Game.Agent.AgentArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Lumina;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.PublicBridge;
using TreeToolKit;

namespace OmniToolbox.Modules;

[ModuleApiVersion(1)]
public sealed unsafe partial class PartyFinderInDuty
(
    IFeatureAccess           featureGate,
    IPartyFinderShareService shareService
) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Daily,
        RequiresPrivateProvider = true
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "Feature.PartyFinderInDuty.Title",
            ["Description"] = "Feature.PartyFinderInDuty.Description",
            ["Source"] = "Feature.PartyFinderInDuty.Source",
            ["ReadOnly"] = "Feature.PartyFinderInDuty.ReadOnly",
            ["Details"] = "Feature.PartyFinderInDuty.Details",
            ["Remaining"] = "Feature.PartyFinderInDuty.Remaining",
            ["OpenSlots"] = "Feature.PartyFinderInDuty.OpenSlots",
            ["Parties"] = "Feature.PartyFinderInDuty.Parties",
            ["Composition"] = "Feature.PartyFinderInDuty.Composition",
            ["PresentJobs"] = "Feature.PartyFinderInDuty.PresentJobs",
            ["NoJobRestriction"] = "Feature.PartyFinderInDuty.NoJobRestriction",
            ["None"] = "Feature.PartyFinderInDuty.None",
            ["Close"] = "Feature.PartyFinderInDuty.Close"
        });

    private Hook<AgentInterface.Delegates.IsActivatable>                 IsActivatableHook           = null!;
    private Hook<AgentLookingForGroup.Delegates.OpenListing>             OpenListingHook             = null!;
    private Hook<AgentLookingForGroup.Delegates.RequestListingsUpdate>   RequestListingsUpdateHook   = null!;
    private Hook<AgentLookingForGroup.Delegates.RequestCategoryListings> RequestCategoryListingsHook = null!;
    private NativePartyFinderPreview                                     nativePreview               = null!;
    private PartyFinderShareModels.Listing?                              selectedListing;
    private bool                                                         openingSharedDetail;
    private readonly int[]                                               categoryCounts              = new int[17];
    private readonly int[]                                               scopeCounts                 = new int[3];
    private readonly NativeCounterState[]                                categoryNodeStates          = new NativeCounterState[17];
    private readonly bool[]                                              scopeCountVisible           = new bool[3];
    private readonly Dictionary<uint, TabBarRadioButtonNode>             dataCenterButtons           = [];
    private ResNode?                                                     headerNode;
    private PartyFinderShareModels.Listing[]                             viewListings                = [];
    private uint                                                         remoteDataCenterID;
    private uint                                                         headerDataCenterID;
    private int                                                          currentPage;
    private int                                                          filteredListingCount;
    private int                                                          pageListingCount;
    private long                                                         injectedVersion             = -1;
    private long                                                         nextExpirationAt            = long.MaxValue;
    private long                                                         nextSnapshotRetryAt;
    private bool                                                         injectionRequested;
    private bool                                                         categoryCountsRequested     = true;
    private bool                                                         refreshRequested;
    private bool                                                         nativeRefreshRequested;
    private bool                                                         windowOpen;
    private bool                                                         nativeListInjected;
    private bool                                                         nativeCountsCaptured;

    public override string ModuleName => nameof(PartyFinderInDuty);

    protected override void OnStart(FeatureLifetime activation)
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
            throw new InvalidOperationException(reason);

        var agent = AgentLookingForGroup.Instance();
        if (agent == null)
            throw new InvalidOperationException("Party finder agent is unavailable.");
        nativePreview = new();

        IsActivatableHook = ((AgentInterface*)agent)->VirtualTable->HookVFuncFromName(
            nameof(AgentInterface.IsActivatable), (AgentInterface.Delegates.IsActivatable)IsActivatableDetour);
        activation.Add(IsActivatableHook);
        activation.OnStopping(IsActivatableHook.Disable);
        IsActivatableHook.Enable();

        OpenListingHook = DService.Instance().Hook.HookFromMemberFunction(
            typeof(AgentLookingForGroup.MemberFunctionPointers),
            nameof(AgentLookingForGroup.OpenListing),
            (AgentLookingForGroup.Delegates.OpenListing)OpenListingDetour);
        activation.Add(OpenListingHook);
        activation.OnStopping(OpenListingHook.Disable);
        OpenListingHook.Enable();

        RequestListingsUpdateHook = DService.Instance().Hook.HookFromMemberFunction(
            typeof(AgentLookingForGroup.MemberFunctionPointers),
            nameof(AgentLookingForGroup.RequestListingsUpdate),
            (AgentLookingForGroup.Delegates.RequestListingsUpdate)RequestListingsUpdateDetour);
        activation.Add(RequestListingsUpdateHook);
        activation.OnStopping(RequestListingsUpdateHook.Disable);
        RequestListingsUpdateHook.Enable();

        RequestCategoryListingsHook = DService.Instance().Hook.HookFromMemberFunction(
            typeof(AgentLookingForGroup.MemberFunctionPointers),
            nameof(AgentLookingForGroup.RequestCategoryListings),
            (AgentLookingForGroup.Delegates.RequestCategoryListings)RequestCategoryListingsDetour);
        activation.Add(RequestCategoryListingsHook);
        activation.OnStopping(RequestCategoryListingsHook.Disable);
        RequestCategoryListingsHook.Enable();

        var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        activation.OnStopping(addonEvents.Dispose);
        addonEvents.Register(AddonEvent.PostSetup, "LookingForGroup", OnPartyFinderEvent);
        addonEvents.Register(AddonEvent.PostRefresh, "LookingForGroup", OnPartyFinderEvent);
        addonEvents.Register(AddonEvent.PostRequestedUpdate, "LookingForGroup", OnNativeRequestedUpdate);
        addonEvents.Register(AddonEvent.PostSetup, "LookingForGroupDetail", OnPartyFinderDetailEvent);
        addonEvents.Register(AddonEvent.PostRefresh, "LookingForGroupDetail", OnPartyFinderDetailEvent);
        addonEvents.Register(AddonEvent.PreFinalize, "LookingForGroupDetail", OnPartyFinderDetailEvent);
        addonEvents.Register(AddonEvent.PreDraw, "LookingForGroup", OnPartyFinderPreDraw);
        addonEvents.Register(AddonEvent.PreReceiveEvent, "LookingForGroup", OnPartyFinderReceiveEvent);
        addonEvents.Register(AddonEvent.PreHide, "LookingForGroup", OnPartyFinderHidden);
        addonEvents.Register(AddonEvent.PreFinalize, "LookingForGroup", OnPartyFinderClosed);
        var agentLifecycle = DService.Instance().AgentLifecycle;
        agentLifecycle.RegisterListener(AgentEvent.PreReceiveEvent,
            Dalamud.Game.Agent.AgentId.LookingForGroup, OnAgentEvent);
        activation.OnStopping(() => agentLifecycle.UnregisterListener(OnAgentEvent));
        FrameworkManager.Instance().Reg(OnFrameworkUpdate);
        activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));

        var addon = AddonHelper.GetByName<AddonLookingForGroup>("LookingForGroup");
        if (CanOperatePartyFinder() && addon != null && ((AtkUnitBase*)addon)->IsVisible &&
            ((AtkUnitBase*)addon)->IsAddonAndNodesReady())
            BeginViewing();
    }

    protected override void OnStop()
    {
        if (!DalamudServices.IsUnloading)
        {
            CloseSharedDetail();
            if (nativeListInjected)
                ClearNativeList();
            RestoreNativeCountState();
        }
        ReleaseNativeHeader();
        shareService.CancelSnapshotRequest();
        selectedListing     = null;
        openingSharedDetail = false;
        Array.Clear(categoryCounts);
        Array.Clear(scopeCounts);
        injectedVersion         = -1;
        nextSnapshotRetryAt     = 0;
        injectionRequested      = false;
        categoryCountsRequested = false;
        refreshRequested        = false;
        nativeRefreshRequested  = false;
        windowOpen              = false;
        remoteDataCenterID      = 0;
        viewListings            = [];
        nativeCountsCaptured    = false;
        nativeListInjected      = false;
    }

    private bool IsActivatableDetour(AgentInterface* agent) =>
        IsActivatableHook.Original(agent) ||
        agent == (AgentInterface*)AgentLookingForGroup.Instance() && CanUseInDuty();

    private bool OpenListingDetour
    (
        AgentLookingForGroup* agent,
        ulong                 listingID
    )
    {
        if (agent == AgentLookingForGroup.Instance() && UsesSharedData && nativeListInjected)
            return FindSharedListing(listingID) is { } listing && OpenSharedListing(agent, listing);

        selectedListing = null;
        return OpenListingHook.Original(agent, listingID);
    }

    private bool RequestListingsUpdateDetour(AgentLookingForGroup* agent)
    {
        if (agent == AgentLookingForGroup.Instance() && UsesSharedData)
        {
            refreshRequested = true;
            return true;
        }
        return RequestListingsUpdateHook.Original(agent);
    }

    private bool RequestCategoryListingsDetour
    (
        AgentLookingForGroup* agent,
        byte                  category
    )
    {
        if (agent == AgentLookingForGroup.Instance() && UsesSharedData)
        {
            agent->CategoryTab = category;
            currentPage        = 0;
            injectionRequested = true;
            return true;
        }
        return RequestCategoryListingsHook.Original(agent, category);
    }

    private void OnPartyFinderEvent
    (
        AddonEvent type,
        AddonArgs  args
    )
    {
        if (!CanOperatePartyFinder())
            return;
        if (type == AddonEvent.PostSetup && !windowOpen)
            BeginViewing();
        categoryCountsRequested = UsesSharedData;
        if (UsesSharedData && injectedVersion != CurrentSnapshotVersion())
            injectionRequested = true;
    }

    private void OnPartyFinderHidden(AddonEvent _, AddonArgs args)
    {
        windowOpen         = false;
        remoteDataCenterID = 0;
        shareService.CancelSnapshotRequest();
    }

    private void OnPartyFinderClosed
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        shareService.CancelSnapshotRequest();
        headerNode = null;
        dataCenterButtons.Clear();
        headerDataCenterID      = 0;
        nativeCountsCaptured    = false;
        nativeListInjected      = false;
        windowOpen              = false;
        remoteDataCenterID      = 0;
        selectedListing         = null;
        viewListings            = [];
        injectedVersion         = -1;
        nextSnapshotRetryAt     = 0;
        injectionRequested      = false;
        categoryCountsRequested = false;
        refreshRequested        = false;
        nativeRefreshRequested  = false;
        currentPage             = 0;
    }

    private void BeginViewing()
    {
        CloseSharedDetail();
        remoteDataCenterID = 0;
        currentPage        = 0;
        var shared = UsesSharedData;
        if (shared && nativeListInjected)
            ClearNativeList();
        windowOpen              = true;
        nativeRefreshRequested  = !shared;
        refreshRequested        = shared;
        injectedVersion         = -1;
        nextSnapshotRetryAt     = 0;
        injectionRequested      = shared;
        categoryCountsRequested = shared;
        UpdateDataCenterSelection();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!windowOpen || CanOperatePartyFinder())
            return;
        CloseSharedDetail();
        shareService.CancelSnapshotRequest();
        if (nativeListInjected)
            ClearNativeList();
        RestoreNativeCountState();
        ReleaseNativeHeader();
        windowOpen             = false;
        selectedListing        = null;
        refreshRequested       = false;
        nativeRefreshRequested = false;
        nextSnapshotRetryAt    = 0;
        remoteDataCenterID     = 0;
    }

    private void OnAgentEvent
    (
        AgentEvent _,
        AgentArgs  args
    )
    {
        if (!UsesSharedData || args is not AgentReceiveEventArgs
            { EventKind: 1, ValueCount: 1 } received || received.AtkValues == 0)
            return;
        var value = (AtkValue*)received.AtkValues;
        if (value->Type != AtkValueType.Int || value->Int != 17)
            return;
        received.PreventOriginal();
        refreshRequested = true;
    }

    private void OnPartyFinderReceiveEvent
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        if (!UsesSharedData || !nativeListInjected || args is not AddonReceiveEventArgs received)
            return;
        var addon = (AddonLookingForGroup*)args.Addon.Address;
        if (addon == null)
            return;

        if ((AtkEventType)received.AtkEventType == AtkEventType.ListItemClick)
        {
            received.PreventOriginal();
            if (received.AtkEventData == 0)
                return;
            var listIndex = ((AtkEventData*)received.AtkEventData)->ListItemData.SelectedIndex;
            var clickAgent = AgentLookingForGroup.Instance();
            if (clickAgent == null || (uint)listIndex >= (uint)pageListingCount)
                return;
            var listingID = clickAgent->Listings.ListingIds[listIndex];
            if (FindSharedListing(listingID) is { } listing)
                OpenSharedListing(clickAgent, listing);
            return;
        }

        if ((AtkEventType)received.AtkEventType != AtkEventType.ButtonClick)
            return;
        var next = addon->NextPageButton;
        var previous = addon->PreviousPageButton;
        var direction = 0;
        var nextParam = next != null && next->OwnerNode != null
            ? next->OwnerNode->GetEventParam(AtkEventType.ButtonClick)
            : 0;
        var previousParam = previous != null && previous->OwnerNode != null
            ? previous->OwnerNode->GetEventParam(AtkEventType.ButtonClick)
            : 0;
        if (nextParam != 0 && nextParam != previousParam && received.EventParam == (int)nextParam)
            direction = 1;
        else if (previousParam != 0 && nextParam != previousParam && received.EventParam == (int)previousParam)
            direction = -1;
        if (direction == 0)
            return;
        received.PreventOriginal();
        var agent = AgentLookingForGroup.Instance();
        if (agent == null)
            return;
        var capacity = agent->Listings.ListingIds.Length;
        currentPage        = Math.Clamp(currentPage + direction, 0,
            Math.Max(0, (filteredListingCount - 1) / Math.Max(1, capacity)));
        injectionRequested = true;
    }

    private bool CanUseInDuty()
    {
        var service = DService.Instance();
        return CanOperatePartyFinder() && IsInDuty &&
               !service.Condition.IsBetweenAreas && !service.Condition.IsOccupiedInEvent;
    }

    private bool CanOperatePartyFinder()
    {
        var service = DService.Instance();
        var currentWorldID = DalamudServices.PlayerState.CurrentWorld.RowId;
        return IsEnabled && !Activation.IsStopping && !DalamudServices.IsUnloading &&
               featureGate.CanUsePrivateFeature(out _) && service.ClientState.IsLoggedIn &&
               DalamudServices.PlayerState.IsLoaded && currentWorldID != 0 &&
               Sheets.CNWorlds.ContainsKey(currentWorldID) && !service.Condition.IsBetweenAreas;
    }

    private bool IsInDuty => DService.Instance().Condition.Any(
        ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95);

    private bool UsesSharedData => CanOperatePartyFinder() && (IsInDuty || remoteDataCenterID != 0);

    private uint SelectedDataCenterID => remoteDataCenterID == 0
        ? shareService.DataCenterID
        : remoteDataCenterID;

    private readonly record struct NativeCounterState
    (
        bool Visible,
        bool DrawDisabled,
        bool ParentVisible
    );
}
