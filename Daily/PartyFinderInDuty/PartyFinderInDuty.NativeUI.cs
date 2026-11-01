using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using OmenTools.Extensions;
using OmenTools.Info.Lumina;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.PublicBridge;
using TreeToolKit;

namespace OmniToolbox.Modules;

public sealed partial class PartyFinderInDuty
{
    private unsafe void EnsureNativeHeader(AddonLookingForGroup* addon)
    {
        if (headerNode != null && headerDataCenterID == shareService.DataCenterID)
            return;
        ReleaseNativeHeader();
        if (addon->RootNode == null)
            return;

        headerNode = new ResNode
        {
            Position = new(110f, 11f),
            Size = new(Math.Max(0f, addon->RootNode->Width - 180f), 24f)
        };
        headerNode.AttachNode((AtkUnitBase*)addon);
        headerDataCenterID = shareService.DataCenterID;
        var otherDataCenters = Sheets.CNWorlds.Values
            .Select(world => world.DataCenter)
            .Where(dataCenter => dataCenter.RowId != shareService.DataCenterID)
            .DistinctBy(dataCenter => dataCenter.RowId)
            .OrderBy(dataCenter => dataCenter.RowId)
            .Take(3)
            .ToArray();
        var x = 0f;
        foreach (var dataCenter in otherDataCenters)
        {
            var dataCenterID = dataCenter.RowId;
            var name = LuminaWrapper.GetDataCenterName(dataCenterID);
            var button = new TabBarRadioButtonNode
            {
                Position = new(x, 0f),
                Size = new(72f, 22f),
                String = name,
                OnClick = () => SelectDataCenter(dataCenterID)
            };
            button.Component->AlwaysFireButtonClickEvent = true;
            button.LabelNode.FontSize                    = 12;
            button.AttachNode(headerNode);
            dataCenterButtons[dataCenterID] = button;
            x += button.Width + 6f;
        }
        UpdateDataCenterSelection();
        NativeNodeDetach.UpdateNodeLists((AtkUnitBase*)addon);
    }

    private unsafe void SelectDataCenter(uint dataCenterID)
    {
        if (!CanOperatePartyFinder())
            return;
        CloseSharedDetail();
        selectedDataCenterID = selectedDataCenterID == dataCenterID ? 0 : dataCenterID;
        shareService.CancelSnapshotRequest();
        selectedListing = null;
        currentPage     = 0;
        injectedVersion = -1;
        var shared = UsesSharedData;
        injectionRequested      = shared;
        categoryCountsRequested = shared;
        refreshRequested        = shared;
        if (shared)
        {
            var addon = AddonHelper.GetByName<AddonLookingForGroup>("LookingForGroup");
            if (addon != null && ((AtkUnitBase*)addon)->IsAddonAndNodesReady())
            {
                CaptureNativeCountState(addon);
                ClearNativeList();
                injectionRequested      = true;
                categoryCountsRequested = true;
                refreshRequested        = true;
            }
        }
        else
            SwitchToNativeData();
        UpdateDataCenterSelection();
    }

    private void SwitchToNativeData()
    {
        CloseSharedDetail();
        shareService.CancelSnapshotRequest();
        if (nativeListInjected)
            ClearNativeList();
        RestoreNativeCountState();
        selectedListing         = null;
        injectedVersion         = -1;
        injectionRequested      = false;
        categoryCountsRequested = false;
        refreshRequested        = false;
        nativeRefreshRequested  = true;
    }

    private void UpdateDataCenterSelection()
    {
        foreach (var (dataCenterID, button) in dataCenterButtons)
        {
            button.IsSelected = dataCenterID == selectedDataCenterID;
            button.IsChecked  = dataCenterID == selectedDataCenterID;
        }
    }

    private void ReleaseNativeHeader()
    {
        NativeNodeDetach.DetachAndDestroy(headerNode);
        headerNode = null;
        dataCenterButtons.Clear();
        headerDataCenterID = 0;
    }
}
