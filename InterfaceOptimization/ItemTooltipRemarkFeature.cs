using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class ItemTooltipRemarkFeature : TreeHouseFeatureBase
{
    private const int RemarkListVisibleRows = 6;
    private const int MaxRemarkLength = 1024;

    private readonly ItemTooltipRemarkConfig config;
    private readonly TooltipManager.ItemTooltipUpdateDelegate tooltipHandler;
    private int editingItemId;
    private string editingText = string.Empty;
    private bool focusTextInput;
    private bool colorPickerOpen;
    private bool editorRequested;
    private TooltipManager? tooltipManager;

    public ItemTooltipRemarkFeature(ItemTooltipRemarkConfig config) : base(new(
        "item-tooltip-remark",
        "Feature.ItemTooltipRemark.Title",
        "Feature.ItemTooltipRemark.Description",
        TreeHouseCategory.InterfaceOptimization))
    {
        this.config = config;
        tooltipHandler = OnItemTooltip;
    }

    public override bool HasSettings => true;

    public void OpenEditor(uint itemId) => LoadEditor(itemId, true);

    private void LoadEditor(uint itemId, bool requestOpen)
    {
        itemId = NormalizeItemId(itemId);
        if (itemId == 0)
        {
            if (!requestOpen)
            {
                editingText = string.Empty;
                focusTextInput = true;
            }

            return;
        }

        editingItemId = (int)Math.Min(itemId, int.MaxValue);
        editingText = config.Remarks.TryGetValue(itemId, out var remark) ? remark : string.Empty;
        focusTextInput = true;
        editorRequested |= requestOpen;
    }

    public bool ConsumeEditorRequest()
    {
        if (!editorRequested)
        {
            return false;
        }

        editorRequested = false;
        return true;
    }

    public bool TryGetRemark(uint itemId, out string remark)
    {
        itemId = NormalizeItemId(itemId);
        if (itemId != 0 &&
            config.Remarks.TryGetValue(itemId, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            remark = value;
            return true;
        }

        remark = string.Empty;
        return false;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        var itemId = args.Target is MenuTargetInventory target && target.TargetItem.HasValue
            ? NormalizeItemId(target.TargetItem.Value.ItemId)
            : 0;
        if (itemId == 0)
        {
            itemId = NormalizeItemId(DService.Instance().GameGUI.HoveredItem);
        }

        if (itemId == 0)
        {
            return;
        }

        args.AddMenuItem(new MenuItem
        {
            Name = new SeStringBuilder()
                .AddUiForeground(10)
                .Append(((char)SeIconChar.BoxedLetterT).ToString())
                .AddUiForegroundOff()
                .Append($" {OmniLoc.Get("Feature.ItemTooltipRemark.Menu")}")
                .Build(),
            OnClicked = _ => OpenEditor(itemId),
            PrefixChar = 'O',
            PrefixColor = 10
        });
    }

    public override bool DrawSettings()
    {
        var changed = false;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ItemTooltipRemark.Color"));
        ImGui.SameLine();
        var colorId = config.ColorKey;
        if (UIColorPicker.Draw(
                "itemTooltipRemark",
                OmniLoc.Get("Feature.ItemTooltipRemark.ColorPicker.Title"),
                ref colorPickerOpen,
                ref colorId))
        {
            config.ColorKey = colorId;
            tooltipManager?.TriggerItemDetailUpdate();
            changed = true;
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        changed |= DrawEditor();
        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        changed |= DrawRemarkList();
        return changed;
    }

    protected override void OnEnable()
    {
        var manager = TooltipManager.Instance();
        manager.RegItem(tooltipHandler);
        tooltipManager = manager;
        DService.Instance().ContextMenu.OnMenuOpened += OnMenuOpened;
        manager.TriggerItemDetailUpdate();
    }

    protected override void OnDisable()
    {
        DService.Instance().ContextMenu.OnMenuOpened -= OnMenuOpened;
        var manager = tooltipManager!;
        try
        {
            manager.Unreg(tooltipHandler);
        }
        finally
        {
            manager.TriggerItemDetailUpdate();
            tooltipManager = null;
        }
    }

    private bool DrawEditor()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ItemTooltipRemark.ItemId"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        if (OmniControls.InputInt("##itemTooltipRemarkItemId", ref editingItemId) && editingItemId < 0)
        {
            editingItemId = 0;
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.ItemTooltipRemark.Load"), false))
        {
            LoadEditor(GetEditingItemId(), false);
        }

        var itemId = GetEditingItemId();
        if (itemId != 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(
                OmniTheme.Tokens.Accent,
                string.Format(
                    OmniLoc.Get("Feature.ItemTooltipRemark.CurrentItem"),
                    GetItemDisplayName(itemId),
                    itemId));
        }

        ImGui.TextUnformatted(OmniLoc.Get("Feature.ItemTooltipRemark.Remark"));
        if (focusTextInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusTextInput = false;
        }

        OmniControls.InputTextMultiline(
            "##itemTooltipRemarkText",
            ref editingText,
            MaxRemarkLength,
            new Vector2(ImGui.GetContentRegionAvail().X, OmniTheme.Scale(82f)));

        var changed = false;
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.ItemTooltipRemark.Save"), false))
        {
            changed |= SaveRemark(GetEditingItemId(), editingText, out editingText);
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.ItemTooltipRemark.Delete"), false))
        {
            changed |= SaveRemark(GetEditingItemId(), string.Empty, out editingText);
        }

        return changed;
    }

    private bool DrawRemarkList()
    {
        ImGui.Separator();
        ImGui.TextUnformatted(string.Format(
            OmniLoc.Get("Feature.ItemTooltipRemark.SavedCount"),
            config.Remarks.Count));
        if (config.Remarks.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.ItemTooltipRemark.Empty"));
            return false;
        }

        var editLabel = OmniLoc.Get("Feature.ItemTooltipRemark.Edit");
        var deleteLabel = OmniLoc.Get("Feature.ItemTooltipRemark.Delete");
        var itemIdWidth = MathF.Max(
            OmniTheme.Scale(88f),
            ImGui.CalcTextSize(uint.MaxValue.ToString()).X +
            ImGui.GetStyle().FramePadding.X * 2f +
            OmniTheme.Scale(12f));
        var editWidth = MathF.Max(
            OmniTheme.Scale(60f),
            ImGui.CalcTextSize(editLabel).X +
            ImGui.GetStyle().FramePadding.X * 2f +
            OmniTheme.Scale(12f));
        var deleteWidth = MathF.Max(
            OmniTheme.Scale(60f),
            ImGui.CalcTextSize(deleteLabel).X +
            ImGui.GetStyle().FramePadding.X * 2f +
            OmniTheme.Scale(12f));
        var deleteItemId = 0u;

        {
            using var table = ImRaii.Table(
                "##itemTooltipRemarkTable",
                5,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.NoSavedSettings,
                new Vector2(
                    ImGui.GetContentRegionAvail().X,
                    ImGui.GetFrameHeightWithSpacing() * (RemarkListVisibleRows + 1f)));
            if (!table)
            {
                return false;
            }

            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.ItemTooltipRemark.ItemId"),
                ImGuiTableColumnFlags.WidthFixed,
                itemIdWidth);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.ItemTooltipRemark.ItemName"),
                ImGuiTableColumnFlags.WidthStretch,
                1.1f);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.ItemTooltipRemark.Remark"),
                ImGuiTableColumnFlags.WidthStretch,
                1.7f);
            ImGui.TableSetupColumn(editLabel, ImGuiTableColumnFlags.WidthFixed, editWidth);
            ImGui.TableSetupColumn(deleteLabel, ImGuiTableColumnFlags.WidthFixed, deleteWidth);
            OmniControls.ScrollableTableHeadersRow();

            foreach (var (itemId, remark) in config.Remarks.OrderBy(pair => pair.Key))
            {
                var itemName = GetItemDisplayName(itemId);
                var itemTooltip = string.Format(
                    OmniLoc.Get("Feature.ItemTooltipRemark.CurrentItem"),
                    itemName,
                    itemId);

                ImGui.PushID(unchecked((int)itemId));
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(itemId.ToString());
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(itemTooltip);
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(itemName);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(itemTooltip);
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(remark);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(remark);
                }

                ImGui.TableNextColumn();
                if (OmniControls.SmallButton(editLabel, false, new Vector2(editWidth, OmniTheme.SmallButtonSize().Y)))
                {
                    LoadEditor(itemId, false);
                }

                ImGui.TableNextColumn();
                if (OmniControls.SmallButton(deleteLabel, false, new Vector2(deleteWidth, OmniTheme.SmallButtonSize().Y)))
                {
                    deleteItemId = itemId;
                }

                ImGui.PopID();
            }
        }

        if (deleteItemId == 0)
        {
            return false;
        }

        if (GetEditingItemId() == deleteItemId)
        {
            editingText = string.Empty;
        }

        return SaveRemark(deleteItemId, string.Empty, out _);
    }

    private void OnItemTooltip(
        ItemKind _,
        uint itemId,
        ref List<TooltipItemModification> modifications)
    {
        if (!TryGetRemark(itemId, out var remark))
        {
            return;
        }

        var manager = tooltipManager!;
        var target = !manager.GetOriginalItemTooltipText(TooltipItemType.Description).IsEmpty
            ? TooltipItemType.Description
            : !manager.GetOriginalItemTooltipText(TooltipItemType.ClassJobLevel).IsEmpty
                ? TooltipItemType.ClassJobLevel
                : TooltipItemType.Effect;
        var colorKey = (ushort)Math.Clamp(config.ColorKey, 0, ushort.MaxValue);
        using var builder = new RentedSeStringBuilder();
        if (colorKey != 0)
        {
            builder.Builder.PushColorType(colorKey);
        }

        builder.Builder
            .Append(OmniLoc.Get("Feature.ItemTooltipRemark.Prefix"))
            .Append(remark);
        if (colorKey != 0)
        {
            builder.Builder.PopColorType();
        }

        modifications.Add(new()
        {
            Target = target,
            Type = TooltipModificationType.Append,
            Text = builder.Builder.ToReadOnlySeString()
        });
    }

    private bool SaveRemark(uint itemId, string text, out string sanitized)
    {
        itemId = NormalizeItemId(itemId);
        sanitized = Sanitize(text);
        if (itemId == 0)
        {
            return false;
        }

        if (sanitized.Length == 0)
        {
            if (!config.Remarks.Remove(itemId))
            {
                return false;
            }
        }
        else
        {
            if (config.Remarks.TryGetValue(itemId, out var current) &&
                string.Equals(current, sanitized, StringComparison.Ordinal))
            {
                return false;
            }

            config.Remarks[itemId] = sanitized;
        }

        tooltipManager?.TriggerItemDetailUpdate();
        return true;
    }

    private uint GetEditingItemId() =>
        editingItemId <= 0 ? 0 : NormalizeItemId((uint)editingItemId);

    private static uint NormalizeItemId(ulong itemId)
    {
        if (itemId < 2_000_000)
        {
            itemId %= 500_000;
        }

        return itemId <= uint.MaxValue ? (uint)itemId : 0;
    }

    private static string Sanitize(string text) =>
        text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string GetItemDisplayName(uint itemId)
    {
        if (LuminaGetter.TryGetRow<Item>(itemId, out var item))
        {
            var name = item.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return string.Format(OmniLoc.Get("Feature.ItemTooltipRemark.ItemNameFallback"), itemId);
    }
}
