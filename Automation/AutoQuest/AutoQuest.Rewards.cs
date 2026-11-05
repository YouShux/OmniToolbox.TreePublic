using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Quests;
using LuminaQuest = Lumina.Excel.Sheets.Quest;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    private unsafe string? DriveQuestReward(Session current, AddonJournalResult* addon, QuestTarget target)
    {
        if (!IsOwnedQuestEvent(current))
            return "external_interaction_busy";
        if (current.TurnInSent)
            return null;
        var native = (AtkUnitBase*)addon;
        var agent = AgentJournalResult.Instance();
        var manager = InventoryManager.Instance();
        if (!native->IsReady || agent == null || manager == null || !agent->IsAgentActive())
            return "resource_loading";
        if (addon->CompleteButton == null || agent->GetAddonId() != native->Id ||
            !LuminaGetter.TryGetRow<LuminaQuest>(target.ID, out var quest) || agent->RewardItems.Count > 32)
            return "quest_reward_unresolved";
        var rewards = new Dictionary<uint, long>();
        for (var index = 0; index < agent->RewardItems.Count; index++)
        {
            var reward = agent->RewardItems[index];
            if (reward.ItemId == 0)
                continue;
            if (reward.Amount <= 0)
                return "quest_reward_unresolved";
            rewards[reward.ItemId] = rewards.GetValueOrDefault(reward.ItemId) + reward.Amount;
        }
        var expected = new Dictionary<uint, long>();
        for (var index = 0; index < quest.Reward.Count; index++)
        {
            var item = quest.Reward[index];
            if (item.RowId == 0)
                continue;
            if (!item.Is<Item>() || quest.ItemCountReward[index] == 0)
                return "quest_reward_unresolved";
            expected[item.RowId] = expected.GetValueOrDefault(item.RowId) + quest.ItemCountReward[index];
        }
        for (var index = 0; index < quest.ItemCatalyst.Count; index++)
            if (quest.ItemCatalyst[index].RowId != 0 && quest.ItemCountCatalyst[index] != 0)
            {
                var itemID = quest.ItemCatalyst[index].RowId;
                expected[itemID] = expected.GetValueOrDefault(itemID) + quest.ItemCountCatalyst[index];
            }
        for (var index = 0; index < quest.OptionalItemReward.Count; index++)
            if (quest.OptionalItemReward[index].RowId != 0)
            {
                var itemID = quest.OptionalItemReward[index].RowId;
                expected[itemID] = expected.GetValueOrDefault(itemID) + quest.OptionalItemCountReward[index];
            }
        var actual = new Dictionary<uint, long>();
        foreach (var (rawID, quantity) in rewards)
        {
            var itemID = rawID is >= 1_000_000 and < 2_000_000 ? rawID - 1_000_000 : rawID;
            actual[itemID] = actual.GetValueOrDefault(itemID) + quantity;
        }
        foreach (var (itemID, quantity) in expected)
            if (actual.GetValueOrDefault(itemID) != quantity)
                return "quest_reward_unresolved";
        foreach (var (itemID, quantity) in actual)
            if (!expected.ContainsKey(itemID) &&
                !(itemID == 1 && quantity == quest.GilReward) &&
                !(itemID == quest.CurrencyReward.RowId && quantity == quest.CurrencyRewardCount))
                return "quest_reward_unresolved";
        if (native->AtkValues == null || native->AtkValuesCount < 98)
            return "quest_reward_unresolved";
        var alternatives = new List<QuestRewardChoice>();
        for (var index = 0; index < quest.OptionalItemReward.Count; index++)
        {
            var itemID = quest.OptionalItemReward[index].RowId;
            var displayed = native->AtkValues[82 + index];
            if (displayed.Type is not (AtkValueType.UInt or AtkValueType.Int))
                return "quest_reward_unresolved";
            if (itemID == 0)
            {
                if (displayed.UInt != 0)
                    return "quest_reward_unresolved";
                continue;
            }
            var rawID = itemID + (quest.OptionalItemIsHQReward[index] ? 1_000_000U : 0);
            var quantity = quest.OptionalItemCountReward[index];
            var amount = native->AtkValues[93 + index];
            if (quantity == 0 || displayed.UInt != rawID || amount.Type is not (AtkValueType.UInt or AtkValueType.Int) ||
                amount.UInt != quantity || !rewards.TryGetValue(rawID, out var available) || available < quantity ||
                !LuminaGetter.TryGetRow<Item>(itemID, out var item))
                return "quest_reward_unresolved";
            rewards[rawID] = available - quantity;
            var improvement = GetRewardGearImprovement(item, manager);
            alternatives.Add(new(rawID, quantity, improvement, item.LevelItem.RowId, (ulong)item.PriceLow * quantity));
        }
        if (quest.GilReward != 0)
            rewards[1] = Math.Max(rewards.GetValueOrDefault(1U), quest.GilReward);
        if (quest.CurrencyReward.RowId != 0 && quest.CurrencyRewardCount != 0)
            rewards[quest.CurrencyReward.RowId] = Math.Max(rewards.GetValueOrDefault(quest.CurrencyReward.RowId), quest.CurrencyRewardCount);
        var chosen = 0U;
        string? capacityError = null;
        if (alternatives.Count != 0)
        {
            foreach (var candidate in alternatives.OrderByDescending(static value => value.Improvement)
                         .ThenByDescending(static value => value.Improvement == 0 ? 0 : value.ItemLevel)
                         .ThenByDescending(static value => value.Value).ThenBy(static value => value.ItemID))
            {
                rewards[candidate.ItemID] = rewards.GetValueOrDefault(candidate.ItemID) + candidate.Quantity;
                var error = CheckRewardCapacity(manager, rewards);
                rewards[candidate.ItemID] -= candidate.Quantity;
                if (error is not null)
                {
                    capacityError ??= error;
                    continue;
                }
                chosen = candidate.ItemID;
                break;
            }
            if (chosen == 0)
                return capacityError ?? "quest_reward_unresolved";
        }
        else
        {
            capacityError = CheckRewardCapacity(manager, rewards);
            if (capacityError is not null)
                return capacityError;
            if (!addon->CompleteButton->IsEnabled)
                return "quest_reward_unavailable";
        }
        current.TurnInSent = true;
        data.MarkTurnInRequested(target);
        native->Callback(0, chosen);
        return null;
    }

    private static unsafe uint GetRewardGearImprovement(Item candidate, InventoryManager* manager)
    {
        var player = PlayerState.Instance();
        if (player == null || !player->IsLoaded || candidate.EquipSlotCategory.RowId == 0 ||
            !CanUseRewardGear(candidate, player))
            return 0;
        var category = candidate.EquipSlotCategory.Value;
        var targetSlot = -1;
        for (var slot = 0; slot < 14; slot++)
            if (RewardFitsSlot(category, slot))
            {
                targetSlot = slot;
                break;
            }
        if (targetSlot < 0)
            return 0;
        var rings = category.FingerR > 0 && category.FingerL > 0;
        uint best = 0;
        uint second = 0;
        var unique = new HashSet<uint>();
        foreach (var type in RewardGearInventories)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                return 0;
            for (var index = 0; index < container->Size; index++)
            {
                var owned = container->GetInventorySlot(index);
                if (owned == null || owned->GetBaseItemId() == 0 ||
                    !LuminaGetter.TryGetRow<Item>(owned->GetBaseItemId(), out var item) || item.EquipSlotCategory.RowId == 0 ||
                    !RewardFitsSlot(item.EquipSlotCategory.Value, targetSlot) || !CanUseRewardGear(item, player))
                    continue;
                if (rings && item.IsUnique && !unique.Add(item.RowId))
                    continue;
                var level = item.LevelItem.RowId;
                if (level >= best)
                {
                    second = best;
                    best = level;
                }
                else if (level > second)
                    second = level;
            }
        }
        var baseline = rings ? second : best;
        return candidate.LevelItem.RowId > baseline ? candidate.LevelItem.RowId - baseline : 0;
    }

    private static unsafe bool CanUseRewardGear(Item item, PlayerState* player) =>
        item.ClassJobCategory.RowId != 0 && item.ClassJobCategory.Value.IsClassJobIn(player->CurrentClassJobId) &&
        item.LevelEquip <= player->CurrentLevel &&
        InventoryManager.CanEquip(item.RowId, player->Race, player->Sex, (ushort)player->CurrentLevel,
                                  player->CurrentClassJobId, player->GrandCompany, 0, null) == 0;

    private static bool RewardFitsSlot(EquipSlotCategory category, int slot) => slot switch
    {
        0 => category.MainHand > 0,
        1 => category.OffHand > 0,
        2 => category.Head > 0,
        3 => category.Body > 0,
        4 => category.Gloves > 0,
        6 => category.Legs > 0,
        7 => category.Feet > 0,
        8 => category.Ears > 0,
        9 => category.Neck > 0,
        10 => category.Wrists > 0,
        11 => category.FingerR > 0,
        12 => category.FingerL > 0,
        13 => category.SoulCrystal > 0,
        _ => false
    };

    private static unsafe string? CheckRewardCapacity(InventoryManager* manager, Dictionary<uint, long> rewards)
    {
        long empty = 0;
        foreach (var type in RewardBagInventories)
        {
            var inventory = manager->GetInventoryContainer(type);
            if (inventory == null || !inventory->IsLoaded)
                return "resource_loading";
            for (var index = 0; index < inventory->Size; index++)
            {
                var slot = inventory->GetInventorySlot(index);
                if (slot != null && slot->GetBaseItemId() == 0)
                    empty++;
            }
        }
        var currencies = CurrencyManager.Instance();
        if (currencies == null)
            return "resource_loading";
        foreach (var (rawID, quantity) in rewards)
        {
            if (quantity <= 0)
                continue;
            var itemID = rawID is >= 1_000_000 and < 2_000_000 ? rawID - 1_000_000 : rawID;
            if (rawID >= 2_000_000 || !LuminaGetter.TryGetRow<Item>(itemID, out var item) || item.StackSize == 0)
                return "quest_reward_capacity_unresolved";
            var hq = rawID != itemID;
            if (item.IsUnique && (rewards.GetValueOrDefault(itemID) + rewards.GetValueOrDefault(itemID + 1_000_000) > 1 ||
                                 manager->GetInventoryItemCount(itemID, false) > 0 ||
                                 manager->GetInventoryItemCount(itemID, true) > 0))
                return "quest_reward_unique_owned";
            if (!hq && currencies->HasItem(itemID))
            {
                if (currencies->IsItemLimited(itemID) && quantity > currencies->GetItemCountRemaining(itemID))
                    return "quest_reward_inventory_full";
                continue;
            }
            if (!hq && TryGetDedicatedRewardCapacity(manager, InventoryType.Currency, itemID, item.StackSize, out var currencyCapacity))
            {
                if (quantity > currencyCapacity)
                    return "quest_reward_inventory_full";
                continue;
            }
            if (!hq && item.FilterGroup == 11)
            {
                if (!TryGetDedicatedRewardCapacity(manager, InventoryType.Crystals, itemID, item.StackSize, out var crystalCapacity))
                    return "quest_reward_capacity_unresolved";
                if (quantity > crystalCapacity)
                    return "quest_reward_inventory_full";
                continue;
            }
            long stackCapacity = 0;
            foreach (var type in RewardBagInventories)
            {
                var inventory = manager->GetInventoryContainer(type);
                for (var index = 0; index < inventory->Size; index++)
                {
                    var slot = inventory->GetInventorySlot(index);
                    if (slot == null || slot->GetItemId() != rawID || slot->IsCollectable())
                        continue;
                    stackCapacity += Math.Max(0, (long)item.StackSize - slot->GetQuantity());
                }
            }
            var remaining = Math.Max(0, quantity - stackCapacity);
            empty -= (remaining + item.StackSize - 1) / item.StackSize;
            if (empty < 0)
                return "quest_reward_inventory_full";
        }
        return null;
    }

    private static unsafe bool TryGetDedicatedRewardCapacity
    (
        InventoryManager* manager,
        InventoryType type,
        uint itemID,
        uint stackSize,
        out long capacity
    )
    {
        capacity = 0;
        var inventory = manager->GetInventoryContainer(type);
        if (inventory == null || !inventory->IsLoaded)
            return false;
        for (var index = 0; index < inventory->Size; index++)
        {
            var slot = inventory->GetInventorySlot(index);
            if (slot == null || slot->GetBaseItemId() != itemID)
                continue;
            capacity = Math.Max(0, (long)stackSize - slot->GetQuantity());
            return true;
        }
        return false;
    }

    private static readonly InventoryType[] RewardBagInventories =
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];

    private static readonly InventoryType[] RewardGearInventories =
    [
        InventoryType.EquippedItems,
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
        InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings, InventoryType.ArmorySoulCrystal
    ];

    private sealed record QuestRewardChoice(uint ItemID, int Quantity, uint Improvement, uint ItemLevel, ulong Value);
}
