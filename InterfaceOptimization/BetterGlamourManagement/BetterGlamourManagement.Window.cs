using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterGlamourManagement
{
    private static readonly GlamourPart[] Parts =
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

    private void DrawManagerWindow()
    {
        if (!managerOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(OmniTheme.Scale(new Vector2(820f, 560f)), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin(
                $"{OmniLoc.Get("Feature.BetterGlamourManagement.WindowTitle")}###betterGlamourManagement",
                ref managerOpen))
        {
            ImGui.End();
            return;
        }

        NormalizeSelection();
        var available = ImGui.GetContentRegionAvail();
        using (var layout = ImRaii.Table(
                   "##betterGlamourLayout",
                   2,
                   ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV,
                   new Vector2(available.X, MathF.Max(200f, available.Y - OmniTheme.Scale(38f)))))
        {
            if (layout)
            {
                ImGui.TableSetupColumn("##presets", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(190f));
                ImGui.TableSetupColumn("##details", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawPresetList();
                ImGui.TableNextColumn();
                DrawSelectedPreset();
            }
        }

        ImGui.End();
    }

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

    private void DrawPresetList()
    {
        if (config.Presets.Count == 0)
        {
            ImGui.TextWrapped(OmniLoc.Get("Feature.BetterGlamourManagement.Empty"));
            return;
        }

        for (var index = 0; index < config.Presets.Count; index++)
        {
            var name = string.IsNullOrWhiteSpace(config.Presets[index].Name)
                ? string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.DefaultName"), index + 1)
                : config.Presets[index].Name;
            if (ImGui.Selectable($"{name}##glamourPreset{index}", selectedPresetIndex == index))
            {
                selectedPresetIndex = index;
            }
        }
    }

    private void DrawSelectedPreset()
    {
        if (selectedPresetIndex < 0 || selectedPresetIndex >= config.Presets.Count)
        {
            ImGui.TextWrapped(OmniLoc.Get("Feature.BetterGlamourManagement.Empty"));
            return;
        }

        var preset = config.Presets[selectedPresetIndex];
        ImGui.SetNextItemWidth(MathF.Max(160f, ImGui.GetContentRegionAvail().X * 0.55f));
        var presetName = preset.Name;
        if (ImGui.InputText(
            $"{OmniLoc.Get("Feature.BetterGlamourManagement.PresetName")}##presetName",
            ref presetName,
            80))
        {
            preset.Name = presetName;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            preset.Name = preset.Name.Trim();
            saveConfig();
        }

        DrawGearsetBinding(preset);
        ImGui.Spacing();
        DrawPresetItems(preset);
        ImGui.Spacing();
        if (OmniControls.SmallButton(
                $"{OmniLoc.Get("Feature.BetterGlamourManagement.Apply")}##applyGlamourPreset",
                false))
        {
            StartTryOn(preset);
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(
                $"{OmniLoc.Get("Feature.BetterGlamourManagement.Export")}##exportGlamourPreset",
                false))
        {
            CopyPreset(preset);
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(
                $"{OmniLoc.Get("Feature.BetterGlamourManagement.Delete")}##deleteGlamourPreset",
                false))
        {
            config.Presets.RemoveAt(selectedPresetIndex);
            selectedPresetIndex = Math.Min(selectedPresetIndex, config.Presets.Count - 1);
            saveConfig();
        }
    }

    private void DrawGearsetBinding(BetterGlamourPreset preset)
    {
        var preview = preset.GearsetIndex >= 0 && TryGetGearsetName(preset.GearsetIndex, out var gearsetName)
            ? $"{preset.GearsetIndex + 1}. {gearsetName}"
            : OmniLoc.Get("Feature.BetterGlamourManagement.Unbound");
        ImGui.SetNextItemWidth(MathF.Max(160f, ImGui.GetContentRegionAvail().X * 0.55f));
        if (!ImGui.BeginCombo(
                $"{OmniLoc.Get("Feature.BetterGlamourManagement.BindGearset")}##presetGearset",
                preview))
        {
            return;
        }

        if (ImGui.Selectable(
                $"{OmniLoc.Get("Feature.BetterGlamourManagement.Unbound")}##gearsetNone",
                preset.GearsetIndex < 0))
        {
            preset.GearsetIndex = -1;
            saveConfig();
        }

        for (var index = 0; index < 100; index++)
        {
            if (!TryGetGearsetName(index, out gearsetName))
            {
                continue;
            }

            if (!ImGui.Selectable($"{index + 1}. {gearsetName}##gearset{index}", preset.GearsetIndex == index))
            {
                continue;
            }

            foreach (var other in config.Presets)
            {
                if (!ReferenceEquals(other, preset) && other.GearsetIndex == index)
                {
                    other.GearsetIndex = -1;
                }
            }

            preset.GearsetIndex = index;
            saveConfig();
        }

        ImGui.EndCombo();
    }

    private static bool TryGetGearsetName(int index, out string name)
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

    private void DrawPresetItems(BetterGlamourPreset preset)
    {
        using var table = ImRaii.Table(
            "##betterGlamourItems",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn(OmniLoc.Get("Feature.BetterGlamourManagement.Column.Part"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(72f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.BetterGlamourManagement.Column.Item"), ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.BetterGlamourManagement.Column.Stain0"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.BetterGlamourManagement.Column.Stain1"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableHeadersRow();

        DrawWeaponRow(preset);
        foreach (var part in Parts)
        {
            DrawItemRow(preset, part);
        }

        DrawGlassesRow(preset);
    }

    private void DrawWeaponRow(BetterGlamourPreset preset)
    {
        var mainHand = GetOrCreateItem(preset, 0);
        var offHand = GetOrCreateItem(preset, 1);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.BetterGlamourManagement.Part.Weapon"));
        ImGui.TableNextColumn();
        DrawItemID(mainHand, OmniLoc.Get("Feature.BetterGlamourManagement.MainHand"), "mainHand");
        DrawItemID(offHand, OmniLoc.Get("Feature.BetterGlamourManagement.OffHand"), "offHand");
        ImGui.TableNextColumn();
        DrawDye(mainHand, true, "mainHand0");
        DrawDye(offHand, true, "offHand0");
        ImGui.TableNextColumn();
        DrawDye(mainHand, false, "mainHand1");
        DrawDye(offHand, false, "offHand1");
    }

    private void DrawItemRow(BetterGlamourPreset preset, GlamourPart part)
    {
        var item = GetOrCreateItem(preset, part.Slot);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get(part.TextKey));
        ImGui.TableNextColumn();
        DrawItemID(item, string.Empty, $"slot{part.Slot}");
        ImGui.TableNextColumn();
        DrawDye(item, true, $"slot{part.Slot}Stain0");
        ImGui.TableNextColumn();
        DrawDye(item, false, $"slot{part.Slot}Stain1");
    }

    private void DrawGlassesRow(BetterGlamourPreset preset)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.BetterGlamourManagement.Part.Glasses"));
        ImGui.TableNextColumn();
        var glassesID = (int)preset.GlassesID;
        ImGui.SetNextItemWidth(OmniTheme.Scale(92f));
        if (ImGui.InputInt("##glassesID", ref glassesID))
        {
            preset.GlassesID = (ushort)Math.Clamp(glassesID, 0, ushort.MaxValue);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            saveConfig();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(GetGlassesName(preset.GlassesID));
        ImGui.TableNextColumn();
        ImGui.TextDisabled(OmniLoc.Get("Feature.BetterGlamourManagement.None"));
        ImGui.TableNextColumn();
        ImGui.TextDisabled(OmniLoc.Get("Feature.BetterGlamourManagement.None"));
    }

    private void DrawItemID(BetterGlamourItem item, string prefix, string id)
    {
        if (!string.IsNullOrEmpty(prefix))
        {
            ImGui.TextDisabled(prefix);
            ImGui.SameLine();
        }

        var itemID = (int)item.ItemID;
        ImGui.SetNextItemWidth(OmniTheme.Scale(92f));
        if (ImGui.InputInt($"##item{id}", ref itemID))
        {
            item.ItemID = (uint)Math.Max(0, itemID);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            saveConfig();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(GetItemName(item.ItemID));
    }

    private void DrawDye(BetterGlamourItem item, bool first, string id)
    {
        var stainID = first ? item.Stain0 : item.Stain1;
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.BeginCombo($"##dye{id}", GetDyeName(stainID)))
        {
            return;
        }

        foreach (var dye in DyeOptions)
        {
            if (!ImGui.Selectable($"{dye.Name}##dye{id}-{dye.ID}", stainID == dye.ID))
            {
                continue;
            }

            if (first)
            {
                item.Stain0 = dye.ID;
            }
            else
            {
                item.Stain1 = dye.ID;
            }

            saveConfig();
        }

        ImGui.EndCombo();
    }

    private static BetterGlamourItem GetOrCreateItem(BetterGlamourPreset preset, int slot)
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

    private static string GetItemName(uint itemID)
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

    private static string GetGlassesName(ushort glassesID)
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

    private static string GetDyeName(byte stainID) => stainID == 0
        ? OmniLoc.Get("Feature.BetterGlamourManagement.NoDye")
        : DyeOptions.Find(option => option.ID == stainID) is { } dye
            ? dye.Name
            : stainID.ToString();

    private static List<GlamourDyeOption> DyeOptions
    {
        get
        {
            if (dyeOptions is not null)
            {
                return dyeOptions;
            }

            dyeOptions = [new(0, OmniLoc.Get("Feature.BetterGlamourManagement.NoDye"))];
            foreach (var stain in LuminaGetter.Get<Stain>())
            {
                if (stain.RowId is 0 or > byte.MaxValue)
                {
                    continue;
                }

                var name = stain.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    dyeOptions.Add(new((byte)stain.RowId, name));
                }
            }

            return dyeOptions;
        }
    }

    private readonly record struct GlamourPart(string TextKey, int Slot);

    private readonly record struct GlamourDyeOption(byte ID, string Name);
}
