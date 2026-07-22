using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using OmniToolbox.Config;
using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal sealed class AutoGcSealNativeUi : NativeAddon
{
    private readonly AutoGcSealConfig config;
    private readonly Action saveConfig;
    private CheckboxNode? skipHqNode;
    private CheckboxNode? skipMateriaNode;
    private CheckboxNode? excludeProtectedNode;
    private TextNode? thresholdLabelNode;
    private NumericInputNode? thresholdInputNode;
    private TextNode? reserveLabelNode;
    private NumericInputNode? reserveInputNode;
    private TextNode? itemIdLabelNode;
    private NumericInputNode? itemIdInputNode;
    private TextNode? itemNameNode;
    private TextNode? excludeSummaryNode;
    private TextNode? statusNode;
    private TextButtonNode? startButton;
    private TextButtonNode? openSupplyButton;
    private TextButtonNode? openExchangeButton;
    private Action? onStart;
    private Action? onStop;
    private Action? onOpenSupply;
    private Action? onOpenExchange;
    private AutoGcSealWindowState state;
    private bool updating;
    private bool nodesReady;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AutoGcSealNativeUi(AutoGcSealConfig config, Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        InternalName = "OmniGcSealDeliveryNative";
        Title = OmniLoc.Get("Feature.AutoGcSeal.Title");
        Subtitle = string.Empty;
        Size = new(520f, 225f);
        ContentPadding = new(8f, 0f);
        CreateWindowNode = static () => new WindowNode { ShowCloseButton = false };
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        ClearNodeReferences();
        skipHqNode = CreateCheckbox("Feature.AutoGcSeal.SkipHq", value =>
        {
            config.SkipHqItems = value;
            saveConfig();
        });
        skipMateriaNode = CreateCheckbox("Feature.AutoGcSeal.SkipMateria", value =>
        {
            config.SkipMateriaItems = value;
            saveConfig();
        });
        excludeProtectedNode = CreateCheckbox("Feature.AutoGcSeal.ExcludeProtected", value =>
        {
            config.SkipExcludedItems = value;
            saveConfig();
        });

        thresholdLabelNode = CreateLabel(OmniLoc.Get("Feature.AutoGcSeal.Threshold"));
        thresholdInputNode = CreateNumericInput(value =>
        {
            config.ExchangeThreshold = (uint)Math.Max(0, value);
            saveConfig();
        });
        reserveLabelNode = CreateLabel(OmniLoc.Get("Feature.AutoGcSeal.Reserve"));
        reserveInputNode = CreateNumericInput(value =>
        {
            config.ReservedSeals = (uint)Math.Max(0, value);
            saveConfig();
        });
        itemIdLabelNode = CreateLabel(OmniLoc.Get("Feature.AutoGcSeal.ExchangeItemId"));
        itemIdInputNode = CreateNumericInput(value =>
        {
            config.ExchangeItemId = (uint)Math.Max(0, value);
            saveConfig();
        });
        itemNameNode = CreateLabel(string.Empty);
        excludeSummaryNode = CreateLabel(string.Empty);
        statusNode = CreateLabel(string.Empty);

        startButton = new()
        {
            OnClick = () =>
            {
                if (state.IsRunning)
                {
                    onStop?.Invoke();
                }
                else
                {
                    onStart?.Invoke();
                }
            }
        };
        startButton.AttachNode(this);

        openSupplyButton = new()
        {
            String = OmniLoc.Get("Feature.AutoGcSeal.OpenSupply"),
            OnClick = () => onOpenSupply?.Invoke()
        };
        openSupplyButton.AttachNode(this);

        openExchangeButton = new()
        {
            String = OmniLoc.Get("Feature.AutoGcSeal.OpenExchange"),
            OnClick = () => onOpenExchange?.Invoke()
        };
        openExchangeButton.AttachNode(this);

        nodesReady = true;
        ResizeContent();
        ApplyState();
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon)
    {
        ResizeContent();
        ApplyState();
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon) => ClearNodeReferences();

    public new void Close()
    {
        ClearNodeReferences();
        base.Close();
    }

    public void UpdateState(
        AutoGcSealWindowState state,
        Action start,
        Action stop,
        Action openSupply,
        Action openExchange)
    {
        this.state = state;
        onStart = start;
        onStop = stop;
        onOpenSupply = openSupply;
        onOpenExchange = openExchange;
        ApplyState();
    }

    private CheckboxNode CreateCheckbox(string labelKey, Action<bool> onClick)
    {
        var node = new CheckboxNode
        {
            String = OmniLoc.Get(labelKey),
            Size = new(150f, 24f),
            OnClick = value =>
            {
                if (!updating)
                {
                    onClick(value);
                }
            }
        };
        node.AttachNode(this);
        return node;
    }

    private TextNode CreateLabel(string text)
    {
        var node = new TextNode
        {
            String = text,
            AlignmentType = AlignmentType.Left
        };
        node.AttachNode(this);
        return node;
    }

    private NumericInputNode CreateNumericInput(Action<int> onValueUpdate)
    {
        var node = new NumericInputNode
        {
            Min = 0,
            Max = int.MaxValue,
            Step = 1,
            Size = new(110f, 28f),
            OnValueUpdate = value =>
            {
                if (!updating)
                {
                    onValueUpdate(value);
                }
            }
        };
        node.AttachNode(this);
        return node;
    }

    private void ApplyState()
    {
        if (!nodesReady)
        {
            return;
        }

        updating = true;
        try
        {
            if (skipHqNode is not null)
            {
                skipHqNode.IsChecked = config.SkipHqItems;
            }

            if (skipMateriaNode is not null)
            {
                skipMateriaNode.IsChecked = config.SkipMateriaItems;
            }

            if (excludeProtectedNode is not null)
            {
                excludeProtectedNode.IsChecked = config.SkipExcludedItems;
            }

            if (thresholdInputNode is not null)
            {
                thresholdInputNode.Value = (int)Math.Min(int.MaxValue, state.Threshold);
            }

            if (reserveInputNode is not null)
            {
                reserveInputNode.Value = (int)Math.Min(int.MaxValue, state.ReservedSeals);
            }

            if (itemIdInputNode is not null)
            {
                itemIdInputNode.Value = (int)Math.Min(int.MaxValue, state.ExchangeItemId);
            }

            if (itemNameNode is not null)
            {
                itemNameNode.String = string.IsNullOrWhiteSpace(state.ExchangeItemName)
                    ? OmniLoc.Get("Feature.AutoGcSeal.InvalidItem")
                    : state.ExchangeItemName;
            }

            if (excludeSummaryNode is not null)
            {
                excludeSummaryNode.String = string.Format(
                    OmniLoc.Get("Feature.AutoGcSeal.CustomExcludeSummary"),
                    state.CustomExcludeCount);
            }

            if (statusNode is not null)
            {
                statusNode.String = string.Format(
                    OmniLoc.Get("Feature.AutoGcSeal.Status"),
                    state.CurrentSeals.ToString("N0"),
                    state.Threshold.ToString("N0"),
                    state.ReservedSeals.ToString("N0"));
            }

            if (startButton is not null)
            {
                startButton.String = OmniLoc.Get(state.IsRunning
                    ? "Feature.AutoGcSeal.Stop"
                    : "Feature.AutoGcSeal.Start");
            }
        }
        finally
        {
            updating = false;
        }
    }

    private void ResizeContent()
    {
        var x = ContentStartPosition.X;
        var y = ContentStartPosition.Y + 3f;
        if (skipHqNode is not null)
        {
            skipHqNode.Position = new(x, y);
            skipHqNode.Size = new(88f, 24f);
        }

        if (skipMateriaNode is not null)
        {
            skipMateriaNode.Position = new(x + 92f, y);
            skipMateriaNode.Size = new(132f, 24f);
        }

        if (excludeProtectedNode is not null)
        {
            excludeProtectedNode.Position = new(x + 228f, y);
            excludeProtectedNode.Size = new(380f, 24f);
        }

        y += 34f;
        LayoutLabelInput(thresholdLabelNode, thresholdInputNode, x, y, 86f);
        LayoutLabelInput(reserveLabelNode, reserveInputNode, x + 215f, y, 70f);

        y += 34f;
        LayoutLabelInput(itemIdLabelNode, itemIdInputNode, x, y, 78f);
        if (itemNameNode is not null)
        {
            itemNameNode.Position = new(x + 198f, y + 4f);
            itemNameNode.Size = new(110f, 22f);
        }

        if (excludeSummaryNode is not null)
        {
            excludeSummaryNode.Position = new(x + 315f, y + 4f);
            excludeSummaryNode.Size = new(MathF.Max(120f, ContentSize.X - 315f), 22f);
        }

        y += 34f;
        if (statusNode is not null)
        {
            statusNode.Position = new(x, y);
            statusNode.Size = new(ContentSize.X, 22f);
        }

        y = MathF.Min(y + 30f, ContentStartPosition.Y + ContentSize.Y - 32f);
        if (startButton is not null)
        {
            startButton.Position = new(x, y);
            startButton.Size = new(48f, 28f);
        }

        if (openSupplyButton is not null)
        {
            openSupplyButton.Position = new(x + 58f, y);
            openSupplyButton.Size = new(104f, 28f);
        }

        if (openExchangeButton is not null)
        {
            openExchangeButton.Position = new(x + 170f, y);
            openExchangeButton.Size = new(112f, 28f);
        }
    }

    private void ClearNodeReferences()
    {
        nodesReady = false;
        skipHqNode = null;
        skipMateriaNode = null;
        excludeProtectedNode = null;
        thresholdLabelNode = null;
        thresholdInputNode = null;
        reserveLabelNode = null;
        reserveInputNode = null;
        itemIdLabelNode = null;
        itemIdInputNode = null;
        itemNameNode = null;
        excludeSummaryNode = null;
        statusNode = null;
        startButton = null;
        openSupplyButton = null;
        openExchangeButton = null;
    }

    private static void LayoutLabelInput(
        TextNode? label,
        NumericInputNode? input,
        float x,
        float y,
        float labelWidth)
    {
        if (label is not null)
        {
            label.Position = new(x, y + 5f);
            label.Size = new(labelWidth, 22f);
        }

        if (input is not null)
        {
            input.Position = new(x + labelWidth + 8f, y);
            input.Size = new(110f, 28f);
        }
    }
}
