using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.PartyFinder.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Network;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.PublicBridge;

namespace OmniToolbox.Modules;

public sealed partial class PartyFinderInDuty
{
    private unsafe void OnPartyFinderPreDraw
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        if (!CanOperatePartyFinder())
            return;

        var addon = (AddonLookingForGroup*)args.Addon.Address;
        if (addon == null || !((AtkUnitBase*)addon)->IsAddonAndNodesReady())
            return;

        if (!windowOpen)
            BeginViewing();
        EnsureNativeHeader(addon);
        if (!UsesSharedData)
        {
            if (nativeListInjected || shareService.IsLoading)
                SwitchToNativeData();
            if (nativeRefreshRequested)
            {
                nativeRefreshRequested = false;
                var agent = AgentLookingForGroup.Instance();
                if (agent != null)
                    RequestListingsUpdateHook.Original(agent);
            }
            return;
        }

        CaptureNativeCountState(addon);
        var now = Environment.TickCount64;
        if (!shareService.IsLoading && !shareService.IsConnected && now >= nextSnapshotRetryAt)
        {
            refreshRequested    = true;
            nextSnapshotRetryAt = now + 5_000;
        }
        if (refreshRequested)
        {
            var dataCenterID = SelectedDataCenterID;
            if (dataCenterID != 0 && shareService.IsConfigured)
            {
                refreshRequested = false;
                currentPage      = 0;
                Activation.Track(shareService.RefreshSnapshotAsync(dataCenterID, Activation.CancellationToken));
            }
        }

        var desiredVersion = CurrentSnapshotVersion();
        if ((injectionRequested || injectedVersion != desiredVersion ||
             DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= nextExpirationAt) && TryInjectSnapshot())
        {
            injectedVersion         = desiredVersion;
            injectionRequested      = false;
            categoryCountsRequested = true;
        }

        if (categoryCountsRequested)
        {
            UpdateNativeCounts(addon);
            categoryCountsRequested = false;
        }
        ApplyNativeCounts(addon);
        EnsureNativeCountsVisible(addon);
    }

    private unsafe void OnNativeRequestedUpdate
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        if (!windowOpen || !UsesSharedData || !nativeListInjected)
            return;
        var addon = (AddonLookingForGroup*)args.Addon.Address;
        if (addon == null || !((AtkUnitBase*)addon)->IsAddonAndNodesReady())
            return;
        UpdateNativeCounts(addon);
        ApplyNativeCounts(addon);
        EnsureNativeCountsVisible(addon);
    }

    private unsafe void ClearNativeList()
    {
        var addon = AddonHelper.GetByName<AddonLookingForGroup>("LookingForGroup");
        if (addon == null || !((AtkUnitBase*)addon)->IsAddonAndNodesReady())
            return;
        var proxy = InfoProxyCrossRealm.Instance();
        var agent = AgentLookingForGroup.Instance();
        if (proxy == null || agent == null)
            return;
        ClearAgentListingState(agent);
        ((InfoProxyInterface*)proxy)->ClearListData();
        SubmitSegment([], 0, true, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        ClearListItems(addon->StandardViewList);
        ClearListItems(addon->CompactViewList);
        Array.Clear(categoryCounts);
        Array.Clear(scopeCounts);
        injectedVersion         = -1;
        injectionRequested      = false;
        categoryCountsRequested = false;
        filteredListingCount    = 0;
        pageListingCount        = 0;
        nextExpirationAt        = long.MaxValue;
        viewListings            = [];
        UpdateNativeCounts(addon);
        ApplyNativeCounts(addon);
        nativeListInjected = false;
    }

    private static unsafe void ClearListItems(AtkComponentList* list)
    {
        if (list == null)
            return;
        list->SetItemCount(0);
        list->UpdateListItems();
    }

    private unsafe bool TryInjectSnapshot()
    {
        var proxy = InfoProxyCrossRealm.Instance();
        var agent = AgentLookingForGroup.Instance();
        if (proxy == null || agent == null)
            return false;
        if (shareService.SnapshotDataCenterID != SelectedDataCenterID ||
            !shareService.IsConnected && shareService.ListingCount == 0)
        {
            return false;
        }

        var nativeCapacity = agent->Listings.ListingIds.Length;
        if (nativeCapacity == 0)
        {
            return false;
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rawSnapshot = shareService.GetSnapshot();
        var snapshot = rawSnapshot
            .Where(listing => IsActiveListing(listing, now))
            .OrderByDescending(listing => listing.ObservedAt)
            .DistinctBy(listing => listing.ID)
            .ToArray();
        for (var tab = 0; tab < scopeCounts.Length; tab++)
            scopeCounts[tab] = snapshot.Count(listing => MatchesSearchArea(listing, (byte)tab));
        nextExpirationAt = snapshot.Length == 0
            ? long.MaxValue
            : snapshot.Min(listing => listing.ObservedAt + listing.SecondsRemaining);
        var searchAreaSource = snapshot.Where(listing => MatchesSearchArea(listing, agent->SearchAreaTab)).ToArray();
        for (var tab = 0; tab < categoryCounts.Length; tab++)
            categoryCounts[tab] = searchAreaSource.Count(listing => MatchesCurrentCategory(listing.Category, (byte)tab));
        var filtered = searchAreaSource
            .Where(listing => MatchesCurrentCategory(listing.Category, agent->CategoryTab))
            .OrderByDescending(listing => (long)listing.SecondsRemaining + listing.ObservedAt)
            .ThenByDescending(listing => listing.ObservedAt)
            .ToArray();
        viewListings         = filtered;
        filteredListingCount = filtered.Length;
        currentPage          = Math.Clamp(currentPage, 0, Math.Max(0, (filtered.Length - 1) / nativeCapacity));
        var source = filtered.Skip(currentPage * nativeCapacity).Take(nativeCapacity).ToArray();
        pageListingCount = source.Length;

        var addon = AddonHelper.GetByName<AddonLookingForGroup>("LookingForGroup");
        if (addon != null && ((AtkUnitBase*)addon)->IsAddonAndNodesReady())
        {
            ClearListItems(addon->StandardViewList);
            ClearListItems(addon->CompactViewList);
        }
        ClearAgentListingState(agent);
        ((InfoProxyInterface*)proxy)->ClearListData();
        for (var offset = 0; offset < source.Length; offset += 4)
        {
            var count = Math.Min(4, source.Length - offset);
            SubmitSegment(source.AsSpan(offset, count), (ushort)(offset / 4 + 1), false, now);
        }
        SubmitSegment([], 0, true, now);
        agent->NumberOfListingsDisplayed = checked((byte)source.Length);
        nativeListInjected               = true;
        return true;
    }


    private unsafe void SubmitSegment
    (
        ReadOnlySpan<PartyFinderShareModels.Listing> listings,
        ushort                                       segmentIndex,
        bool                                         final,
        long                                         now
    )
    {
        var packet = new byte[sizeof(ServerIpcSegment<CrossRealmListingSegmentPacket>)];
        var payload = packet.AsSpan(sizeof(ServerIpcSegmentHeader));
        var header = packet.AsSpan(0, sizeof(ServerIpcSegmentHeader));
        BinaryPrimitives.WriteUInt16LittleEndian(header, 0x14);
        BinaryPrimitives.WriteInt32LittleEndian(header[0x0C..], checked((int)now));
        BinaryPrimitives.WriteUInt32LittleEndian(payload, final ? 0x00F4_240U : 0U);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[0x0C..], segmentIndex);

        for (var index = 0; index < listings.Length; index++)
            WriteListing(payload[(0x10 + index * 0x190)..], listings[index], now);

        fixed (byte* value = packet)
            shareService.ReceiveSharedListings(InfoProxyCrossRealm.Instance(),
                (ServerIpcSegment<CrossRealmListingSegmentPacket>*)value);
    }

    private static void WriteListing
    (
        Span<byte>                     entry,
        PartyFinderShareModels.Listing listing,
        long                           now
    )
    {
        BinaryPrimitives.WriteUInt64LittleEndian(entry, ulong.Parse(listing.ID, CultureInfo.InvariantCulture));
        BinaryPrimitives.WriteUInt64LittleEndian(entry[0x10..], ParseContentID(listing.LeaderContentID));
        BinaryPrimitives.WriteUInt32LittleEndian(entry[0x1C..], listing.Category);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[0x20..], listing.DutyID);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[0x22..], checked((ushort)listing.DutyType));
        BinaryPrimitives.WriteUInt16LittleEndian(entry[0x2E..], checked((ushort)listing.CreatedWorldID));
        entry[0x38] = listing.Objective;
        entry[0x39] = listing.BeginnersWelcome ? (byte)1 : (byte)0;
        entry[0x3A] = listing.Conditions;
        entry[0x3B] = listing.DutyFinderSettings;
        entry[0x3C] = listing.LootRule;
        BinaryPrimitives.WriteInt32LittleEndian(entry[0x40..], listing.LastServerRestart);
        var elapsed = Math.Max(0, now - listing.ObservedAt);
        var remaining = listing.SecondsRemaining > elapsed ? listing.SecondsRemaining - elapsed : 0;
        BinaryPrimitives.WriteUInt16LittleEndian(entry[0x44..], checked((ushort)Math.Min(remaining, ushort.MaxValue)));
        BinaryPrimitives.WriteUInt16LittleEndian(entry[0x4C..], listing.MinimumItemLevel);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[0x4E..], checked((ushort)listing.HomeWorldID));
        BinaryPrimitives.WriteUInt16LittleEndian(entry[0x50..], checked((ushort)listing.CurrentWorldID));
        entry[0x52] = (byte)AgentLookingForGroup.Language.English;
        entry[0x53] = listing.SlotsAvailable;
        entry[0x54] = listing.SlotsFilled;
        entry[0x56] = checked((byte)listing.SearchArea);
        entry[0x57] = listing.IsAlliance ? (byte)1 : (byte)0;
        entry[0x58] = listing.Parties;
        for (var index = 0; index < Math.Min(8, listing.SlotFlags.Length); index++)
            BinaryPrimitives.WriteUInt64LittleEndian(
                entry[(0x60 + index * sizeof(ulong))..],
                ulong.Parse(listing.SlotFlags[index], CultureInfo.InvariantCulture));
        for (var index = 0; index < Math.Min(8, listing.JobsPresent.Length); index++)
            entry[0xA0 + index] = checked((byte)listing.JobsPresent[index]);
        WriteUtf8(entry[0xA8..0xC8], listing.RecruiterName);
        WriteUtf8(entry[0xC8..0x188], listing.Description);
    }

    private static ulong ParseContentID(string? value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var contentID)
            ? contentID
            : 0;

    private static void WriteUtf8
    (
        Span<byte> target,
        string     value
    )
    {
        target.Clear();
        Encoding.UTF8.GetEncoder().Convert(
            value.AsSpan(),
            target[..^1],
            true,
            out _,
            out _,
            out _);
    }

    private static bool MatchesCurrentCategory
    (
        uint category,
        byte tab
    )
    {
        if (tab == 0)
            return true;
        if (tab == 16)
            return category == 0;
        var flag = CategoryFlag(tab);
        return flag == 0 || (category & flag) != 0;
    }

    private static uint CategoryFlag(byte tab) =>
        tab switch
        {
            1 => 1U << 1,
            2 => 1U << 2,
            3 => 1U << 3,
            4 => 1U << 4,
            5 => 1U << 5,
            6 => 1U << 6,
            7 => 1U << 7,
            8 => 1U << 8,
            9 => 1U << 9,
            10 => 1U << 10,
            11 => 1U << 11,
            12 => 1U << 12,
            13 => 1U << 13,
            14 => 1U << 14,
            15 => 1U << 15,
            _ => 0U
        };

    private static unsafe void ClearAgentListingState(AgentLookingForGroup* agent)
    {
        var listingIDs = agent->Listings.ListingIds;
        for (var index = 0; index < listingIDs.Length; index++)
            listingIDs[index] = 0;
        agent->NumberOfListingsDisplayed = 0;
    }

    private static bool IsActiveListing
    (
        PartyFinderShareModels.Listing listing,
        long                           now
    ) =>
        listing.ObservedAt + listing.SecondsRemaining > now;

    private bool MatchesSearchArea
    (
        PartyFinderShareModels.Listing listing,
        byte                           searchArea
    ) => searchArea switch
    {
        0 => true,
        1 => SelectedDataCenterID == shareService.DataCenterID && (listing.SearchArea & 8) != 0 &&
             listing.CurrentWorldID == DalamudServices.PlayerState.CurrentWorld.RowId,
        _ => false
    };

    private unsafe void UpdateNativeCounts(AddonLookingForGroup* addon)
    {
        if (addon->ResultsCountTextNode != null)
            addon->ResultsCountTextNode->SetText($"{pageListingCount}({filteredListingCount})");
        if (addon->CurrentPageTextNode != null)
        {
            var agent = AgentLookingForGroup.Instance();
            var first = pageListingCount == 0 || agent == null ? 0 : currentPage * agent->Listings.ListingIds.Length + 1;
            addon->CurrentPageTextNode->SetText($"{first}–{Math.Max(0, first + pageListingCount - 1)}");
        }
        if (addon->NextPageButton != null)
        {
            var agent = AgentLookingForGroup.Instance();
            addon->NextPageButton->SetEnabledState(agent != null &&
                (currentPage + 1) * agent->Listings.ListingIds.Length < filteredListingCount);
        }
        if (addon->PreviousPageButton != null)
            addon->PreviousPageButton->SetEnabledState(currentPage > 0);
    }

    private unsafe void ApplyNativeCounts(AddonLookingForGroup* addon)
    {
        for (var tab = 0; tab < categoryCounts.Length; tab++)
        {
            var node = addon->CategoryCountTextNodes[tab].Value;
            if (node != null)
                node->SetNumber(categoryCounts[tab]);
        }
        for (var tab = 0; tab < 3; tab++)
        {
            var button = addon->WorldSelectButtons[tab].Value;
            if (button == null)
                continue;
            var count = button->GetTextNodeById(4);
            if (count != null)
                count->SetNumber(scopeCounts[tab]);
        }
    }

    private static unsafe void EnsureNativeCountsVisible(AddonLookingForGroup* addon)
    {
        for (var tab = 0; tab < addon->CategoryCountTextNodes.Length; tab++)
        {
            var node = addon->CategoryCountTextNodes[tab].Value;
            if (node == null)
                continue;
            if ((node->NodeFlags & NodeFlags.Visible) == 0)
                node->ToggleVisibility(true);
            if (node->IsDrawDisabled)
                node->IsDrawDisabled = false;
            if (node->ParentNode != null && (node->ParentNode->NodeFlags & NodeFlags.Visible) == 0)
                node->ParentNode->ToggleVisibility(true);
        }
        for (var tab = 0; tab < addon->WorldSelectButtons.Length; tab++)
        {
            var button = addon->WorldSelectButtons[tab].Value;
            if (button == null)
                continue;
            var count = button->GetTextNodeById(4);
            if (count != null && (count->NodeFlags & NodeFlags.Visible) == 0)
                count->ToggleVisibility(true);
        }
    }

    private unsafe void CaptureNativeCountState(AddonLookingForGroup* addon)
    {
        if (nativeCountsCaptured)
            return;
        for (var tab = 0; tab < categoryNodeStates.Length; tab++)
        {
            var node = addon->CategoryCountTextNodes[tab].Value;
            if (node == null)
                continue;
            categoryNodeStates[tab] = new(
                (node->NodeFlags & NodeFlags.Visible) != 0,
                node->IsDrawDisabled,
                node->ParentNode != null && (node->ParentNode->NodeFlags & NodeFlags.Visible) != 0);
        }
        for (var tab = 0; tab < scopeCountVisible.Length; tab++)
        {
            var button = addon->WorldSelectButtons[tab].Value;
            var count = button == null ? null : button->GetTextNodeById(4);
            scopeCountVisible[tab] = count != null && (count->NodeFlags & NodeFlags.Visible) != 0;
        }
        nativeCountsCaptured = true;
    }

    private unsafe void RestoreNativeCountState()
    {
        if (!nativeCountsCaptured)
            return;
        var addon = AddonHelper.GetByName<AddonLookingForGroup>("LookingForGroup");
        if (addon != null && ((AtkUnitBase*)addon)->IsAddonAndNodesReady())
        {
            for (var tab = 0; tab < categoryNodeStates.Length; tab++)
            {
                var node = addon->CategoryCountTextNodes[tab].Value;
                if (node == null)
                    continue;
                var state = categoryNodeStates[tab];
                node->ToggleVisibility(state.Visible);
                node->IsDrawDisabled = state.DrawDisabled;
                if (node->ParentNode != null)
                    node->ParentNode->ToggleVisibility(state.ParentVisible);
            }
            for (var tab = 0; tab < scopeCountVisible.Length; tab++)
            {
                var button = addon->WorldSelectButtons[tab].Value;
                var count = button == null ? null : button->GetTextNodeById(4);
                if (count != null)
                    count->ToggleVisibility(scopeCountVisible[tab]);
            }
        }
        nativeCountsCaptured = false;
    }

    private unsafe long CurrentSnapshotVersion()
    {
        var agent = AgentLookingForGroup.Instance();
        var categoryTab = agent == null ? byte.MaxValue : agent->CategoryTab;
        var searchAreaTab = agent == null ? byte.MaxValue : agent->SearchAreaTab;
        var groupTypeTab = agent == null ? byte.MaxValue : agent->GroupTypeTab;
        return HashCode.Combine(
            shareService.Revision,
            shareService.GeneratedAt,
            categoryTab,
            searchAreaTab,
            groupTypeTab,
            currentPage,
            shareService.IsConnected);
    }

    private PartyFinderShareModels.Listing? FindSharedListing(ulong listingID)
    {
        var id = listingID.ToString(CultureInfo.InvariantCulture);
        return viewListings.FirstOrDefault(listing => listing.ID == id);
    }

    private unsafe void OnPartyFinderDetailEvent
    (
        AddonEvent type,
        AddonArgs  args
    )
    {
        if (type == AddonEvent.PreFinalize)
        {
            if (!openingSharedDetail)
                selectedListing = null;
            return;
        }

        if (selectedListing is null)
            return;
        var addon = (AddonLookingForGroupDetail*)args.Addon.Address;
        if (addon == null || !((AtkUnitBase*)addon)->IsAddonAndNodesReady())
            return;
        SetNativeDetailReadOnly(addon);
    }

    private unsafe bool OpenSharedListing
    (
        AgentLookingForGroup*          agent,
        PartyFinderShareModels.Listing listing
    )
    {
        if (openingSharedDetail || !IsActiveListing(listing, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            return false;

        var detail = CreateNativeDetail(listing);
        selectedListing     = listing;
        openingSharedDetail = true;
        try
        {
            nativePreview.Open(agent, ref detail, checked((ushort)listing.DutyType));
        }
        finally
        {
            openingSharedDetail = false;
        }

        var addon = AddonHelper.GetByName<AddonLookingForGroupDetail>("LookingForGroupDetail");
        var populated = agent->LastViewedListing.ListingId == detail.ListingId;
        var ready = addon != null && ((AtkUnitBase*)addon)->IsAddonAndNodesReady();
        if (!populated)
        {
            selectedListing = null;
            return false;
        }
        if (ready)
            SetNativeDetailReadOnly(addon);
        return true;
    }

    private static unsafe void SetNativeDetailReadOnly(AddonLookingForGroupDetail* addon)
    {
        if (addon->JoinPartyButton != null)
            addon->JoinPartyButton->SetEnabledState(false);
        foreach (var button in addon->JoinAllianceButtons)
            if (button.Value != null)
                button.Value->SetEnabledState(false);
        if (addon->SendTellButton != null)
            addon->SendTellButton->SetEnabledState(false);
        if (addon->RelayPartyFinderInfoButton != null)
            addon->RelayPartyFinderInfoButton->SetEnabledState(false);
    }

    private unsafe void CloseSharedDetail()
    {
        if (selectedListing is null)
            return;
        selectedListing = null;
        var addon = AddonHelper.GetByName<AddonLookingForGroupDetail>("LookingForGroupDetail");
        if (addon != null && ((AtkUnitBase*)addon)->IsAddonAndNodesReady())
            addon->Close(true);
    }

    private static AgentLookingForGroup.Detailed CreateNativeDetail(PartyFinderShareModels.Listing listing)
    {
        var detail = new AgentLookingForGroup.Detailed();
        detail.ListingId                = ulong.Parse(listing.ID, CultureInfo.InvariantCulture);
        detail.LeaderContentId          = ParseContentID(listing.LeaderContentID);
        detail.Category                 = (AgentLookingForGroup.DutyCategory)listing.Category;
        detail.DutyId                   = listing.DutyID;
        detail.World                    = checked((ushort)listing.CreatedWorldID);
        detail.Objective                = (AgentLookingForGroup.Objective)listing.Objective;
        detail.BeginnerFriendly         = listing.BeginnersWelcome ? (byte)1 : (byte)0;
        detail.CompletionStatus         = (AgentLookingForGroup.CompletionStatus)listing.Conditions;
        detail.DutyFinderSettingFlags   = (AgentLookingForGroup.DutyFinderSetting)listing.DutyFinderSettings;
        detail.LootRule                 = (AgentLookingForGroup.LootRule)listing.LootRule;
        detail.LastPatchHotfixTimestamp = checked((uint)listing.LastServerRestart);
        var elapsed = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - listing.ObservedAt);
        detail.TimeLeft             = (uint)Math.Max(0L, (long)listing.SecondsRemaining - elapsed);
        detail.AvgItemLv            = listing.MinimumItemLevel;
        detail.HomeWorld            = checked((ushort)listing.HomeWorldID);
        detail.CurrentWorld         = checked((ushort)listing.CurrentWorldID);
        detail.LeaderClientLanguage = AgentLookingForGroup.Language.English;
        detail.LanguageFlags        = (AgentLookingForGroup.Language)0x0F;
        detail.TotalSlots           = listing.SlotsAvailable;
        detail.SlotsFilled          = listing.SlotsFilled;
        detail.JoinConditionFlags   = (AgentLookingForGroup.JoinCondition)listing.SearchArea;
        detail.IsAlliance           = listing.IsAlliance;
        detail.NumberOfParties      = Math.Clamp(listing.Parties, (byte)1, (byte)6);
        detail.MemberContentIds[0]  = detail.LeaderContentId;
        for (var index = 0; index < detail.SlotFlags.Length && index < listing.SlotFlags.Length; index++)
            detail.SlotFlags[index] = ParseContentID(listing.SlotFlags[index]);
        for (var index = 0; index < detail.Jobs.Length && index < listing.JobsPresent.Length; index++)
            detail.Jobs[index] = checked((byte)listing.JobsPresent[index]);
        WriteUtf8(detail.Leader, listing.RecruiterName);
        WriteUtf8(detail.Comment, listing.Description);
        return detail;
    }
}
