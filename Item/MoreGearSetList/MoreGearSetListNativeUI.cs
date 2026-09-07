using System.Globalization;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.ContextMenu;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node.Simple;
using Lumina.Text.ReadOnly;
using OmniToolbox.UI.Theme;

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

internal sealed class MoreGearSetListItemHost
{
    public Action<MoreGearSetListEntry>? OnRightClick;
    public Action<MoreGearSetListEntry>? OnActivate;
    public Action<MoreGearSetListEntry>? OnToggle;
    public Func<MoreGearSetListEntry, bool>? IsMarked;
    public Func<MoreGearSetListEntry?>? GetCurrent;
    public bool BatchMode;
    public MoreGearSetListEntry? LastLeftEntry;
    public long LastLeftTick;

    public void ResetClick()
    {
        LastLeftEntry = null;
        LastLeftTick  = 0;
    }

    public void Clear()
    {
        OnRightClick = null;
        OnActivate   = null;
        OnToggle     = null;
        IsMarked     = null;
        GetCurrent   = null;
        BatchMode    = false;
        ResetClick();
    }
}

internal sealed class MoreGearSetListNativeUI : NativeAddon
{
    private const float ItemSpacing = 0f;

    private static float MoveButtonSize => OmniTheme.Scale(18f);
    private static float RowHeight => OmniTheme.Scale(24f);
    private static float ButtonHeight => OmniTheme.Scale(28f);
    private static float JobWidth => OmniTheme.Scale(88f);
    private static float Gap => OmniTheme.Scale(6f);

    private readonly Action onSave;
    private readonly Action<MoreGearSetListEntry> onUpdate;
    private readonly Action<MoreGearSetListEntry> onDelete;
    private readonly Action onImport;
    private readonly Action<MoreGearSetListEntry> onApply;
    private readonly Action onFilterChanged;
    private readonly MoreGearSetList feature;
    private readonly HashSet<MoreGearSetListEntry> batchPicks = [];
    private readonly MoreGearSetListItemHost itemHost = new();

    private List<MoreGearSetListRow> rows = [];
    private MoreGearSetListEntry? selected;
    private bool loggedIn;
    private bool updatingFilter;
    private string emptyText = string.Empty;

    private TextDropDownNode? jobDropDown;
    private TextInputNode? searchNode;
    private TextButtonNode? importButton;
    private TextButtonNode? batchButton;
    private TextButtonNode? selectAllButton;
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
    private Vector2 lastContentSize;
    private Vector2 lastContentStart;

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
        Size                  = new(OmniTheme.Scale(268f), OmniTheme.Scale(480f));
        ContentPadding        = new(OmniTheme.Scale(8f), OmniTheme.Scale(8f));
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
            OnOptionSelected = OnJobSelected,
            OnUncollapsed    = AlignJobDropDownList
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

        selectAllButton = new()
        {
            String    = "全选",
            OnClick   = ToggleSelectAll,
            IsVisible = false
        };
        selectAllButton.AttachNode(this);

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
        MoreGearSetListItemNode.Host = itemHost;
        itemHost.OnRightClick = ShowContextMenu;
        itemHost.OnActivate   = onApply;
        itemHost.OnToggle     = TogglePick;
        itemHost.IsMarked     = batchPicks.Contains;
        itemHost.GetCurrent   = () => selected;
        ResizeContent();
        ApplyData();
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon)
    {
        if (ContentSize != lastContentSize || ContentStartPosition != lastContentStart)
        {
            ResizeContent();
        }

        if (pendingFollow && FollowSelected())
        {
            pendingFollow = false;
        }

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
        if (jobDropDown is not null)
        {
            jobDropDown.OnUncollapsed = null;
            jobDropDown               = null;
        }

        searchNode         = null;
        importButton       = null;
        batchButton        = null;
        selectAllButton    = null;
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
        itemHost.Clear();
        MoreGearSetListItemNode.Host = null;
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

        if (selectAllButton is not null)
        {
            var allPicked = batchMode && IsAllVisiblePicked();
            selectAllButton.IsVisible = batchMode;
            selectAllButton.IsEnabled = batchMode && rows.Count > 0;
            selectAllButton.String    = allPicked ? "取消全选" : "全选";
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
        lastContentSize  = ContentSize;
        lastContentStart = ContentStartPosition;
        var x = ContentStartPosition.X;
        var y = ContentStartPosition.Y;
        var width = ContentSize.X;

        var jobWidth = JobWidth;
        var gap = Gap;
        var rowHeight = RowHeight;
        if (jobDropDown is not null && jobDropDown.IsCollapsed)
        {
            jobDropDown.Position = new(x, y);
            jobDropDown.Size     = new(jobWidth, rowHeight);
        }

        if (searchNode is not null)
        {
            searchNode.Position = new(x + jobWidth + gap, y);
            searchNode.Size     = new(MathF.Max(OmniTheme.Scale(72f), width - jobWidth - gap), rowHeight);
        }

        y += OmniTheme.Scale(32f);
        var toolCount = batchMode ? 3f : 2f;
        var toolWidth = MathF.Max(OmniTheme.Scale(60f), (width - gap * (toolCount - 1f)) / toolCount);
        if (importButton is not null)
        {
            importButton.Position  = new(x, y);
            importButton.Size      = new(toolWidth, rowHeight);
            importButton.IsVisible = !batchMode;
        }

        if (batchButton is not null)
        {
            batchButton.Position = new(batchMode ? x : x + toolWidth + gap, y);
            batchButton.Size     = new(toolWidth, rowHeight);
        }

        if (selectAllButton is not null)
        {
            selectAllButton.Position  = new(x + toolWidth + gap, y);
            selectAllButton.Size      = new(toolWidth, rowHeight);
            selectAllButton.IsVisible = batchMode;
        }

        if (confirmBatchButton is not null)
        {
            confirmBatchButton.Position  = new(x + (toolWidth + gap) * 2f, y);
            confirmBatchButton.Size      = new(toolWidth, rowHeight);
            confirmBatchButton.IsVisible = batchMode;
        }

        y += OmniTheme.Scale(32f);
        var listHeight = MathF.Max(OmniTheme.Scale(60f), ContentSize.Y - OmniTheme.Scale(102f));
        if (emptyNode is not null)
        {
            emptyNode.Position = new(x, y + OmniTheme.Scale(8f));
            emptyNode.Size     = new(width, OmniTheme.Scale(56f));
        }

        if (listNode is not null)
        {
            listNode.Position = new(x + OmniTheme.Scale(2f), y);
            listNode.Size     = new(width - OmniTheme.Scale(4f), listHeight);
        }

        var moveSize = MoveButtonSize;
        var moveX = x + width - moveSize;
        if (upButton is not null)
        {
            upButton.Position = new(moveX, y);
            upButton.Size     = new(moveSize, moveSize);
        }

        if (downButton is not null)
        {
            downButton.Position = new(moveX, y + moveSize + OmniTheme.Scale(2f));
            downButton.Size     = new(moveSize, moveSize);
        }

        var buttonY = y + listHeight + OmniTheme.Scale(8f);
        var buttonWidth = MathF.Max(OmniTheme.Scale(60f), (width - gap * 2f) / 3f);
        PositionButton(saveButton, x, buttonY, buttonWidth);
        PositionButton(updateButton, x + buttonWidth + gap, buttonY, buttonWidth);
        PositionButton(deleteButton, x + (buttonWidth + gap) * 2f, buttonY, buttonWidth);
    }

    private void AlignJobDropDownList()
    {
        if (jobDropDown is null) return;

        jobDropDown.OptionListNode.Width = ContentSize.X;
        jobDropDown.RecalculateScrollParams();
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
        {
            FollowSelected();
        }
    }

    private int VisibleOffset(int index) =>
        index < 0 ? 0 : index - GetListScroll();

    private bool FollowSelected()
    {
        if (batchMode || listNode is null || selected is null) return true;

        var index = rows.FindIndex(row => ReferenceEquals(row.Entry, selected));
        if (index < 0) return true;

        var itemHeight = MoreGearSetListItemNode.ItemHeight + ItemSpacing;
        if (itemHeight <= 0f || listNode.Height < itemHeight) return false;

        var visible   = Math.Max(1, (int)(listNode.Height / itemHeight));
        var maxScroll = Math.Max(0, rows.Count - visible);
        var target    = Math.Clamp(index - followOffset, 0, maxScroll);
        followOffset  = index - target;
        SetListScroll(target);
        return true;
    }

    private int GetListScroll()
    {
        var itemHeight = MoreGearSetListItemNode.ItemHeight + ItemSpacing;
        if (listNode is null || itemHeight <= 0f) return 0;

        return (int)(listNode.ScrollBarNode.ScrollPosition / itemHeight);
    }

    private void SetListScroll(int position)
    {
        if (listNode is null) return;

        var itemHeight = MoreGearSetListItemNode.ItemHeight + ItemSpacing;
        listNode.ScrollBarNode.ScrollPosition = (int)(position * itemHeight);
    }

    private void ToggleBatchMode()
    {
        batchMode = !batchMode;
        itemHost.BatchMode = batchMode;
        if (!batchMode)
        {
            batchPicks.Clear();
            confirmUI?.Close();
        }

        ResizeContent();
        listNode?.FullRebuild();
        ApplyButtons();
    }

    private void ToggleSelectAll()
    {
        if (!batchMode || rows.Count == 0)
        {
            return;
        }

        if (IsAllVisiblePicked())
        {
            foreach (var row in rows)
            {
                batchPicks.Remove(row.Entry);
            }
        }
        else
        {
            foreach (var row in rows)
            {
                batchPicks.Add(row.Entry);
            }
        }

        ApplyButtons();
    }

    private bool IsAllVisiblePicked()
    {
        if (rows.Count == 0)
        {
            return false;
        }

        foreach (var row in rows)
        {
            if (!batchPicks.Contains(row.Entry))
            {
                return false;
            }
        }

        return true;
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
            confirmUI.SetWindowPosition(new(addon->X + OmniTheme.Scale(28f), addon->Y + OmniTheme.Scale(72f)));
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
        itemHost.BatchMode = false;
        selected = null;
        ResizeContent();
        listNode?.FullRebuild();
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
            renameUI.SetWindowPosition(new(addon->X + OmniTheme.Scale(28f), addon->Y + OmniTheme.Scale(72f)));
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
        button.Size     = new(width, ButtonHeight);
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
        Size                  = new(OmniTheme.Scale(280f), OmniTheme.Scale(128f));
        ContentPadding        = new(OmniTheme.Scale(8f), OmniTheme.Scale(8f));
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
            nameInput.Size     = new(width, OmniTheme.Scale(24f));
        }

        if (confirmButton is not null)
        {
            confirmButton.Position = new(x, y + OmniTheme.Scale(32f));
            confirmButton.Size     = new(width, OmniTheme.Scale(28f));
        }
    }
}

internal sealed unsafe class MoreGearSetListItemNode : ListItemNode<MoreGearSetListRow>, IListItemNode
{
    public static float ItemHeight => OmniTheme.Scale(28f);

    internal static MoreGearSetListItemHost? Host;

    private readonly CheckboxNode checkNode;
    private readonly TextNode numberNode;
    private readonly IconImageNode iconNode;
    private readonly TextNode nameNode;
    private readonly TextNode statusNode;
    private bool laidOutForBatch;

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
                    Host?.OnToggle?.Invoke(picked);
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

    private void OnMouseDown(AtkEventListener* thisPtr, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, AtkEventData* atkEventData)
    {
        if (atkEventData == null || ItemData?.Entry is not { } entry || Host is not { } host)
        {
            return;
        }

        if (host.BatchMode)
        {
            if (atkEventData->MouseData.ButtonId == 0)
            {
                host.OnToggle?.Invoke(entry);
            }

            host.ResetClick();
            return;
        }

        if (atkEventData->MouseData.ButtonId == 1)
        {
            host.ResetClick();
            host.OnRightClick?.Invoke(entry);
            return;
        }

        if (atkEventData->MouseData.ButtonId != 0)
        {
            host.ResetClick();
            return;
        }

        var now = Environment.TickCount64;
        if (host.LastLeftEntry == entry && now - host.LastLeftTick < 400)
        {
            host.ResetClick();
            host.OnActivate?.Invoke(entry);
            return;
        }

        host.LastLeftEntry = entry;
        host.LastLeftTick  = now;
    }

    private void OnMouseUp(AtkEventListener* thisPtr, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, AtkEventData* atkEventData)
    {
        if (atkEventData == null || atkEventData->MouseData.ButtonId != 1)
        {
            return;
        }

        Host?.ResetClick();
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        var iconSize = OmniTheme.Scale(24f);
        var checkWidth = OmniTheme.Scale(20f);
        var numberWidth = OmniTheme.Scale(32f);
        var statusWidth = OmniTheme.Scale(36f);
        var batchMode = Host?.BatchMode ?? false;
        var checkPad = batchMode ? checkWidth + OmniTheme.Scale(2f) : 0f;
        laidOutForBatch     = batchMode;
        checkNode.IsVisible = batchMode;
        checkNode.Position  = new(0f, OmniTheme.Scale(2f));
        checkNode.Size      = new(checkWidth, Height - OmniTheme.Scale(4f));
        numberNode.Position = new(checkPad, 0f);
        numberNode.Size     = new(numberWidth, Height);
        iconNode.Position   = new(checkPad + numberWidth + OmniTheme.Scale(2f), (Height - iconSize) / 2f);
        iconNode.Size       = new(iconSize, iconSize);
        var nameX = checkPad + numberWidth + iconSize + OmniTheme.Scale(8f);
        nameNode.Position   = new(nameX, 0f);
        nameNode.Size       = new(MathF.Max(OmniTheme.Scale(20f), Width - nameX - statusWidth - OmniTheme.Scale(6f)), Height);
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
        var host = Host;
        var marked = host is { BatchMode: true } && (host.IsMarked?.Invoke(itemData.Entry) ?? false);
        checkNode.IsChecked  = marked;
        IsSelected           = host is { BatchMode: true }
            ? marked
            : ReferenceEquals(itemData.Entry, host?.GetCurrent?.Invoke());
        OnSizeChanged();
    }

    public override void Update()
    {
        if (ItemData?.Entry is not { } entry)
        {
            return;
        }

        if (Host is not { } host)
        {
            checkNode.IsVisible = false;
            if (laidOutForBatch)
            {
                OnSizeChanged();
            }

            return;
        }

        var batch = host.BatchMode;
        var marked = batch && (host.IsMarked?.Invoke(entry) ?? false);
        checkNode.IsChecked = marked;
        checkNode.IsVisible = batch;
        IsSelected = batch
            ? marked
            : ReferenceEquals(entry, host.GetCurrent?.Invoke());
        if (laidOutForBatch != batch)
        {
            OnSizeChanged();
        }
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
        Size                  = new(OmniTheme.Scale(280f), OmniTheme.Scale(128f));
        ContentPadding        = new(OmniTheme.Scale(8f), OmniTheme.Scale(8f));
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
            messageNode.Size     = new(width, OmniTheme.Scale(36f));
        }

        var buttonWidth = MathF.Max(OmniTheme.Scale(60f), (width - OmniTheme.Scale(6f)) / 2f);
        if (cancelButton is not null)
        {
            cancelButton.Position = new(x, y + OmniTheme.Scale(44f));
            cancelButton.Size     = new(buttonWidth, OmniTheme.Scale(28f));
        }

        if (confirmButton is not null)
        {
            confirmButton.Position = new(x + buttonWidth + OmniTheme.Scale(6f), y + OmniTheme.Scale(44f));
            confirmButton.Size     = new(buttonWidth, OmniTheme.Scale(28f));
        }
    }
}
