using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal sealed class ArmoireRecordNativeUi : NativeAddon
{
    private const float WindowWidth = 380f;
    private const float WindowHeight = 434f;
    private const float ItemSpacing = 2f;
    private const float ButtonGap = 10f;
    private const float ButtonOffset = 12f;
    private const int VisibleItemRows = 5;

    private readonly List<ArmoireRecordItem> items = [];
    private TextNode? summaryNode;
    private TextNode? scopeNode;
    private TextNode? emptyNode;
    private ListNode<ArmoireRecordItem, ArmoireRecordListItemNode>? listNode;
    private TextButtonNode? checkButton;
    private TextButtonNode? retrieveButton;
    private TextButtonNode? storeButton;
    private string summaryText = string.Empty;
    private string scopeText = string.Empty;
    private bool isStoring;
    private bool isRetrieving;
    private bool canRetrieve;
    private Func<bool>? onCheck;
    private Action? onRetrieve;
    private Action? onStore;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ArmoireRecordNativeUi()
    {
        InternalName = "OmniArmoireRecord";
        Title = OmniLoc.Get("Feature.ArmoireRecord.Title");
        Subtitle = string.Empty;
        Size = new(WindowWidth, WindowHeight);
        ContentPadding = new(8f, 0f);
        RememberClosePosition = false;
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        if (WindowNode is WindowNode windowNode)
        {
            windowNode.ShowCloseButton = false;
        }

        addon->SetPosition((short)(addon->X + 9f), (short)addon->Y);

        summaryNode = CreateTextNode();
        scopeNode = CreateTextNode();
        emptyNode = new()
        {
            AlignmentType = AlignmentType.TopLeft,
            TextFlags = TextFlags.MultiLine,
            IsVisible = false
        };
        emptyNode.AttachNode(this);

        listNode = new()
        {
            ItemSpacing = ItemSpacing,
            OptionsList = items
        };
        listNode.AttachNode(this);

        checkButton = new()
        {
            String = OmniLoc.Get("Feature.ArmoireRecord.Check"),
            OnClick = () =>
            {
                onCheck?.Invoke();
            }
        };
        checkButton.AttachNode(this);

        retrieveButton = new()
        {
            String = OmniLoc.Get("Feature.ArmoireRecord.Retrieve"),
            OnClick = () => onRetrieve?.Invoke()
        };
        retrieveButton.AttachNode(this);

        storeButton = new()
        {
            String = OmniLoc.Get("Feature.ArmoireRecord.Store"),
            OnClick = () => onStore?.Invoke()
        };
        storeButton.AttachNode(this);

        ResizeContent();
        ApplyData();
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        summaryNode = null;
        scopeNode = null;
        emptyNode = null;
        listNode = null;
        checkButton = null;
        retrieveButton = null;
        storeButton = null;
    }

    public void UpdateData(
        string summary,
        string scope,
        IReadOnlyList<ArmoireRecordItem> newItems,
        bool storing,
        bool retrieving,
        bool retrieveAvailable,
        Func<bool> check,
        Action retrieve,
        Action store)
    {
        summaryText = summary;
        scopeText = scope;
        isStoring = storing;
        isRetrieving = retrieving;
        canRetrieve = retrieveAvailable;
        onCheck = check;
        onRetrieve = retrieve;
        onStore = store;
        items.Clear();
        items.AddRange(newItems);
        ApplyData();
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

    private void ApplyData()
    {
        if (summaryNode is not null)
        {
            summaryNode.String = summaryText;
        }

        if (scopeNode is not null)
        {
            scopeNode.String = scopeText;
        }

        if (listNode is not null)
        {
            listNode.OptionsList = items;
        }

        if (emptyNode is not null)
        {
            emptyNode.IsVisible = items.Count == 0;
            emptyNode.String = emptyNode.IsVisible
                ? OmniLoc.Get("Feature.ArmoireRecord.Empty")
                : string.Empty;
        }

        if (checkButton is not null)
        {
            checkButton.IsEnabled = !isStoring && !isRetrieving;
        }

        if (retrieveButton is not null)
        {
            retrieveButton.IsEnabled =
                !isStoring &&
                !isRetrieving &&
                canRetrieve &&
                items.Exists(static item => item.CanRetrieve);
            retrieveButton.String = OmniLoc.Get(isRetrieving
                ? "Feature.ArmoireRecord.Retrieving"
                : "Feature.ArmoireRecord.Retrieve");
        }

        if (storeButton is not null)
        {
            storeButton.IsEnabled =
                !isStoring &&
                !isRetrieving &&
                items.Exists(static item => item.CanDirectStore);
            storeButton.String = OmniLoc.Get(isStoring
                ? "Feature.ArmoireRecord.Storing"
                : "Feature.ArmoireRecord.Store");
        }
    }

    private void ResizeContent()
    {
        var x = ContentStartPosition.X;
        var y = ContentStartPosition.Y + 4f;

        if (summaryNode is not null)
        {
            summaryNode.Position = new(x, y);
            summaryNode.Size = new(ContentSize.X, 22f);
        }

        y += 23f;
        if (scopeNode is not null)
        {
            scopeNode.Position = new(x, y);
            scopeNode.Size = new(ContentSize.X, 22f);
        }

        y += 28f;
        var listHeight = (ArmoireRecordListItemNode.ItemHeight + ItemSpacing) * VisibleItemRows;
        if (emptyNode is not null)
        {
            emptyNode.Position = new(x, y + 8f);
            emptyNode.Size = new(ContentSize.X, 56f);
        }

        if (listNode is not null)
        {
            listNode.Position = new(x + 2f, y);
            listNode.Size = new(ContentSize.X - 4f, listHeight);
        }

        var buttonY = y + listHeight + ButtonOffset;
        var buttonWidth = MathF.Max(80f, (ContentSize.X - ButtonGap * 2f) / 3f);
        if (checkButton is not null)
        {
            checkButton.Position = new(x, buttonY);
            checkButton.Size = new(buttonWidth, 28f);
        }

        if (retrieveButton is not null)
        {
            retrieveButton.Position = new(x + buttonWidth + ButtonGap, buttonY);
            retrieveButton.Size = new(buttonWidth, 28f);
        }

        if (storeButton is not null)
        {
            storeButton.Position = new(x + (buttonWidth + ButtonGap) * 2f, buttonY);
            storeButton.Size = new(buttonWidth, 28f);
        }
    }
}

internal sealed class ArmoireRecordListItemNode : ListItemNode<ArmoireRecordItem>, IListItemNode
{
    public static float ItemHeight => 54f;

    private readonly IconImageNode iconNode;
    private readonly TextNode nameNode;
    private readonly TextNode locationNode;
    private readonly TextNode statusNode;

    public ArmoireRecordListItemNode()
    {
        iconNode = new()
        {
            FitTexture = true,
            IconId = 60072
        };
        iconNode.AttachNode(this);

        nameNode = new()
        {
            TextFlags = TextFlags.Ellipsis | TextFlags.Emboss,
            FontSize = 14,
            LineSpacing = 14,
            AlignmentType = AlignmentType.Left,
            TextColor = ColorHelper.GetColor(8),
            TextOutlineColor = ColorHelper.GetColor(7)
        };
        nameNode.AttachNode(this);

        locationNode = new()
        {
            TextFlags = TextFlags.Ellipsis,
            FontSize = 12,
            LineSpacing = 12,
            AlignmentType = AlignmentType.Left,
            TextColor = ColorHelper.GetColor(3)
        };
        locationNode.AttachNode(this);

        statusNode = new()
        {
            TextFlags = TextFlags.Ellipsis | TextFlags.Emboss,
            FontSize = 12,
            LineSpacing = 12,
            AlignmentType = AlignmentType.Right,
            TextColor = ColorHelper.GetColor(3),
            TextOutlineColor = ColorHelper.GetColor(7)
        };
        statusNode.AttachNode(this);
        DisableInteractions();
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();

        iconNode.Position = new(2f, 5f);
        iconNode.Size = new(42f, 42f);

        const float textX = 50f;
        const float statusWidth = 92f;
        nameNode.Position = new(textX, 4f);
        nameNode.Size = new(MathF.Max(40f, Width - textX - statusWidth - 8f), 22f);
        statusNode.Position = new(MathF.Max(textX, Width - statusWidth), 4f);
        statusNode.Size = new(statusWidth, 22f);
        locationNode.Position = new(textX, 28f);
        locationNode.Size = new(MathF.Max(40f, Width - textX - 6f), 20f);
    }

    protected override void SetNodeData(ArmoireRecordItem itemData)
    {
        iconNode.IconId = itemData.IconId;
        nameNode.String = $"{itemData.Name} ×{itemData.Quantity}";
        locationNode.String = itemData.LocationsText;
        statusNode.String = OmniLoc.Get(itemData.CanDirectStore
            ? "Feature.ArmoireRecord.Status.DirectStore"
            : "Feature.ArmoireRecord.Status.MoveToBackpack");
        statusNode.TextColor = ColorHelper.GetColor(itemData.CanDirectStore ? 45u : 3u);
        OnSizeChanged();
    }
}
