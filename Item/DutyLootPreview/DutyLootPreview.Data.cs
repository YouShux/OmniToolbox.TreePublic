using System.Linq;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using OmenTools.Info.Game.ItemSource;
using OmenTools.Info.Game.ItemSource.Enums;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Host;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class DutyLootPreview
{
    private readonly Dictionary<uint, Dictionary<uint, HashSet<string>>> dropsByDuty = [];

    private void LoadDrops()
    {
        dropsByDuty.Clear();
        var bosses = new Dictionary<(uint ContentFinderConditionID, uint FightNo), DungeonBoss>();
        foreach (var boss in Load<DungeonBoss>(CsvLoader.DungeonBossResourceName))
        {
            bosses.TryAdd((boss.ContentFinderConditionId, boss.FightNo), boss);
        }

        var chests = Load<DungeonChest>(CsvLoader.DungeonChestResourceName)
            .ToDictionary(static chest => chest.RowId);

        foreach (var drop in Load<DungeonBossDrop>(CsvLoader.DungeonBossDropResourceName))
        {
            AddBoss(drop.ContentFinderConditionId, drop.FightNo, drop.ItemId);
        }

        foreach (var drop in Load<DungeonBossChest>(CsvLoader.DungeonBossChestResourceName))
        {
            AddBoss(drop.ContentFinderConditionId, drop.FightNo, drop.ItemId);
        }

        foreach (var drop in Load<DungeonChestItem>(CsvLoader.DungeonChestItemResourceName))
        {
            if (chests.TryGetValue(drop.ChestId, out var chest))
            {
                AddDrop(chest.ContentFinderConditionId, drop.ItemId, OmniLoc.Get("Feature.DutyLootPreview.Chest"));
            }
        }

        return;

        void AddBoss(uint dutyID, uint fight, uint itemID)
        {
            var source = bosses.TryGetValue((dutyID, fight), out var boss) &&
                         LuminaGetter.TryGetRow<BNpcName>(boss.BNpcNameId, out var name)
                ? name.Singular.ExtractText()
                : string.Format(OmniLoc.Get("Feature.DutyLootPreview.Boss"), fight);
            AddDrop(dutyID, itemID, source);
        }
    }

    private static List<T> Load<T>(string resourceName) where T : ICsv, new()
    {
        var rows = CsvLoader.LoadResource<T>(resourceName, true, out var failedLines, out var exceptions);
        if (failedLines.Count != 0)
        {
            DalamudServices.PluginLog.Warning(
                $"Duty loot resource {resourceName}: {failedLines.Count} invalid rows. {exceptions.FirstOrDefault()?.Message}");
        }

        return rows;
    }

    private void AddDrop(uint dutyID, uint itemID, string source)
    {
        if (dutyID == 0 || itemID == 0)
        {
            return;
        }

        if (!dropsByDuty.TryGetValue(dutyID, out var items))
        {
            dropsByDuty.Add(dutyID, items = []);
        }

        if (!items.TryGetValue(itemID, out var sources))
        {
            items.Add(itemID, sources = []);
        }

        sources.Add(source);
    }

    private List<LootItem> BuildItems(uint dutyID)
    {
        exchangeDataPending = false;
        if (!dropsByDuty.TryGetValue(dutyID, out var drops))
        {
            return [];
        }

        nextExchangeRetryAt = Environment.TickCount64 + 500;
        var allDrops = drops.ToDictionary(
            static pair => pair.Key,
            static pair => new HashSet<string>(pair.Value));

        foreach (var (currencyID, _) in drops)
        {
            var exchange = ItemSourceInfo.QueryExchangeItems(currencyID);
            if (exchange.State == ItemSourceQueryState.Building)
            {
                exchangeDataPending = true;
                continue;
            }

            if (exchange is not { State: ItemSourceQueryState.Ready, Data: { } data })
            {
                continue;
            }

            var currencyName = LuminaGetter.TryGetRow<Item>(currencyID, out var currency) &&
                               !currency.Name.IsEmpty
                ? currency.Name.ExtractText()
                : currencyID.ToString();
            foreach (var exchangedItem in data.Items)
            {
                if (exchangedItem.ItemID == 0)
                {
                    continue;
                }

                if (!allDrops.TryGetValue(exchangedItem.ItemID, out var exchangedSources))
                {
                    allDrops.Add(exchangedItem.ItemID, exchangedSources = []);
                }

                exchangedSources.Add(
                    string.Format(OmniLoc.Get("Feature.DutyLootPreview.Exchange"), currencyName));
            }
        }

        var items = new List<LootItem>(allDrops.Count);
        foreach (var (itemID, sources) in allDrops)
        {
            if (!LuminaGetter.TryGetRow<Item>(itemID, out var item) || item.Icon == 0 || item.Name.IsEmpty)
            {
                continue;
            }

            items.Add(new(
                this,
                item,
                item.Name.ExtractText(),
                string.Join('\n', sources),
                DalamudServices.UnlockState.IsItemUnlockable(item)));
        }

        return items.OrderBy(static item => item.IsEquipment ? 2 : item.IsUnlockable ? 0 : 1)
            .ThenByDescending(static item => item.Row.ItemUICategory.ValueNullable?.OrderMajor ?? 0)
            .ThenByDescending(static item => item.Row.ItemUICategory.ValueNullable?.OrderMinor ?? 0)
            .ThenBy(static item => item.Name, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record LootItem(DutyLootPreview Owner, Item Row, string Name, string Sources, bool IsUnlockable)
    {
        public bool IsEquipment => Row.EquipSlotCategory.RowId != 0;

        public bool IsFavorite => Owner.config.FavoriteItems.Contains(Row.RowId);

        public bool IsUnlocked => IsUnlockable && DalamudServices.UnlockState.IsItemUnlocked(Row);
    }
}
