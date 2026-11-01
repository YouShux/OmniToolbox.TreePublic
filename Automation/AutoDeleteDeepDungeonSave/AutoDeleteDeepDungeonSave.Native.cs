using System.Globalization;
using Dalamud.Game.Text.Evaluator;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class AutoDeleteDeepDungeonSave
{
    private static bool TryGetSaveData(out AtkUnitBase* addon, out uint dungeonID)
    {
        dungeonID = 0;
        addon = AddonHelper.GetByName(SAVE_ADDON_NAME);
        if (!addon->IsAddonAndNodesReady() || !addon->IsVisible || addon->AtkValues == null || addon->AtkValuesCount != 73 ||
            GetText(addon, 10) != LuminaWrapper.GetAddonText(RESET_TITLE_ROW_ID) ||
            GetText(addon, 2).Length == 0 || GetText(addon, 3).Length == 0)
            return false;

        dungeonID = ResolveDungeonID(addon->AtkValues, 6, 7);
        var list = addon->GetComponentListById(SAVE_LIST_NODE_ID);
        return dungeonID != 0 && list != null && list->GetItemCount() == SAVE_SLOT_COUNT;
    }

    private static uint ResolveDungeonID(AtkValue* values, int armIndex, int armorIndex)
    {
        if (values[armIndex].Type != AtkValueType.Int || values[armorIndex].Type != AtkValueType.Int)
            return 0;

        uint dungeonID = 0;
        for (uint rowID = 1; rowID <= 4; rowID++)
        {
            if (!LuminaGetter.TryGetRow<DeepDungeon>(rowID, out var dungeon) ||
                dungeon.AetherpoolArm.RowId != values[armIndex].Int ||
                dungeon.AetherpoolArmor.RowId != values[armorIndex].Int)
                continue;

            if (dungeonID != 0)
                return 0;
            dungeonID = rowID;
        }
        return dungeonID;
    }

    private static bool IsDeepDungeonMenuReady(uint dungeonID)
    {
        var menu = AddonHelper.GetByName("DeepDungeonMenu");
        return menu->IsAddonAndNodesReady() && menu->AtkValues != null && menu->AtkValuesCount == 21 &&
               ResolveDungeonID(menu->AtkValues, 7, 12) == dungeonID &&
               !((AtkUnitBase*)AddonHelper.GetByName<AddonSelectYesno>("SelectYesno"))->IsAddonAndNodesReady();
    }

    private bool TryReadSlot(AtkUnitBase* addon, uint dungeonID, int index, out SaveSlot slot)
    {
        slot = default;
        var offset = SAVE_VALUES_OFFSET + (index * SAVE_VALUES_STRIDE);
        if (addon->AtkValues[offset].Type != AtkValueType.Int || addon->AtkValues[offset].Int != 1 ||
            addon->AtkValues[offset + 2].Type != AtkValueType.UInt ||
            GetText(addon, offset + 1) != DService.Instance().SeStringEvaluator.EvaluateFromAddon(10403, [(uint)index + 1]).ExtractText() ||
            !TryGetFloor(GetText(addon, offset + 3), dungeonID, out var floor))
            return false;

        var identity = new List<string>();
        for (var valueIndex = offset + 3; valueIndex < offset + SAVE_VALUES_STRIDE; valueIndex++)
        {
            ref var value = ref addon->AtkValues[valueIndex];
            switch (value.Type)
            {
                case AtkValueType.String:
                case AtkValueType.ConstString:
                case AtkValueType.ManagedString:
                    if (!value.String.HasValue)
                        return false;
                    identity.Add(GetText(addon, valueIndex));
                    break;
                case AtkValueType.Int:
                case AtkValueType.UInt:
                    identity.Add(value.UInt.ToString(CultureInfo.InvariantCulture));
                    break;
                case AtkValueType.Undefined:
                    identity.Add(string.Empty);
                    break;
                default:
                    return false;
            }
        }

        slot = new(index, floor, addon->AtkValues[offset + 2].UInt, string.Join('\0', identity));
        return true;
    }

    private bool TryGetFloor(string text, uint dungeonID, out int floor)
    {
        floor = 0;
        var ranges = GetFloorRanges(dungeonID);
        if (ranges.IsEmpty)
            return false;

        var start = 0;
        while (start < text.Length && !char.IsAsciiDigit(text[start]))
            start++;
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
            end++;
        if (!int.TryParse(text.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out floor) ||
            floor < 1 || floor > ranges[^1].Maximum)
            return false;

        for (uint style = 1; style <= 3; style++)
        {
            if (DService.Instance().SeStringEvaluator.EvaluateFromAddon(FLOOR_TEXT_ROW_ID, [(uint)floor, style]).ExtractText() == text)
                return true;
        }
        return false;
    }

    private static bool IsEmptySlot(AtkUnitBase* addon, int index)
    {
        var offset = SAVE_VALUES_OFFSET + (index * SAVE_VALUES_STRIDE);
        return addon->AtkValues[offset].Type == AtkValueType.Int && addon->AtkValues[offset].Int == 0 &&
               GetText(addon, offset + 3) == LuminaWrapper.GetAddonText(10404);
    }

    private static bool IsDeleteConfirmation(AddonSelectYesno* confirmation, AtkUnitBase* saveAddon)
    {
        if (confirmation->ConfirmCheckBox == null ||
            confirmation->YesButton == null || GetText((AtkUnitBase*)confirmation, 8) != LuminaWrapper.GetAddonText(10413))
            return false;

        var actual = GetText((AtkUnitBase*)confirmation, 0).ReplaceLineEndings("\n");
        for (uint style = 1; style <= 3; style++)
        {
            SeStringParameter[] parameters =
            [
                new ReadOnlySeString(saveAddon->AtkValues[2].String.AsSpan()),
                new ReadOnlySeString(saveAddon->AtkValues[3].String.AsSpan()),
                style
            ];
            if (DService.Instance().SeStringEvaluator.EvaluateFromAddon(10412, parameters).ExtractText().ReplaceLineEndings("\n") == actual)
                return true;
        }
        return false;
    }

    private static string GetText(AtkUnitBase* addon, int index)
    {
        if (addon->AtkValues == null || index < 0 || index >= addon->AtkValuesCount)
            return string.Empty;

        ref var value = ref addon->AtkValues[index];
        return value.Type is AtkValueType.String or AtkValueType.ConstString or AtkValueType.ManagedString && value.String.HasValue
            ? new ReadOnlySeString(value.String.AsSpan()).ExtractText()
            : string.Empty;
    }
}
