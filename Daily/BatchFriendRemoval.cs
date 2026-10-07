using System.Globalization;
using System.Linq;
using System.Threading;
using Dalamud.Game.Text.Evaluator;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Threading.TaskHelper;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe class BatchFriendRemoval : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BatchFriendRemovalTitle"),
        Description = OmniLoc.Get("BatchFriendRemovalDescription"),
        Category = ModuleCategory.Daily
    };

    public override bool HasSettings => true;

    private readonly HashSet<ulong> selectedContentIDs = [];
    private volatile FriendSnapshot snapshot = new(0, [], 0);
    private FriendSnapshot? displayedSnapshot;
    private FriendEntry[] confirmationTargets = [];
    private ulong confirmationOwnerContentID;
    private TaskHelper? tasks;
    private Hook<InfoProxyFriendList.Delegates.EndRequest>? endRequestHook;
    private FriendEntry? currentFriend;
    private int lastSettingsFrame = -1;
    private ulong settingsOwnerContentID;
    private bool refreshRequested;
    private int operationVersion;
    private int removedCount;
    private int totalCount;
    private uint friendAddonID;
    private uint menuAddonID;
    private int nativeRowIndex;
    private string currentStep = string.Empty;

    protected override void OnEnable()
    {
        lastSettingsFrame = -1;
        tasks = new()
        {
            RetryIntervalMS = 100, TimeoutMS = 30_000
        };
        DService.Instance().ClientState.Logout += OnLogout;
    }

    protected override void OnDisable()
    {
        DService.Instance().ClientState.Logout -= OnLogout;
        StopOperation("Stopped");
        endRequestHook?.Dispose();
        endRequestHook = null;
        tasks?.Dispose();
        tasks = null;
        snapshot = new(0, [], snapshot.Version + 1);
        confirmationTargets = [];
        selectedContentIDs.Clear();
        refreshRequested = false;
    }

    protected override bool OnInterruptAutomation()
    {
        if (tasks?.IsBusy != true)
            return false;

        StopOperation("Stopped");
        return true;
    }

    public override bool DrawSettings()
    {
        var currentSnapshot = snapshot;
        var ownerContentID = DalamudServices.PlayerState.ContentId;
        var loggedIn = DService.Instance().ClientState.IsLoggedIn && ownerContentID != 0;
        var frame = ImGui.GetFrameCount();
        if (frame != lastSettingsFrame + 1 || settingsOwnerContentID != ownerContentID)
            refreshRequested = true;
        lastSettingsFrame = frame;
        settingsOwnerContentID = ownerContentID;
        if (refreshRequested && IsEnabled && loggedIn && tasks?.IsBusy == false)
        {
            refreshRequested = false;
            RefreshFriends(ownerContentID);
        }

        var hasSnapshot = loggedIn && currentSnapshot.OwnerContentID == ownerContentID;
        if (displayedSnapshot != currentSnapshot)
        {
            if (displayedSnapshot?.OwnerContentID != currentSnapshot.OwnerContentID)
                selectedContentIDs.Clear();
            else
                selectedContentIDs.IntersectWith(currentSnapshot.Friends.Select(static friend => friend.ContentID));
            displayedSnapshot = currentSnapshot;
        }

        var busy = tasks?.IsBusy == true;
        var refreshLabel = OmniLoc.Get("Feature.BatchFriendRemoval.Refresh");
        var selectUnresolvedLabel = OmniLoc.Get("Feature.BatchFriendRemoval.SelectUnresolved");
        var removeLabel = OmniLoc.Get("Feature.BatchFriendRemoval.RemoveSelected");
        var stopLabel = OmniLoc.Get("Feature.BatchFriendRemoval.Stop");
        var stopSize = OmniControls.CompactButtonSize(stopLabel);
        var openConfirmation = false;
        using (ImRaii.Disabled(!loggedIn || busy))
            if (OmniControls.SmallButton(refreshLabel, false))
                RefreshFriends(ownerContentID);

        OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(selectUnresolvedLabel).X);
        using (ImRaii.Disabled(!hasSnapshot || busy || currentSnapshot.Friends.All(static friend => !friend.IsUnresolved)))
            if (OmniControls.SmallButton(selectUnresolvedLabel, false))
                selectedContentIDs.UnionWith(currentSnapshot.Friends.Where(static friend => friend.IsUnresolved)
                    .Select(static friend => friend.ContentID));

        OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(removeLabel).X);
        using (ImRaii.Disabled(!hasSnapshot || busy || selectedContentIDs.Count == 0))
        {
            if (OmniControls.SmallButton(removeLabel, false))
            {
                confirmationTargets = currentSnapshot.Friends
                    .Where(friend => selectedContentIDs.Contains(friend.ContentID)).ToArray();
                confirmationOwnerContentID = ownerContentID;
                openConfirmation = true;
                DalamudServices.PluginLog.Information(
                    "[BatchFriendRemoval] 点击批量删除：所选 {SelectedCount}，确认目标 {TargetCount}。",
                    selectedContentIDs.Count, confirmationTargets.Length);
            }
        }

        OmniControls.SameLineOrWrap(stopSize.X);
        using (ImRaii.Disabled(!busy))
            if (OmniControls.SmallButton(stopLabel, false, stopSize))
                StopOperation("Stopped");

        if (openConfirmation)
            ImGui.OpenPopup(CONFIRM_POPUP_ID);
        DrawConfirmation();

        if (!loggedIn || !hasSnapshot || currentSnapshot.Friends.Length == 0)
            return false;

        DrawFriendList(currentSnapshot.Friends, busy);
        return false;
    }

    private void DrawFriendList(FriendEntry[] friends, bool busy)
    {
        using var cellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(ImGui.GetStyle().CellPadding.X, OmniTheme.Scale(2f)));
        var nameLabel = OmniLoc.Get("Feature.BatchFriendRemoval.Column.Name");
        var worldLabel = OmniLoc.Get("Feature.BatchFriendRemoval.Column.World");
        var checkboxSize = new Vector2(OmniTheme.CheckboxSize());
        var checkboxWidth = OmniControls.MeasureCheckbox(string.Empty).X;
        var nameWidth = ImGui.CalcTextSize(nameLabel).X;
        var worldWidth = ImGui.CalcTextSize(worldLabel).X;
        foreach (var friend in friends)
        {
            nameWidth = MathF.Max(nameWidth, ImGui.CalcTextSize(friend.DisplayName).X);
            worldWidth = MathF.Max(worldWidth,
                ImGui.CalcTextSize(friend.DisplayWorldName).X + ImGui.GetTextLineHeight());
        }

        var rowContentHeight = MathF.Max(
            OmniTheme.CheckboxSize(),
            MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight()));
        var scroll = friends.Length > 8;
        var height = scroll
            ? (rowContentHeight + ImGui.GetStyle().CellPadding.Y * 2f) * 9f +
              ImGui.GetStyle().ChildBorderSize * 2f
            : 0f;
        using var table = OmniControls.DataTable(
            "##BatchFriendRemovalList", ["##selection", nameLabel, worldLabel],
            [checkboxWidth, nameWidth, worldWidth], [checkboxWidth, nameWidth, worldWidth], out var detailLayout,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings |
            ImGuiTableFlags.SizingStretchProp | (scroll ? ImGuiTableFlags.ScrollY : ImGuiTableFlags.None),
            2, new Vector2(ImGui.GetContentRegionAvail().X, height), stretchColumn: 1);
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        using var disabled = ImRaii.Disabled(busy);
        OmniControls.BeginTableHeaderRow(rowContentHeight);
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        var selectAll = selectedContentIDs.Count == friends.Length;
        OmniControls.CenterTableItem(checkboxSize, rowContentHeight);
        if (OmniControls.Checkbox("##selectAll", ref selectAll))
        {
            if (selectAll)
                selectedContentIDs.UnionWith(friends.Select(static friend => friend.ContentID));
            else
                selectedContentIDs.Clear();
        }

        if (!detailLayout)
        {
            OmniControls.TableHeader(nameLabel, rowContentHeight);
            OmniControls.TableHeader(worldLabel, rowContentHeight);
        }

        foreach (var friend in friends)
        {
            using var id = ImRaii.PushId(friend.ContentID.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
            ImGui.TableNextColumn();
            OmniControls.CenterTableItem(checkboxSize, rowContentHeight);
            var selected = selectedContentIDs.Contains(friend.ContentID);
            if (OmniControls.Checkbox("##selected", ref selected))
            {
                if (selected)
                    selectedContentIDs.Add(friend.ContentID);
                else
                    selectedContentIDs.Remove(friend.ContentID);
            }

            OmniControls.NextTableField(nameLabel, detailLayout);
            if (detailLayout)
                OmniControls.TableTextWrappedCentered(friend.DisplayName);
            else
                OmniControls.TableTextCentered(friend.DisplayName, rowContentHeight);

            OmniControls.NextTableField(worldLabel, detailLayout);
            OmniControls.TableWorldNameCentered(friend.DisplayWorldName, rowContentHeight);
        }
    }

    private void DrawConfirmation()
    {
        using var popupStyles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, OmniTheme.PopupPadding())
            .Push(ImGuiStyleVar.PopupRounding, OmniTheme.Scale(OmniTheme.Tokens.BorderRadius))
            .Push(ImGuiStyleVar.PopupBorderSize, 0f);
        var title = OmniLoc.Get("Feature.BatchFriendRemoval.ConfirmTitle");
        var message = string.Format(CultureInfo.CurrentCulture,
            OmniLoc.Get("Feature.BatchFriendRemoval.ConfirmDescription"), confirmationTargets.Length);
        var confirmLabel = OmniLoc.Get("Feature.BatchFriendRemoval.Confirm");
        var cancelLabel = OmniLoc.Get("Feature.BatchFriendRemoval.Cancel");
        var confirmSize = OmniControls.CompactButtonSize(confirmLabel);
        var cancelSize = OmniControls.CompactButtonSize(cancelLabel);
        var actionsSize = OmniControls.MeasureGroup([confirmSize, cancelSize]);
        var textWidth = MathF.Max(ImGui.CalcTextSize(title).X, ImGui.CalcTextSize(message).X);
        var viewportSize = ImGui.GetMainViewport().WorkSize;
        var width = MathF.Min(MathF.Floor(viewportSize.X), MathF.Ceiling(
            MathF.Max(textWidth, actionsSize.X) + ImGui.GetStyle().WindowPadding.X * 2f));
        OmniControls.SetNextAutoResizeWindowSizeConstraints(new Vector2(width, 1f), new Vector2(width, viewportSize.Y));
        using var background = ImRaii.PushColor(ImGuiCol.PopupBg, OmniTheme.Tokens.Surface);
        using var popup = ImRaii.Popup(CONFIRM_POPUP_ID, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
        if (!popup)
            return;

        OmniControls.DrawPopupBackground();

        if (ImGui.IsWindowAppearing())
            DalamudServices.PluginLog.Information("[BatchFriendRemoval] 确认窗口已显示，目标 {TargetCount}。", confirmationTargets.Length);
        using var textWrap = ImRaii.TextWrapPos(textWidth <= ImGui.GetContentRegionAvail().X ? -1f : 0f);
        ImGui.TextUnformatted(title);
        ImGui.TextUnformatted(message);
        var actionsFit = actionsSize.X <= ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f,
            (ImGui.GetContentRegionAvail().X - (actionsFit ? actionsSize.X : confirmSize.X)) * 0.5f));
        using (ImRaii.Disabled(tasks?.IsBusy == true || confirmationTargets.Length == 0))
        {
            if (OmniControls.SmallButton(confirmLabel, false, confirmSize))
            {
                DalamudServices.PluginLog.Information("[BatchFriendRemoval] 用户确认删除 {TargetCount} 位好友。", confirmationTargets.Length);
                BeginRemoval(confirmationTargets, confirmationOwnerContentID);
                ImGui.CloseCurrentPopup();
            }
        }

        if (actionsFit)
            OmniControls.SameLineOrWrap(cancelSize.X);
        else
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f,
                (ImGui.GetContentRegionAvail().X - cancelSize.X) * 0.5f));
        if (OmniControls.SmallButton(cancelLabel, false, cancelSize))
        {
            DalamudServices.PluginLog.Information("[BatchFriendRemoval] 用户取消批量删除。");
            ImGui.CloseCurrentPopup();
        }
    }

    private void OnLogout(int _, int unusedReason)
    {
        StopOperation("CharacterChanged");
        snapshot = new(0, [], snapshot.Version + 1);
    }

    private void RefreshFriends(ulong ownerContentID)
    {
        if (!IsEnabled || tasks is null || tasks.IsBusy)
            return;

        var version = Interlocked.Increment(ref operationVersion);
        totalCount = 0;
        removedCount = 0;
        DalamudServices.PluginLog.Information("[BatchFriendRemoval] 刷新好友名单，批次 {Version}。", version);
        var responseVersion = snapshot.Version;
        EnqueueStep(version, ownerContentID, "RequestFriends", () => RequestFriends(ref responseVersion));
        EnqueueStep(version, ownerContentID, "WaitForFriends", () =>
            snapshot.OwnerContentID == ownerContentID && snapshot.Version != responseVersion);
        EnqueueStep(version, ownerContentID, "ReadDisplayedFriends", () => CaptureDisplayedFriends(ownerContentID));
    }

    private bool PrepareFriendList(ref bool showRequested)
    {
        var addon = AddonHelper.GetByName<AddonFriendList>("FriendList");
        var social = AddonHelper.GetByName<AddonSocial>("Social");
        if (((AtkUnitBase*)social)->IsAddonAndNodesReady() &&
            ((AtkUnitBase*)addon)->IsAddonAndNodesReady() && addon->FriendList != null)
            return !addon->FriendList->IsUpdatePending;

        if (showRequested)
            return false;

        var agent = AgentFriendlist.Instance();
        var uiModule = UIModule.Instance();
        if (agent == null || !agent->IsActivatable() || uiModule == null)
            return false;

        showRequested = true;
        DalamudServices.PluginLog.Information("[BatchFriendRemoval] 删除操作正在打开原生好友名单。");
        uiModule->ExecuteMainCommand(FRIEND_LIST_MAIN_COMMAND_ID);
        return false;
    }

    private bool CaptureDisplayedFriends(ulong ownerContentID)
    {
        var proxy = InfoProxyFriendList.Instance();
        var addon = AddonHelper.GetByName<AddonFriendList>("FriendList");
        if (proxy == null || !((AtkUnitBase*)addon)->IsAddonAndNodesReady() || addon->FriendList == null)
            return true;

        if (addon->FriendList->IsUpdatePending || (proxy->EntryCount > 0 && proxy->CharData == null))
            return true;

        CaptureFriends(proxy, ownerContentID, snapshot.Version);
        return true;
    }

    private bool RequestFriends(ref int responseVersion)
    {
        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null)
            return false;

        if (endRequestHook is null)
        {
            endRequestHook = proxy->VirtualTable->HookVFuncFromName(
                "EndRequest", (InfoProxyFriendList.Delegates.EndRequest)OnEndRequest);
            endRequestHook.Enable();
        }

        responseVersion = snapshot.Version;
        var accepted = proxy->RequestData();
        if (accepted)
            DalamudServices.PluginLog.Information("[BatchFriendRemoval] 好友数据请求已受理，之前的快照版本 {Version}。", responseVersion);
        return accepted;
    }

    private void OnEndRequest(InfoProxyFriendList* proxy)
    {
        endRequestHook!.Original(proxy);
        var ownerContentID = DalamudServices.PlayerState.ContentId;
        if (!IsEnabled || !DService.Instance().ClientState.IsLoggedIn || ownerContentID == 0 ||
            proxy != InfoProxyFriendList.Instance() || (proxy->EntryCount > 0 && proxy->CharData == null))
            return;

        CaptureFriends(proxy, ownerContentID, snapshot.Version + 1);
        DalamudServices.PluginLog.Information(
            "[BatchFriendRemoval] 好友数据回包：原生条目 {EntryCount}，有效 Content ID {FriendCount}，姓名未获取 {UnresolvedCount}，快照版本 {Version}。",
            proxy->EntryCount, snapshot.Friends.Length, snapshot.Friends.Count(static friend => friend.IsUnresolved), snapshot.Version);
    }

    private void CaptureFriends(InfoProxyFriendList* proxy, ulong ownerContentID, int version)
    {
        var entries = proxy->CharDataSpan;
        var unavailableName = LuminaWrapper.GetAddonText(UNAVAILABLE_CHARACTER_TEXT_ID);
        var addon = AddonHelper.GetByName<AddonFriendList>("FriendList");
        var stage = AtkStage.Instance();
        var strings = stage != null ? stage->GetStringArrayData(StringArrayType.FriendList) : null;
        var useDisplayedNames = ((AtkUnitBase*)addon)->IsAddonAndNodesReady() && addon->FriendList != null &&
            !addon->FriendList->IsUpdatePending && addon->FriendList->ListLength == entries.Length &&
            proxy->FilterGroup == InfoProxyCommonList.DisplayGroup.All && strings != null &&
            strings->StringArray != null && strings->Size >= entries.Length * 5;
        // 先逐项核对已知姓名，避免排序或旧展示数组导致姓名与 Content ID 错配。
        var nameAnchors = 0;
        if (useDisplayedNames)
        {
            for (var index = 0; index < entries.Length; index++)
            {
                var name = entries[index].NameString;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var displayedName = strings->StringArray[index * 5];
                if (!displayedName.HasValue ||
                    new ReadOnlySeString(displayedName.AsSpan()).ExtractText() != name)
                {
                    useDisplayedNames = false;
                    break;
                }
                nameAnchors++;
            }
            useDisplayedNames &= nameAnchors > 0;
        }

        var friends = new List<FriendEntry>(entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            ref readonly var entry = ref entries[index];
            if (entry.ContentId == 0)
                continue;

            var name = entry.NameString;
            if (name == unavailableName)
                name = string.Empty;
            var homeWorld = entry.HomeWorld;
            if (homeWorld <= 1 && entry.Index != null && entry.Index->ContentId == entry.ContentId)
                homeWorld = entry.Index->HomeWorld;
            if (string.IsNullOrWhiteSpace(name))
            {
                if (useDisplayedNames && strings->StringArray[index * 5].HasValue)
                {
                    name = new ReadOnlySeString(strings->StringArray[index * 5].AsSpan()).ExtractText();
                    if (name == unavailableName)
                        name = string.Empty;
                }
                DalamudServices.PluginLog.Information(
                    "[BatchFriendRemoval] 补充姓名：ContentID={ContentID}，NativeNamesAligned={Aligned}，NameRead={NameRead}，HomeWorld={HomeWorld}/{ResolvedHomeWorld}，State={State}。",
                    entry.ContentId, useDisplayedNames, !string.IsNullOrWhiteSpace(name), entry.HomeWorld, homeWorld, entry.State);
            }

            var worldName = homeWorld > 1 ? LuminaWrapper.GetWorldName(homeWorld) : string.Empty;
            friends.Add(new(entry.ContentId, name, homeWorld, worldName));
        }

        snapshot = new(ownerContentID, friends.ToArray(), version);
    }

    private void BeginRemoval(FriendEntry[] targets, ulong ownerContentID)
    {
        if (!IsEnabled || tasks is null || tasks.IsBusy || targets.Length == 0)
        {
            DalamudServices.PluginLog.Warning(
                "[BatchFriendRemoval] 删除未开始：Enabled={Enabled}，Busy={Busy}，目标 {TargetCount}。",
                IsEnabled, tasks?.IsBusy == true, targets.Length);
            return;
        }

        var version = Interlocked.Increment(ref operationVersion);
        removedCount = 0;
        totalCount = targets.Length;
        DalamudServices.PluginLog.Information("[BatchFriendRemoval] 开始删除，批次 {Version}，目标 {TargetCount}。", version, totalCount);
        var showRequested = false;
        EnqueueStep(version, ownerContentID, "PrepareFriendList", () => PrepareFriendList(ref showRequested));
        var initialResponseVersion = snapshot.Version;
        EnqueueStep(version, ownerContentID, "RequestFriendsBeforeRemoval", () => RequestFriends(ref initialResponseVersion));
        EnqueueStep(version, ownerContentID, "WaitForFriendsBeforeRemoval", () =>
            snapshot.OwnerContentID == ownerContentID && snapshot.Version != initialResponseVersion);
        foreach (var friend in targets)
        {
            var preferredIndex = -1;
            var scanIndex = 0;
            var awaitingMenu = false;
            var responseVersion = 0;
            EnqueueStep(version, ownerContentID, "OpenFriendMenu", () =>
                FindFriendMenu(friend, ref preferredIndex, ref scanIndex, ref awaitingMenu), 60_000);
            EnqueueStep(version, ownerContentID, "SelectRemoval", () => SelectRemoval(friend));
            EnqueueStep(version, ownerContentID, "ConfirmRemoval", () => ConfirmRemoval(friend));
            EnqueueStep(version, ownerContentID, "RequestFriendsAfterRemoval", () => RequestFriends(ref responseVersion));
            EnqueueStep(version, ownerContentID, "WaitForRemoval", () =>
            {
                var currentSnapshot = snapshot;
                if (currentSnapshot.OwnerContentID != ownerContentID || currentSnapshot.Version == responseVersion ||
                    currentSnapshot.Friends.Any(entry => entry.ContentID == friend.ContentID))
                    return false;

                removedCount++;
                DalamudServices.PluginLog.Information(
                    "[BatchFriendRemoval] 已确认移除：ContentID={ContentID}，进度 {RemovedCount}/{TotalCount}。",
                    friend.ContentID, removedCount, totalCount);
                currentFriend = null;
                return true;
            });
        }

        EnqueueStep(version, ownerContentID, "Complete", () =>
        {
            StopOperation("Completed");
            return true;
        });
    }

    private bool FindFriendMenu(FriendEntry friend, ref int preferredIndex, ref int scanIndex, ref bool awaitingMenu)
    {
        currentFriend = friend;
        var proxy = InfoProxyFriendList.Instance();
        var addon = AddonHelper.GetByName<AddonFriendList>("FriendList");
        if (proxy == null || !((AtkUnitBase*)addon)->IsAddonAndNodesReady() || addon->FriendList == null ||
            proxy->FilterGroup != InfoProxyCommonList.DisplayGroup.All)
        {
            StopOperation("NativeWindowRequired");
            return true;
        }

        if (addon->FriendList->IsUpdatePending || (proxy->EntryCount > 0 && proxy->CharData == null) ||
            addon->FriendList->ListLength != proxy->EntryCount)
            return false;

        if (friendAddonID != 0 && friendAddonID != addon->Id)
        {
            StopOperation("NativeWindowRequired");
            return true;
        }

        friendAddonID = addon->Id;
        var menu = Addons.ContextMenuAddon;
        if (awaitingMenu)
        {
            if (!menu->IsAddonAndNodesReady())
                return false;

            var context = AgentContext.Instance();
            var agent = AgentFriendlist.Instance();
            if (context == null || agent == null || context->OwnerAddon != friendAddonID ||
                (menuAddonID != 0 && menu->Id != menuAddonID))
            {
                StopOperation("TargetMismatch");
                return true;
            }

            menuAddonID = menu->Id;
            if (agent->SelectedContentId == friend.ContentID)
            {
                DalamudServices.PluginLog.Information(
                    "[BatchFriendRemoval] 原生菜单目标已匹配：ContentID={ContentID}，Row={Row}，MenuAddon={MenuAddonID}。",
                    friend.ContentID, nativeRowIndex, menuAddonID);
                return true;
            }

            menu->Close(true);
            awaitingMenu = false;
            return false;
        }

        if (menuAddonID != 0 && menu->IsAddonAndNodesReady() && menu->Id == menuAddonID)
            return false;

        if (menu->IsAddonAndNodesReady() || Addons.SelectYesno->IsAddonAndNodesReady())
        {
            StopOperation("MenuBusy");
            return true;
        }
        menuAddonID = 0;

        if (preferredIndex < 0)
        {
            var entries = proxy->CharDataSpan;
            for (var index = 0; index < entries.Length; index++)
            {
                if (entries[index].ContentId != friend.ContentID)
                    continue;
                preferredIndex = index;
                nativeRowIndex = index;
                break;
            }
            if (preferredIndex < 0)
            {
                StopOperation("TargetMismatch");
                return true;
            }
        }
        else
        {
            if (scanIndex == preferredIndex)
                scanIndex++;
            if (scanIndex >= addon->FriendList->ListLength)
            {
                StopOperation("TargetMismatch");
                return true;
            }
            nativeRowIndex = scanIndex++;
        }

        awaitingMenu = true;
        DalamudServices.PluginLog.Information(
            "[BatchFriendRemoval] 点击原生好友行：Row={Row}，目标 ContentID={ContentID}，HomeWorld={HomeWorld}。",
            nativeRowIndex, friend.ContentID, friend.HomeWorld);
        addon->FriendList->DispatchItemEvent(nativeRowIndex, AtkEventType.ListItemClick);
        menu = Addons.ContextMenuAddon;
        if (menu != null)
            menuAddonID = menu->Id;
        return false;
    }

    private bool SelectRemoval(FriendEntry friend)
    {
        var context = AgentContext.Instance();
        var agent = AgentFriendlist.Instance();
        var menu = (AddonContextMenu*)Addons.ContextMenuAddon;
        var addon = AddonHelper.GetByName<AddonFriendList>("FriendList");
        if (!((AtkUnitBase*)menu)->IsAddonAndNodesReady())
            return false;

        if (context == null || agent == null || !((AtkUnitBase*)addon)->IsAddonAndNodesReady() || addon->Id != friendAddonID ||
            context->OwnerAddon != friendAddonID || agent->SelectedContentId != friend.ContentID ||
            menu->Id != menuAddonID)
        {
            StopOperation("TargetMismatch");
            return true;
        }

        var removeText = LuminaWrapper.GetAddonText(REMOVE_FRIEND_MENU_TEXT_ID);
        if (removeText.Length == 0 || menu->AtkValues == null || menu->AtkValuesCount <= 8)
            return false;

        var entryCount = menu->AtkValues[0].UInt;
        if (entryCount > menu->AtkValuesCount - 8 || entryCount > 32)
            return false;

        for (var index = 0; index < entryCount; index++)
        {
            ref var value = ref menu->AtkValues[index + 8];
            if (value.Type is not (AtkValueType.String or AtkValueType.ManagedString) ||
                !value.String.HasValue || value.String.ToString() != removeText)
                continue;

            if (context->CurrentContextMenu == null ||
                (context->CurrentContextMenu->ContextItemDisabledMask & (1u << index)) != 0)
                return false;

            DalamudServices.PluginLog.Information("[BatchFriendRemoval] 选择删除菜单项，Index={Index}，ContentID={ContentID}。", index, friend.ContentID);
            return AddonContextMenuEvent.Select(index);
        }
        return false;
    }

    private bool ConfirmRemoval(FriendEntry friend)
    {
        var agent = AgentFriendlist.Instance();
        var confirmation = (AddonSelectYesno*)Addons.SelectYesno;
        var addon = AddonHelper.GetByName<AddonFriendList>("FriendList");
        if (!((AtkUnitBase*)confirmation)->IsAddonAndNodesReady())
            return false;

        if (agent == null || !((AtkUnitBase*)addon)->IsAddonAndNodesReady() || addon->Id != friendAddonID ||
            agent->SelectedContentId != friend.ContentID || agent->SelectYesNoAddonId != confirmation->Id ||
            confirmation->PromptText == null)
        {
            StopOperation("TargetMismatch");
            return true;
        }

        SeStringParameter[] parameters = [new ReadOnlySeString(agent->SelectedPlayerName.AsSpan())];
        var expectedText = DService.Instance().SeStringEvaluator.Evaluate(
            LuminaWrapper.GetAddonTextSeString(REMOVE_FRIEND_CONFIRM_TEXT_ID), parameters).ExtractText();
        var actualText = new ReadOnlySeString(confirmation->PromptText->NodeText.AsSpan()).ExtractText();
        if (expectedText.Length == 0 || actualText != expectedText)
        {
            DalamudServices.PluginLog.Warning(
                "[BatchFriendRemoval] 确认文本不匹配：ContentID={ContentID}，Expected={Expected}，Actual={Actual}。",
                friend.ContentID, expectedText, actualText);
            StopOperation("TargetMismatch");
            return true;
        }

        if (confirmation->YesButton == null || !confirmation->YesButton->IsEnabled)
            return false;

        DalamudServices.PluginLog.Information("[BatchFriendRemoval] 点击原生删除确认，ContentID={ContentID}。", friend.ContentID);
        return AddonSelectYesnoEvent.ClickYes();
    }

    private void EnqueueStep(int version, ulong ownerContentID, string step, Func<bool> action, int timeoutMS = 30_000)
    {
        var started = false;
        tasks!.EnqueueAsync(
            _ => DalamudServices.Framework.RunOnFrameworkThread(() =>
            {
                if (!IsCurrentOperation(version, ownerContentID))
                    return true;

                if (!started)
                {
                    started = true;
                    currentStep = step;
                    DalamudServices.PluginLog.Information("[BatchFriendRemoval] 开始步骤 {Step}，批次 {Version}。", step, version);
                }

                return action();
            }),
            name: $"{ModuleName}.{step}",
            timeoutMS: timeoutMS,
            timeoutAction: () => OnTaskFailed(version, step, true),
            exceptionAction: () => OnTaskFailed(version, step, false));
    }

    private bool IsCurrentOperation(int version, ulong ownerContentID)
    {
        if (!IsEnabled || version != Volatile.Read(ref operationVersion))
            return false;

        if (DService.Instance().ClientState.IsLoggedIn && ownerContentID != 0 &&
            DalamudServices.PlayerState.ContentId == ownerContentID)
            return true;

        StopOperation("CharacterChanged");
        return false;
    }

    private void OnTaskFailed(int version, string step, bool timedOut) => _ = DalamudServices.Framework.RunOnFrameworkThread(() =>
                                                                               {
                                                                                   if (version != Volatile.Read(ref operationVersion))
                                                                                       return;

                                                                                   DalamudServices.PluginLog.Warning(
                                                                                       "[BatchFriendRemoval] 步骤失败：Step={Step}，TimedOut={TimedOut}，批次 {Version}。",
                                                                                       step, timedOut, version);
                                                                                   StopOperation(!timedOut
                                                                                       ? "OperationFailed"
                                                                                       : step == "PrepareFriendList" ? "NativeWindowRequired"
                                                                                       : currentFriend is null ? "LoadFailed" : "RemoveFailed");
                                                                               });

    private void StopOperation(string reason)
    {
        DalamudServices.PluginLog.Information(
            "[BatchFriendRemoval] 操作结束：Reason={Reason}，Step={Step}，ContentID={ContentID}，进度 {RemovedCount}/{TotalCount}。",
            reason, currentStep, currentFriend?.ContentID ?? 0, removedCount, totalCount);
        if (reason is not ("Completed" or "Stopped" or
            "CharacterChanged"))
            LogNativeState();
        Interlocked.Increment(ref operationVersion);
        tasks?.Abort();
        currentFriend = null;
        friendAddonID = 0;
        menuAddonID = 0;
    }

    private void LogNativeState()
    {
        var proxy = InfoProxyFriendList.Instance();
        var addon = AddonHelper.GetByName<AddonFriendList>("FriendList");
        var addonReady = ((AtkUnitBase*)addon)->IsAddonAndNodesReady();
        var context = AgentContext.Instance();
        var confirmation = (AddonSelectYesno*)Addons.SelectYesno;
        var menu = Addons.ContextMenuAddon;
        DalamudServices.PluginLog.Warning(
            "[BatchFriendRemoval] 原生状态：FriendListReady={Ready}，Addon={AddonID}/{ExpectedAddonID}，Entries={EntryCount}，Rows={RowCount}，Filter={Filter}，Row={Row}，MenuReady={MenuReady}，ConfirmReady={ConfirmReady}，Snapshot={SnapshotVersion}。",
            addonReady, addonReady ? addon->Id : 0, friendAddonID,
            proxy != null ? proxy->EntryCount : 0,
            addonReady && addon->FriendList != null ? addon->FriendList->ListLength : 0,
            proxy != null ? proxy->FilterGroup.ToString() : "Unavailable", nativeRowIndex,
            menu->IsAddonAndNodesReady(), ((AtkUnitBase*)confirmation)->IsAddonAndNodesReady(), snapshot.Version);
        if (context != null)
            DalamudServices.PluginLog.Warning(
                "[BatchFriendRemoval] 菜单目标：OwnerAddon={OwnerAddon}，TargetCID={TargetCID}，ConfirmCID={ConfirmCID}，ConfirmAddon={ConfirmAddon}，HomeWorld={HomeWorld}/{ExpectedHomeWorld}。",
                context->OwnerAddon, context->TargetContentId, context->YesNoTargetContentId,
                context->YesNoAddon, context->YesNoTargetHomeWorldId, currentFriend?.HomeWorld ?? 0);
        var agent = AgentFriendlist.Instance();
        if (agent != null)
            DalamudServices.PluginLog.Warning(
                "[BatchFriendRemoval] 好友代理：SelectedCID={SelectedCID}，ConfirmAddon={ConfirmAddon}，实际确认窗口={ActualConfirmAddon}，实际菜单={ActualMenuAddon}/{ExpectedMenuAddon}。",
                agent->SelectedContentId, agent->SelectYesNoAddonId,
                ((AtkUnitBase*)confirmation)->IsAddonAndNodesReady() ? confirmation->Id : 0,
                menu->IsAddonAndNodesReady() ? menu->Id : 0, menuAddonID);
    }

    private sealed record FriendEntry(ulong ContentID, string Name, ushort HomeWorld, string WorldName)
    {
        public bool IsUnresolved => string.IsNullOrWhiteSpace(Name);

        public string DisplayName => IsUnresolved ? OmniLoc.Get("Feature.BatchFriendRemoval.Unresolved") : Name;

        public string DisplayWorldName => string.IsNullOrWhiteSpace(WorldName)
            ? OmniLoc.Get("Feature.BatchFriendRemoval.Unresolved") : WorldName;
    }

    private sealed record FriendSnapshot(ulong OwnerContentID, FriendEntry[] Friends, int Version);

    #region 常量

    private const uint FRIEND_LIST_MAIN_COMMAND_ID = 13;

    private const uint REMOVE_FRIEND_MENU_TEXT_ID = 65;

    private const uint REMOVE_FRIEND_CONFIRM_TEXT_ID = 129;

    private const uint UNAVAILABLE_CHARACTER_TEXT_ID = 964;

    private const string CONFIRM_POPUP_ID = "##BatchFriendRemovalConfirm";

    #endregion
}
