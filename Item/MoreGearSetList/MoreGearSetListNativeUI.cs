using System.Globalization;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.ContextMenu;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node.Simple;
using Lumina.Text.ReadOnly;

namespace OmniToolbox.TreePublic;

internal sealed class MoreGearSetListRow
{
    public MoreGearSetListEntry Entry { get; init; } = null!;
    public int Number { get; init; }
    public string Name { get; init; } = string.Empty;
    public uint IconID { get; init; }
    public string Status { get; init; } = string.Empty;
    public uint StatusColor { get; init; }
}

internal sealed class MoreGearSetListNativeUI : NativeAddon
{
    private const float ItemSpacing = 0f;
    private const float MoveButtonSize = 18f;

    private readonly Action onSave;
    private readonly Action<MoreGearSetListEntry> onUpdate;
    private readonly Action<MoreGearSetListEntry> onDelete;
    private readonly Action onImport;
    private readonly Action<MoreGearSetListEntry> onApply;
    private readonly Action onFilterChanged;
    private readonly MoreGearSetList feature;
    private readonly HashSet<MoreGearSetListEntry> batchPicks = [];

    private List<MoreGearSetListRow> rows = [];
    private MoreGearSetListEntry? selected;
    private bool loggedIn;
    private bool updatingFilter;
    private string emptyText = string.Empty;

    private TextDropDownNode? jobDropDown;
    private TextInputNode? searchNode;
    private TextButtonNode? importButton;
    private TextButtonNode? batchButton;
    private TextButtonNode? confirmBatchButton;
    private TextNode? emptyNode;
    private ListNode<MoreGearSetListRow, MoreGearSetListItemNode>? listNode;
    private TextButtonNode? saveButton;
    private TextButtonNode? updateButton;
    private TextButtonNode? deleteButton;
    private CircleButtonNode? upButton;
    private CircleButtonNode? downButton;
    private KamiToolKit.ContextMenu.ContextMenu? itemMenu;
    private MoreGearSetListRenameUI? renameUI;
    private MoreGearSetListConfirmUI? confirmUI;
    private MoreGearSetListEntry? pendingMenu;
    private int menuWait;
    private bool batchMode;
    private int followOffset;
    private bool pendingFollow;
    private static FieldInfo? listScrollField;
    private static FieldInfo? listSelectedField;
    private static MethodInfo? listPopulate;
    private static MethodInfo? listRecalculate;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MoreGearSetListNativeUI(
        Action onSave,
        Action<MoreGearSetListEntry> onUpdate,
        Action<MoreGearSetListEntry> onDelete,
        Action onImport,
        Action<MoreGearSetListEntry> onApply,
        Action onFilterChanged,
        MoreGearSetList feature)
    {
        InternalName          = "OmniMoreGearSetList";
        Title                 = "套装列表";
        Subtitle              = string.Empty;
        Size                  = new(268f, 480f);
        ContentPadding        = new(8f, 8f);
        RememberClosePosition = false;
        CreateWindowNode      = static () => new WindowNode { ShowCloseButton = true };
        RespectCloseAll       = true;
        DisableClose          = false;
        this.onSave           = onSave;
        this.onUpdate         = onUpdate;
        this.onDelete         = onDelete;
        this.onImport         = onImport;
        this.onApply          = onApply;
        this.onFilterChanged  = onFilterChanged;
        this.feature          = feature;
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        jobDropDown = new()
        {
            Options          = [],
            MaxListOptions   = 10,
            OnOptionSelected = OnJobSelected
        };
        jobDropDown.AttachNode(this);
        jobDropDown.Options = MoreGearSetList.GetJobChoiceLabels();

        searchNode = new()
        {
            MaxCharacters     = 32,
            ShowLimitText     = false,
            PlaceholderString = "搜索名称",
            OnInputReceived   = OnSearchInput
        };
        searchNode.AttachNode(this);

        importButton = new()
        {
            String  = "导入原生套装",
            OnClick = onImport
        };
        importButton.AttachNode(this);

        batchButton = new()
        {
            String  = "批量删除",
            OnClick = ToggleBatchMode
        };
        batchButton.AttachNode(this);

        confirmBatchButton = new()
        {
            String    = "确认删除",
            OnClick   = AskBatchDelete,
            IsVisible = false
        };
        confirmBatchButton.AttachNode(this);

        emptyNode = new()
        {
            AlignmentType = AlignmentType.TopLeft,
            TextFlags     = TextFlags.MultiLine,
            IsVisible     = false
        };
        emptyNode.AttachNode(this);

        listNode = new()
        {
            ItemSpacing    = ItemSpacing,
            OptionsList    = [],
            OnItemSelected = SelectRow
        };
        listNode.AttachNode(this);

        saveButton   = CreateButton("新建套装", onSave);
        updateButton = CreateButton("保存当前所穿", () =>
        {
            if (selected is not null)
            {
                onUpdate(selected);
            }
        });
        deleteButton = CreateButton("删除", () =>
        {
            if (selected is not null)
            {
                onDelete(selected);
            }
        });

        upButton = new()
        {
            Icon    = ButtonIcon.UpArrow,
            Size    = new(MoveButtonSize, MoveButtonSize),
            OnClick = () => MoveSelected(-1)
        };
        upButton.AttachNode(this);

        downButton = new()
        {
            Icon    = ButtonIcon.ArrowDown,
            Size    = new(MoveButtonSize, MoveButtonSize),
            OnClick = () => MoveSelected(1)
        };
        downButton.AttachNode(this);

        itemMenu = new();
        MoreGearSetListItemNode.OnRightClick = ShowContextMenu;
        MoreGearSetListItemNode.OnActivate   = onApply;
        MoreGearSetListItemNode.OnToggle     = TogglePick;
        MoreGearSetListItemNode.IsMarked    = batchPicks.Contains;
        MoreGearSetListItemNode.GetCurrent  = () => selected;
        ResizeContent();
        ApplyData();
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon)
    {
        ResizeContent();
        SyncListSelection();
        if (pendingFollow && FollowSelected())
            pendingFollow = false;

        listNode?.Update();
        if (pendingMenu is not { } entry)
        {
            return;
        }

        if (menuWait > 0)
        {
            menuWait--;
            return;
        }

        if (ImGui.GetIO().MouseDown[1])
        {
            return;
        }

        pendingMenu = null;
        OpenItemMenu(entry);
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        jobDropDown        = null;
        searchNode         = null;
        importButton       = null;
        batchButton        = null;
        confirmBatchButton = null;
        emptyNode          = null;
        listNode           = null;
        saveButton         = null;
        updateButton       = null;
        deleteButton       = null;
        upButton           = null;
        downButton         = null;
        itemMenu?.Close();
        itemMenu?.Dispose();
        itemMenu    = null;
        pendingMenu = null;
        menuWait    = 0;
        renameUI?.Close();
        renameUI?.Dispose();
        renameUI = null;
        confirmUI?.Close();
        confirmUI?.Dispose();
        confirmUI = null;
        batchMode     = false;
        pendingFollow = false;
        batchPicks.Clear();
        MoreGearSetListItemNode.BatchMode    = false;
        MoreGearSetListItemNode.OnRightClick = null;
        MoreGearSetListItemNode.OnActivate   = null;
        MoreGearSetListItemNode.OnToggle     = null;
        MoreGearSetListItemNode.IsMarked     = null;
        MoreGearSetListItemNode.GetCurrent   = null;
        MoreGearSetListItemNode.ResetClick();
    }

    public void UpdateData(List<MoreGearSetListRow> newRows, bool isLoggedIn, string emptyMessage)
    {
        rows      = newRows;
        loggedIn  = isLoggedIn;
        emptyText = emptyMessage;
        if (selected is not null && !rows.Exists(row => row.Entry == selected))
        {
            selected = null;
        }

        batchPicks.RemoveWhere(entry => !rows.Exists(row => row.Entry == entry));
        ApplyData();
    }

    public void SetSelected(MoreGearSetListEntry? entry) => selected = entry;

    private void SelectRow(MoreGearSetListRow? row)
    {
        if (row is null)
        {
            if (!batchMode)
            {
                selected = null;
                ApplyButtons();
            }

            return;
        }

        if (batchMode)
        {
            TogglePick(row.Entry);
            return;
        }

        selected = row.Entry;
        followOffset = VisibleOffset(rows.FindIndex(item => ReferenceEquals(item.Entry, selected)));
        ApplyButtons();
    }

    private void ApplyData()
    {
        if (jobDropDown is not null)
        {
            updatingFilter = true;
            jobDropDown.SelectedOption = MoreGearSetList.GetJobFilterLabel();
            updatingFilter = false;
        }

        if (searchNode is not null)
        {
            updatingFilter = true;
            searchNode.String = MoreGearSetListPanel.SearchText;
            updatingFilter = false;
        }

        if (listNode is not null && !ReferenceEquals(listNode.OptionsList, rows))
        {
            listNode.OptionsList = rows;
        }

        SyncListSelection();

        if (emptyNode is not null)
        {
            emptyNode.IsVisible = rows.Count == 0;
            emptyNode.String    = emptyNode.IsVisible ? emptyText : string.Empty;
        }

        ApplyButtons();
    }

    private void OnSearchInput(ReadOnlySeString value)
    {
        if (updatingFilter)
        {
            return;
        }

        var text = value.ExtractText();
        if (text == MoreGearSetListPanel.SearchText)
        {
            return;
        }

        MoreGearSetListPanel.SearchText = text;
        onFilterChanged();
    }

    private void OnJobSelected(string label)
    {
        if (updatingFilter || label == MoreGearSetList.GetJobFilterLabel())
        {
            return;
        }

        MoreGearSetList.ApplyJobFilterLabel(label);
        onFilterChanged();
    }

    private void ApplyButtons()
    {
        if (importButton is not null)
        {
            importButton.IsEnabled = loggedIn && !batchMode;
            importButton.IsVisible = !batchMode;
        }

        if (batchButton is not null)
        {
            batchButton.String    = batchMode ? "取消" : "批量删除";
            batchButton.IsEnabled = loggedIn || batchMode;
        }

        if (confirmBatchButton is not null)
        {
            confirmBatchButton.IsVisible = batchMode;
            confirmBatchButton.IsEnabled = batchPicks.Count > 0;
            confirmBatchButton.String    = batchPicks.Count == 0
                ? "确认删除"
                : $"确认删除（{batchPicks.Count}）";
        }

        if (saveButton is not null)
        {
            saveButton.IsEnabled = loggedIn && !batchMode;
        }

        if (updateButton is not null)
        {
            updateButton.IsEnabled = selected is not null && !batchMode;
        }

        if (deleteButton is not null)
        {
            deleteButton.IsEnabled = selected is not null && !batchMode;
        }

        var index = selected is null ? -1 : feature.GetSetIndex(selected);
        var count = feature.GetSetCount();
        if (upButton is not null)
        {
            upButton.IsVisible = !batchMode;
            upButton.IsEnabled = !batchMode && index > 0;
        }

        if (downButton is not null)
        {
            downButton.IsVisible = !batchMode;
            downButton.IsEnabled = !batchMode && index >= 0 && index < count - 1;
        }
    }

    private void ResizeContent()
    {
        var x = ContentStartPosition.X;
        var y = ContentStartPosition.Y;
        var width = ContentSize.X;

        const float jobWidth = 88f;
        const float gap = 6f;
        if (jobDropDown is not null)
        {
            jobDropDown.Position = new(x, y);
            jobDropDown.Size     = new(jobWidth, 24f);
        }

        if (searchNode is not null)
        {
            searchNode.Position = new(x + jobWidth + gap, y);
            searchNode.Size     = new(MathF.Max(72f, width - jobWidth - gap), 24f);
        }

        y += 32f;
        var toolWidth = MathF.Max(60f, (width - gap) / 2f);
        if (importButton is not null)
        {
            importButton.Position  = new(x, y);
            importButton.Size      = new(toolWidth, 24f);
            importButton.IsVisible = !batchMode;
        }

        if (batchButton is not null)
        {
            batchButton.Position = new(batchMode ? x : x + toolWidth + gap, y);
            batchButton.Size     = new(toolWidth, 24f);
        }

        if (confirmBatchButton is not null)
        {
            confirmBatchButton.Position  = new(x + toolWidth + gap, y);
            confirmBatchButton.Size      = new(toolWidth, 24f);
            confirmBatchButton.IsVisible = batchMode;
        }

        y += 32f;
        var listHeight = MathF.Max(60f, ContentSize.Y - 102f);
        if (emptyNode is not null)
        {
            emptyNode.Position = new(x, y + 8f);
            emptyNode.Size     = new(width, 56f);
        }

        if (listNode is not null)
        {
            listNode.Position = new(x + 2f, y);
            listNode.Size     = new(width - 4f, listHeight);
        }

        var moveX = x + width - MoveButtonSize;
        if (upButton is not null)
        {
            upButton.Position = new(moveX, y);
            upButton.Size     = new(MoveButtonSize, MoveButtonSize);
        }

        if (downButton is not null)
        {
            downButton.Position = new(moveX, y + MoveButtonSize + 2f);
            downButton.Size     = new(MoveButtonSize, MoveButtonSize);
        }

        var buttonY = y + listHeight + 8f;
        var buttonWidth = MathF.Max(60f, (width - gap * 2f) / 3f);
        PositionButton(saveButton, x, buttonY, buttonWidth);
        PositionButton(updateButton, x + buttonWidth + gap, buttonY, buttonWidth);
        PositionButton(deleteButton, x + (buttonWidth + gap) * 2f, buttonY, buttonWidth);
    }

    private void MoveSelected(int delta)
    {
        if (selected is not { } entry)
        {
            return;
        }

        if (!feature.TryMove(entry, delta))
        {
            return;
        }

        SetSelected(entry);
        pendingFollow = true;
        ApplyButtons();
        if (listNode is not null && listNode.Height >= MoreGearSetListItemNode.ItemHeight)
            FollowSelected();
    }

    private int VisibleOffset(int index) =>
        index < 0 ? 0 : index - GetListScroll();

    private bool FollowSelected()
    {
        if (batchMode || listNode is null || selected is null)
            return true;

        var index = rows.FindIndex(row => ReferenceEquals(row.Entry, selected));
        if (index < 0)
            return true;

        var itemHeight = MoreGearSetListItemNode.ItemHeight + ItemSpacing;
        if (itemHeight <= 0f || listNode.Height < itemHeight)
            return false;

        var visible   = Math.Max(1, (int)(listNode.Height / itemHeight));
        var maxScroll = Math.Max(0, rows.Count - visible);
        var target    = Math.Clamp(index - followOffset, 0, maxScroll);
        followOffset  = index - target;
        SetListScroll(target);
        return true;
    }

    private int GetListScroll()
    {
        EnsureListScrollAccess();
        if (listNode is not null && listScrollField?.GetValue(listNode) is int position)
            return position;

        var itemHeight = MoreGearSetListItemNode.ItemHeight + ItemSpacing;
        if (listNode is null || itemHeight <= 0f)
            return 0;

        return (int)(listNode.ScrollBarNode.ScrollPosition / itemHeight);
    }

    private void SetListScroll(int position)
    {
        if (listNode is null)
            return;

        EnsureListScrollAccess();
        SyncListSelection();
        listScrollField?.SetValue(listNode, position);
        listRecalculate?.Invoke(listNode, null);
        listPopulate?.Invoke(listNode, null);
        if (listScrollField is not null)
            return;

        var itemHeight = MoreGearSetListItemNode.ItemHeight + ItemSpacing;
        listNode.ScrollBarNode.ScrollPosition = (int)(position * itemHeight);
    }

    private void SyncListSelection()
    {
        if (listNode is null)
            return;

        EnsureListScrollAccess();
        MoreGearSetListRow? row = null;
        if (selected is not null)
        {
            foreach (var item in rows)
            {
                if (ReferenceEquals(item.Entry, selected))
                {
                    row = item;
                    break;
                }
            }
        }

        listSelectedField?.SetValue(listNode, row);
    }

    private static void EnsureListScrollAccess()
    {
        if (listScrollField is not null)
            return;

        var type = typeof(ListNode<MoreGearSetListRow, MoreGearSetListItemNode>);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        listScrollField   = type.GetField("scrollPosition", flags);
        listSelectedField = type.GetField("selectedItem", flags);
        listPopulate      = type.GetMethod("PopulateNodes", flags);
        listRecalculate   = type.GetMethod("RecalculateScroll", flags);
    }

    private void ToggleBatchMode()
    {
        batchMode = !batchMode;
        MoreGearSetListItemNode.BatchMode = batchMode;
        if (!batchMode)
        {
            batchPicks.Clear();
            confirmUI?.Close();
        }

        ApplyButtons();
    }

    private void TogglePick(MoreGearSetListEntry entry)
    {
        if (!batchPicks.Remove(entry))
        {
            batchPicks.Add(entry);
        }

        ApplyButtons();
    }

    private unsafe void AskBatchDelete()
    {
        if (batchPicks.Count == 0)
        {
            return;
        }

        confirmUI ??= new(ConfirmBatchDelete);
        AtkUnitBase* addon = this;
        if (addon != null)
        {
            confirmUI.SetWindowPosition(new(addon->X + 28f, addon->Y + 72f));
        }

        confirmUI.Show(batchPicks.Count);
    }

    private void ConfirmBatchDelete()
    {
        if (batchPicks.Count == 0)
        {
            return;
        }

        feature.TryDeleteMany([.. batchPicks]);
        batchPicks.Clear();
        batchMode = false;
        MoreGearSetListItemNode.BatchMode = false;
        selected = null;
        ApplyButtons();
    }

    private void ShowContextMenu(MoreGearSetListEntry entry)
    {
        if (batchMode)
        {
            TogglePick(entry);
            return;
        }

        pendingMenu = entry;
        menuWait    = 2;
    }

    private unsafe void OpenItemMenu(MoreGearSetListEntry entry)
    {
        if (itemMenu is null)
        {
            return;
        }

        selected = entry;
        ApplyButtons();
        itemMenu.Clear();
        itemMenu.AddItem("应用", () => onApply(entry));
        itemMenu.AddItem("保存当前所穿", () => onUpdate(entry));
        itemMenu.AddItem("重命名", () => OpenRename(entry));
        itemMenu.AddItem("删除", () => onDelete(entry));
        itemMenu.AddItem("装备一览", () => feature.ShowItems(entry));
        itemMenu.AddItem("预览", () => feature.OpenPreview(entry));
        itemMenu.AddItem("投影台", () => feature.OpenGlamourPlate(entry));
        itemMenu.AddItem("肖像", () => feature.OpenPortrait(entry));
        itemMenu.Open();
        AtkUnitBase* addon = this;
        var ctx = AgentContext.Instance();
        if (addon != null && ctx != null)
        {
            ctx->OpenContextMenuForAddon(addon->Id, true);
        }
    }

    private unsafe void OpenRename(MoreGearSetListEntry entry)
    {
        renameUI ??= new(feature);
        AtkUnitBase* addon = this;
        if (addon != null)
        {
            renameUI.SetWindowPosition(new(addon->X + 28f, addon->Y + 72f));
        }

        renameUI.Show(entry);
    }

    private TextButtonNode CreateButton(string text, Action onClick)
    {
        var button = new TextButtonNode
        {
            String  = text,
            OnClick = onClick
        };
        button.AttachNode(this);
        return button;
    }

    private static void PositionButton(TextButtonNode? button, float x, float y, float width)
    {
        if (button is null)
        {
            return;
        }

        button.Position = new(x, y);
        button.Size     = new(width, 28f);
    }
}

internal sealed class MoreGearSetListRenameUI : NativeAddon
{
    private readonly MoreGearSetList feature;
    private MoreGearSetListEntry? entry;
    private TextInputNode? nameInput;
    private TextButtonNode? confirmButton;
    private bool updatingName;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MoreGearSetListRenameUI(MoreGearSetList feature)
    {
        InternalName          = "OmniMoreGearSetRename";
        Title                 = "重命名";
        Subtitle              = string.Empty;
        Size                  = new(280f, 128f);
        ContentPadding        = new(8f, 8f);
        RememberClosePosition = false;
        CreateWindowNode      = static () => new WindowNode { ShowCloseButton = true };
        RespectCloseAll       = false;
        DisableClose          = false;
        this.feature          = feature;
    }

    public void Show(MoreGearSetListEntry target)
    {
        entry = target;
        if (IsOpen)
        {
            ApplyName();
            return;
        }

        Open();
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        nameInput = new()
        {
            MaxCharacters     = 32,
            ShowLimitText     = false,
            PlaceholderString = "套装名称",
            OnInputComplete   = _ => Confirm()
        };
        nameInput.AttachNode(this);

        confirmButton = new()
        {
            String  = "确定",
            OnClick = Confirm
        };
        confirmButton.AttachNode(this);
        ResizeContent();
        ApplyName();
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon) =>
        ResizeContent();

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        nameInput     = null;
        confirmButton = null;
    }

    private void ApplyName()
    {
        if (nameInput is null || entry is null)
        {
            return;
        }

        updatingName     = true;
        nameInput.String = entry.Name;
        updatingName     = false;
    }

    private void Confirm()
    {
        if (updatingName || entry is null || nameInput is null)
        {
            return;
        }

        feature.TryRename(entry, nameInput.String.ExtractText());
        Close();
    }

    private void ResizeContent()
    {
        var x = ContentStartPosition.X;
        var y = ContentStartPosition.Y;
        var width = ContentSize.X;
        if (nameInput is not null)
        {
            nameInput.Position = new(x, y);
            nameInput.Size     = new(width, 24f);
        }

        if (confirmButton is not null)
        {
            confirmButton.Position = new(x, y + 32f);
            confirmButton.Size     = new(width, 28f);
        }
    }
}

internal sealed unsafe class MoreGearSetListItemNode : ListItemNode<MoreGearSetListRow>, IListItemNode
{
    public static float ItemHeight => 28f;

    internal static Action<MoreGearSetListEntry>? OnRightClick;
    internal static Action<MoreGearSetListEntry>? OnActivate;
    internal static Action<MoreGearSetListEntry>? OnToggle;
    internal static Func<MoreGearSetListEntry, bool>? IsMarked;
    internal static Func<MoreGearSetListEntry?>? GetCurrent;
    internal static bool BatchMode;

    private static MoreGearSetListEntry? lastLeftEntry;
    private static long lastLeftTick;

    private readonly CheckboxNode checkNode;
    private readonly TextNode numberNode;
    private readonly IconImageNode iconNode;
    private readonly TextNode nameNode;
    private readonly TextNode statusNode;

    public MoreGearSetListItemNode()
    {
        checkNode = new()
        {
            String    = string.Empty,
            IsVisible = false,
            OnClick   = _ =>
            {
                if (ItemData?.Entry is { } picked)
                {
                    OnToggle?.Invoke(picked);
                }
            }
        };
        checkNode.AttachNode(this);

        numberNode = new()
        {
            AlignmentType = AlignmentType.Center
        };
        numberNode.AttachNode(this);

        iconNode = new()
        {
            FitTexture = true
        };
        iconNode.AttachNode(this);

        nameNode = new()
        {
            AlignmentType    = AlignmentType.Left,
            TextFlags        = TextFlags.Ellipsis | TextFlags.Emboss,
            TextColor        = ColorHelper.GetColor(8),
            TextOutlineColor = ColorHelper.GetColor(7)
        };
        nameNode.AttachNode(this);

        statusNode = new()
        {
            AlignmentType    = AlignmentType.Right,
            TextFlags        = TextFlags.Ellipsis | TextFlags.Emboss,
            TextOutlineColor = ColorHelper.GetColor(7)
        };
        statusNode.AttachNode(this);
        CollisionNode.AddEvent(AtkEventType.MouseDown, OnMouseDown);
        CollisionNode.AddEvent(AtkEventType.MouseUp, OnMouseUp);
    }

    internal static void ResetClick()
    {
        lastLeftEntry = null;
        lastLeftTick  = 0;
    }

    private void OnMouseDown(AtkEventListener* thisPtr, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, AtkEventData* atkEventData)
    {
        if (atkEventData == null || ItemData?.Entry is not { } entry)
        {
            return;
        }

        if (BatchMode)
        {
            if (atkEventData->MouseData.ButtonId == 0)
            {
                OnToggle?.Invoke(entry);
            }

            ResetClick();
            return;
        }

        if (atkEventData->MouseData.ButtonId == 1)
        {
            ResetClick();
            OnRightClick?.Invoke(entry);
            return;
        }

        if (atkEventData->MouseData.ButtonId != 0)
        {
            ResetClick();
            return;
        }

        var now = Environment.TickCount64;
        if (lastLeftEntry == entry && now - lastLeftTick < 400)
        {
            lastLeftEntry = null;
            lastLeftTick  = 0;
            OnActivate?.Invoke(entry);
            return;
        }

        lastLeftEntry = entry;
        lastLeftTick  = now;
    }

    private void OnMouseUp(AtkEventListener* thisPtr, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, AtkEventData* atkEventData)
    {
        if (atkEventData == null || atkEventData->MouseData.ButtonId != 1)
        {
            return;
        }

        ResetClick();
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        const float iconSize = 24f;
        const float checkWidth = 20f;
        const float numberWidth = 32f;
        const float statusWidth = 36f;
        var checkPad = BatchMode ? checkWidth + 2f : 0f;
        checkNode.IsVisible = BatchMode;
        checkNode.Position  = new(0f, 2f);
        checkNode.Size      = new(checkWidth, Height - 4f);
        numberNode.Position = new(checkPad, 0f);
        numberNode.Size     = new(numberWidth, Height);
        iconNode.Position   = new(checkPad + numberWidth + 2f, (Height - iconSize) / 2f);
        iconNode.Size       = new(iconSize, iconSize);
        var nameX = checkPad + numberWidth + iconSize + 8f;
        nameNode.Position   = new(nameX, 0f);
        nameNode.Size       = new(MathF.Max(20f, Width - nameX - statusWidth - 6f), Height);
        statusNode.Position = new(MathF.Max(nameX, Width - statusWidth), 0f);
        statusNode.Size     = new(statusWidth, Height);
    }

    protected override void SetNodeData(MoreGearSetListRow itemData)
    {
        numberNode.String    = itemData.Number.ToString("00", CultureInfo.CurrentCulture);
        iconNode.IconId      = itemData.IconID;
        iconNode.IsVisible   = itemData.IconID > 0;
        nameNode.String      = itemData.Name;
        statusNode.String    = itemData.Status;
        statusNode.TextColor = ColorHelper.GetColor(itemData.StatusColor);
        var marked = BatchMode && (IsMarked?.Invoke(itemData.Entry) ?? false);
        checkNode.IsChecked  = marked;
        IsSelected           = BatchMode
            ? marked
            : ReferenceEquals(itemData.Entry, GetCurrent?.Invoke());
        OnSizeChanged();
    }

    public override void Update()
    {
        if (ItemData?.Entry is not { } entry)
        {
            return;
        }

        var marked = BatchMode && (IsMarked?.Invoke(entry) ?? false);
        checkNode.IsChecked = marked;
        checkNode.IsVisible = BatchMode;
        IsSelected = BatchMode
            ? marked
            : ReferenceEquals(entry, GetCurrent?.Invoke());
    }
}

internal sealed class MoreGearSetListConfirmUI : NativeAddon
{
    private readonly Action onConfirm;
    private TextNode? messageNode;
    private TextButtonNode? cancelButton;
    private TextButtonNode? confirmButton;
    private int count;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MoreGearSetListConfirmUI(Action onConfirm)
    {
        InternalName          = "OmniMoreGearSetConfirm";
        Title                 = "确认删除";
        Subtitle              = string.Empty;
        Size                  = new(280f, 128f);
        ContentPadding        = new(8f, 8f);
        RememberClosePosition = false;
        CreateWindowNode      = static () => new WindowNode { ShowCloseButton = true };
        RespectCloseAll       = false;
        DisableClose          = false;
        this.onConfirm        = onConfirm;
    }

    public void Show(int deleteCount)
    {
        count = deleteCount;
        if (IsOpen)
        {
            ApplyMessage();
            return;
        }

        Open();
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        messageNode = new()
        {
            AlignmentType = AlignmentType.Left,
            TextFlags     = TextFlags.MultiLine
        };
        messageNode.AttachNode(this);

        cancelButton = new()
        {
            String  = "取消",
            OnClick = Close
        };
        cancelButton.AttachNode(this);

        confirmButton = new()
        {
            String  = "删除",
            OnClick = () =>
            {
                Close();
                onConfirm();
            }
        };
        confirmButton.AttachNode(this);
        ApplyMessage();
        ResizeContent();
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon) =>
        ResizeContent();

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        messageNode   = null;
        cancelButton  = null;
        confirmButton = null;
    }

    private void ApplyMessage()
    {
        if (messageNode is not null)
        {
            messageNode.String = $"确定删除选中的 {count} 套？";
        }
    }

    private void ResizeContent()
    {
        var x = ContentStartPosition.X;
        var y = ContentStartPosition.Y;
        var width = ContentSize.X;
        if (messageNode is not null)
        {
            messageNode.Position = new(x, y);
            messageNode.Size     = new(width, 36f);
        }

        var buttonWidth = MathF.Max(60f, (width - 6f) / 2f);
        if (cancelButton is not null)
        {
            cancelButton.Position = new(x, y + 44f);
            cancelButton.Size     = new(buttonWidth, 28f);
        }

        if (confirmButton is not null)
        {
            confirmButton.Position = new(x + buttonWidth + 6f, y + 44f);
            confirmButton.Size     = new(buttonWidth, 28f);
        }
    }
}
