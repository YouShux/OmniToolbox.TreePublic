using System.Diagnostics;
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
    private readonly Dictionary<(uint ContentFinderConditionID, uint FightNo), DungeonBoss> bossRows = [];
    private readonly Dictionary<(uint ContentFinderConditionID, uint FightNo), string> bossSources = [];
    private readonly Dictionary<uint, DungeonChest> chestRows = [];
    private List<DungeonChestItem>? pendingChestItems;
    private int pendingChestItemIndex;
    private int dropsLoadStep = -1;

    private void BeginLoadDrops()
    {
        dropsByDuty.Clear();
        bossRows.Clear();
        bossSources.Clear();
        chestRows.Clear();
        pendingChestItems = null;
        pendingChestItemIndex = 0;
        dropsLoadStep = 0;
    }

    private bool StepLoadDrops()
    {
        var phaseStarted = Stopwatch.GetTimestamp();
        switch (dropsLoadStep)
        {
            case 0:
                foreach (var boss in Load<DungeonBoss>(CsvLoader.DungeonBossResourceName))
                {
                    bossRows.TryAdd((boss.ContentFinderConditionId, boss.FightNo), boss);
                }
                dropsLoadStep = 1;
                return false;
            case 1:
                foreach (var chest in Load<DungeonChest>(CsvLoader.DungeonChestResourceName))
                {
                    chestRows.TryAdd(chest.RowId, chest);
                }
                dropsLoadStep = 2;
                return false;
            case 2:
                foreach (var drop in Load<DungeonBossDrop>(CsvLoader.DungeonBossDropResourceName))
                {
                    AddBoss(drop.ContentFinderConditionId, drop.FightNo, drop.ItemId);
                }
                dropsLoadStep = 3;
                return false;
            case 3:
                foreach (var drop in Load<DungeonBossChest>(CsvLoader.DungeonBossChestResourceName))
                {
                    AddBoss(drop.ContentFinderConditionId, drop.FightNo, drop.ItemId);
                }
                dropsLoadStep = 4;
                return false;
            case 4:
                pendingChestItems = Load<DungeonChestItem>(CsvLoader.DungeonChestItemResourceName);
                pendingChestItemIndex = 0;
                dropsLoadStep = 5;
                return false;
            case 5:
                var chestSource = OmniLoc.Get("Feature.DutyLootPreview.Chest");
                while (pendingChestItems is not null && pendingChestItemIndex < pendingChestItems.Count)
                {
                    var drop = pendingChestItems[pendingChestItemIndex++];
                    if (chestRows.TryGetValue(drop.ChestId, out var chest))
                    {
                        AddDrop(chest.ContentFinderConditionId, drop.ItemId, chestSource);
                    }

                    if (Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds >= 8)
                        break;
                }
                if (pendingChestItems is not null && pendingChestItemIndex < pendingChestItems.Count)
                    return false;

                pendingChestItems = null;
                dropsLoadStep = -1;
                return true;
            default:
                return true;
        }
    }

    private void AddBoss(uint dutyID, uint fight, uint itemID)
    {
        if (!bossSources.TryGetValue((dutyID, fight), out var source))
        {
            source = bossRows.TryGetValue((dutyID, fight), out var boss) &&
                     LuminaGetter.TryGetRow<BNpcName>(boss.BNpcNameId, out var name)
                ? name.Singular.ExtractText()
                : string.Format(OmniLoc.Get("Feature.DutyLootPreview.Boss"), fight);
            bossSources.Add((dutyID, fight), source);
        }

        AddDrop(dutyID, itemID, source);
    }

    private static List<T> Load<T>(string resourceName) where T : ICsv, new()
    {
        var rows = CsvLoader.LoadResource<T>(resourceName, true, out var failedLines, out var exceptions);

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
