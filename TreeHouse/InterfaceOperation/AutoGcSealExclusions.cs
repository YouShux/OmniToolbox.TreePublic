using System.Collections.Frozen;
using Lumina.Excel.Sheets;
using OmniToolbox.Collections;
using OmniToolbox.Config;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class AutoGcSealExclusions
{
    private static readonly uint[] AdditionalRelicItemIds =
    [
        41_679, 41_680, 41_681, 41_682, 41_683, 41_684, 41_685, 41_686, 41_687, 41_688,
        41_689, 41_690, 41_691, 41_692, 41_693, 41_694, 41_695, 41_696, 41_697, 41_698,
        41_700, 44_243, 44_244, 49_826, 49_827, 49_828, 49_829, 49_830
    ];

    private static readonly Lazy<FrozenSet<uint>> DefaultItemIds = new(BuildDefaultItemIds);

    public static bool Contains(AutoGcSealConfig config, uint itemId) =>
        config.SkipExcludedItems &&
        (DefaultItemIds.Value.Contains(itemId) || config.CustomExcludedItemIds.Contains(itemId));

    private static FrozenSet<uint> BuildDefaultItemIds()
    {
        var itemIds = new HashSet<uint>(RelicWeaponCatalog.AllKnownItemIds);
        itemIds.UnionWith(AdditionalRelicItemIds);
        AddUltimateTotemExchangeItems(itemIds);
        return itemIds.ToFrozenSet();
    }

    private static void AddUltimateTotemExchangeItems(HashSet<uint> itemIds)
    {
        foreach (var shop in LuminaGetter.Get<SpecialShop>())
        {
            foreach (var entry in shop.Item)
            {
                var usesUltimateTotem = false;
                foreach (var cost in entry.ItemCosts)
                {
                    if (Array.IndexOf(RelicWeaponCatalog.UltimateTotemItemIds, cost.ItemCost.RowId) < 0)
                    {
                        continue;
                    }

                    usesUltimateTotem = true;
                    break;
                }

                if (!usesUltimateTotem)
                {
                    continue;
                }

                foreach (var receiveItem in entry.ReceiveItems)
                {
                    if (receiveItem.Item.RowId == 0)
                    {
                        continue;
                    }

                    var item = receiveItem.Item.Value;
                    if (item.FilterGroup is 1 or 2 or 3)
                    {
                        itemIds.Add(item.RowId);
                    }
                }
            }
        }
    }
}
