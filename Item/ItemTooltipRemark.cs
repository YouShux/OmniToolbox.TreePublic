using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class ItemTooltipRemark : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ItemTooltipRemarkTitle"),
        Description = OmniLoc.Get("ItemTooltipRemarkDescription"),
        Category = ModuleCategory.Item
    };

    private readonly ItemTooltipRemarkConfig config;
    private readonly TooltipManager.ItemTooltipUpdateDelegate tooltipHandler;
    private KeyValuePair<uint, string>[] orderedRemarks = [];
    private int editingItemID;
    private string editingText = string.Empty;
    private bool focusTextInput;
    private bool editorRequested;
    private bool remarksOrderDirty = true;
    private TooltipManager? tooltipManager;

    public ItemTooltipRemark(ItemTooltipRemarkConfig config)
    {
        this.config = config;
        tooltipHandler = OnItemTooltip;
    }

    public override bool HasSettings => true;

    public void OpenEditor(uint itemID) => LoadEditor(itemID, true);

    private void LoadEditor(uint itemID, bool requestOpen)
    {
        itemID = NormalizeItemID(itemID);
        if (itemID == 0)
        {
            if (!requestOpen)
            {
                editingText = string.Empty;
                focusTextInput = true;
            }

            return;
        }

        editingItemID = (int)Math.Min(itemID, int.MaxValue);
        editingText = config.Remarks.TryGetValue(itemID, out var remark) ? remark : string.Empty;
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

    public bool TryGetRemark(uint itemID, out string remark)
    {
        itemID = NormalizeItemID(itemID);
        if (itemID != 0 &&
            config.Remarks.TryGetValue(itemID, out var value) &&
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
        var itemID = args.Target is MenuTargetInventory target && target.TargetItem.HasValue
            ? NormalizeItemID(target.TargetItem.Value.ItemId)
            : 0;
        if (itemID == 0)
        {
            itemID = NormalizeItemID(DService.Instance().GameGUI.HoveredItem);
        }

        if (itemID == 0)
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
            OnClicked = _ => OpenEditor(itemID),
            PrefixChar = 'O',
            PrefixColor = 10
        });
    }

    public override bool DrawSettings()
    {
        var changed = DrawEditor();
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
        var changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ItemTooltipRemark.Color"));
        ImGui.SameLine();
        var color = UIColorPicker.Resolve(config.ColorKey, config.UseCustomColor, config.Color);
        if (UIColorPicker.Draw(
                "itemTooltipRemark",
                ref color,
                ImGuiColorEditFlags.DisplayRgb))
        {
            config.Color = color;
            config.UseCustomColor = true;
            tooltipManager?.TriggerItemDetailUpdate();
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ItemTooltipRemark.ItemId"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        if (OmniControls.InputInt("##itemTooltipRemarkItemId", ref editingItemID) && editingItemID < 0)
        {
            editingItemID = 0;
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.ItemTooltipRemark.Load"), false))
        {
            LoadEditor(GetEditingItemID(), false);
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.ItemTooltipRemark.Save"), false))
        {
            changed |= SaveRemark(GetEditingItemID(), editingText, out editingText);
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ItemTooltipRemark.Remark"));
        if (focusTextInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusTextInput = false;
        }

        OmniControls.InputTextMultiline(
            "##itemTooltipRemarkText",
            ref editingText,
            1024,
            new Vector2(ImGui.GetContentRegionAvail().X, OmniTheme.Scale(82f)));
        return changed;
    }

    private bool DrawRemarkList()
    {
        ImGui.Separator();
        if (config.Remarks.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.ItemTooltipRemark.Empty"));
            return false;
        }

        var editLabel = OmniLoc.Get("Feature.ItemTooltipRemark.Edit");
        var deleteLabel = OmniLoc.Get("Common.Delete");
        var editButtonSize = OmniControls.CompactButtonSize(editLabel);
        var deleteButtonSize = OmniControls.CompactButtonSize(deleteLabel);
        var actionGap = ImGui.GetStyle().ItemSpacing.X;
        var actionSize = new Vector2(
            editButtonSize.X + actionGap + deleteButtonSize.X,
            MathF.Max(editButtonSize.Y, deleteButtonSize.Y));
        var iconSize = OmniTheme.Scale(40f);
        var rowContentHeight = MathF.Max(iconSize, actionSize.Y);
        var cellPadding = ImGui.GetStyle().CellPadding.Y * 2f;
        var rowHeight = rowContentHeight + cellPadding;
        RebuildOrderedRemarks();
        var rows = orderedRemarks;
        var deleteItemID = 0u;

        {
            using var table = ImRaii.Table(
                "##itemTooltipRemarkTable",
                3,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.NoSavedSettings,
                new Vector2(
                    ImGui.GetContentRegionAvail().X,
                    OmniTheme.SmallButtonSize().Y + cellPadding +
                    rowHeight * 5 + OmniTheme.BorderThickness() * 2f));
            if (!table)
            {
                return false;
            }

            ImGui.TableSetupColumn(OmniLoc.Get("Common.Item"), ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.ItemTooltipRemark.Remark"),
                ImGuiTableColumnFlags.WidthStretch,
                1.8f);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Common.Action"),
                ImGuiTableColumnFlags.WidthFixed,
                actionSize.X + ImGui.GetStyle().CellPadding.X * 2f);
            ImGui.TableSetupScrollFreeze(0, 1);
            OmniControls.BeginTableHeaderRow();
            OmniControls.TableHeader(OmniLoc.Get("Common.Item"));
            OmniControls.TableHeader(OmniLoc.Get("Feature.ItemTooltipRemark.Remark"));
            OmniControls.TableHeader(OmniLoc.Get("Common.Action"));

            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(rows.Length, rowHeight);
            while (clipper.Step())
            {
                for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                {
                    var (itemId, remark) = rows[index];
                    ImGui.PushID(unchecked((int)itemId));
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);

                    ImGui.TableNextColumn();
                    ItemSelectionTable.DrawItemCell(itemId, rowContentHeight, iconSize);

                    ImGui.TableNextColumn();
                    OmniControls.TableTextCentered(remark, rowContentHeight);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(remark);
                    }

                    ImGui.TableNextColumn();
                    OmniControls.CenterTableItem(actionSize, rowContentHeight);
                    if (OmniControls.SmallButton(editLabel, false, editButtonSize))
                    {
                        LoadEditor(itemId, false);
                    }

                    ImGui.SameLine(0f, actionGap);
                    if (OmniControls.SmallButton(deleteLabel, false, deleteButtonSize))
                    {
                        deleteItemID = itemId;
                    }

                    ImGui.PopID();
                }
            }

            clipper.End();
            clipper.Destroy();
        }

        if (deleteItemID == 0)
        {
            return false;
        }

        if (GetEditingItemID() == deleteItemID)
        {
            editingText = string.Empty;
        }

        return SaveRemark(deleteItemID, string.Empty, out _);
    }

    private void OnItemTooltip(
        ItemKind _,
        uint itemID,
        ref List<TooltipItemModification> modifications)
    {
        if (!TryGetRemark(itemID, out var remark))
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
        var useCustomColor = config.UseCustomColor;
        using var builder = new RentedSeStringBuilder();
        if (useCustomColor)
        {
            builder.Builder.PushColorRgba(UIColorPicker.ToRgba(config.Color));
        }
        else if (colorKey != 0)
        {
            builder.Builder.PushColorType(colorKey);
        }

        builder.Builder
            .Append(OmniLoc.Get("Feature.ItemTooltipRemark.Prefix"))
            .Append(remark);
        if (useCustomColor)
        {
            builder.Builder.PopColor();
        }
        else if (colorKey != 0)
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

    private bool SaveRemark(uint itemID, string text, out string sanitized)
    {
        itemID = NormalizeItemID(itemID);
        sanitized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (itemID == 0)
        {
            return false;
        }

        if (sanitized.Length == 0)
        {
            if (!config.Remarks.Remove(itemID))
            {
                return false;
            }
        }
        else
        {
            if (config.Remarks.TryGetValue(itemID, out var current) &&
                string.Equals(current, sanitized, StringComparison.Ordinal))
            {
                return false;
            }

            config.Remarks[itemID] = sanitized;
        }

        remarksOrderDirty = true;
        tooltipManager?.TriggerItemDetailUpdate();
        return true;
    }

    private void RebuildOrderedRemarks()
    {
        if (!remarksOrderDirty)
        {
            return;
        }

        orderedRemarks = config.Remarks.OrderBy(pair => pair.Key).ToArray();
        remarksOrderDirty = false;
    }

    private uint GetEditingItemID() =>
        editingItemID <= 0 ? 0 : NormalizeItemID((uint)editingItemID);

    private static uint NormalizeItemID(ulong itemID) =>
        itemID <= uint.MaxValue ? ItemUtil.GetBaseId((uint)itemID).ItemId : 0;

}

[Serializable]
public sealed class ItemTooltipRemarkConfig
{
    public int ColorKey { get; set; } = 1;

    public bool UseCustomColor { get; set; }

    public Vector3 Color { get; set; } = Vector3.One;

    public Dictionary<uint, string> Remarks { get; set; } = [];
}
