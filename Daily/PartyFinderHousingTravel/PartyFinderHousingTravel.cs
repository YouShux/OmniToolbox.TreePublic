using System.Drawing;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Teleport;
using Address = OmniToolbox.Modules.PartyFinderHousingTravel.PartyFinderHousingAddressParser.Address;

namespace OmniToolbox.Modules;

[ModuleApiVersion(1)]
public sealed partial class PartyFinderHousingTravel
(
    ITeleportService teleportService,
    IFeatureAccess   featureGate
) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Daily,
        RequiresPrivateProvider = true,
        ShowsAutoPathStatus = true
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "PartyFinderHousingTravelTitle",
            ["Description"] = "PartyFinderHousingTravelDescription",
            ["Feature.HousingSales.AddressFormat"] = "Feature.HousingSales.AddressFormat",
            ["Feature.PartyFinderHousingTravel.Menu"] = "Feature.PartyFinderHousingTravel.Menu"
        });

    public override string ModuleName => nameof(PartyFinderHousingTravel);

    private readonly object                     listingLock = new();
    private readonly Dictionary<ulong, Listing> listings    = [];
    private HousingMenuEntry?                   menuEntry;
    private bool                                ownsTravel;

    protected override unsafe void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => DService.Instance().PartyFinder.ReceiveListing -= OnReceiveListing);
        activation.OnStopping(() => DService.Instance().ClientState.Logout -= OnLogout);
        activation.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(OnRecruitmentClosed));

        if (!featureGate.CanUsePrivateFeature(out var reason))
            throw new InvalidOperationException(reason);

        menuEntry = new(this);
        ContextMenuManager.Instance().Reg(menuEntry);
        DService.Instance().PartyFinder.ReceiveListing += OnReceiveListing;
        DService.Instance().ClientState.Logout += OnLogout;
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "LookingForGroup", OnRecruitmentClosed);
        var agent = AgentLookingForGroup.Instance();
        if (agent != null && agent->IsAgentActive())
            agent->RequestListingsUpdate();
    }

    protected override void OnStop()
    {
        if (menuEntry != null)
            ContextMenuManager.Instance().Unreg(menuEntry);
        menuEntry = null;
        ClearListings();
        OnInterruptAutomation();
    }

    protected override bool OnInterruptAutomation()
    {
        DalamudServices.Framework.Update -= ObserveTravel;
        if (!ownsTravel)
            return false;

        ownsTravel = false;
        if (!teleportService.IsHousingPlotTravelRunning)
            return false;

        teleportService.Cancel();
        return true;
    }

    private void OnReceiveListing
    (
        IPartyFinderListing          listing,
        IPartyFinderListingEventArgs args
    )
    {
        lock (listingLock)
        {
            listings[listing.Id] = new(listing.ContentId, listing.HomeWorld.RowId,
                listing.CurrentWorld.RowId, listing.Description.TextValue);
        }
    }

    private void OnRecruitmentClosed
    (
        AddonEvent _,
        AddonArgs  args
    ) => ClearListings();

    private void OnLogout
    (
        int type,
        int code
    ) => ClearListings();

    private void ClearListings()
    {
        lock (listingLock)
        {
            listings.Clear();
        }
    }

    private unsafe IReadOnlyList<ContextMenuItem>? CreateMenuItems(ContextMenuOpenedArgs args)
    {
        if (!IsEnabled)
            return null;

        var agent = AgentLookingForGroup.Instance();
        if (agent == null || !agent->IsAgentActive())
            return null;

        var targetContentID = args.TargetCharacter != null
            ? args.TargetCharacter->ContentId
            : args.TargetContentID;
        int targetHomeWorldID = args.TargetCharacter != null
            ? args.TargetCharacter->HomeWorld
            : args.TargetHomeWorldID;
        if (args.AddonName != "LookingForGroup" || targetContentID == 0)
            return null;
        if (!featureGate.CanUsePrivateFeature(out _))
            return null;

        Listing? selected = null;
        ulong selectedID = 0;
        lock (listingLock)
        {
            foreach (var listingID in agent->Listings.ListingIds)
            {
                if (!listings.TryGetValue(listingID, out var listing))
                    continue;
                if (listing.ContentID != targetContentID ||
                    (targetHomeWorldID > 0 && listing.HomeWorldID != targetHomeWorldID))
                    continue;
                if (selected != null && selectedID != listingID)
                    return null;
                selected   = listing;
                selectedID = listingID;
            }
        }
        if (selected == null)
            return null;

        var items = new List<ContextMenuItem>();
        var addresses = PartyFinderHousingAddressParser.Parse(selected.Description, selected.CurrentWorldID);
        foreach (var address in addresses)
        {
            if (!LuminaGetter.TryGetRow<TerritoryType>(address.TerritoryID, out var territory))
                continue;

            var location = string.Format(OmniLoc.Get("Feature.HousingSales.AddressFormat"),
                territory.PlaceName.Value.Name.ToString(), address.Ward, address.Plot);
            var template = OmniLoc.Get(this, "Feature.PartyFinderHousingTravel.Menu").Split("{0}");
            using var builder = new RentedSeStringBuilder();
            var name = builder.Builder.Append(template[0])
                .PushColorRgba(KnownColor.MediumPurple.ToVector4())
                .AppendIcon((uint)BitmapFontIcon.CrossWorld).PopColor()
                .Append(" ").Append(LuminaWrapper.GetWorldName(address.WorldID))
                .Append(" ").Append(location).Append(template[1]).ToReadOnlySeString();
            items.Add(new()
            {
                Name = name,
                Prefix = OmniContextMenu.Prefix,
                OnClicked = _ => Travel(address)
            });
        }
        return items;
    }

    private void Travel(Address address)
    {
        if (!IsEnabled)
            return;
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            return;
        }
        if (!teleportService.TryTravelToHousingPlot(address.WorldID, address.TerritoryID, address.Ward, address.Plot))
        {
            return;
        }

        ownsTravel = true;
        DalamudServices.Framework.Update -= ObserveTravel;
        DalamudServices.Framework.Update += ObserveTravel;
    }

    private void ObserveTravel(IFramework _)
    {
        if (!featureGate.CanUsePrivateFeature(out var reason))
        {
            OnInterruptAutomation();
            return;
        }
        if (teleportService.IsHousingPlotTravelRunning)
            return;

        ownsTravel = false;
        DalamudServices.Framework.Update -= ObserveTravel;
    }

    private sealed class HousingMenuEntry(PartyFinderHousingTravel module) : ContextMenuEntry
    {
        public override string Identifier => nameof(PartyFinderHousingTravel);

        public override IReadOnlyList<ContextMenuItem>? CreateMultiple(ContextMenuOpenedArgs args) => module.CreateMenuItems(args);
    }

    private sealed record Listing
    (
        ulong  ContentID,
        uint   HomeWorldID,
        uint   CurrentWorldID,
        string Description
    );
}
