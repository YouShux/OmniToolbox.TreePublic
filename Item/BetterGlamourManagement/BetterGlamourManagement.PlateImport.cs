using Dalamud.Interface.ImGuiNotification;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Interop.Game.Lumina;
using TreeTools.Threading;
using OmniToolbox.Items;
using OmniToolbox.Notifications;
using Cabinet = Lumina.Excel.Sheets.Cabinet;
using GameCabinet = FFXIVClientStructs.FFXIV.Client.Game.UI.Cabinet;
using Item = Lumina.Excel.Sheets.Item;
using Stain = Lumina.Excel.Sheets.Stain;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMiragePrismMiragePlateData;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterGlamourManagement
{
    private TaskHelper? plateImportTaskHelper;

    private bool CanImportCurrentGlamourPlate() =>
        plateImportTaskHelper?.IsBusy != true && TryGetPlateImportEditor(out _);

    private void AbortPlateImport()
    {
        if (plateImportTaskHelper?.IsBusy == true)
            plateImportTaskHelper.Abort();
    }

    private void DisposePlateImport()
    {
        plateImportTaskHelper = null;
    }

    private void ImportPresetToCurrentGlamourPlate(BetterGlamourPreset preset)
    {
        if (plateImportTaskHelper?.IsBusy == true)
        {
            OmniNotifier.Popup(Title, OmniLoc.Get(this, "Feature.BetterGlamourManagement.PlateImport.Busy"), NotificationType.Warning);
            return;
        }
        if (!TryGetPlateImportEditor(out var agent))
        {
            OmniNotifier.Popup(Title, OmniLoc.Get(this, "Feature.BetterGlamourManagement.PlateImport.Unavailable"), NotificationType.Warning);
            return;
        }

        var plateIndex = agent->Data->SelectedMiragePlateIndex;
        var contentID = DService.Instance().PlayerState.ContentId;
        var plan = new List<PlateImportSlot>(InspectSlots.Length);
        var reservedDyes = new Dictionary<(InventoryType, short), PlateImportDye>();
        var missingDyes = new Dictionary<byte, int>();
        var missingItems = 0;
        var ignoredChannels = 0;
        Inventories.Player.TryGetItems(item => item.ItemId != 0 && item.Quantity > 0, out var inventory);
        var manager = MirageManager.Instance();
        var uiState = UIState.Instance();
        var cabinet = uiState != null && uiState->Cabinet.IsCabinetLoaded() ? &uiState->Cabinet : null;
        for (var index = 0; index < InspectSlots.Length; index++)
        {
            var item = preset.Items.Find(entry => entry.Slot == InspectSlots[index]);
            var itemID = item is null ? 0 : ItemUtil.GetBaseId(item.ItemID).ItemId;
            if (itemID == 0 ||
                !LuminaGetter.TryGetRow<Item>(itemID, out var row) ||
                !FitsSlot(row, InspectSlots[index]))
            {
                if (itemID != 0)
                    missingItems++;
                plan.Add(new((uint)index, default, 0, 0, 0));
                continue;
            }

            var stain0 = row.DyeCount > 0 ? item?.Stain0 ?? 0 : (byte)0;
            var stain1 = row.DyeCount > 1 ? item?.Stain1 ?? 0 : (byte)0;
            ignoredChannels += row.DyeCount < 1 && item?.Stain0 > 0 ? 1 : 0;
            ignoredChannels += row.DyeCount < 2 && item?.Stain1 > 0 ? 1 : 0;
            var available0 = GetAvailablePlateImportDyes(stain0, inventory, reservedDyes);
            var available1 = stain0 == stain1 ? available0 : GetAvailablePlateImportDyes(stain1, inventory, reservedDyes);
            var source = FindPlateImportSource(manager, cabinet, itemID, index, stain0, stain1, row.DyeCount, available0, available1);
            var slot = new PlateImportSlot((uint)index, source, stain0, stain1, row.DyeCount);
            plan.Add(slot);
            if (source.ItemID == 0)
            {
                missingItems++;
                continue;
            }
            if (row.DyeCount > 0 && source.Stain0 != stain0)
                slot.Dye0 = ReservePlateImportDye(stain0, inventory, reservedDyes, missingDyes);
            if (row.DyeCount > 1 && source.Stain1 != stain1)
                slot.Dye1 = ReservePlateImportDye(stain1, inventory, reservedDyes, missingDyes);
        }

        if (missingDyes.Count > 0)
        {
            var shortages = string.Join("、", missingDyes.Select(entry =>
            {
                var name = GetDyeName(entry.Key);
                if (entry.Key == 0 && LuminaGetter.TryGetRow<Stain>(0, out var stain) &&
                    LuminaGetter.TryGetRow<Item>(stain.Item[0].RowId, out var dyeItem))
                    name = dyeItem.Name.ToString();
                return $"{name} × {entry.Value}";
            }));
            OmniNotifier.Popup(
                Title,
                string.Format(OmniLoc.Get(this, "Feature.BetterGlamourManagement.PlateImport.MissingDyes"), shortages),
                NotificationType.Warning);
            return;
        }

        if (plateImportTaskHelper is null)
        {
            plateImportTaskHelper = new()
            {
                TimeoutMS = 10_000
            };
            Activation.Add(plateImportTaskHelper);
            plateImportTaskHelper.TaskTimeout += _ => NotifyPlateImportFailure(OmniLoc.Get(this, "Feature.BetterGlamourManagement.PlateImport.Timeout"));
            plateImportTaskHelper.TaskGetException += _ => NotifyPlateImportFailure(OmniLoc.Get(this, "Feature.BetterGlamourManagement.PlateImport.SourceChanged"));
        }

        plateImportTaskHelper.Enqueue(() =>
        {
            if (GetPlateImportEditor(plateIndex, contentID) == null)
                return;
            foreach (var slot in plan)
                ValidatePlateImportSource(slot);
            foreach (var dye in reservedDyes.Values)
                GetPlateImportDyeItem(dye);
        });
        foreach (var slot in plan)
        {
            plateImportTaskHelper.Enqueue(() =>
            {
                var editor = GetPlateImportEditor(plateIndex, contentID);
                if (editor == null)
                    return;
                ValidatePlateImportSource(slot);
                editor->Data->SelectedItemIndex = slot.Index;
                editor->ClearItemData(slot.Index);
                if (editor->Data->CurrentItems[(int)slot.Index].ItemId != 0)
                    throw new InvalidOperationException();
                if (slot.Source.ItemID != 0)
                    editor->SetSelectedItemData(
                        slot.Source.Kind,
                        slot.Source.SourceID,
                        slot.Source.ItemID,
                        slot.Source.Stain0,
                        slot.Source.Stain1);
                editor->Data->HasChanges = true;
                editor->CharaView.IsUpdatePending = true;
            });
            plateImportTaskHelper.Enqueue(() =>
            {
                var editor = GetPlateImportEditor(plateIndex, contentID);
                if (editor == null)
                    return true;
                ref var current = ref editor->Data->CurrentItems[(int)slot.Index];
                if (ItemUtil.GetBaseId(current.ItemId).ItemId != ItemUtil.GetBaseId(slot.Source.ItemID).ItemId)
                    return false;
                ValidatePlateImportSource(slot);
                if (slot.Dye0 is { } dye0)
                {
                    current.Flags |= ItemFlag.HasStain0;
                    editor->SetItemStain(slot.Index, slot.Stain0, GetPlateImportDyeItem(dye0), dye0.Item.ItemId, 0);
                }
                if (slot.Dye1 is { } dye1)
                {
                    current.Flags |= ItemFlag.HasStain1;
                    editor->SetItemStain(slot.Index, slot.Stain1, GetPlateImportDyeItem(dye1), dye1.Item.ItemId, 1);
                }
                editor->Data->HasChanges = true;
                editor->CharaView.IsUpdatePending = true;
                return true;
            });
        }
        plateImportTaskHelper.Enqueue(() =>
        {
            var editor = GetPlateImportEditor(plateIndex, contentID);
            if (editor == null)
                return true;
            if (editor->CharaView.IsUpdatePending)
                return false;
            foreach (var slot in plan)
            {
                ValidatePlateImportSource(slot);
                var current = editor->Data->CurrentItems[(int)slot.Index];
                if (ItemUtil.GetBaseId(current.ItemId).ItemId != ItemUtil.GetBaseId(slot.Source.ItemID).ItemId)
                    throw new InvalidOperationException();
                if (slot.Source.ItemID == 0)
                    continue;
                if (slot.DyeCount > 0 && (slot.Dye0 is not null ? current.PendingStainIds[0] : current.StainIds[0]) != slot.Stain0 ||
                    slot.DyeCount > 1 && (slot.Dye1 is not null ? current.PendingStainIds[1] : current.StainIds[1]) != slot.Stain1)
                    throw new InvalidOperationException();
            }
            foreach (var dye in reservedDyes.Values)
                GetPlateImportDyeItem(dye);
            OmniNotifier.Popup(
                Title,
                string.Format(
                    OmniLoc.Get(this, "Feature.BetterGlamourManagement.PlateImport.Completed"),
                    plateIndex + 1,
                    missingItems,
                    ignoredChannels),
                NotificationType.Success);
            return true;
        });
    }

    private static bool TryGetPlateImportEditor(out AgentMiragePrismMiragePlate* agent)
    {
        agent = AgentMiragePrismMiragePlate.Instance();
        var manager = MirageManager.Instance();
        return DService.Instance().ClientState.IsLoggedIn &&
               DService.Instance().PlayerState.ContentId != 0 &&
               TryGetReadyAddon(GLAMOUR_PLATE_ADDON_NAME, out var addon) && addon->IsVisible &&
               agent != null && agent->IsAgentActive() && agent->Data != null &&
               agent->Data->OpenMode == 0 && agent->Data->SelectedMiragePlateIndex < MIRAGE_PLATE_COUNT &&
               agent->DialogAddonId == 0 && manager != null && manager->PrismBoxLoaded;
    }

    private AgentMiragePrismMiragePlate* GetPlateImportEditor(uint plateIndex, ulong contentID)
    {
        if (TryGetPlateImportEditor(out var agent) &&
            agent->Data->SelectedMiragePlateIndex == plateIndex &&
            DService.Instance().PlayerState.ContentId == contentID)
            return agent;
        AbortPlateImport();
        NotifyPlateImportFailure(OmniLoc.Get(this, "Feature.BetterGlamourManagement.PlateImport.Interrupted"));
        return null;
    }

    private void NotifyPlateImportFailure(string reason) => OmniNotifier.Popup(
        Title,
        string.Format(OmniLoc.Get(this, "Feature.BetterGlamourManagement.PlateImport.Failed"), reason),
        NotificationType.Warning);

    private static PlateImportSource FindPlateImportSource(
        MirageManager* manager,
        GameCabinet* cabinet,
        uint itemID,
        int slot,
        byte stain0,
        byte stain1,
        byte dyeCount,
        int available0,
        int available1)
    {
        var best = default(PlateImportSource);
        var bestCost = int.MaxValue;
        var bestMissing = int.MaxValue;
        for (var index = 0; index < manager->PrismBoxItemIds.Length; index++)
        {
            var rawID = manager->PrismBoxItemIds[index];
            if (rawID == 0)
                continue;
            var storedID = ItemUtil.GetBaseId(rawID).ItemId;
            var parts = ItemSetCatalog.GetItemSlots(storedID);
            var setSlot = Math.Min(slot, 10);
            var isSetPart = parts.Count > setSlot && parts[setSlot] == itemID && manager->IsSetSlotUnlocked((uint)index, setSlot);
            if (!isSetPart && (storedID != itemID || parts.Count != 0))
                continue;
            var source = new PlateImportSource(
                ItemSource.PrismBox,
                (uint)index,
                isSetPart ? itemID : rawID,
                rawID,
                isSetPart ? (byte)0 : manager->PrismBoxStain0Ids[index],
                isSetPart ? (byte)0 : manager->PrismBoxStain1Ids[index]);
            ConsiderSource(source);
            if (bestCost == 0)
                return best;
        }

        if (cabinet != null)
        {
            foreach (var row in LuminaGetter.Get<Cabinet>())
            {
                if (row.Item.RowId != itemID || !cabinet->IsItemInCabinet(row.RowId))
                    continue;
                ConsiderSource(new(ItemSource.Cabinet, row.RowId, itemID, itemID, 0, 0));
                break;
            }
        }
        return best;

        void ConsiderSource(PlateImportSource source)
        {
            var cost = GetPlateImportDyeCost(source.Stain0, source.Stain1, stain0, stain1, dyeCount);
            var need0 = dyeCount > 0 && source.Stain0 != stain0 ? 1 : 0;
            var need1 = dyeCount > 1 && source.Stain1 != stain1 ? 1 : 0;
            var missing = stain0 == stain1
                ? Math.Max(0, need0 + need1 - available0)
                : Math.Max(0, need0 - available0) + Math.Max(0, need1 - available1);
            if (missing > bestMissing || missing == bestMissing && cost >= bestCost)
                return;
            best = source;
            bestCost = cost;
            bestMissing = missing;
        }
    }

    private static int GetAvailablePlateImportDyes(
        byte stainID,
        List<InventoryItem> inventory,
        Dictionary<(InventoryType, short), PlateImportDye> reserved)
    {
        if (!LuminaGetter.TryGetRow<Stain>(stainID, out var stain))
            return 0;
        var count = 0;
        foreach (var item in inventory)
        {
            if (item.ItemId != stain.Item[0].RowId && item.ItemId != stain.Item[1].RowId)
                continue;
            reserved.TryGetValue((item.Container, item.Slot), out var reservation);
            count += item.Quantity - (reservation?.Count ?? 0);
        }
        return count;
    }

    private static PlateImportDye? ReservePlateImportDye(
        byte stainID,
        List<InventoryItem> inventory,
        Dictionary<(InventoryType, short), PlateImportDye> reserved,
        Dictionary<byte, int> missing)
    {
        if (LuminaGetter.TryGetRow<Stain>(stainID, out var stain) &&
            TryReservePlateImportDye(stain.Item[0].RowId, stain.Item[1].RowId, inventory, reserved) is { } reservation)
            return reservation;
        missing[stainID] = missing.GetValueOrDefault(stainID) + 1;
        return null;
    }

    private static int GetPlateImportDyeCost(byte source0, byte source1, byte target0, byte target1, byte dyeCount) =>
        (dyeCount > 0 && source0 != target0 ? 1 : 0) + (dyeCount > 1 && source1 != target1 ? 1 : 0);

    private static PlateImportDye? TryReservePlateImportDye(
        uint firstItemID,
        uint secondItemID,
        List<InventoryItem> inventory,
        Dictionary<(InventoryType, short), PlateImportDye> reserved)
    {
        foreach (var dyeID in new[] { firstItemID, secondItemID })
        {
            if (dyeID == 0)
                continue;
            foreach (var item in inventory)
            {
                if (item.ItemId != dyeID)
                    continue;
                var key = (item.Container, item.Slot);
                reserved.TryGetValue(key, out var reservation);
                if ((reservation?.Count ?? 0) >= item.Quantity)
                    continue;
                if (reservation is null)
                {
                    reservation = new(item);
                    reserved.Add(key, reservation);
                }
                reservation.Count++;
                return reservation;
            }
        }
        return null;
    }

    private static InventoryItem* GetPlateImportDyeItem(PlateImportDye dye)
    {
        var manager = InventoryManager.Instance();
        var container = manager != null ? manager->GetInventoryContainer(dye.Item.Container) : null;
        var item = container != null && container->IsLoaded ? container->GetInventorySlot(dye.Item.Slot) : null;
        if (item == null || item->ItemId != dye.Item.ItemId || item->Quantity < dye.Count)
            throw new InvalidOperationException();
        return item;
    }

    private static void ValidatePlateImportSource(PlateImportSlot slot)
    {
        var source = slot.Source;
        if (source.ItemID == 0)
            return;
        if (source.Kind == ItemSource.Cabinet)
        {
            var state = UIState.Instance();
            if (state == null || !state->Cabinet.IsCabinetLoaded() || !state->Cabinet.IsItemInCabinet(source.SourceID))
                throw new InvalidOperationException();
            return;
        }
        var manager = MirageManager.Instance();
        if (manager == null || !manager->PrismBoxLoaded || source.SourceID >= manager->PrismBoxItemIds.Length ||
            manager->PrismBoxItemIds[(int)source.SourceID] != source.StoredItemID)
            throw new InvalidOperationException();
        if (ItemUtil.GetBaseId(source.ItemID).ItemId != ItemUtil.GetBaseId(source.StoredItemID).ItemId)
        {
            if (!manager->IsSetSlotUnlocked(source.SourceID, (int)Math.Min(slot.Index, 10U)))
                throw new InvalidOperationException();
        }
        else if (manager->PrismBoxStain0Ids[(int)source.SourceID] != source.Stain0 ||
                 manager->PrismBoxStain1Ids[(int)source.SourceID] != source.Stain1)
            throw new InvalidOperationException();
    }

    private readonly record struct PlateImportSource(
        ItemSource Kind,
        uint SourceID,
        uint ItemID,
        uint StoredItemID,
        byte Stain0,
        byte Stain1);

    private sealed record PlateImportSlot(uint Index, PlateImportSource Source, byte Stain0, byte Stain1, byte DyeCount)
    {
        public PlateImportDye? Dye0;
        public PlateImportDye? Dye1;
    }

    private sealed class PlateImportDye(InventoryItem item)
    {
        public InventoryItem Item { get; } = item;
        public int Count;
    }
}
