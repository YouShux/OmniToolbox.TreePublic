using System.Text;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using OmniToolbox.Items;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Extensions;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterGlamourManagement
{
    private static BetterGlamourItemSearchResult[]? itemSearchIndex;
    private static BetterGlamourItemSearchResult[]? hairstyleSearchIndex;
    private static BetterGlamourItemSearchResult[]? glassesSearchIndex;

    internal static readonly GlamourPart[] Parts =
    [
        new("Feature.BetterGlamourManagement.Part.Head", 2),
        new("Feature.BetterGlamourManagement.Part.Body", 3),
        new("Feature.BetterGlamourManagement.Part.Hands", 4),
        new("Feature.BetterGlamourManagement.Part.Legs", 6),
        new("Feature.BetterGlamourManagement.Part.Feet", 7),
        new("Feature.BetterGlamourManagement.Part.Ears", 8),
        new("Feature.BetterGlamourManagement.Part.Neck", 9),
        new("Feature.BetterGlamourManagement.Part.Wrists", 10),
        new("Feature.BetterGlamourManagement.Part.RingRight", 11),
        new("Feature.BetterGlamourManagement.Part.RingLeft", 12)
    ];

    private static List<GlamourDyeOption>? dyeOptions;

    private void NormalizeSelection()
    {
        if (config.Presets.Count == 0)
        {
            selectedPresetIndex = -1;
        }
        else if (selectedPresetIndex < 0 || selectedPresetIndex >= config.Presets.Count)
        {
            selectedPresetIndex = 0;
        }
    }

    internal static bool TryGetGearsetName(int index, out string name)
    {
        name = string.Empty;
        var module = RaptureGearsetModule.Instance();
        var gearset = module != null ? module->GetGearset(index) : null;
        if (gearset == null ||
            gearset->Id != index ||
            !gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
        {
            return false;
        }

        name = gearset->NameString;
        return !string.IsNullOrWhiteSpace(name);
    }

    internal static BetterGlamourItem GetOrCreateItem(BetterGlamourPreset preset, int slot)
    {
        var item = preset.Items.Find(entry => entry.Slot == slot);
        if (item is not null)
        {
            return item;
        }

        item = new() { Slot = slot };
        preset.Items.Add(item);
        return item;
    }

    private void CopyPreset(BetterGlamourPreset preset)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Format(
            OmniLoc.Get("Feature.BetterGlamourManagement.Export.Single"),
            OmniLoc.Get("Feature.BetterGlamourManagement.Hairstyle"),
            GetHairstyleName(preset.HairstyleID)));
        builder.AppendLine(string.Format(
            OmniLoc.Get("Feature.BetterGlamourManagement.Export.Weapon"),
            FormatWeapon(preset)));
        AppendPair(builder, preset, Parts[0], Parts[5]);
        AppendPair(builder, preset, Parts[1], Parts[6]);
        AppendPair(builder, preset, Parts[2], Parts[7]);
        AppendPair(builder, preset, Parts[3], Parts[8]);
        AppendPair(builder, preset, Parts[4], Parts[9]);
        builder.Append(string.Format(
            OmniLoc.Get("Feature.BetterGlamourManagement.Export.Single"),
            OmniLoc.Get("Feature.BetterGlamourManagement.Part.Glasses"),
            GetGlassesName(preset.GlassesID)));
        ImGui.SetClipboardText(builder.ToString());
        OmniNotifier.Popup(
            Info.Title,
            OmniLoc.Get("Feature.BetterGlamourManagement.Copied"),
            Dalamud.Interface.ImGuiNotification.NotificationType.Success);
    }

    private static void AppendPair(
        StringBuilder builder,
        BetterGlamourPreset preset,
        GlamourPart left,
        GlamourPart right) => builder.AppendLine(string.Format(
        OmniLoc.Get("Feature.BetterGlamourManagement.Export.Pair"),
        OmniLoc.Get(left.TextKey),
        FormatItem(GetOrCreateItem(preset, left.Slot)),
        OmniLoc.Get(right.TextKey),
        FormatItem(GetOrCreateItem(preset, right.Slot))));

    private static string FormatWeapon(BetterGlamourPreset preset)
    {
        var mainHand = FormatItem(GetOrCreateItem(preset, 0));
        var offHand = GetOrCreateItem(preset, 1);
        return offHand.ItemID == 0
            ? mainHand
            : $"{OmniLoc.Get("Feature.BetterGlamourManagement.MainHand")}：{mainHand}；{OmniLoc.Get("Feature.BetterGlamourManagement.OffHand")}：{FormatItem(offHand)}";
    }

    private static string FormatItem(BetterGlamourItem item)
    {
        if (item.ItemID == 0)
        {
            return OmniLoc.Get("Feature.BetterGlamourManagement.None");
        }

        if (!LuminaGetter.TryGetRow<Item>(item.ItemID, out var row))
        {
            return string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.UnknownItem"), item.ItemID);
        }

        var name = row.Name.ExtractText();
        return row.DyeCount switch
        {
            0 => name,
            1 => $"{name} {GetDyeName(item.Stain0)}",
            _ => $"{name} {GetDyeName(item.Stain0)} {GetDyeName(item.Stain1)}"
        };
    }

    internal static string GetItemName(uint itemID)
    {
        if (itemID == 0)
        {
            return OmniLoc.Get("Feature.BetterGlamourManagement.None");
        }

        var name = LuminaWrapper.GetItemName(itemID);
        return string.IsNullOrWhiteSpace(name)
            ? string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.UnknownItem"), itemID)
            : name;
    }

    internal static List<BetterGlamourItemSearchResult> SearchItems(string query)
    {
        itemSearchIndex ??= BuildItemSearchIndex();
        return SearchItems(query, itemSearchIndex);
    }

    internal static List<BetterGlamourItemSearchResult> SearchHairstyles(string query)
    {
        hairstyleSearchIndex ??= BuildHairstyleSearchIndex();
        return SearchItems(query, hairstyleSearchIndex);
    }

    internal static List<BetterGlamourItemSearchResult> SearchGlasses(string query)
    {
        glassesSearchIndex ??= BuildGlassesSearchIndex();
        return SearchItems(query, glassesSearchIndex);
    }

    private static List<BetterGlamourItemSearchResult> SearchItems(
        string query,
        BetterGlamourItemSearchResult[] searchIndex)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        var results = new List<BetterGlamourItemSearchResult>();
        for (var index = 0; index < searchIndex.Length; index++)
        {
            if (searchIndex[index].Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(searchIndex[index]);
            }
        }

        for (var index = 0; index < searchIndex.Length; index++)
        {
            if (!searchIndex[index].Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                searchIndex[index].Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(searchIndex[index]);
        }

        return results;
    }

    private static BetterGlamourItemSearchResult[] BuildItemSearchIndex()
    {
        var items = new List<BetterGlamourItemSearchResult>
        {
            new(0, OmniLoc.Get("Feature.BetterGlamourManagement.None"), 0)
        };
        foreach (var item in LuminaGetter.Get<Item>())
        {
            if (item.RowId == 0 ||
                item.Name.IsEmpty ||
                item.EquipSlotCategory.RowId == 0 ||
                item.ModelMain == 0)
            {
                continue;
            }

            items.Add(new(item.RowId, item.Name.ExtractText(), (uint)item.Icon));
        }

        return [.. items];
    }

    private static BetterGlamourItemSearchResult[] BuildHairstyleSearchIndex()
    {
        var hairstyles = new List<BetterGlamourItemSearchResult>
        {
            new(0, OmniLoc.Get("Feature.BetterGlamourManagement.None"), 0)
        };
        foreach (var hairstyle in HairstyleData.GetPurchasableRows())
        {
            var name = GetHairstyleName(hairstyle.RowId);
            if (string.IsNullOrWhiteSpace(name) || name == OmniLoc.Get("Feature.BetterGlamourManagement.None"))
            {
                continue;
            }

            hairstyles.Add(new(hairstyle.RowId, name, (uint)hairstyle.Icon));
        }

        return [.. hairstyles];
    }

    private static BetterGlamourItemSearchResult[] BuildGlassesSearchIndex()
    {
        var glasses = new List<BetterGlamourItemSearchResult>
        {
            new(0, OmniLoc.Get("Feature.BetterGlamourManagement.None"), 0)
        };
        foreach (var row in LuminaGetter.Get<Glasses>())
        {
            if (row.RowId == 0 || row.Name.IsEmpty)
            {
                continue;
            }

            glasses.Add(new(row.RowId, row.Name.ExtractText(), (uint)row.Icon));
        }

        return [.. glasses];
    }

    internal static string GetGlassesName(ushort glassesID)
    {
        if (glassesID == 0)
        {
            return OmniLoc.Get("Feature.BetterGlamourManagement.None");
        }

        if (LuminaGetter.TryGetRow<Glasses>(glassesID, out var glasses))
        {
            var name = glasses.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.UnknownGlasses"), glassesID);
    }

    internal static string GetHairstyleName(uint hairstyleID)
    {
        if (hairstyleID == 0)
        {
            return OmniLoc.Get("Feature.BetterGlamourManagement.None");
        }

        if (LuminaGetter.TryGetRow<CharaMakeCustomize>(hairstyleID, out var hairstyle))
        {
            var name = HairstyleData.GetUnlockItem(hairstyle)?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return $"{OmniLoc.Get("Feature.BetterGlamourManagement.Hairstyle")} {hairstyle.FeatureID}";
        }

        return string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.UnknownHairstyle"), hairstyleID);
    }

    internal static uint GetItemIcon(uint itemID) =>
        LuminaGetter.TryGetRow<Item>(itemID, out var item) ? (uint)item.Icon : 0;

    internal static uint GetGlassesIcon(ushort glassesID) =>
        LuminaGetter.TryGetRow<Glasses>(glassesID, out var glasses) ? (uint)glasses.Icon : 0;

    internal static uint GetHairstyleIcon(uint hairstyleID) =>
        LuminaGetter.TryGetRow<CharaMakeCustomize>(hairstyleID, out var hairstyle) ? (uint)hairstyle.Icon : 0;

    internal static uint ResolveInspectHairstyleID(CustomizeData customizeData)
    {
        foreach (var hairMakeType in LuminaGetter.Get<HairMakeType>())
        {
            if (hairMakeType.Tribe.RowId != customizeData.Tribe || hairMakeType.Gender != customizeData.Sex)
            {
                continue;
            }

            foreach (var rowID in hairMakeType.CharaMakeStruct[0].SubMenuParam)
            {
                if (LuminaGetter.TryGetRow<CharaMakeCustomize>(rowID, out var hairstyle) &&
                    hairstyle.FeatureID == customizeData.Hairstyle)
                {
                    return hairstyle.RowId;
                }
            }

            break;
        }

        return 0;
    }

    internal static string GetDyeName(byte stainID) => stainID == 0
        ? OmniLoc.Get("Feature.BetterGlamourManagement.NoDye")
        : DyeOptions.Find(option => option.ID == stainID) is { } dye
            ? dye.Name
            : stainID.ToString();

    internal static List<GlamourDyeOption> DyeOptions
    {
        get
        {
            if (dyeOptions is not null)
            {
                return dyeOptions;
            }

            dyeOptions = [new(0, OmniLoc.Get("Feature.BetterGlamourManagement.NoDye"), Vector4.Zero)];
            foreach (var stain in LuminaGetter.Get<Stain>())
            {
                if (stain.RowId is 0 or > byte.MaxValue)
                {
                    continue;
                }

                var name = stain.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    dyeOptions.Add(new((byte)stain.RowId, name, stain.Color.ReverseToVector4()));
                }
            }

            return dyeOptions;
        }
    }

    internal readonly record struct GlamourPart(string TextKey, int Slot);

    internal readonly record struct GlamourDyeOption(byte ID, string Name, Vector4 Color);
}

internal readonly record struct BetterGlamourItemSearchResult(uint ItemID, string Name, uint IconID);
