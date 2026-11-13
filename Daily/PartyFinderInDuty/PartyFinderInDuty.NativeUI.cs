using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using OmenTools.Extensions;
using OmenTools.Info.Lumina;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using TreeToolKit;

namespace OmniToolbox.Modules;

public sealed partial class PartyFinderInDuty
{
    private unsafe void EnsureNativeHeader(AddonLookingForGroup* addon)
    {
        var localDataCenterID = shareService.DataCenterID;
        if (headerNode != null && headerDataCenterID == localDataCenterID)
            return;
        ReleaseNativeHeader();
        if (addon->RootNode == null || localDataCenterID == 0)
            return;

        headerNode = new ResNode
        {
            Position = new(110f, 11f),
            Size = new(Math.Max(0f, addon->RootNode->Width - 180f), 24f)
        };
        headerNode.AttachNode((AtkUnitBase*)addon);
        headerDataCenterID = localDataCenterID;
        var dataCenterIDs = Sheets.CNWorlds.Values
            .Select(world => world.DataCenter.RowId)
            .Distinct()
            .OrderBy(id => id != localDataCenterID)
            .ThenBy(id => id);
        var x = 0f;
        foreach (var dataCenterID in dataCenterIDs)
        {
            var name = LuminaWrapper.GetDataCenterName(dataCenterID);
            var button = new TabBarRadioButtonNode
            {
                Position = new(x, 0f),
                Size = new(72f, 22f),
                String = name,
                OnClick = () => SelectDataCenter(dataCenterID)
            };
            button.LabelNode.FontSize = 12;
            button.AttachNode(headerNode);
            dataCenterButtons[dataCenterID] = button;
            x += button.Width + 6f;
        }
        UpdateDataCenterSelection();
        NativeNodeDetach.UpdateNodeLists((AtkUnitBase*)addon);
    }

    private unsafe void SelectDataCenter(uint dataCenterID)
    {
        if (!windowOpen || !CanOperatePartyFinder() || dataCenterID == SelectedDataCenterID)
            return;
        remoteDataCenterID = dataCenterID == shareService.DataCenterID ? 0 : dataCenterID;
        currentPage        = 0;
        if (UsesSharedData)
        {
            CloseSharedDetail();
            shareService.CancelSnapshotRequest();
            var addon = AddonHelper.GetByName<AddonLookingForGroup>("LookingForGroup");
            if (addon != null && ((AtkUnitBase*)addon)->IsAddonAndNodesReady())
            {
                CaptureNativeCountState(addon);
                ClearNativeList();
            }
            injectedVersion         = -1;
            injectionRequested      = true;
            categoryCountsRequested = true;
            refreshRequested        = true;
            nativeRefreshRequested  = false;
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
        var selectedID = SelectedDataCenterID;
        foreach (var (dataCenterID, button) in dataCenterButtons)
        {
            button.IsSelected = dataCenterID == selectedID;
            button.IsChecked  = dataCenterID == selectedID;
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
