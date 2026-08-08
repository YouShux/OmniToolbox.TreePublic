using System.Drawing;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node;
using KamiToolKit.Premade.Node.Simple;
using Lumina.Text.ReadOnly;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

internal sealed class BetterGlamourManagerNativeUI : NativeAddon
{
    private readonly Action<BetterGlamourPreset> onSelected;
    private readonly Action<BetterGlamourPreset> onChanged;
    private readonly Action<BetterGlamourPreset, int> onGearsetChanged;
    private readonly Action<BetterGlamourPreset> onApply;
    private readonly Action<BetterGlamourPreset> onExport;
    private readonly Action<BetterGlamourPreset> onDelete;
    private readonly List<BetterGlamourEditorRow> editorRows = [];
    private readonly List<GearsetOption> gearsetOptions = [];
    private List<BetterGlamourPreset> presets = [];
    private BetterGlamourPreset? selectedPreset;
    private BetterGlamourItem? selectedDyeItem;
    private bool pendingDyeSelectionRefresh;
    private ListNode<BetterGlamourPreset, BetterGlamourPresetListItemNode>? presetListNode;
    private VerticalLineNode? separatorNode;
    private TextNode? emptyNode;
    private TextNode? nameLabelNode;
    private TextInputNode? nameInputNode;
    private TextNode? gearsetLabelNode;
    private TextDropDownNode? gearsetDropDownNode;
    private TextNode? partHeaderNode;
    private TextNode? itemHeaderNode;
    private TextNode? dyeHeaderNode;
    private ListNode<BetterGlamourEditorRow, BetterGlamourEditorListItemNode>? itemListNode;
    private TextButtonNode? applyButton;
    private TextButtonNode? exportButton;
    private TextButtonNode? deleteButton;
    private BetterGlamourDyePickerNativeUI? dyePicker;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public BetterGlamourManagerNativeUI(
        Action<BetterGlamourPreset> onSelected,
        Action<BetterGlamourPreset> onChanged,
        Action<BetterGlamourPreset, int> onGearsetChanged,
        Action<BetterGlamourPreset> onApply,
        Action<BetterGlamourPreset> onExport,
        Action<BetterGlamourPreset> onDelete)
    {
        InternalName = "OmniBetterGlamourManager";
        Title = OmniLoc.Get("Feature.BetterGlamourManagement.WindowTitle");
        Subtitle = FormatPresetCount(0);
        Size = new(900f, 540f);
        ContentPadding = new(10f, 8f);
        RememberClosePosition = true;
        CreateWindowNode = static () => new WindowNode { ShowCloseButton = true };
        this.onSelected = onSelected;
        this.onChanged = onChanged;
        this.onGearsetChanged = onGearsetChanged;
        this.onApply = onApply;
        this.onExport = onExport;
        this.onDelete = onDelete;
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        presetListNode = new()
        {
            ItemSpacing = 2f,
            OptionsList = presets,
            OnItemSelected = SelectPreset
        };
        presetListNode.AttachNode(this);

        separatorNode = new();
        separatorNode.AttachNode(this);

        emptyNode = new()
        {
            AlignmentType = AlignmentType.Center,
            TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
            String = OmniLoc.Get("Feature.BetterGlamourManagement.Empty")
        };
        emptyNode.AttachNode(this);

        nameLabelNode = CreateLabel("Feature.BetterGlamourManagement.PresetName");
        nameInputNode = new()
        {
            MaxCharacters = 80,
            ShowLimitText = true,
            OnInputReceived = value =>
            {
                if (selectedPreset is not null)
                {
                    selectedPreset.Name = value.ExtractText();
                }
            },
            OnInputComplete = _ => SaveName(),
            OnFocusLost = SaveName
        };
        nameInputNode.AttachNode(this);
        if (WindowNode is WindowNode windowNode)
        {
            windowNode.SubtitleNode.FontType = FontType.MiedingerMed;
            windowNode.SubtitleNode.FontSize = 14;
            windowNode.SubtitleNode.Position = new(windowNode.SubtitleNode.X, 13f);
            windowNode.SubtitleNode.Size = new(64f, 19f);
        }

        gearsetLabelNode = CreateLabel("Feature.BetterGlamourManagement.BindGearset");
        gearsetDropDownNode = new()
        {
            Options = [],
            MaxListOptions = 8,
            OnOptionSelected = SelectGearset
        };
        gearsetDropDownNode.AttachNode(this);

        partHeaderNode = CreateHeader("Feature.BetterGlamourManagement.Column.Part");
        itemHeaderNode = CreateHeader("Feature.BetterGlamourManagement.Column.Item");
        dyeHeaderNode = CreateHeader("Feature.BetterGlamourManagement.Dye");

        itemListNode = new()
        {
            ItemSpacing = 1f,
            OptionsList = editorRows
        };
        itemListNode.AttachNode(this);

        applyButton = CreateButton("Feature.BetterGlamourManagement.Apply", () => Invoke(onApply));
        exportButton = CreateButton("Feature.BetterGlamourManagement.Export", () => Invoke(onExport));
        deleteButton = CreateButton("Feature.BetterGlamourManagement.Delete", () => Invoke(onDelete));

        ResizeContent();
        ApplyData();
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon)
    {
        presetListNode?.Update();
        itemListNode?.Update();
        if (pendingDyeSelectionRefresh)
        {
            pendingDyeSelectionRefresh = false;
            if (selectedPreset is not null)
            {
                BuildEditorRows();
                itemListNode?.FullRebuild();
            }
        }
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        dyePicker?.Close();
        dyePicker?.Dispose();
        dyePicker = null;
        selectedDyeItem = null;
        pendingDyeSelectionRefresh = false;
        presetListNode = null;
        separatorNode = null;
        emptyNode = null;
        nameLabelNode = null;
        nameInputNode = null;
        gearsetLabelNode = null;
        gearsetDropDownNode = null;
        partHeaderNode = null;
        itemHeaderNode = null;
        dyeHeaderNode = null;
        itemListNode = null;
        applyButton = null;
        exportButton = null;
        deleteButton = null;
    }

    public void UpdateData(List<BetterGlamourPreset> newPresets, int selectedIndex)
    {
        presets = newPresets;
        selectedPreset = presets.Count == 0
            ? null
            : presets[Math.Clamp(selectedIndex, 0, presets.Count - 1)];
        UpdateWindowTitle();
        ApplyData();
    }

    private TextNode CreateLabel(string textKey)
    {
        var node = new TextNode
        {
            AlignmentType = AlignmentType.Left,
            String = OmniLoc.Get(textKey)
        };
        node.AttachNode(this);
        return node;
    }

    private TextNode CreateHeader(string textKey)
    {
        var node = CreateLabel(textKey);
        node.AlignmentType = AlignmentType.Center;
        node.TextColor = ColorHelper.GetColor(50);
        return node;
    }

    private TextButtonNode CreateButton(string textKey, Action onClick)
    {
        var button = new TextButtonNode
        {
            String = OmniLoc.Get(textKey),
            OnClick = onClick
        };
        button.AttachNode(this);
        return button;
    }

    private void SelectPreset(BetterGlamourPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        selectedPreset = preset;
        selectedDyeItem = null;
        onSelected(preset);
        ApplySelection();
    }

    private void SelectGearset(string label)
    {
        if (selectedPreset is null)
        {
            return;
        }

        foreach (var option in gearsetOptions)
        {
            if (option.Label != label)
            {
                continue;
            }

            onGearsetChanged(selectedPreset, option.Index);
            presetListNode?.FullRebuild();
            return;
        }
    }

    private void SaveName()
    {
        if (selectedPreset is null || nameInputNode is null)
        {
            return;
        }

        selectedPreset.Name = nameInputNode.String.ExtractText().Trim();
        nameInputNode.String = selectedPreset.Name;
        onChanged(selectedPreset);
        presetListNode?.FullRebuild();
    }

    private void Invoke(Action<BetterGlamourPreset> action)
    {
        if (selectedPreset is not null)
        {
            action(selectedPreset);
        }
    }

    private void ApplyData()
    {
        if (presetListNode is null)
        {
            return;
        }

        presetListNode.OptionsList = presets;
        ApplySelection();
    }

    private void ApplySelection()
    {
        var hasSelection = selectedPreset is not null;
        if (emptyNode is not null)
        {
            emptyNode.IsVisible = !hasSelection;
        }

        SetDetailVisibility(hasSelection);
        if (!hasSelection)
        {
            editorRows.Clear();
            if (itemListNode is not null)
            {
                itemListNode.OptionsList = editorRows;
            }

            return;
        }

        nameInputNode!.String = selectedPreset!.Name;
        BuildGearsetOptions();
        gearsetDropDownNode!.Options = gearsetOptions.ConvertAll(static option => option.Label);
        gearsetDropDownNode.SelectedOption = gearsetOptions.Find(option => option.Index == selectedPreset.GearsetIndex).Label ??
                                               gearsetOptions[0].Label;
        BuildEditorRows();
        itemListNode!.OptionsList = editorRows;
    }

    private void BuildGearsetOptions()
    {
        gearsetOptions.Clear();
        gearsetOptions.Add(new(-1, OmniLoc.Get("Feature.BetterGlamourManagement.Unbound")));
        for (var index = 0; index < 100; index++)
        {
            if (BetterGlamourManagement.TryGetGearsetName(index, out var name))
            {
                gearsetOptions.Add(new(index, $"{index + 1}. {name}"));
            }
        }
    }

    private void BuildEditorRows()
    {
        editorRows.Clear();
        editorRows.Add(new(
            OmniLoc.Get("Feature.BetterGlamourManagement.Hairstyle"),
            selectedPreset!,
            null,
            false,
            true,
            false,
            null,
            () => onChanged(selectedPreset!)));
        AddItemRow("Feature.BetterGlamourManagement.MainHand", 0);
        AddItemRow("Feature.BetterGlamourManagement.OffHand", 1);
        foreach (var part in BetterGlamourManagement.Parts)
        {
            AddItemRow(part.TextKey, part.Slot);
        }

        editorRows.Add(new(
            OmniLoc.Get("Feature.BetterGlamourManagement.Part.Glasses"),
            selectedPreset!,
            null,
            true,
            false,
            false,
            null,
            () => onChanged(selectedPreset!)));
    }

    private void AddItemRow(string textKey, int slot)
    {
        var item = BetterGlamourManagement.GetOrCreateItem(selectedPreset!, slot);
        editorRows.Add(new(
            OmniLoc.Get(textKey),
            selectedPreset!,
            item,
            false,
            false,
            ReferenceEquals(item, selectedDyeItem),
            () => OpenDyePicker(item),
            () => onChanged(selectedPreset!)));
    }

    private void OpenDyePicker(BetterGlamourItem item)
    {
        dyePicker ??= new();
        selectedDyeItem = item;
        pendingDyeSelectionRefresh = true;
        dyePicker.OpenFor(item, () =>
        {
            if (selectedPreset is not null)
            {
                onChanged(selectedPreset);
            }

            pendingDyeSelectionRefresh = true;
        });
    }

    private void SetDetailVisibility(bool visible)
    {
        if (nameLabelNode is not null) nameLabelNode.IsVisible = visible;
        if (nameInputNode is not null) nameInputNode.IsVisible = visible;
        if (gearsetLabelNode is not null) gearsetLabelNode.IsVisible = visible;
        if (gearsetDropDownNode is not null) gearsetDropDownNode.IsVisible = visible;
        if (partHeaderNode is not null) partHeaderNode.IsVisible = visible;
        if (itemHeaderNode is not null) itemHeaderNode.IsVisible = visible;
        if (dyeHeaderNode is not null) dyeHeaderNode.IsVisible = visible;
        if (itemListNode is not null) itemListNode.IsVisible = visible;
        if (applyButton is not null) applyButton.IsVisible = visible;
        if (exportButton is not null) exportButton.IsVisible = visible;
        if (deleteButton is not null) deleteButton.IsVisible = visible;
    }

    private void ResizeContent()
    {
        const float presetWidth = 190f;
        const float separatorGap = 12f;
        const float labelWidth = 72f;
        const float rowGap = 6f;
        const float buttonGap = 8f;
        var contentX = ContentStartPosition.X;
        var contentY = ContentStartPosition.Y;
        var detailX = contentX + presetWidth + separatorGap;
        var detailWidth = MathF.Max(420f, ContentSize.X - presetWidth - separatorGap);

        if (presetListNode is not null)
        {
            presetListNode.Position = new(contentX, contentY);
            presetListNode.Size = new(presetWidth, ContentSize.Y);
        }

        if (separatorNode is not null)
        {
            separatorNode.Position = new(contentX + presetWidth + 4f, contentY);
            separatorNode.Size = new(4f, ContentSize.Y);
        }

        if (emptyNode is not null)
        {
            emptyNode.Position = new(detailX, contentY);
            emptyNode.Size = new(detailWidth, ContentSize.Y);
        }

        PositionLabeledControl(nameLabelNode, nameInputNode, detailX, contentY, detailWidth, labelWidth);
        PositionLabeledControl(gearsetLabelNode, gearsetDropDownNode, detailX, contentY + 34f, detailWidth, labelWidth);

        var headerY = contentY + 72f;
        var columns = BetterGlamourEditorListItemNode.GetColumns(detailWidth);
        PositionHeader(partHeaderNode, detailX, headerY, columns.PartWidth);
        PositionHeader(itemHeaderNode, detailX + columns.PartWidth + rowGap, headerY, columns.ItemWidth);
        PositionHeader(dyeHeaderNode, detailX + columns.PartWidth + columns.ItemWidth + rowGap * 2f, headerY, columns.DyeWidth);

        var buttonY = contentY + ContentSize.Y - 28f;
        if (itemListNode is not null)
        {
            itemListNode.Position = new(detailX, headerY + 24f);
            itemListNode.Size = new(detailWidth, MathF.Max(100f, buttonY - headerY - 34f));
        }

        var buttonWidth = MathF.Max(80f, (detailWidth - buttonGap * 2f) / 3f);
        PositionButton(applyButton, detailX, buttonY, buttonWidth);
        PositionButton(exportButton, detailX + buttonWidth + buttonGap, buttonY, buttonWidth);
        PositionButton(deleteButton, detailX + (buttonWidth + buttonGap) * 2f, buttonY, buttonWidth);
    }

    private void UpdateWindowTitle()
    {
        Subtitle = FormatPresetCount(presets.Count);
        if (IsOpen)
        {
            WindowNode?.SetTitle(Title.ToString(), Subtitle?.ToString());
        }
    }

    private static string FormatPresetCount(int presetCount) =>
        $"{presetCount}/{BetterGlamourManagement.MaxPresetCount}";

    private static void PositionLabeledControl(
        TextNode? label,
        NodeBase? control,
        float x,
        float y,
        float width,
        float labelWidth)
    {
        if (label is not null)
        {
            label.Position = new(x, y + 2f);
            label.Size = new(labelWidth, 28f);
        }

        if (control is not null)
        {
            control.Position = new(x + labelWidth + 4f, y + 2f);
            control.Size = new(width - labelWidth - 4f, 28f);
        }
    }

    private static void PositionHeader(TextNode? node, float x, float y, float width)
    {
        if (node is null)
        {
            return;
        }

        node.Position = new(x, y);
        node.Size = new(width, 22f);
    }

    private static void PositionButton(TextButtonNode? button, float x, float y, float width)
    {
        if (button is null)
        {
            return;
        }

        button.Position = new(x, y);
        button.Size = new(width, 28f);
    }

    private readonly record struct GearsetOption(int Index, string Label);
}

internal sealed class BetterGlamourPresetListItemNode : ListItemNode<BetterGlamourPreset>, IListItemNode
{
    public static float ItemHeight => 28f;

    private readonly TextNode nameNode;

    public BetterGlamourPresetListItemNode()
    {
        nameNode = new()
        {
            AlignmentType = AlignmentType.Left,
            TextFlags = TextFlags.Ellipsis
        };
        nameNode.AttachNode(this);
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        nameNode.Position = new(8f, 0f);
        nameNode.Size = new(MathF.Max(20f, Width - 12f), Height);
    }

    protected override void SetNodeData(BetterGlamourPreset itemData)
    {
        var name = string.IsNullOrWhiteSpace(itemData.Name)
            ? OmniLoc.Get("Feature.BetterGlamourManagement.WindowTitle")
            : itemData.Name;
        nameNode.String = itemData.GearsetIndex >= 0
            ? string.Format(
                OmniLoc.Get("Feature.BetterGlamourManagement.PresetList.Bound"),
                name,
                itemData.GearsetIndex + 1)
            : string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.PresetList.Unbound"), name);
        OnSizeChanged();
    }
}

internal sealed record BetterGlamourEditorRow(
    string PartName,
    BetterGlamourPreset Preset,
    BetterGlamourItem? Item,
    bool IsGlasses,
    bool IsHairstyle,
    bool IsDyeSelected,
    Action? OpenDye,
    Action OnChanged);

internal sealed class BetterGlamourEditorListItemNode : ListItemNode<BetterGlamourEditorRow>, IListItemNode
{
    public static float ItemHeight => 40f;

    private const float Gap = 6f;
    private readonly TextNode partNode;
    private readonly NumericInputNode itemIDNode;
    private readonly BetterGlamourItemIconNode itemIconNode;
    private readonly TextNode itemNameNode;
    private readonly BetterGlamourDyeButtonNode dyeNode;
    private readonly TextNode noDyeNode;
    private BetterGlamourEditorRow? row;

    public BetterGlamourEditorListItemNode()
    {
        partNode = CreateTextNode();
        partNode.AlignmentType = AlignmentType.Center;
        itemIDNode = new()
        {
            Min = 0,
            Max = int.MaxValue,
            Step = 1,
            OnValueUpdate = UpdateItemID
        };
        itemIDNode.AttachNode(this);

        itemIconNode = new();
        itemIconNode.AttachNode(this);
        itemNameNode = CreateTextNode();
        dyeNode = CreateDyeButton();
        noDyeNode = CreateTextNode();
        noDyeNode.AlignmentType = AlignmentType.Center;
        noDyeNode.String = OmniLoc.Get("Feature.BetterGlamourManagement.None");
        DisableInteractions();
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        var columns = GetColumns(Width);
        var x = 0f;
        PositionNode(partNode, x, columns.PartWidth);
        x += columns.PartWidth + Gap;
        itemIDNode.Position = new(x, (ItemHeight - 28f) / 2f);
        itemIDNode.Size = new(columns.ItemIDWidth, 28f);
        itemIDNode.BackgroundNode.Y = 2f;
        itemIDNode.AddButton.Y = 0f;
        itemIDNode.SubtractButton.Y = 0f;
        itemIDNode.ValueTextNode.Position = new(6f, 0f);
        itemIDNode.ValueTextNode.Size = new(MathF.Max(1f, columns.ItemIDWidth - 58f), 28f);
        itemIDNode.ValueTextNode.AlignmentType = AlignmentType.Center;
        x += columns.ItemIDWidth + Gap;
        itemIconNode.Position = new(x, (ItemHeight - columns.ItemIconWidth) / 2f);
        itemIconNode.Size = new(columns.ItemIconWidth, columns.ItemIconWidth);
        x += columns.ItemIconWidth + Gap;
        PositionNode(itemNameNode, x, columns.ItemNameWidth);
        x += columns.ItemNameWidth + Gap;
        dyeNode.Position = new(x, (ItemHeight - 28f) / 2f);
        dyeNode.Size = new(columns.DyeWidth, 28f);
        PositionNode(noDyeNode, x, columns.DyeWidth);
    }

    protected override void SetNodeData(BetterGlamourEditorRow itemData)
    {
        row = itemData;
        partNode.String = itemData.PartName;
        itemIDNode.Value = itemData.IsHairstyle
            ? (int)Math.Min(itemData.Preset.HairstyleID, int.MaxValue)
            : itemData.IsGlasses
                ? itemData.Preset.GlassesID
                : (int)Math.Min(itemData.Item!.ItemID, int.MaxValue);
        itemIconNode.IconID = itemData.IsHairstyle
            ? BetterGlamourManagement.GetHairstyleIcon(itemData.Preset.HairstyleID)
            : itemData.IsGlasses
                ? BetterGlamourManagement.GetGlassesIcon(itemData.Preset.GlassesID)
                : BetterGlamourManagement.GetItemIcon(itemData.Item!.ItemID);
        itemIconNode.ItemTooltip = itemData.IsGlasses || itemData.IsHairstyle ? 0 : itemData.Item!.ItemID;
        itemNameNode.String = itemData.IsHairstyle
            ? BetterGlamourManagement.GetHairstyleName(itemData.Preset.HairstyleID)
            : itemData.IsGlasses
                ? BetterGlamourManagement.GetGlassesName(itemData.Preset.GlassesID)
                : BetterGlamourManagement.GetItemName(itemData.Item!.ItemID);
        var canDye = !itemData.IsGlasses && !itemData.IsHairstyle;
        dyeNode.IsVisible = canDye;
        noDyeNode.IsVisible = !canDye;
        if (canDye)
        {
            dyeNode.IsSelected = itemData.IsDyeSelected;
            dyeNode.SetStains(itemData.Item!.Stain0, itemData.Item.Stain1);
        }

        OnSizeChanged();
    }

    private TextNode CreateTextNode()
    {
        var node = new TextNode
        {
            AlignmentType = AlignmentType.Left,
            TextFlags = TextFlags.Ellipsis
        };
        node.AttachNode(this);
        return node;
    }

    private BetterGlamourDyeButtonNode CreateDyeButton()
    {
        var node = new BetterGlamourDyeButtonNode();
        node.CollisionNode.AddEvent(AtkEventType.MouseClick, () => row?.OpenDye?.Invoke());
        node.AttachNode(this);
        return node;
    }

    private void UpdateItemID(int value)
    {
        if (row is null || IsSettingNodeData)
        {
            return;
        }

        if (row.IsHairstyle)
        {
            row.Preset.HairstyleID = (uint)Math.Max(0, value);
            itemIconNode.IconID = BetterGlamourManagement.GetHairstyleIcon(row.Preset.HairstyleID);
            itemNameNode.String = BetterGlamourManagement.GetHairstyleName(row.Preset.HairstyleID);
        }
        else if (row.IsGlasses)
        {
            row.Preset.GlassesID = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
            itemIconNode.IconID = BetterGlamourManagement.GetGlassesIcon(row.Preset.GlassesID);
            itemNameNode.String = BetterGlamourManagement.GetGlassesName(row.Preset.GlassesID);
        }
        else
        {
            row.Item!.ItemID = (uint)Math.Max(0, value);
            itemIconNode.IconID = BetterGlamourManagement.GetItemIcon(row.Item.ItemID);
            itemIconNode.ItemTooltip = row.Item.ItemID;
            itemNameNode.String = BetterGlamourManagement.GetItemName(row.Item.ItemID);
        }

        row.OnChanged();
    }

    internal static EditorColumns GetColumns(float width)
    {
        const float partWidth = 76f;
        const float itemIDWidth = 132f;
        const float itemIconWidth = 36f;
        const float dyeWidth = 148f;
        return new(
            partWidth,
            itemIDWidth,
            itemIconWidth,
            MathF.Max(80f, width - partWidth - itemIDWidth - itemIconWidth - dyeWidth - Gap * 4f),
            dyeWidth);
    }

    private static void PositionNode(NodeBase node, float x, float width)
    {
        node.Position = new(x, 0f);
        node.Size = new(width, ItemHeight);
    }

    internal readonly record struct EditorColumns(
        float PartWidth,
        float ItemIDWidth,
        float ItemIconWidth,
        float ItemNameWidth,
        float DyeWidth)
    {
        public float ItemWidth => ItemIDWidth + ItemIconWidth + ItemNameWidth + Gap * 2f;
    }
}

internal sealed class BetterGlamourDyeButtonNode : TabBarRadioButtonNode
{
    private readonly BetterGlamourDyeColorNode[] previewNodes = new BetterGlamourDyeColorNode[2];

    public BetterGlamourDyeButtonNode()
    {
        String = OmniLoc.Get("Feature.BetterGlamourManagement.Dye");
        for (var index = 0; index < previewNodes.Length; index++)
        {
            previewNodes[index] = new(0f, 0f, 0f) { ShowBorder = false, DisableCollisionNode = true };
            previewNodes[index].AttachNode(this);
        }
    }

    public void SetStains(byte stain0, byte stain1)
    {
        previewNodes[0].SetDye(stain0, GetStainColor(stain0));
        previewNodes[1].SetDye(stain1, GetStainColor(stain1));
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        const float previewSize = 18f;
        const float labelWidth = 40f;
        const float gap = 5f;
        var contentX = MathF.Max(8f, (Width - labelWidth - previewSize * 2f - gap * 2f) / 2f);
        previewNodes[0].Position = new(contentX, (Height - previewSize) / 2f);
        previewNodes[1].Position = new(contentX + previewSize + gap, (Height - previewSize) / 2f);
        LabelNode.Position = new(contentX + previewSize * 2f + gap * 2f, 0f);
        LabelNode.Size = new(labelWidth, Height);
        previewNodes[0].Size = new(previewSize, previewSize);
        previewNodes[1].Size = new(previewSize, previewSize);
    }

    private static Vector4 GetStainColor(byte stainID)
    {
        var option = BetterGlamourManagement.DyeOptions.Find(option => option.ID == stainID);
        return option.ID == 0 ? KnownColor.White.Vector() : option.Color;
    }
}

internal sealed class BetterGlamourItemIconNode : SimpleComponentNode
{
    private readonly IconNode iconNode;

    public BetterGlamourItemIconNode()
    {
        iconNode = new();
        iconNode.AttachNode(this);
    }

    public uint IconID
    {
        get => iconNode.IconId;
        set => iconNode.IconId = value;
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        var scale = MathF.Max(0.01f, MathF.Min(Width, Height) / 48f);
        iconNode.Size = new(48f, 48f);
        iconNode.Scale = new(scale, scale);
        iconNode.Position = new((Width - 48f * scale) / 2f, (Height - 48f * scale) / 2f);
    }
}
