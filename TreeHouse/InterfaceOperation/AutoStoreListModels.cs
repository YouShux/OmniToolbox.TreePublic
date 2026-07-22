using System.Globalization;
using Lumina.Excel.Sheets;
using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.UI;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal sealed record AutoStoreListItem(
    uint ItemId,
    string Name,
    uint IconId,
    int Quantity,
    ushort Level,
    uint ItemLevel,
    string Locations,
    int RetrievePriority)
{
    public bool Matches(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var text = searchText.Trim();
        return Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               ItemId.ToString(CultureInfo.InvariantCulture).Contains(text, StringComparison.OrdinalIgnoreCase) ||
               Level.ToString(CultureInfo.InvariantCulture).Contains(text, StringComparison.OrdinalIgnoreCase) ||
               ItemLevel.ToString(CultureInfo.InvariantCulture).Contains(text, StringComparison.OrdinalIgnoreCase) ||
               Locations.Contains(text, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class AutoStoreListOptions
{
    public static readonly AutoStoreListSource[] Sources =
    [
        AutoStoreListSource.All,
        AutoStoreListSource.Inventory,
        AutoStoreListSource.Saddlebag,
        AutoStoreListSource.FreeCompanyChest,
        AutoStoreListSource.Retainer1,
        AutoStoreListSource.Retainer2,
        AutoStoreListSource.Retainer3,
        AutoStoreListSource.Retainer4,
        AutoStoreListSource.Retainer5,
        AutoStoreListSource.Retainer6,
        AutoStoreListSource.Retainer7,
        AutoStoreListSource.Retainer8,
        AutoStoreListSource.Retainer9,
        AutoStoreListSource.Retainer10
    ];

    public static readonly AutoStoreListTarget[] Targets =
    [
        AutoStoreListTarget.Saddlebag,
        AutoStoreListTarget.GlamourDresser,
        AutoStoreListTarget.Armoire,
        AutoStoreListTarget.FreeCompanyChest,
        AutoStoreListTarget.Retainer1,
        AutoStoreListTarget.Retainer2,
        AutoStoreListTarget.Retainer3,
        AutoStoreListTarget.Retainer4,
        AutoStoreListTarget.Retainer5,
        AutoStoreListTarget.Retainer6,
        AutoStoreListTarget.Retainer7,
        AutoStoreListTarget.Retainer8,
        AutoStoreListTarget.Retainer9,
        AutoStoreListTarget.Retainer10
    ];

    public static readonly AutoStoreHotkeyModifier[] Modifiers =
    [
        AutoStoreHotkeyModifier.None,
        AutoStoreHotkeyModifier.Control,
        AutoStoreHotkeyModifier.Shift,
        AutoStoreHotkeyModifier.Alt
    ];

    public static string GetSourceLabel(AutoStoreListSource source)
    {
        if (source is >= AutoStoreListSource.Retainer1 and <= AutoStoreListSource.Retainer10)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.AutoStore.Source.Retainer"),
                (int)source - (int)AutoStoreListSource.Retainer1 + 1);
        }

        return OmniLoc.Get(source switch
        {
            AutoStoreListSource.All => "Feature.AutoStore.Source.All",
            AutoStoreListSource.Inventory => "Feature.AutoStore.Source.Inventory",
            AutoStoreListSource.Saddlebag => "Feature.AutoStore.Source.Saddlebag",
            AutoStoreListSource.FreeCompanyChest => "Feature.AutoStore.Source.FreeCompanyChest",
            _ => "Feature.AutoStore.Source.All"
        });
    }

    public static string GetTargetLabel(AutoStoreListTarget target)
    {
        if (target is >= AutoStoreListTarget.Retainer1 and <= AutoStoreListTarget.Retainer10)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.AutoStore.Target.Retainer"),
                (int)target - (int)AutoStoreListTarget.Retainer1 + 1);
        }

        return OmniLoc.Get(target switch
        {
            AutoStoreListTarget.Saddlebag => "Feature.AutoStore.Target.Saddlebag",
            AutoStoreListTarget.GlamourDresser => "Feature.AutoStore.Target.GlamourDresser",
            AutoStoreListTarget.Armoire => "Feature.AutoStore.Target.Armoire",
            AutoStoreListTarget.FreeCompanyChest => "Feature.AutoStore.Target.FreeCompanyChest",
            _ => "Feature.AutoStore.Target.Saddlebag"
        });
    }

    public static string GetModifierLabel(AutoStoreHotkeyModifier modifier) => OmniLoc.Get(modifier switch
    {
        AutoStoreHotkeyModifier.Control => "Feature.AutoStore.Hotkey.Modifier.Control",
        AutoStoreHotkeyModifier.Shift => "Feature.AutoStore.Hotkey.Modifier.Shift",
        AutoStoreHotkeyModifier.Alt => "Feature.AutoStore.Hotkey.Modifier.Alt",
        _ => "Feature.AutoStore.Hotkey.Modifier.None"
    });
}

internal static class AutoStoreListProjection
{
    private const int UnknownPriority = 999;

    public static void Build(
        IReadOnlyList<InventorySnapshotItem> snapshot,
        AutoStoreListSource source,
        List<AutoStoreListItem> target)
    {
        var items = new Dictionary<uint, ItemAccumulator>();
        var retainerIndices = new Dictionary<ulong, int>();
        foreach (var item in snapshot)
        {
            if (item.ItemId == 0 || !IsSupported(item.Location))
            {
                continue;
            }

            var retainerIndex = item.Location == ItemInventoryLocation.Retainer
                ? GetRetainerIndex(item.HolderId, retainerIndices)
                : -1;
            if (!MatchesSource(item.Location, retainerIndex, source))
            {
                continue;
            }

            if (!items.TryGetValue(item.ItemId, out var accumulated))
            {
                accumulated = new();
                items.Add(item.ItemId, accumulated);
            }

            accumulated.Quantity = (int)Math.Min((long)accumulated.Quantity + Math.Max(0, item.Quantity), int.MaxValue);
            accumulated.Locations |= item.Location;
            accumulated.RetrievePriority = Math.Min(
                accumulated.RetrievePriority,
                GetPriority(item.Location, retainerIndex));
            if (item.Location == ItemInventoryLocation.Retainer)
            {
                if (retainerIndex is >= 0 and < 10)
                {
                    accumulated.RetainerMask |= (ushort)(1 << retainerIndex);
                }
                else
                {
                    accumulated.HasUnknownRetainer = true;
                }
            }
        }

        target.Clear();
        foreach (var (itemId, accumulated) in items)
        {
            if (!LuminaGetter.TryGetRow<Item>(itemId, out var row))
            {
                continue;
            }

            var name = row.Name.ExtractText();
            target.Add(new(
                itemId,
                string.IsNullOrWhiteSpace(name)
                    ? string.Format(CultureInfo.CurrentCulture, OmniLoc.Get("Feature.AutoStore.List.UnknownItem"), itemId)
                    : name,
                row.Icon,
                accumulated.Quantity,
                row.LevelEquip,
                row.LevelItem.RowId,
                FormatLocations(accumulated),
                accumulated.RetrievePriority));
        }

        target.Sort(static (left, right) => right.ItemId.CompareTo(left.ItemId));
    }

    public static AutoStoreListItem CreatePlaceholder(uint itemId)
    {
        if (LuminaGetter.TryGetRow<Item>(itemId, out var row))
        {
            var name = row.Name.ExtractText();
            return new(
                itemId,
                string.IsNullOrWhiteSpace(name)
                    ? string.Format(CultureInfo.CurrentCulture, OmniLoc.Get("Feature.AutoStore.List.UnknownItem"), itemId)
                    : name,
                row.Icon,
                0,
                row.LevelEquip,
                row.LevelItem.RowId,
                string.Empty,
                UnknownPriority);
        }

        return new(
            itemId,
            string.Format(CultureInfo.CurrentCulture, OmniLoc.Get("Feature.AutoStore.List.UnknownItem"), itemId),
            0,
            0,
            0,
            0,
            string.Empty,
            UnknownPriority);
    }

    private static bool IsSupported(ItemInventoryLocation location) => location is
        ItemInventoryLocation.Inventory or
        ItemInventoryLocation.Saddlebag or
        ItemInventoryLocation.FreeCompanyChest or
        ItemInventoryLocation.Retainer or
        ItemInventoryLocation.GlamourDresser or
        ItemInventoryLocation.Armoire;

    private static bool MatchesSource(
        ItemInventoryLocation location,
        int retainerIndex,
        AutoStoreListSource source)
    {
        if (source is >= AutoStoreListSource.Retainer1 and <= AutoStoreListSource.Retainer10)
        {
            return location == ItemInventoryLocation.Retainer &&
                   retainerIndex == (int)source - (int)AutoStoreListSource.Retainer1;
        }

        return source switch
        {
            AutoStoreListSource.All => true,
            AutoStoreListSource.Inventory => location == ItemInventoryLocation.Inventory,
            AutoStoreListSource.Saddlebag => location == ItemInventoryLocation.Saddlebag,
            AutoStoreListSource.FreeCompanyChest => location == ItemInventoryLocation.FreeCompanyChest,
            _ => false
        };
    }

    private static int GetRetainerIndex(ulong holderId, Dictionary<ulong, int> indices)
    {
        if (holderId == 0)
        {
            return -1;
        }

        if (indices.TryGetValue(holderId, out var index))
        {
            return index;
        }

        index = AutoStoreNativeUi.TryGetRetainerIndex(holderId, out var resolved) ? resolved : -1;
        indices.Add(holderId, index);
        return index;
    }

    private static int GetPriority(ItemInventoryLocation location, int retainerIndex) => location switch
    {
        ItemInventoryLocation.Inventory => 0,
        ItemInventoryLocation.Saddlebag => 1,
        ItemInventoryLocation.FreeCompanyChest => 2,
        ItemInventoryLocation.Retainer => retainerIndex >= 0 ? 101 + retainerIndex : 100,
        ItemInventoryLocation.GlamourDresser => 200,
        ItemInventoryLocation.Armoire => 201,
        _ => UnknownPriority
    };

    private static string FormatLocations(ItemAccumulator item)
    {
        var labels = new List<string>(16);
        if ((item.Locations & ItemInventoryLocation.Inventory) != 0)
        {
            labels.Add(AutoStoreListOptions.GetSourceLabel(AutoStoreListSource.Inventory));
        }

        if ((item.Locations & ItemInventoryLocation.Saddlebag) != 0)
        {
            labels.Add(AutoStoreListOptions.GetSourceLabel(AutoStoreListSource.Saddlebag));
        }

        if ((item.Locations & ItemInventoryLocation.FreeCompanyChest) != 0)
        {
            labels.Add(AutoStoreListOptions.GetSourceLabel(AutoStoreListSource.FreeCompanyChest));
        }

        for (var index = 0; index < 10; index++)
        {
            if ((item.RetainerMask & 1 << index) != 0)
            {
                labels.Add(AutoStoreListOptions.GetSourceLabel((AutoStoreListSource)(101 + index)));
            }
        }

        if (item.HasUnknownRetainer)
        {
            labels.Add(OmniLoc.Get("Feature.AutoStore.Source.RetainerUnknown"));
        }

        if ((item.Locations & ItemInventoryLocation.GlamourDresser) != 0)
        {
            labels.Add(OmniLoc.Get("Feature.AutoStore.Source.GlamourDresser"));
        }

        if ((item.Locations & ItemInventoryLocation.Armoire) != 0)
        {
            labels.Add(OmniLoc.Get("Feature.AutoStore.Source.Armoire"));
        }

        return string.Join(", ", labels);
    }

    private sealed class ItemAccumulator
    {
        public int Quantity { get; set; }

        public ItemInventoryLocation Locations { get; set; }

        public ushort RetainerMask { get; set; }

        public int RetrievePriority { get; set; } = UnknownPriority;

        public bool HasUnknownRetainer { get; set; }
    }
}
