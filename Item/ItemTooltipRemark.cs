using Dalamud.Utility;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class ItemTooltipRemark : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "ItemTooltipRemarkTitle",
            ["Description"] = "ItemTooltipRemarkDescription",
            ["Common.Action"] = "Common.Action",
            ["Common.Delete"] = "Common.Delete",
            ["Common.Item"] = "Common.Item",
            ["Feature.ItemTooltipRemark.Color"] = "Feature.ItemTooltipRemark.Color",
            ["Feature.ItemTooltipRemark.Edit"] = "Feature.ItemTooltipRemark.Edit",
            ["Feature.ItemTooltipRemark.ItemId"] = "Feature.ItemTooltipRemark.ItemId",
            ["Feature.ItemTooltipRemark.Load"] = "Feature.ItemTooltipRemark.Load",
            ["Feature.ItemTooltipRemark.Menu"] = "Feature.ItemTooltipRemark.Menu",
            ["Feature.ItemTooltipRemark.Prefix"] = "Feature.ItemTooltipRemark.Prefix",
            ["Feature.ItemTooltipRemark.Remark"] = "Feature.ItemTooltipRemark.Remark",
            ["Feature.ItemTooltipRemark.Save"] = "Feature.ItemTooltipRemark.Save"
        });

    private readonly Settings                                 config;
    private readonly TooltipManager.ItemTooltipUpdateDelegate tooltipHandler;
    private RemarkMenuEntry?                                  menuEntry;
    private KeyValuePair<uint, string>[]                      orderedRemarks    = [];
    private int                                               editingItemID;
    private string                                            editingText       = string.Empty;
    private bool                                              focusTextInput;
    private bool                                              editorRequested;
    private bool                                              remarksOrderDirty = true;
    private TooltipManager?                                   tooltipManager;

    public ItemTooltipRemark(Settings config)
    {
        this.config    = config;
        tooltipHandler = OnItemTooltip;
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        var manager = TooltipManager.Instance();
        manager.RegItem(tooltipHandler);
        tooltipManager = manager;
        menuEntry      = new(this);
        ContextMenuManager.Instance().Reg(menuEntry);
        manager.TriggerItemDetailUpdate();
    }

    protected override void OnStop()
    {
        if (menuEntry != null)
            ContextMenuManager.Instance().Unreg(menuEntry);
        menuEntry = null;
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

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        using var wrap = ImRaii.TextWrapPos(0f);
        var changed = DrawEditor();
        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        changed |= DrawRemarkList();
        return changed;
    }

    private bool DrawEditor()
    {
        var changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "Feature.ItemTooltipRemark.Color"));
        OmniControls.SameLineOrWrap(ImGui.GetFrameHeight());
        var color = UIColorPicker.Resolve(config.ColorKey, config.UseCustomColor, config.Color);
        if (UIColorPicker.Draw(
                "itemTooltipRemark",
                ref color,
                ImGuiColorEditFlags.DisplayRgb))
        {
            config.Color          = color;
            config.UseCustomColor = true;
            tooltipManager?.TriggerItemDetailUpdate();
            changed = true;
        }

        var inputWidth = OmniControls.MeasureInput(int.MaxValue.ToString(), OmniTheme.Scale(120f)).X;
        OmniControls.SameLineOrWrap(ImGui.CalcTextSize(OmniLoc.Get(this, "Feature.ItemTooltipRemark.ItemId")).X +
            inputWidth + ImGui.GetStyle().ItemSpacing.X, OmniTheme.Scale(12f));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "Feature.ItemTooltipRemark.ItemId"));
        OmniControls.SameLineOrWrap(inputWidth);
        if (OmniControls.InputInt("##itemTooltipRemarkItemId", ref editingItemID, inputWidth) && editingItemID < 0)
        {
            editingItemID = 0;
        }

        OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(OmniLoc.Get(this, "Feature.ItemTooltipRemark.Load")).X);
        if (OmniControls.SmallButton(OmniLoc.Get(this, "Feature.ItemTooltipRemark.Load"), false))
        {
            LoadEditor(GetEditingItemID(), false);
        }

        OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(OmniLoc.Get(this, "Feature.ItemTooltipRemark.Save")).X);
        if (OmniControls.SmallButton(OmniLoc.Get(this, "Feature.ItemTooltipRemark.Save"), false))
        {
            changed |= SaveRemark(GetEditingItemID(), editingText, out editingText);
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        ImGui.TextUnformatted(OmniLoc.Get(this, "Feature.ItemTooltipRemark.Remark"));
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
        if (config.Remarks.Count == 0)
        {
            return false;
        }

        ImGui.Separator();
        var editLabel = OmniLoc.Get(this, "Feature.ItemTooltipRemark.Edit");
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
            using var table = OmniControls.DataTable(
                "##itemTooltipRemarkTable",
                [OmniLoc.Get("Common.Item"), OmniLoc.Get(this, "Feature.ItemTooltipRemark.Remark"), OmniLoc.Get("Common.Action")],
                [iconSize + OmniTheme.Scale(6f) + ImGui.GetFontSize() * 4f, ImGui.GetFontSize() * 4f,
                    MathF.Max(editButtonSize.X, deleteButtonSize.X)],
                [OmniTheme.Scale(220f), OmniTheme.Scale(330f), actionSize.X],
                out var detailLayout,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.NoSavedSettings,
                new Vector2(
                    ImGui.GetContentRegionAvail().X,
                    OmniTheme.SmallButtonSize().Y + cellPadding +
                    rowHeight * 5 + OmniTheme.BorderThickness() * 2f), 1);
            if (!table)
            {
                return false;
            }

            if (!detailLayout)
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                OmniControls.BeginTableHeaderRow();
                OmniControls.TableHeader(OmniLoc.Get("Common.Item"));
                OmniControls.TableHeader(OmniLoc.Get(this, "Feature.ItemTooltipRemark.Remark"));
                OmniControls.TableHeader(OmniLoc.Get("Common.Action"));
                ImGui.TableSetColumnIndex(0);
                var itemWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
                ImGui.TableSetColumnIndex(1);
                var remarkWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
                ImGui.TableSetColumnIndex(2);
                rowContentHeight = MathF.Max(rowContentHeight, OmniControls.WrappedGroupHeight(
                    [editButtonSize, deleteButtonSize], ImGui.GetContentRegionAvail().X, actionGap));
                foreach (var (itemID, remark) in rows)
                {
                    rowContentHeight = MathF.Max(rowContentHeight,
                        ItemSelectionTable.MeasureItemCellHeight(LuminaWrapper.GetItemName(itemID), itemWidth, iconSize));
                    rowContentHeight = MathF.Max(rowContentHeight, ImGui.CalcTextSize(remark, false, remarkWidth).Y);
                }
                rowHeight = rowContentHeight + cellPadding;
            }

            if (detailLayout)
            {
                for (var index = 0; index < rows.Length; index++)
                    DrawRow(index);
            }
            else
            {
                var clipper = ImGui.ImGuiListClipper();
                clipper.Begin(rows.Length, rowHeight);
                while (clipper.Step())
                {
                    for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                    {
                        DrawRow(index);
                    }
                }

                clipper.End();
                clipper.Destroy();
            }

            void DrawRow(int index)
            {
                var (itemId, remark) = rows[index];
                ImGui.PushID(unchecked((int)itemId));
                ImGui.TableNextRow(ImGuiTableRowFlags.None, detailLayout ? 0f : rowContentHeight);

                ImGui.TableNextColumn();
                ItemSelectionTable.DrawItemCell(itemId, detailLayout ? 0f : rowContentHeight, iconSize, wrapName: true);

                OmniControls.NextTableField(OmniLoc.Get(this, "Feature.ItemTooltipRemark.Remark"), detailLayout);
                OmniControls.TableTextCentered(remark, detailLayout ? 0f : rowContentHeight);
                OmniControls.HelpTooltip(remark);

                OmniControls.NextTableField(OmniLoc.Get("Common.Action"), detailLayout);
                OmniControls.CenterTableItem(actionSize, detailLayout ? 0f : rowContentHeight);
                if (OmniControls.SmallButton(editLabel, false, editButtonSize))
                {
                    LoadEditor(itemId, false);
                }

                OmniControls.SameLineOrWrap(deleteButtonSize.X, actionGap);
                if (OmniControls.SmallButton(deleteLabel, false, deleteButtonSize))
                {
                    deleteItemID = itemId;
                }

                ImGui.PopID();
            }
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

    public void OpenEditor(uint itemID) => LoadEditor(itemID, true);

    private void LoadEditor
    (
        uint itemID,
        bool requestOpen
    )
    {
        itemID = NormalizeItemID(itemID);
        if (itemID == 0)
        {
            if (!requestOpen)
            {
                editingText    = string.Empty;
                focusTextInput = true;
            }

            return;
        }

        editingItemID  = (int)Math.Min(itemID, int.MaxValue);
        editingText    = config.Remarks.TryGetValue(itemID, out var remark) ? remark : string.Empty;
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

    public bool TryGetRemark
    (
        uint       itemID,
        out string remark
    )
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

    private void OnItemTooltip
    (
        ItemKind                          _,
        uint                              itemID,
        ref List<TooltipItemModification> modifications
    )
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
            .Append(OmniLoc.Get(this, "Feature.ItemTooltipRemark.Prefix"))
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

    private bool SaveRemark
    (
        uint       itemID,
        string     text,
        out string sanitized
    )
    {
        itemID    = NormalizeItemID(itemID);
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

        orderedRemarks    = config.Remarks.OrderBy(pair => pair.Key).ToArray();
        remarksOrderDirty = false;
    }

    private uint GetEditingItemID() =>
        editingItemID <= 0 ? 0 : NormalizeItemID((uint)editingItemID);

    private static uint NormalizeItemID(ulong itemID) =>
        itemID <= uint.MaxValue ? ItemUtil.GetBaseId((uint)itemID).ItemId : 0;

    private sealed class RemarkMenuEntry(ItemTooltipRemark module) : ContextMenuEntry
    {
        public override string Identifier => nameof(ItemTooltipRemark);

        public override ContextMenuItem? Create(ContextMenuOpenedArgs args)
        {
            var itemID = OmniContextMenu.GetTargetItemID(args);
            if (itemID == 0)
                itemID = NormalizeItemID(DService.Instance().GameGUI.HoveredItem);
            if (itemID == 0)
                return null;

            return new()
            {
                Name = OmniLoc.Get("Feature.ItemTooltipRemark.Menu"),
                Prefix = OmniContextMenu.Prefix,
                OnClicked = _ => module.OpenEditor(itemID)
            };
        }
    }


    public sealed class Settings
    {
        public int ColorKey { get; set; } = 1;

        public bool UseCustomColor { get; set; }

        public Vector3 Color { get; set; } = Vector3.One;

        public Dictionary<uint, string> Remarks { get; set; } = [];
    }
}
