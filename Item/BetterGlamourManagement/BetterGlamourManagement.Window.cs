using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Items;
using OmniToolbox.Notifications;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterGlamourManagement
{
    private static BetterGlamourItemSearchResult[]? ItemSearchIndex;
    private static BetterGlamourItemSearchResult[]? HairstyleSearchIndex;
    private static BetterGlamourItemSearchResult[]? GlassesSearchIndex;

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

    private static List<GlamourDyeOption>? CachedDyeOptions;

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

    internal static bool TryGetGearsetName
    (
        int        index,
        out string name
    )
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

    internal static BetterGlamourItem GetOrCreateItem
    (
        BetterGlamourPreset preset,
        int                 slot
    )
    {
        var item = preset.Items.Find(entry => entry.Slot == slot);
        if (item is not null)
        {
            return item;
        }

        item = new()
        {
            Slot = slot
        };
        preset.Items.Add(item);
        return item;
    }

    private void CopyPreset(BetterGlamourPreset preset)
    {
        ImGui.SetClipboardText(GlamourClipboard.Export(preset));
        OmniNotifier.Popup(
            Title,
            OmniLoc.Get(this, "Feature.BetterGlamourManagement.Copied"),
            NotificationType.Success);
    }

    private void CreatePreset()
    {
        if (config.Presets.Count >= MAX_PRESET_COUNT)
        {
            OmniNotifier.Popup(
                Title,
                string.Format(OmniLoc.Get(this, "Feature.BetterGlamourManagement.PresetLimitReached"), MAX_PRESET_COUNT),
                NotificationType.Warning);
            return;
        }

        var preset = new BetterGlamourPreset
        {
            Name = string.Format(
                OmniLoc.Get(this, "Feature.BetterGlamourManagement.DefaultName"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
        };
        foreach (var slot in InspectSlots)
        {
            GetOrCreateItem(preset, slot).ItemID = slot switch
            {
                2 => 10032,
                3 => 10033,
                4 => 10034,
                6 => 10035,
                7 => 10036,
                8 => 9293,
                9 => 9292,
                10 => 9294,
                11 or 12 => 9295,
                _ => 0
            };
        }

        var oldSelection = selectedPresetIndex;
        config.Presets.Add(preset);
        selectedPresetIndex = config.Presets.Count - 1;
        try
        {
            saveConfig();
        }
        catch (Exception error)
        {
            config.Presets.Remove(preset);
            selectedPresetIndex = oldSelection;
            OmniNotifier.Popup(
                Title,
                string.Format(OmniLoc.Get(this, "Feature.BetterGlamourManagement.CreateFailed"), error.Message),
                NotificationType.Error);
            return;
        }

        managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
        QueuePortraitPreview(preset);
    }

    private void ImportClipboard()
    {
        try
        {
            if (config.Presets.Count >= MAX_PRESET_COUNT)
                throw new InvalidOperationException(string.Format(OmniLoc.Get(this, "Feature.BetterGlamourManagement.PresetLimitReached"), MAX_PRESET_COUNT));
            var preset = GlamourClipboard.Import(ImGui.GetClipboardText());
            ValidateRemotePreset(preset);
            var oldSelection = selectedPresetIndex;
            config.Presets.Add(preset);
            selectedPresetIndex = config.Presets.Count - 1;
            try
            {
                saveConfig();
            }
            catch
            {
                config.Presets.Remove(preset);
                selectedPresetIndex = oldSelection;
                throw;
            }
            managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
            QueuePortraitPreview(preset);
            OmniNotifier.Popup(Title, string.Format(OmniLoc.Get(this, "Feature.BetterGlamourManagement.Imported"), preset.Name), NotificationType.Success);
        }
        catch (Exception error)
        {
            OmniNotifier.Popup(Title, string.Format(OmniLoc.Get(this, "Feature.BetterGlamourManagement.ImportFailed"), error.Message), NotificationType.Error);
        }
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
        ItemSearchIndex ??= BuildItemSearchIndex();
        return SearchItems(query, ItemSearchIndex);
    }

    internal static List<BetterGlamourItemSearchResult> SearchHairstyles(string query)
    {
        HairstyleSearchIndex ??= BuildHairstyleSearchIndex();
        return SearchItems(query, HairstyleSearchIndex);
    }

    private static List<BetterGlamourItemSearchResult> SearchHairstyles
    (
        string        query,
        CustomizeData appearance
    )
    {
        var availableIDs = new HashSet<uint>();
        foreach (var hairMakeType in LuminaGetter.Get<HairMakeType>())
        {
            if (hairMakeType.Tribe.RowId != appearance.Tribe || hairMakeType.Gender != appearance.Sex)
                continue;

            foreach (var rowID in hairMakeType.CharaMakeStruct[0].SubMenuParam)
            {
                if (rowID != 0)
                    availableIDs.Add(rowID);
            }
            break;
        }

        return SearchHairstyles(query).FindAll(option => availableIDs.Contains(option.ItemID));
    }

    internal static List<BetterGlamourItemSearchResult> SearchGlasses(string query)
    {
        GlassesSearchIndex ??= BuildGlassesSearchIndex();
        return SearchItems(query, GlassesSearchIndex);
    }

    private static List<BetterGlamourItemSearchResult> SearchItems
    (
        string                          query,
        BetterGlamourItemSearchResult[] searchIndex
    )
    {
        query = query.Trim();
        var results = new List<BetterGlamourItemSearchResult>();
        var isItemID = uint.TryParse(query, out var itemID);
        foreach (var result in searchIndex)
        {
            if (query.Length == 0 ||
                result.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                isItemID && result.ItemID == itemID)
            {
                results.Add(result);
            }
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

            items.Add(new(item.RowId, item.Name.ExtractText(), (uint)item.Icon, item.LevelEquip));
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

            hairstyles.Add(new(hairstyle.RowId, name, (uint)hairstyle.Icon, Kind: BetterGlamourEditorItemKind.Hairstyle));
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

            glasses.Add(new(row.RowId, row.Name.ExtractText(), (uint)row.Icon, Kind: BetterGlamourEditorItemKind.Glasses));
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
            if (CachedDyeOptions is not null)
            {
                return CachedDyeOptions;
            }

            CachedDyeOptions = [new(0, OmniLoc.Get("Feature.BetterGlamourManagement.NoDye"), Vector4.Zero)];
            foreach (var stain in LuminaGetter.Get<Stain>())
            {
                if (stain.RowId is 0 or > byte.MaxValue)
                {
                    continue;
                }

                var name = stain.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    CachedDyeOptions.Add(new((byte)stain.RowId, name, stain.Color.ReverseToVector4()));
                }
            }

            return CachedDyeOptions;
        }
    }

    internal readonly record struct GlamourPart
    (
        string TextKey,
        int    Slot
    );

    internal readonly record struct GlamourDyeOption
    (
        byte    ID,
        string  Name,
        Vector4 Color
    );
}
