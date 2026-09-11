using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private static readonly MultiToolbarMenuEntryKind[] DynamicMenuAddOrder =
    [
        MultiToolbarMenuEntryKind.Command, MultiToolbarMenuEntryKind.Url,
        MultiToolbarMenuEntryKind.Item, MultiToolbarMenuEntryKind.KeyItem, MultiToolbarMenuEntryKind.Collection,
        MultiToolbarMenuEntryKind.Emote, MultiToolbarMenuEntryKind.Minion, MultiToolbarMenuEntryKind.Mount,
        MultiToolbarMenuEntryKind.Ornament, MultiToolbarMenuEntryKind.GeneralAction, MultiToolbarMenuEntryKind.MainCommand,
        MultiToolbarMenuEntryKind.ExtraCommand, MultiToolbarMenuEntryKind.IndividualMacro, MultiToolbarMenuEntryKind.SharedMacro,
        MultiToolbarMenuEntryKind.Recipe, MultiToolbarMenuEntryKind.Category
    ];
    private readonly record struct DynamicShortcut(uint ID, string Name, uint Icon, string Description = "");
    private readonly List<DynamicShortcut> dynamicChoices = [];
    private readonly List<DynamicShortcut> filteredDynamicChoices = [];
    private MultiToolbarMenuEntryKind dynamicPickerKind;
    private bool openDynamicPicker;
    private string dynamicPickerSearch = string.Empty;
    private string? dynamicPickerLastSearch;
    private int dynamicPickerPage;
    private readonly Dictionary<string, uint> dynamicMacroIcons = [];

    private static bool ShouldFrameDynamicIcon(MultiToolbarMenuEntryKind kind, uint targetID, uint iconID)
    {
        if (kind is MultiToolbarMenuEntryKind.Item or MultiToolbarMenuEntryKind.Collection &&
            LuminaGetter.TryGetRow<Item>(targetID % 1_000_000, out var item) && ItemDataService.IsCurrency(item)) return false;
        return IconBrowser.IsActionOrItemIcon(iconID);
    }

    private unsafe void RefreshDynamicMacroIcons(MultiToolbarWidgetConfig widget)
    {
        dynamicMacroIcons.Clear();
        var macros = RaptureMacroModule.Instance();
        if (macros == null) return;
        foreach (var entry in widget.MenuEntries)
        {
            if (entry.Kind is not (MultiToolbarMenuEntryKind.IndividualMacro or MultiToolbarMenuEntryKind.SharedMacro) ||
                (uint)entry.MacroIndex >= 100) continue;
            var set = entry.Kind == MultiToolbarMenuEntryKind.SharedMacro ? 1u : 0u;
            var macro = macros->GetMacro(set, (uint)entry.MacroIndex);
            if (macro == null || (entry.IconID != 0 && entry.IconID != macro->IconId)) continue;
            var icon = BetterUserMacro.ResolveNativeMacroIconID(macros, set, (uint)entry.MacroIndex);
            if (icon != 0) dynamicMacroIcons[entry.ID] = icon;
        }
    }

    private static bool IsDynamicShortcut(MultiToolbarMenuEntryKind kind) =>
        kind is MultiToolbarMenuEntryKind.IndividualMacro or MultiToolbarMenuEntryKind.SharedMacro || kind >= MultiToolbarMenuEntryKind.Item;

    private static uint DynamicAddIcon(MultiToolbarMenuEntryKind kind) => kind switch
    {
        MultiToolbarMenuEntryKind.Item => 2,
        MultiToolbarMenuEntryKind.KeyItem or MultiToolbarMenuEntryKind.Collection => 3,
        MultiToolbarMenuEntryKind.Emote or MultiToolbarMenuEntryKind.ExtraCommand => 9,
        MultiToolbarMenuEntryKind.Minion => 59,
        MultiToolbarMenuEntryKind.Mount => 58,
        MultiToolbarMenuEntryKind.Ornament => 86,
        MultiToolbarMenuEntryKind.GeneralAction => 4,
        MultiToolbarMenuEntryKind.MainCommand => 29,
        MultiToolbarMenuEntryKind.IndividualMacro or MultiToolbarMenuEntryKind.SharedMacro => 30,
        MultiToolbarMenuEntryKind.Recipe => 22,
        _ => 0
    };

    private static bool DrawDynamicAddOption(MultiToolbarMenuEntryKind kind)
    {
        var position = ImGui.GetCursorScreenPos();
        var height = MathF.Max(ImGui.GetTextLineHeight(), OmniTheme.Scale(22f));
        var icon = DynamicAddIcon(kind);
        var label = MenuText(kind.ToString());
        var clicked = ImGui.Selectable("##add" + kind, false, ImGuiSelectableFlags.None,
            new Vector2(MathF.Max(OmniTheme.Scale(250f), ImGui.CalcTextSize(label).X + height + 8f), height));
        if (icon != 0) FramedGameIcon.Draw(icon, position, new Vector2(height), drawFrame: false);
        ImGui.GetWindowDrawList().AddText(position + new Vector2(height + ImGui.GetStyle().ItemInnerSpacing.X, 0f),
            OmniTheme.Color(OmniTheme.Tokens.Text), label);
        return clicked;
    }

    private void OpenDynamicShortcutPicker(MultiToolbarMenuEntryKind kind)
    {
        dynamicPickerKind = kind;
        dynamicPickerSearch = string.Empty;
        dynamicPickerLastSearch = null;
        dynamicPickerPage = 0;
        dynamicChoices.Clear();
        BuildDynamicShortcuts(kind);
        dynamicChoices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        openDynamicPicker = true;
    }

    private bool DrawDynamicShortcutPicker(MultiToolbarWidgetConfig widget)
    {
        if (openDynamicPicker)
        {
            openDynamicPicker = false;
            ImGui.OpenPopup("##dynamicShortcutPicker");
        }
        ImGui.SetNextWindowSize(Vector2.Min(ImGui.GetMainViewport().WorkSize, OmniTheme.Scale(new Vector2(560f, 700f))), ImGuiCond.Appearing);
        using var popup = ImRaii.Popup("##dynamicShortcutPicker", ImGuiWindowFlags.NoBackground);
        if (!popup) return false;
        DrawEditorBackground(MenuText(dynamicPickerKind.ToString()));
        const int pageSize = 50;
        var previousLabel = OmniLoc.Get("Common.Previous");
        var nextLabel = OmniLoc.Get("Common.Next");
        var previousSize = OmniControls.CompactButtonSize(previousLabel);
        var nextSize = OmniControls.CompactButtonSize(nextLabel);
        var maximumPages = Math.Max(1, (dynamicChoices.Count + pageSize - 1) / pageSize);
        var pageWidth = ImGui.CalcTextSize(string.Format(OmniLoc.Get("Monitor.Ledger.PageIndicator"), maximumPages, maximumPages)).X;
        ImGui.SetNextItemWidth(MathF.Max(1f, ImGui.GetContentRegionAvail().X - previousSize.X - nextSize.X - pageWidth -
            3f * ImGui.GetStyle().ItemSpacing.X));
        OmniControls.InputTextWithHint("##dynamicSearch", MenuText("Search"), ref dynamicPickerSearch, 128);
        if (dynamicPickerLastSearch != dynamicPickerSearch)
        {
            dynamicPickerLastSearch = dynamicPickerSearch;
            dynamicPickerPage = 0;
            filteredDynamicChoices.Clear();
            filteredDynamicChoices.AddRange(dynamicChoices.Where(choice =>
                choice.Name.Contains(dynamicPickerSearch.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
                choice.ID.ToString().Contains(dynamicPickerSearch.Trim(), StringComparison.Ordinal)));
        }
        var pages = Math.Max(1, (filteredDynamicChoices.Count + pageSize - 1) / pageSize);
        ImGui.SameLine();
        using (ImRaii.Disabled(dynamicPickerPage == 0))
        {
            if (OmniControls.SmallButton(previousLabel + "##dynamicPrevious", false, previousSize)) dynamicPickerPage--;
        }
        ImGui.SameLine();
        var pagePosition = ImGui.GetCursorScreenPos();
        var pageText = string.Format(OmniLoc.Get("Monitor.Ledger.PageIndicator"), dynamicPickerPage + 1, pages);
        ImGui.GetWindowDrawList().AddText(pagePosition + new Vector2(
            (pageWidth - ImGui.CalcTextSize(pageText).X) * 0.5f,
            (previousSize.Y - ImGui.GetTextLineHeight()) * 0.5f), OmniTheme.Color(OmniTheme.Tokens.Text), pageText);
        ImGui.Dummy(new Vector2(pageWidth, previousSize.Y));
        ImGui.SameLine();
        using (ImRaii.Disabled(dynamicPickerPage + 1 >= pages))
        {
            if (OmniControls.SmallButton(nextLabel + "##dynamicNext", false, nextSize)) dynamicPickerPage++;
        }
        using var child = ImRaii.Child("##dynamicChoices", Vector2.Zero);
        if (!child) return false;
        foreach (var choice in filteredDynamicChoices.Skip(dynamicPickerPage * pageSize).Take(pageSize))
        {
            var position = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var iconSize = OmniTheme.Scale(32f);
            var textWidth = MathF.Max(1f, width - iconSize - ImGui.GetStyle().ItemSpacing.X);
            var descriptionSize = choice.Description.Length == 0 ? Vector2.Zero :
                ImGui.CalcTextSize(choice.Description, false, textWidth);
            var height = MathF.Max(iconSize, ImGui.GetTextLineHeightWithSpacing() + descriptionSize.Y) + OmniTheme.Scale(8f);
            var clicked = ImGui.Selectable($"##choice{choice.ID}", false, ImGuiSelectableFlags.None, new Vector2(width, height));
            FramedGameIcon.Draw(choice.Icon, position + new Vector2(0f, (height - iconSize) * 0.5f), new Vector2(iconSize),
                drawFrame: ShouldFrameDynamicIcon(dynamicPickerKind, choice.ID, choice.Icon));
            var textPosition = position + new Vector2(iconSize + ImGui.GetStyle().ItemSpacing.X,
                choice.Description.Length == 0 ? (height - ImGui.GetTextLineHeight()) * 0.5f : OmniTheme.Scale(4f));
            ImGui.GetWindowDrawList().AddText(textPosition, OmniTheme.Color(OmniTheme.Tokens.Text), EllipsizeToolbarText(choice.Name, textWidth));
            if (choice.Description.Length > 0)
            {
                ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                    textPosition + new Vector2(0f, ImGui.GetTextLineHeightWithSpacing()),
                    OmniTheme.Color(OmniTheme.Tokens.Text with { W = 0.75f }), choice.Description, textWidth);
            }
            OmniControls.HelpTooltip($"{choice.Name} [{choice.ID}]");
            if (!clicked) continue;
            editingMenuEntry = new MultiToolbarMenuEntry
            {
                Kind = dynamicPickerKind, TargetID = choice.ID, Label = choice.Name, IconID = choice.Icon,
                MacroIndex = (int)choice.ID
            };
            widget.MenuEntries.Add(editingMenuEntry);
            ImGui.CloseCurrentPopup();
            return true;
        }
        return false;
    }

    private unsafe void BuildDynamicShortcuts(MultiToolbarMenuEntryKind kind)
    {
        if (!DService.Instance().ClientState.IsLoggedIn) return;
        var data = DalamudServices.DataManager;
        var player = PlayerState.Instance();
        var ui = UIState.Instance();
        if (player == null || ui == null) return;
        void Add(uint id, string name, uint icon) { if (name.Length > 0) dynamicChoices.Add(new(id, name, icon)); }
        switch (kind)
        {
            case MultiToolbarMenuEntryKind.Item:
            case MultiToolbarMenuEntryKind.KeyItem:
                var inventory = InventoryManager.Instance();
                if (inventory == null) return;
                var containers = kind == MultiToolbarMenuEntryKind.KeyItem ? new[] { InventoryType.KeyItems } :
                    new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
                var seen = new HashSet<uint>();
                foreach (var type in containers)
                {
                    var container = inventory->GetInventoryContainer(type);
                    if (container == null || !container->IsLoaded) continue;
                    for (var index = 0; index < container->Size; index++)
                    {
                        var slot = container->GetInventorySlot(index);
                        if (slot == null || slot->ItemId == 0) continue;
                        var id = slot->ItemId;
                        if (kind == MultiToolbarMenuEntryKind.Item && (slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0) id += 1_000_000;
                        if (!seen.Add(id)) continue;
                        if (kind == MultiToolbarMenuEntryKind.KeyItem && data.GetExcelSheet<EventItem>().TryGetRow(id, out var key))
                        {
                            var name = key.Name.ExtractText();
                            // 部分关键物品仅在 Singular 中提供名称。
                            Add(id, string.IsNullOrWhiteSpace(name) ? key.Singular.ExtractText() : name, key.Icon);
                        }
                        else if (kind == MultiToolbarMenuEntryKind.Item && data.GetExcelSheet<Item>().TryGetRow(id % 1_000_000, out var item))
                            Add(id, item.Name.ExtractText() + (id >= 1_000_000 ? " HQ" : string.Empty), item.Icon);
                    }
                }
                break;
            case MultiToolbarMenuEntryKind.Collection:
                foreach (var row in data.GetExcelSheet<McGuffin>())
                    if (row.RowId != 0 && player->IsMcGuffinUnlocked(row.RowId)) Add(row.RowId, row.UIData.Value.Name.ExtractText(), row.UIData.Value.Icon);
                break;
            case MultiToolbarMenuEntryKind.Emote:
                foreach (var row in data.GetExcelSheet<Emote>())
                    if (row.RowId != 0 && row.TextCommand.RowId != 0 && ui->IsEmoteUnlocked((ushort)row.RowId)) Add(row.RowId, row.Name.ExtractText(), row.Icon);
                break;
            case MultiToolbarMenuEntryKind.Minion:
                foreach (var row in data.GetExcelSheet<Companion>())
                    if (row.RowId != 0 && ui->IsCompanionUnlocked(row.RowId)) Add(row.RowId, row.Singular.ExtractText(), row.Icon);
                break;
            case MultiToolbarMenuEntryKind.Mount:
                foreach (var row in data.GetExcelSheet<Mount>())
                    if (row.RowId != 0 && player->IsMountUnlocked(row.RowId)) Add(row.RowId, row.Singular.ExtractText(), (uint)row.Icon);
                break;
            case MultiToolbarMenuEntryKind.Ornament:
                foreach (var row in data.GetExcelSheet<Ornament>())
                    if (row.RowId != 0 && player->IsOrnamentUnlocked(row.RowId)) Add(row.RowId, row.Singular.ExtractText(), (uint)row.Icon);
                break;
            case MultiToolbarMenuEntryKind.GeneralAction:
                foreach (var row in data.GetExcelSheet<GeneralAction>()) if (row.RowId != 0) Add(row.RowId, row.Name.ExtractText(), (uint)row.Icon);
                break;
            case MultiToolbarMenuEntryKind.MainCommand:
                foreach (var row in data.GetExcelSheet<MainCommand>()) if (row.RowId != 0) Add(row.RowId, row.Name.ExtractText(), (uint)row.Icon);
                break;
            case MultiToolbarMenuEntryKind.ExtraCommand:
                foreach (var row in data.GetExcelSheet<ExtraCommand>()) if (row.RowId != 0) Add(row.RowId, row.Name.ExtractText(), (uint)row.Icon);
                break;
            case MultiToolbarMenuEntryKind.Recipe:
                foreach (var row in data.GetExcelSheet<Recipe>())
                    if (row.RowId != 0 && row.ItemResult.ValueNullable is { } result) Add(row.RowId, result.Name.ExtractText(), result.Icon);
                break;
            case MultiToolbarMenuEntryKind.IndividualMacro:
            case MultiToolbarMenuEntryKind.SharedMacro:
                var macros = RaptureMacroModule.Instance();
                if (macros == null) return;
                for (uint index = 0; index < 100; index++)
                {
                    var macro = macros->GetMacro(kind == MultiToolbarMenuEntryKind.SharedMacro ? 1u : 0u, index);
                    if (macro != null && macro->IsNotEmpty())
                    {
                        var icon = BetterUserMacro.ResolveNativeMacroIconID(macros,
                            kind == MultiToolbarMenuEntryKind.SharedMacro ? 1u : 0u, index);
                        var lines = new List<string>();
                        foreach (var line in macro->Lines)
                        {
                            var text = line.ToString();
                            if (text.Length > 0) lines.Add(text);
                        }
                        dynamicChoices.Add(new(index, macro->Name.ToString(), icon == 0 ? macro->IconId : icon,
                            string.Join("\n", lines)));
                    }
                }
                break;
        }
    }

    private static unsafe bool InvokeDynamicShortcut(MultiToolbarMenuEntry entry)
    {
        if (!DService.Instance().ClientState.IsLoggedIn || entry.TargetID == 0) return false;
        if (entry.Kind == MultiToolbarMenuEntryKind.Recipe && DService.Instance().Condition[ConditionFlag.Crafting]) return false;
        var type = entry.Kind switch
        {
            MultiToolbarMenuEntryKind.Item => RaptureHotbarModule.HotbarSlotType.Item,
            MultiToolbarMenuEntryKind.KeyItem => RaptureHotbarModule.HotbarSlotType.EventItem,
            MultiToolbarMenuEntryKind.Collection => RaptureHotbarModule.HotbarSlotType.McGuffin,
            MultiToolbarMenuEntryKind.Emote => RaptureHotbarModule.HotbarSlotType.Emote,
            MultiToolbarMenuEntryKind.Minion => RaptureHotbarModule.HotbarSlotType.Companion,
            MultiToolbarMenuEntryKind.Mount => RaptureHotbarModule.HotbarSlotType.Mount,
            MultiToolbarMenuEntryKind.Ornament => RaptureHotbarModule.HotbarSlotType.Ornament,
            MultiToolbarMenuEntryKind.GeneralAction => RaptureHotbarModule.HotbarSlotType.GeneralAction,
            MultiToolbarMenuEntryKind.MainCommand => RaptureHotbarModule.HotbarSlotType.MainCommand,
            MultiToolbarMenuEntryKind.ExtraCommand => RaptureHotbarModule.HotbarSlotType.ExtraCommand,
            MultiToolbarMenuEntryKind.Recipe => RaptureHotbarModule.HotbarSlotType.Recipe,
            _ => RaptureHotbarModule.HotbarSlotType.Empty
        };
        var hotbars = RaptureHotbarModule.Instance();
        if (hotbars == null || type == RaptureHotbarModule.HotbarSlotType.Empty) return false;
        // 使用临时槽位执行游戏自身的检查与动作，不写入玩家热键栏。
        var slot = new RaptureHotbarModule.HotbarSlot { CommandType = type, CommandId = entry.TargetID };
        return hotbars->ExecuteSlot(&slot) != 0;
    }
}
