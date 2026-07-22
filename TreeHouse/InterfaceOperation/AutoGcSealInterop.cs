using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.TreeHouse;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
using IGameObject = OmenTools.Dalamud.Services.ObjectTable.Abstractions.ObjectKinds.IGameObject;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal enum AutoGcSealDeliveryScanResult
{
    NotReady,
    Empty,
    Found
}

internal sealed unsafe class AutoGcSealInterop
{
    private const int ExpertDeliveryTabIndex = 2;
    private const int SealRewardValueOffset = 265;
    private const int ContainerValueOffset = 345;
    private const int SlotValueOffset = 385;
    private const int ItemIdValueOffset = 425;
    private const int ExchangeItemCountValueIndex = 1;
    private const int ExchangeItemIdValueOffset = 317;
    private const float WindowNpcDistance = 24.5f;
    private const string SupplyMenuTextKey = "TEXT_COMDEFGRANDCOMPANYOFFICER_00073_A4_002";

    private readonly ListItemLockStateCache rowLockCache = new();
    private string? supplyMenuText;

    public bool TryGetWindowContext(out uint currentSeals)
    {
        currentSeals = 0;
        if (!TryGetGrandCompany(out var grandCompany) ||
            DService.Instance().Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            !TryGetOfficerDistances(grandCompany, out var personnelDistanceSquared, out var quartermasterDistanceSquared) ||
            personnelDistanceSquared > WindowNpcDistance * WindowNpcDistance ||
            quartermasterDistanceSquared > WindowNpcDistance * WindowNpcDistance)
        {
            return false;
        }

        currentSeals = GetCurrentSeals();
        return true;
    }

    public bool CanOperate() => TryGetGrandCompany(out _);

    public static bool IsSupportedTerritory(uint territoryId) => territoryId is 128 or 130 or 132;

    public static uint GetCurrentSeals()
    {
        var playerState = PlayerState.Instance();
        var inventoryManager = InventoryManager.Instance();
        return playerState == null || inventoryManager == null || playerState->GrandCompany == 0
            ? 0
            : inventoryManager->GetCompanySeals(playerState->GrandCompany);
    }

    public bool TryOpenSupply()
    {
        if (!TryGetGrandCompany(out var grandCompany) ||
            !TryFindPersonnelOfficer(grandCompany, out var personnelOfficer))
        {
            return false;
        }

        var gameObject = (NativeGameObject*)personnelOfficer.Address;
        if (gameObject == null || gameObject->EventHandler == null)
        {
            return false;
        }

        var eventId = gameObject->EventHandler->Info.EventId.Id;
        if (eventId == 0 || personnelOfficer.GameObjectID == 0)
        {
            return false;
        }

        new EventStartPackt(personnelOfficer.GameObjectID, eventId).Send();
        return true;
    }

    public bool TryOpenExchange()
    {
        if (!TryGetGrandCompany(out var grandCompany) ||
            !TryFindPersonnelOfficer(grandCompany, out var personnelOfficer))
        {
            return false;
        }

        var eventId = DService.Instance().ClientState.TerritoryType switch
        {
            128 => 1_441_793U,
            132 => 1_441_794U,
            130 => 1_441_795U,
            _ => 0U
        };
        if (eventId == 0 || personnelOfficer.GameObjectID == 0)
        {
            return false;
        }

        new EventStartPackt(personnelOfficer.GameObjectID, eventId).Send();
        return true;
    }

    public bool TrySelectSupplyMenu()
    {
        supplyMenuText ??= ResolveOfficerText(SupplyMenuTextKey);
        if (!string.IsNullOrWhiteSpace(supplyMenuText))
        {
            return AddonSelectStringEvent.Select(supplyMenuText) ||
                   AddonSelectIconStringEvent.Select(supplyMenuText);
        }

        return AddonSelectStringEvent.Select(0) || AddonSelectIconStringEvent.Select(0);
    }

    public static bool IsSelectionMenuOpen()
    {
        var selectString = Addons.SelectString;
        if (selectString != null && selectString->IsAddonAndNodesReady())
        {
            return true;
        }

        var selectIconString = Addons.SelectIconString;
        return selectIconString != null && selectIconString->IsAddonAndNodesReady();
    }

    public static bool TryCloseSelectionMenu()
    {
        var closed = AddonSelectStringEvent.Select(-1);
        return AddonSelectIconStringEvent.Select(-1) || closed;
    }

    public static bool TryCloseConversation()
    {
        var operated = TryCloseSelectionMenu();
        var talk = Addons.Talk;
        if (talk != null && talk->IsVisible)
        {
            talk->Close(true);
            operated = true;
        }

        return operated;
    }

    public static bool IsSupplyOpen()
    {
        var addon = Addons.GrandCompanySupplyList;
        return addon != null && addon->IsAddonAndNodesReady();
    }

    public static bool IsExchangeOpen()
    {
        var addon = Addons.GrandCompanyExchange;
        return addon != null && addon->IsAddonAndNodesReady();
    }

    public static bool IsSupplyRewardOpen()
    {
        var addon = Addons.GrandCompanySupplyReward;
        return addon != null && addon->IsAddonAndNodesReady();
    }

    public static bool TrySelectExpertDeliveryTab()
    {
        if (!TryGetSupplyAddon(out var addon, out var supplyAddon))
        {
            return false;
        }

        if (IsExpertDeliveryTab(addon, supplyAddon))
        {
            return true;
        }

        addon->Callback(0, ExpertDeliveryTabIndex);
        return true;
    }

    public static bool IsExpertDeliveryTabReady()
    {
        return TryGetSupplyAddon(out var addon, out var supplyAddon) &&
               IsExpertDeliveryTab(addon, supplyAddon);
    }

    public AutoGcSealDeliveryScanResult ScanFirstDelivery(
        AutoGcSealConfig config,
        HashSet<ulong> skippedSlots,
        out AutoGcSealDeliveryItem item)
    {
        item = default;
        if (!TryGetSupplyAddon(out var addon, out var supplyAddon) ||
            !IsExpertDeliveryTab(addon, supplyAddon) ||
            supplyAddon->ExpertDeliveryList == null ||
            !supplyAddon->ExpertDeliveryList->AtkComponentBase.OwnerNode->AtkResNode.IsVisible())
        {
            return AutoGcSealDeliveryScanResult.NotReady;
        }

        var agent = AgentGrandCompanySupply.Instance();
        if (agent == null || !agent->IsAgentActive() || agent->ItemArray == null)
        {
            return AutoGcSealDeliveryScanResult.NotReady;
        }

        var listLength = Math.Max(0, supplyAddon->ExpertDeliveryList->ListLength);
        for (var dataIndex = 0; dataIndex < listLength; dataIndex++)
        {
            if (!TryResolveDeliveryItem(addon, dataIndex, out var candidate) ||
                !IsExpertDeliveryItem(agent, candidate.ItemId) ||
                ShouldSkipDelivery(config, skippedSlots, candidate))
            {
                continue;
            }

            item = candidate;
            return AutoGcSealDeliveryScanResult.Found;
        }

        return AutoGcSealDeliveryScanResult.Empty;
    }

    public static bool TryHandIn(AutoGcSealDeliveryItem item)
    {
        if (!TryGetSupplyAddon(out var addon, out var supplyAddon) ||
            !IsExpertDeliveryTab(addon, supplyAddon))
        {
            return false;
        }

        addon->Callback(1, item.ListIndex);
        return true;
    }

    public static bool TryRefreshSupplyList()
    {
        if (IsSupplyRewardOpen() || !IsExpertDeliveryTabReady())
        {
            return false;
        }

        AgentId.GrandCompanySupply.SendEvent(0, 0, ExpertDeliveryTabIndex);
        return true;
    }

    public static bool TryCloseSupplyList()
    {
        var addon = Addons.GrandCompanySupplyList;
        if (addon == null || !addon->IsAddonAndNodesReady())
        {
            return false;
        }

        addon->Close(true);
        return true;
    }

    public static bool TryConfirmSupplyReward(bool confirm)
    {
        var addon = Addons.GrandCompanySupplyReward;
        if (addon == null || !addon->IsAddonAndNodesReady())
        {
            return false;
        }

        addon->Callback(confirm ? 0 : 1);
        return true;
    }

    public static bool TryConfirmYesNo(bool confirm) =>
        confirm ? AddonSelectYesnoEvent.ClickYes() : AddonSelectYesnoEvent.ClickNo();

    public static bool TrySelectExchangeTier(AutoGcSealExchangeItem item)
    {
        if (!TryGetExchangeAddon(out var addon))
        {
            return false;
        }

        addon->Callback(1, Math.Max(0, (int)item.Tier - 1));
        return true;
    }

    public static bool TrySelectExchangeCategory(AutoGcSealExchangeItem item)
    {
        if (!TryGetExchangeAddon(out var addon))
        {
            return false;
        }

        addon->Callback(2, (int)item.Category);
        return true;
    }

    public static bool TryTriggerExchange(AutoGcSealExchangeItem item, int quantity)
    {
        if (quantity <= 0 || !TryGetExchangeAddon(out var addon) ||
            addon->AtkValuesCount <= ExchangeItemCountValueIndex)
        {
            return false;
        }

        var itemCount = addon->AtkValues[ExchangeItemCountValueIndex].UInt;
        for (var index = 0U; index < itemCount; index++)
        {
            var valueIndex = ExchangeItemIdValueOffset + (int)index;
            if (valueIndex >= addon->AtkValuesCount)
            {
                break;
            }

            if (addon->AtkValues[valueIndex].UInt != item.ItemId)
            {
                continue;
            }

            AgentId.GrandCompanyExchange.SendEvent(0, 0, (int)index, quantity, 0, true, false);
            return true;
        }

        return false;
    }

    public static bool TryCloseExchange()
    {
        var addon = Addons.GrandCompanyExchange;
        if (addon == null || !addon->IsAddonAndNodesReady())
        {
            return false;
        }

        addon->Close(true);
        return true;
    }

    public static bool TryGetExchangePlan(
        AutoGcSealConfig config,
        out AutoGcSealExchangeItem item,
        out int quantity)
    {
        item = default;
        quantity = 0;
        if (config.ExchangeItemId == 0 ||
            !TryGetGrandCompany(out var grandCompany) ||
            !TryResolveExchangeItem(config.ExchangeItemId, grandCompany, out item))
        {
            return false;
        }

        var currentSeals = GetCurrentSeals();
        var spendableSeals = currentSeals > config.ReservedSeals
            ? currentSeals - config.ReservedSeals
            : 0;
        if (item.Cost == 0)
        {
            return false;
        }

        quantity = (int)(spendableSeals / item.Cost);
        if (item.ItemId != 21_072)
        {
            quantity = Math.Min(quantity, 99);
        }

        return quantity > 0;
    }

    public static bool ShouldExchange(AutoGcSealConfig config) =>
        config.ExchangeThreshold > 0 &&
        GetCurrentSeals() >= config.ExchangeThreshold &&
        TryGetExchangePlan(config, out _, out _);

    public static bool WouldExceedSealCap(uint sealReward)
    {
        var playerState = PlayerState.Instance();
        if (playerState == null ||
            playerState->GrandCompany == 0 ||
            !LuminaGetter.TryGetRow<GrandCompanyRank>(playerState->GetGrandCompanyRank(), out var rank))
        {
            return true;
        }

        var afterReward = GetCurrentSeals() + (uint)(sealReward * GetSealMultiplier());
        return afterReward > rank.MaxSeals;
    }

    public static bool IsHighQuality(AutoGcSealDeliveryItem item)
    {
        var inventoryItem = GetInventoryItem(item.Container, item.Slot);
        return inventoryItem != null &&
               (inventoryItem->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) ||
                inventoryItem->SpiritbondOrCollectability > 10_000);
    }

    public static ulong GetInventorySlotKey(AutoGcSealDeliveryItem item) =>
        ((ulong)(uint)item.Container << 16) | item.Slot;

    public bool TryGetCurrentDeliveryItem(out AutoGcSealDeliveryItem item)
    {
        item = default;
        if (!TryGetSupplyAddon(out var addon, out var supplyAddon) ||
            !IsExpertDeliveryTab(addon, supplyAddon) ||
            supplyAddon->ExpertDeliveryList == null)
        {
            return false;
        }

        var list = supplyAddon->ExpertDeliveryList;
        var dataIndex = list->SelectedItemIndex;
        if (dataIndex < 0 || dataIndex >= list->ListLength)
        {
            dataIndex = list->HeldItemIndex;
        }

        if (dataIndex < 0 || dataIndex >= list->ListLength)
        {
            dataIndex = list->HoveredItemIndex;
        }

        return dataIndex >= 0 &&
               dataIndex < list->ListLength &&
               TryResolveDeliveryItem(addon, dataIndex, out item);
    }

    public bool TryCaptureProtectedSelection(
        AutoGcSealConfig config,
        AddonArgs args,
        out AutoGcSealDeliveryItem item)
    {
        item = default;
        if (args is not AddonReceiveEventArgs receiveArgs ||
            receiveArgs.AtkEventType is not (AddonEventType.MouseUp or AddonEventType.InputBaseInputReceived) ||
            !TryGetSupplyAddon(out var addon, out var supplyAddon) ||
            !IsExpertDeliveryTab(addon, supplyAddon) ||
            supplyAddon->ExpertDeliveryList == null ||
            supplyAddon->ExpertDeliveryList->ItemRendererList == null)
        {
            return false;
        }

        var target = TryGetEventTargetNode(receiveArgs);
        if (target == null)
        {
            return false;
        }

        var list = supplyAddon->ExpertDeliveryList;
        var rendererCount = Math.Max(0, (int)list->AllocatedItemRendererListLength);
        if (rendererCount == 0)
        {
            rendererCount = Math.Max(0, (int)list->NumVisibleItems);
        }

        if (rendererCount == 0)
        {
            rendererCount = Math.Max(0, (int)list->VisibleRowCount);
        }

        var listLength = Math.Max(0, list->ListLength);
        for (var visibleIndex = 0; visibleIndex < rendererCount; visibleIndex++)
        {
            var renderer = list->ItemRendererList[visibleIndex].AtkComponentListItemRenderer;
            if (renderer == null || !RendererContainsNode(renderer, target))
            {
                continue;
            }

            var dataIndex = GetRendererDataIndex(renderer, visibleIndex, listLength);
            if (dataIndex < 0 || dataIndex >= listLength ||
                !TryResolveDeliveryItem(addon, dataIndex, out item) ||
                !AutoGcSealExclusions.Contains(config, item.ItemId))
            {
                return false;
            }

            if (list->SelectedItemIndex == dataIndex)
            {
                list->DeselectItem();
            }

            list->SetItemDisabledState(dataIndex, true);
            rowLockCache.Apply(renderer, true);
            return true;
        }

        return false;
    }

    public void RefreshProtectedRows(AutoGcSealConfig config)
    {
        if (!TryGetSupplyAddon(out var addon, out var supplyAddon) ||
            supplyAddon->ExpertDeliveryList == null ||
            supplyAddon->ExpertDeliveryList->ItemRendererList == null)
        {
            return;
        }

        var list = supplyAddon->ExpertDeliveryList;
        var rendererCount = Math.Max(0, (int)list->AllocatedItemRendererListLength);
        if (rendererCount == 0)
        {
            rendererCount = Math.Max(0, (int)list->NumVisibleItems);
        }

        if (rendererCount == 0)
        {
            rendererCount = Math.Max(0, (int)list->VisibleRowCount);
        }

        var visibleCount = Math.Max(0, (int)list->NumVisibleItems);
        if (visibleCount == 0)
        {
            visibleCount = Math.Max(0, (int)list->VisibleRowCount);
        }

        if (visibleCount == 0)
        {
            visibleCount = rendererCount;
        }

        var listLength = Math.Max(0, list->ListLength);
        var lockEnabled = IsExpertDeliveryTab(addon, supplyAddon) && config.SkipExcludedItems;
        for (var visibleIndex = 0; visibleIndex < Math.Min(rendererCount, visibleCount); visibleIndex++)
        {
            var renderer = list->ItemRendererList[visibleIndex].AtkComponentListItemRenderer;
            if (renderer == null)
            {
                continue;
            }

            var dataIndex = GetRendererDataIndex(renderer, visibleIndex, listLength);
            var locked = lockEnabled &&
                         dataIndex >= 0 &&
                         dataIndex < listLength &&
                         TryResolveDeliveryItem(addon, dataIndex, out var item) &&
                         AutoGcSealExclusions.Contains(config, item.ItemId);
            if (locked && list->SelectedItemIndex == dataIndex)
            {
                list->DeselectItem();
            }

            if (dataIndex >= 0 && dataIndex < listLength)
            {
                list->SetItemDisabledState(dataIndex, locked);
            }

            rowLockCache.Apply(renderer, locked);
        }
    }

    public void RestoreProtectedRows()
    {
        if (TryGetSupplyAddon(out _, out var supplyAddon) &&
            supplyAddon->ExpertDeliveryList != null &&
            supplyAddon->ExpertDeliveryList->ItemRendererList != null)
        {
            var list = supplyAddon->ExpertDeliveryList;
            for (var index = 0; index < list->ListLength; index++)
            {
                list->SetItemDisabledState(index, false);
            }
        }

        rowLockCache.Restore();
    }

    public void ClearProtectedRowCache() => rowLockCache.Forget();

    private static bool TryGetGrandCompany(out GrandCompany grandCompany)
    {
        grandCompany = GrandCompany.None;
        var services = DService.Instance();
        var playerState = PlayerState.Instance();
        if (!services.ClientState.IsLoggedIn ||
            services.ObjectTable.LocalPlayer is null ||
            !IsSupportedTerritory(services.ClientState.TerritoryType) ||
            playerState == null)
        {
            return false;
        }

        grandCompany = (GrandCompany)playerState->GrandCompany;
        return grandCompany != GrandCompany.None && playerState->GetGrandCompanyRank() >= 6;
    }

    private static bool TryGetOfficerDistances(
        GrandCompany grandCompany,
        out float personnelDistanceSquared,
        out float quartermasterDistanceSquared)
    {
        personnelDistanceSquared = float.MaxValue;
        quartermasterDistanceSquared = float.MaxValue;
        var services = DService.Instance();
        var localPlayer = services.ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return false;
        }

        var personnelId = GetPersonnelOfficerId(grandCompany);
        var quartermasterId = GetQuartermasterId(grandCompany);
        foreach (var gameObject in services.ObjectTable)
        {
            if (!gameObject.IsValid() || gameObject.ObjectKind != ObjectKind.EventNpc)
            {
                continue;
            }

            var distanceSquared = Vector3.DistanceSquared(localPlayer.Position, gameObject.Position);
            if (gameObject.DataID == personnelId && distanceSquared < personnelDistanceSquared)
            {
                personnelDistanceSquared = distanceSquared;
            }
            else if (gameObject.DataID == quartermasterId && distanceSquared < quartermasterDistanceSquared)
            {
                quartermasterDistanceSquared = distanceSquared;
            }
        }

        return personnelDistanceSquared < float.MaxValue && quartermasterDistanceSquared < float.MaxValue;
    }

    private static bool TryFindPersonnelOfficer(GrandCompany grandCompany, out IGameObject personnelOfficer)
    {
        personnelOfficer = null!;
        var services = DService.Instance();
        var localPlayer = services.ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return false;
        }

        var personnelId = GetPersonnelOfficerId(grandCompany);
        var bestDistanceSquared = float.MaxValue;
        foreach (var gameObject in services.ObjectTable)
        {
            if (!gameObject.IsValid() ||
                gameObject.ObjectKind != ObjectKind.EventNpc ||
                gameObject.DataID != personnelId)
            {
                continue;
            }

            var distanceSquared = Vector3.DistanceSquared(localPlayer.Position, gameObject.Position);
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            personnelOfficer = gameObject;
            bestDistanceSquared = distanceSquared;
        }

        return personnelOfficer is not null;
    }

    private static uint GetPersonnelOfficerId(GrandCompany grandCompany) => grandCompany switch
    {
        GrandCompany.Maelstrom => 1_002_388,
        GrandCompany.ImmortalFlames => 1_002_391,
        GrandCompany.TwinAdder => 1_002_394,
        _ => 0
    };

    private static uint GetQuartermasterId(GrandCompany grandCompany) => grandCompany switch
    {
        GrandCompany.Maelstrom => 1_002_387,
        GrandCompany.ImmortalFlames => 1_002_390,
        GrandCompany.TwinAdder => 1_002_393,
        _ => 0
    };

    private static bool TryGetSupplyAddon(
        out AtkUnitBase* addon,
        out AddonGrandCompanySupplyList* supplyAddon)
    {
        addon = Addons.GrandCompanySupplyList;
        supplyAddon = (AddonGrandCompanySupplyList*)addon;
        return addon != null && addon->IsAddonAndNodesReady();
    }

    private static bool IsExpertDeliveryTab(
        AtkUnitBase* addon,
        AddonGrandCompanySupplyList* supplyAddon) =>
        addon != null &&
        supplyAddon != null &&
        addon->AtkValuesCount > 0 &&
        addon->AtkValues[0].UInt == ExpertDeliveryTabIndex &&
        supplyAddon->SelectedTab == ExpertDeliveryTabIndex;

    private static bool TryResolveDeliveryItem(
        AtkUnitBase* addon,
        int dataIndex,
        out AutoGcSealDeliveryItem item)
    {
        item = default;
        if (addon == null || dataIndex < 0)
        {
            return false;
        }

        var rewardIndex = SealRewardValueOffset + dataIndex;
        var containerIndex = ContainerValueOffset + dataIndex;
        var slotIndex = SlotValueOffset + dataIndex;
        var itemIdIndex = ItemIdValueOffset + dataIndex;
        if (rewardIndex >= addon->AtkValuesCount ||
            containerIndex >= addon->AtkValuesCount ||
            slotIndex >= addon->AtkValuesCount ||
            itemIdIndex >= addon->AtkValuesCount)
        {
            return false;
        }

        var container = (InventoryType)addon->AtkValues[containerIndex].UInt;
        var slot = (ushort)addon->AtkValues[slotIndex].UInt;
        var inventoryItem = GetInventoryItem(container, slot);
        var itemId = inventoryItem == null
            ? NormalizeItemId(addon->AtkValues[itemIdIndex].UInt)
            : NormalizeItemId(inventoryItem->GetBaseItemId());
        if (itemId == 0)
        {
            return false;
        }

        item = new(itemId, container, slot, addon->AtkValues[rewardIndex].UInt, dataIndex);
        return true;
    }

    private static InventoryItem* GetInventoryItem(InventoryType container, ushort slot)
    {
        if (container == 0 || container == InventoryType.Invalid || slot == ushort.MaxValue)
        {
            return null;
        }

        var inventoryManager = InventoryManager.Instance();
        return inventoryManager == null ? null : inventoryManager->GetInventorySlot(container, slot);
    }

    private static bool IsExpertDeliveryItem(AgentGrandCompanySupply* agent, uint itemId)
    {
        for (var index = 11U; index < agent->NumItems; index++)
        {
            var agentItem = agent->ItemArray[index];
            if (NormalizeItemId(agentItem.ItemId) == itemId)
            {
                return !agentItem.IsBonusReward && agentItem.ExpReward <= 0 && agentItem.SealReward > 0;
            }
        }

        return false;
    }

    private static bool ShouldSkipDelivery(
        AutoGcSealConfig config,
        HashSet<ulong> skippedSlots,
        AutoGcSealDeliveryItem item)
    {
        if (AutoGcSealExclusions.Contains(config, item.ItemId))
        {
            return true;
        }

        if (skippedSlots.Contains(GetInventorySlotKey(item)))
        {
            return true;
        }

        var inventoryItem = GetInventoryItem(item.Container, item.Slot);
        if (inventoryItem == null)
        {
            return false;
        }

        if (config.SkipHqItems &&
            (inventoryItem->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) ||
             inventoryItem->SpiritbondOrCollectability > 10_000))
        {
            return true;
        }

        if (!config.SkipMateriaItems ||
            !LuminaGetter.TryGetRow<Item>(item.ItemId, out var itemRow) ||
            itemRow.MateriaSlotCount == 0)
        {
            return false;
        }

        for (var materiaIndex = 0;
             materiaIndex < Math.Min((int)itemRow.MateriaSlotCount, inventoryItem->Materia.Length);
             materiaIndex++)
        {
            if (inventoryItem->Materia[materiaIndex] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static decimal GetSealMultiplier()
    {
        var localPlayer = DService.Instance().ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return 1M;
        }

        foreach (var status in localPlayer.StatusList)
        {
            if (status.StatusID == 1078)
            {
                return 1.15M;
            }
        }

        foreach (var status in localPlayer.StatusList)
        {
            if (status.StatusID == 414)
            {
                return 1M + status.Param / 100M;
            }
        }

        return 1M;
    }

    private static bool TryResolveExchangeItem(
        uint itemId,
        GrandCompany grandCompany,
        out AutoGcSealExchangeItem item)
    {
        item = default;
        var playerState = PlayerState.Instance();
        if (playerState == null)
        {
            return false;
        }

        var rank = playerState->GetGrandCompanyRank();
        foreach (var subrows in LuminaGetter.GetSub<GCScripShopItem>())
        {
            foreach (var shopItem in subrows)
            {
                if (shopItem.Item.RowId != itemId ||
                    shopItem.CostGCSeals == 0 ||
                    rank < shopItem.RequiredGrandCompanyRank.RowId ||
                    !LuminaGetter.TryGetRow<GCScripShopCategory>(shopItem.RowId, out var category) ||
                    category.GrandCompany.RowId != (uint)grandCompany)
                {
                    continue;
                }

                item = new(itemId, shopItem.CostGCSeals, (uint)category.Tier, (uint)category.SubCategory);
                return true;
            }
        }

        return false;
    }

    private static bool TryGetExchangeAddon(out AtkUnitBase* addon)
    {
        addon = Addons.GrandCompanyExchange;
        return addon != null && addon->IsAddonAndNodesReady();
    }

    private static int GetRendererDataIndex(
        AtkComponentListItemRenderer* renderer,
        int fallbackIndex,
        int listLength)
    {
        var index = renderer->ListItemIndex;
        return index >= 0 && index < listLength ? index : fallbackIndex;
    }

    private static AtkResNode* TryGetEventTargetNode(AddonReceiveEventArgs args)
    {
        try
        {
            var atkEvent = (byte*)args.AtkEvent;
            if (atkEvent == null)
            {
                return null;
            }

            var target = *(AtkResNode**)atkEvent;
            return target == null ? *(AtkResNode**)(atkEvent + 8) : target;
        }
        catch
        {
            return null;
        }
    }

    private static bool RendererContainsNode(AtkComponentListItemRenderer* renderer, AtkResNode* target)
    {
        if (renderer == null || target == null || renderer->UldManager.RootNode == null)
        {
            return false;
        }

        var root = renderer->UldManager.RootNode;
        while (root->PrevSiblingNode != null)
        {
            root = root->PrevSiblingNode;
        }

        for (var node = root; node != null; node = node->NextSiblingNode)
        {
            if (ContainsNode(node, target, 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsNode(AtkResNode* root, AtkResNode* target, int depth)
    {
        if (root == null || target == null || depth > ListItemLockStateCache.MaxDepth)
        {
            return false;
        }

        if (root == target)
        {
            return true;
        }

        for (var child = root->ChildNode; child != null; child = child->PrevSiblingNode)
        {
            if (ContainsNode(child, target, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static uint NormalizeItemId(uint itemId) =>
        itemId < 2_000_000 ? itemId % 500_000 : itemId;

    private static string? ResolveOfficerText(string key)
    {
        try
        {
            foreach (var row in DalamudServices.DataManager.GetExcelSheet<AutoGcSealOfficerText>())
            {
                if (string.Equals(row.Key.ToString(), key, StringComparison.Ordinal))
                {
                    return row.Value.ToString();
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}

#pragma warning disable DR0009
[Sheet("custom/000/ComDefGrandCompanyOfficer_00073")]
internal readonly struct AutoGcSealOfficerText(ExcelPage page, uint offset, uint row)
    : IExcelRow<AutoGcSealOfficerText>
{
    public ExcelPage ExcelPage => page;

    public uint RowOffset => offset;

    public uint RowId => row;

    public ReadOnlySeString Key => page.ReadString(offset, offset);

    public ReadOnlySeString Value => page.ReadString(offset + 4, offset);

    static AutoGcSealOfficerText IExcelRow<AutoGcSealOfficerText>.Create(
        ExcelPage page,
        uint offset,
        uint row) => new(page, offset, row);
}
#pragma warning restore DR0009
