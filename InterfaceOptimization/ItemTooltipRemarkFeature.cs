using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class ItemTooltipRemarkFeature : TreeHouseFeatureBase
{
    private const int RemarkListVisibleRows = 5;
    private const int MaxRemarkLength = 1024;

    private readonly ItemTooltipRemarkConfig config;
    private readonly TooltipManager.ItemTooltipUpdateDelegate tooltipHandler;
    private int editingItemId;
    private string editingText = string.Empty;
    private bool focusTextInput;
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
        var colorId = config.ColorKey;
        if (UIColorPicker.Draw("itemTooltipRemark", ref colorId))
        {
            config.ColorKey = colorId;
            tooltipManager?.TriggerItemDetailUpdate();
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(12f));
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

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.ItemTooltipRemark.Save"), false))
        {
            changed |= SaveRemark(GetEditingItemId(), editingText, out editingText);
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
            MaxRemarkLength,
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
        var rows = config.Remarks.OrderBy(pair => pair.Key).ToArray();
        var deleteItemId = 0u;

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
                    rowHeight * RemarkListVisibleRows + OmniTheme.BorderThickness() * 2f));
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
                        deleteItemId = itemId;
                    }

                    ImGui.PopID();
                }
            }

            clipper.End();
            clipper.Destroy();
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
}
