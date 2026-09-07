using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node;
using Lumina.Excel.Sheets;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

internal sealed class ChocoboColorPreviewNativeUI : NativeAddon
{
    private const float RowHeight = 24f;
    private static readonly float FeedListHeight = ChocoboFeedOrderListItemNode.ItemHeight * 6f;

    private readonly System.Action<byte> onTargetColorSelected;
    private readonly System.Action onPreview;
    private readonly System.Action onClear;
    private List<ChocoboFruitRequirement> fruitRequirements = [];
    private List<ChocoboFeedOrder> feedOrder = [];
    private string currentColorName = string.Empty;
    private Vector4 currentColor;
    private string targetColorName = string.Empty;
    private Vector4 targetColor;
    private TextNode? currentLabelNode;
    private BackgroundImageNode? currentColorNode;
    private TextNode? currentColorNameNode;
    private TextNode? targetLabelNode;
    private BackgroundImageNode? targetColorNode;
    private LuminaDropDownNode<Stain>? targetColorDropDown;
    private TextButtonNode? previewButton;
    private TextButtonNode? clearButton;
    private TextNode? fruitRequirementsLabelNode;
    private ListNode<ChocoboFruitRequirement, ChocoboFruitRequirementListItemNode>? fruitRequirementsListNode;
    private TextNode? feedOrderLabelNode;
    private TextNode? noFruitNode;
    private ListNode<ChocoboFeedOrder, ChocoboFeedOrderListItemNode>? feedOrderListNode;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ChocoboColorPreviewNativeUI(
        System.Action<byte> onTargetColorSelected,
        System.Action onPreview,
        System.Action onClear)
    {
        InternalName = "OmniChocoboColorPreviewNative";
        Title = OmniLoc.Get("ChocoboColorPreviewTitle");
        Subtitle = string.Empty;
        Size = new(420f, 390f);
        ContentPadding = new(8f, 0f);
        RememberClosePosition = false;
        CreateWindowNode = static () => new WindowNode { ShowCloseButton = false };
        RespectCloseAll = false;
        DisableClose = true;
        this.onTargetColorSelected = onTargetColorSelected;
        this.onPreview = onPreview;
        this.onClear = onClear;
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        currentLabelNode = new()
        {
            String = OmniLoc.Get("Feature.ChocoboColorPreview.CurrentColor"),
            AlignmentType = AlignmentType.Left
        };
        currentLabelNode.AttachNode(this);

        currentColorNode = new();
        currentColorNode.AttachNode(this);

        currentColorNameNode = new()
        {
            AlignmentType = AlignmentType.Left,
            TextFlags = TextFlags.Ellipsis
        };
        currentColorNameNode.AttachNode(this);

        targetLabelNode = new()
        {
            String = OmniLoc.Get("Feature.ChocoboColorPreview.TargetColor"),
            AlignmentType = AlignmentType.Left
        };
        targetLabelNode.AttachNode(this);

        targetColorNode = new();
        targetColorNode.AttachNode(this);

        targetColorDropDown = new()
        {
            LabelFunction = stain => stain.Name.ExtractText(),
            FilterFunction = stain => stain.RowId is > 0 and <= 85,
            MaxListOptions = 8,
            OnOptionSelected = stain => onTargetColorSelected((byte)stain.RowId)
        };
        targetColorDropDown.AttachNode(this);

        previewButton = new()
        {
            String = OmniLoc.Get("Feature.ChocoboColorPreview.Preview"),
            OnClick = onPreview
        };
        previewButton.AttachNode(this);

        clearButton = new()
        {
            String = OmniLoc.Get("Feature.ChocoboColorPreview.Clear"),
            OnClick = onClear
        };
        clearButton.AttachNode(this);

        fruitRequirementsLabelNode = new()
        {
            String = OmniLoc.Get("Feature.ChocoboColorPreview.FruitRequirements"),
            AlignmentType = AlignmentType.Left
        };
        fruitRequirementsLabelNode.AttachNode(this);

        fruitRequirementsListNode = new()
        {
            ItemSpacing = 0f,
            OptionsList = fruitRequirements
        };
        fruitRequirementsListNode.AttachNode(this);

        feedOrderLabelNode = new()
        {
            String = OmniLoc.Get("Feature.ChocoboColorPreview.FeedOrder"),
            AlignmentType = AlignmentType.Left
        };
        feedOrderLabelNode.AttachNode(this);

        noFruitNode = new()
        {
            String = OmniLoc.Get("Feature.ChocoboColorPreview.NoFruit"),
            AlignmentType = AlignmentType.Left
        };
        noFruitNode.AttachNode(this);

        feedOrderListNode = new()
        {
            ItemSpacing = 0f,
            OptionsList = feedOrder
        };
        feedOrderListNode.AttachNode(this);

        ResizeContent();
        ApplyData();
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon)
    {
        feedOrderListNode?.Update();
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        currentLabelNode = null;
        currentColorNode = null;
        currentColorNameNode = null;
        targetLabelNode = null;
        targetColorNode = null;
        targetColorDropDown = null;
        previewButton = null;
        clearButton = null;
        fruitRequirementsLabelNode = null;
        fruitRequirementsListNode = null;
        feedOrderLabelNode = null;
        noFruitNode = null;
        feedOrderListNode = null;
    }

    public void UpdateData(
        string currentColorName,
        Vector4 currentColor,
        string targetColorName,
        Vector4 targetColor,
        List<ChocoboFruitRequirement> fruitRequirements,
        List<ChocoboFeedOrder> feedOrder)
    {
        this.currentColorName = currentColorName;
        this.currentColor = currentColor;
        this.targetColorName = targetColorName;
        this.targetColor = targetColor;
        this.fruitRequirements = fruitRequirements;
        this.feedOrder = feedOrder;
        ApplyData();
    }

    private void ApplyData()
    {
        if (currentColorNode is not null)
        {
            currentColorNode.Color = currentColor;
        }

        if (currentColorNameNode is not null)
        {
            currentColorNameNode.String = currentColorName;
        }

        if (targetColorNode is not null)
        {
            targetColorNode.Color = targetColor;
        }

        if (targetColorDropDown is not null)
        {
            targetColorDropDown.LabelNode.String = targetColorName;
        }

        if (fruitRequirementsListNode is not null)
        {
            fruitRequirementsListNode.OptionsList = fruitRequirements;
            fruitRequirementsListNode.IsVisible = fruitRequirements.Count > 0;
        }

        if (noFruitNode is not null)
        {
            noFruitNode.IsVisible = fruitRequirements.Count == 0;
        }

        if (feedOrderListNode is not null)
        {
            feedOrderListNode.OptionsList = feedOrder;
            feedOrderListNode.IsVisible = feedOrder.Count > 0;
        }
    }

    private void ResizeContent()
    {
        var contentX = ContentStartPosition.X;
        var contentY = ContentStartPosition.Y;
        var contentWidth = ContentSize.X;
        const float labelWidth = 66f;
        const float colorSize = 18f;
        const float gap = 4f;
        const float buttonWidth = 52f;

        if (currentLabelNode is not null)
        {
            currentLabelNode.Position = new(contentX, contentY);
            currentLabelNode.Size = new(labelWidth, RowHeight);
        }

        if (currentColorNode is not null)
        {
            currentColorNode.Position = new(contentX + labelWidth, contentY + 3f);
            currentColorNode.Size = new(colorSize, colorSize);
        }

        if (currentColorNameNode is not null)
        {
            currentColorNameNode.Position = new(contentX + labelWidth + colorSize + gap, contentY);
            currentColorNameNode.Size = new(contentWidth - labelWidth - colorSize - gap, RowHeight);
        }

        var targetY = contentY + RowHeight;
        var dropdownX = contentX + labelWidth + colorSize + gap;
        var dropdownWidth = MathF.Max(100f, contentWidth - labelWidth - colorSize - buttonWidth * 2f - gap * 4f);
        var previewX = dropdownX + dropdownWidth + gap;
        var clearX = previewX + buttonWidth + gap;
        if (targetLabelNode is not null)
        {
            targetLabelNode.Position = new(contentX, targetY);
            targetLabelNode.Size = new(labelWidth, RowHeight);
        }

        if (targetColorDropDown is not null)
        {
            targetColorDropDown.Position = new(dropdownX, targetY);
            targetColorDropDown.Size = new(dropdownWidth, RowHeight);
        }

        if (targetColorNode is not null)
        {
            targetColorNode.Position = new(contentX + labelWidth, targetY + 3f);
            targetColorNode.Size = new(colorSize, colorSize);
        }

        if (previewButton is not null)
        {
            previewButton.Position = new(previewX, targetY);
            previewButton.Size = new(buttonWidth, RowHeight);
        }

        if (clearButton is not null)
        {
            clearButton.Position = new(clearX, targetY);
            clearButton.Size = new(buttonWidth, RowHeight);
        }

        var fruitRequirementsY = targetY + RowHeight + 8f;
        if (fruitRequirementsLabelNode is not null)
        {
            fruitRequirementsLabelNode.Position = new(contentX, fruitRequirementsY);
            fruitRequirementsLabelNode.Size = new(contentWidth, RowHeight);
        }

        if (fruitRequirementsListNode is not null)
        {
            fruitRequirementsListNode.Position = new(contentX, fruitRequirementsY + RowHeight);
            fruitRequirementsListNode.Size = new(contentWidth, ChocoboFruitRequirementListItemNode.ItemHeight * 3f);
        }

        var feedOrderY = fruitRequirementsY + RowHeight * 4f + 4f;
        if (feedOrderLabelNode is not null)
        {
            feedOrderLabelNode.Position = new(contentX, feedOrderY);
            feedOrderLabelNode.Size = new(contentWidth, RowHeight);
        }

        if (noFruitNode is not null)
        {
            noFruitNode.Position = new(contentX + 8f, feedOrderY + RowHeight);
            noFruitNode.Size = new(contentWidth - 8f, RowHeight);
        }

        if (feedOrderListNode is not null)
        {
            feedOrderListNode.Position = new(contentX, feedOrderY + RowHeight);
            feedOrderListNode.Size = new(
                contentWidth,
                MathF.Min(FeedListHeight, MathF.Max(ChocoboFeedOrderListItemNode.ItemHeight, ContentSize.Y - (feedOrderY + RowHeight - contentY))));
        }
    }
}

internal sealed class ChocoboFruitRequirementListItemNode : ListItemNode<ChocoboFruitRequirement>, IListItemNode
{
    public static float ItemHeight => 24f;

    private readonly IconImageNode iconNode;
    private readonly TextNode textNode;

    public ChocoboFruitRequirementListItemNode()
    {
        iconNode = new()
        {
            FitTexture = true,
            Size = new(18f, 18f),
            Position = new(2f, 3f)
        };
        iconNode.AttachNode(this);

        textNode = new()
        {
            AlignmentType = AlignmentType.Left,
            TextFlags = TextFlags.Ellipsis
        };
        textNode.AttachNode(this);
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        iconNode.Position = new(2f, (Height - 18f) / 2f);
        textNode.Position = new(26f, 0f);
        textNode.Size = new(Width - 26f, Height);
    }

    protected override void SetNodeData(ChocoboFruitRequirement itemData)
    {
        iconNode.IconId = itemData.IconID;
        iconNode.IsVisible = itemData.IconID > 0;
        textNode.String = $"{itemData.Name} ×{itemData.Count}";
        OnSizeChanged();
    }
}

internal sealed class ChocoboFeedOrderListItemNode : ListItemNode<ChocoboFeedOrder>, IListItemNode
{
    public static float ItemHeight => 24f;

    private readonly IconImageNode iconNode;
    private readonly TextNode textNode;

    public ChocoboFeedOrderListItemNode()
    {
        iconNode = new()
        {
            FitTexture = true,
            Size = new(18f, 18f),
            Position = new(4f, 3f)
        };
        iconNode.AttachNode(this);

        textNode = new()
        {
            AlignmentType = AlignmentType.Left,
            TextFlags = TextFlags.Ellipsis
        };
        textNode.AttachNode(this);
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        iconNode.Position = new(4f, (Height - 18f) / 2f);
        textNode.Position = new(30f, 0f);
        textNode.Size = new(Width - 30f, Height);
    }

    protected override void SetNodeData(ChocoboFeedOrder itemData)
    {
        iconNode.IconId = itemData.IconID;
        iconNode.IsVisible = itemData.IconID > 0;
        textNode.String = $"{itemData.Index}. {itemData.Name}";
        OnSizeChanged();
    }
}
