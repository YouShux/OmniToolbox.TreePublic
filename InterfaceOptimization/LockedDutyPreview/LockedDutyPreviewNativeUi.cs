using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal readonly record struct LockedDutyPreviewRow(
    uint ContentFinderConditionId,
    string Name,
    bool IsExcluded);

internal sealed class LockedDutyPreviewNativeUi : NativeAddon
{
    private const float WindowWidth = 400f;
    private const float WindowHeight = 400f;
    private const float ItemSpacing = 2f;

    private List<LockedDutyPreviewRow> rows = [];
    private TextNode? summaryNode;
    private ListNode<LockedDutyPreviewRow, LockedDutyPreviewListItemNode>? listNode;
    private TextButtonNode? lockedTabButton;
    private TextButtonNode? excludedTabButton;
    private bool showExcludedView;
    private string summaryText = string.Empty;
    private readonly Action<bool> onViewChanged;
    private readonly Action<LockedDutyPreviewRow> onExclude;
    private readonly Action<LockedDutyPreviewRow> onRestore;
    private readonly Action<LockedDutyPreviewRow> onWiki;
    private readonly Action<string> onCopyName;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public LockedDutyPreviewNativeUi(
        Action<bool> onViewChanged,
        Action<LockedDutyPreviewRow> onExclude,
        Action<LockedDutyPreviewRow> onRestore,
        Action<LockedDutyPreviewRow> onWiki,
        Action<string> onCopyName)
    {
        InternalName = "OmniLockedDutiesNative";
        Title = OmniLoc.Get("Feature.LockedDutyPreview.Title");
        Subtitle = string.Empty;
        Size = new(WindowWidth, WindowHeight);
        ContentPadding = new(8f, 0f);
        RememberClosePosition = false;
        CreateWindowNode = static () => new WindowNode { ShowCloseButton = false };
        RespectCloseAll = false;
        DisableClose = true;
        this.onViewChanged = onViewChanged;
        this.onExclude = onExclude;
        this.onRestore = onRestore;
        this.onWiki = onWiki;
        this.onCopyName = onCopyName;
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        LockedDutyPreviewListItemNode.SetCallbacks(onExclude, onRestore, onWiki);
        summaryNode = new()
        {
            Position = ContentStartPosition,
            Size = new(ContentSize.X, 24f),
            AlignmentType = AlignmentType.Left
        };
        summaryNode.AttachNode(this);

        listNode = new()
        {
            ItemSpacing = ItemSpacing,
            Position = new(ContentStartPosition.X + 2f, ContentStartPosition.Y + 30f),
            Size = new(ContentSize.X - 4f, ContentSize.Y - 75f),
            OptionsList = rows,
            OnItemSelected = item => onCopyName(item.Name)
        };
        listNode.AttachNode(this);

        lockedTabButton = new()
        {
            String = OmniLoc.Get("Feature.LockedDutyPreview.Tab.Locked"),
            OnClick = () => SetView(false)
        };
        lockedTabButton.AttachNode(this);

        excludedTabButton = new()
        {
            String = OmniLoc.Get("Feature.LockedDutyPreview.Tab.Excluded"),
            OnClick = () => SetView(true)
        };
        excludedTabButton.AttachNode(this);

        ResizeContent();
        ApplyData();
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon)
    {
        ResizeContent();
        listNode?.Update();
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        LockedDutyPreviewListItemNode.ClearCallbacks();
        summaryNode = null;
        listNode = null;
        lockedTabButton = null;
        excludedTabButton = null;
    }

    public void UpdateData(string summary, List<LockedDutyPreviewRow> rows, bool showExcluded)
    {
        summaryText = summary;
        showExcludedView = showExcluded;
        this.rows = rows;
        ApplyData();
    }

    public void ClearCallbacks()
    {
        LockedDutyPreviewListItemNode.ClearCallbacks();
    }

    private void SetView(bool showExcluded)
    {
        if (showExcludedView == showExcluded)
        {
            return;
        }

        showExcludedView = showExcluded;
        onViewChanged(showExcluded);
        ApplyData();
    }

    private void ApplyData()
    {
        if (summaryNode is not null)
        {
            summaryNode.String = summaryText;
        }

        if (listNode is not null)
        {
            listNode.OptionsList = rows;
        }

        if (lockedTabButton is not null)
        {
            lockedTabButton.IsChecked = !showExcludedView;
        }

        if (excludedTabButton is not null)
        {
            excludedTabButton.IsChecked = showExcludedView;
        }
    }

    private void ResizeContent()
    {
        if (summaryNode is not null)
        {
            summaryNode.Size = new(ContentSize.X, 24f);
        }

        if (listNode is not null)
        {
            listNode.Position = new(ContentStartPosition.X + 2f, ContentStartPosition.Y + 30f);
            listNode.Size = new(ContentSize.X - 4f, MathF.Max(60f, ContentSize.Y - 75f));
        }

        var bottomY = ContentStartPosition.Y + ContentSize.Y - 32f;
        var tabGap = 4f;
        var tabWidth = MathF.Max(80f, (ContentSize.X - tabGap) * 0.5f);
        if (lockedTabButton is not null)
        {
            lockedTabButton.Position = new(ContentStartPosition.X, bottomY);
            lockedTabButton.Size = new(tabWidth, 28f);
        }

        if (excludedTabButton is not null)
        {
            excludedTabButton.Position = new(ContentStartPosition.X + tabWidth + tabGap, bottomY);
            excludedTabButton.Size = new(tabWidth, 28f);
        }
    }
}

internal sealed class LockedDutyPreviewListItemNode : ListItemNode<LockedDutyPreviewRow>, IListItemNode
{
    public static float ItemHeight => 28f;

    private static Action<LockedDutyPreviewRow>? onExclude;
    private static Action<LockedDutyPreviewRow>? onRestore;
    private static Action<LockedDutyPreviewRow>? onWiki;

    private readonly TextNode nameNode;
    private readonly TextButtonNode primaryButton;
    private readonly TextButtonNode wikiButton;
    private LockedDutyPreviewRow item;

    public LockedDutyPreviewListItemNode()
    {
        nameNode = new()
        {
            AlignmentType = AlignmentType.Left,
            TextFlags = TextFlags.Ellipsis,
            ShowClickableCursor = true
        };
        nameNode.AttachNode(this);

        primaryButton = new()
        {
            OnClick = () =>
            {
                if (item.IsExcluded)
                {
                    onRestore?.Invoke(item);
                }
                else
                {
                    onExclude?.Invoke(item);
                }
            }
        };
        primaryButton.AttachNode(this);

        wikiButton = new()
        {
            String = OmniLoc.Get("Feature.LockedDutyPreview.Wiki"),
            OnClick = () => onWiki?.Invoke(item)
        };
        wikiButton.AttachNode(this);
    }

    public static void SetCallbacks(
        Action<LockedDutyPreviewRow> exclude,
        Action<LockedDutyPreviewRow> restore,
        Action<LockedDutyPreviewRow> wiki)
    {
        onExclude = exclude;
        onRestore = restore;
        onWiki = wiki;
    }

    public static void ClearCallbacks()
    {
        onExclude = null;
        onRestore = null;
        onWiki = null;
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        const float gap = 4f;
        var primaryWidth = item.IsExcluded ? 72f : 52f;
        var wikiWidth = item.IsExcluded ? 0f : 52f;
        var rightWidth = primaryWidth + (item.IsExcluded ? 0f : gap + wikiWidth);
        nameNode.Position = new(2f, 4f);
        nameNode.Size = new(MathF.Max(40f, Width - rightWidth - gap - 4f), Height - 2f);
        primaryButton.Position = new(Width - rightWidth, 0f);
        primaryButton.Size = new(primaryWidth, 26f);
        wikiButton.Position = new(Width - wikiWidth, 0f);
        wikiButton.Size = new(wikiWidth, 26f);
    }

    protected override void SetNodeData(LockedDutyPreviewRow itemData)
    {
        item = itemData;
        nameNode.String = itemData.Name;
        primaryButton.String = OmniLoc.Get(itemData.IsExcluded
            ? "Feature.LockedDutyPreview.Restore"
            : "Feature.LockedDutyPreview.Exclude");
        wikiButton.IsVisible = !itemData.IsExcluded;
        OnSizeChanged();
    }
}
