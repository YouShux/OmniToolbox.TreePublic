using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

internal sealed class BetterGlamourActionsNativeUI : NativeAddon
{
    private readonly Action onTryOnAll;
    private readonly Action onSave;
    private readonly Action onExport;
    private readonly Action onManager;
    private readonly Action onClear;
    private TextButtonNode? tryOnAllButton;
    private TextButtonNode? saveButton;
    private TextButtonNode? exportButton;
    private TextButtonNode? managerButton;
    private TextButtonNode? clearButton;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public BetterGlamourActionsNativeUI(
        Action onTryOnAll,
        Action onSave,
        Action onExport,
        Action onManager,
        Action onClear)
    {
        InternalName = "OmniBetterGlamourActions";
        Title = OmniLoc.Get("BetterGlamourManagementTitle");
        Subtitle = string.Empty;
        Size = new(300f, 168f);
        ContentPadding = new(8f, 8f);
        RememberClosePosition = false;
        CreateWindowNode = static () => new WindowNode { ShowCloseButton = false };
        RespectCloseAll = false;
        DisableClose = true;
        this.onTryOnAll = onTryOnAll;
        this.onSave = onSave;
        this.onExport = onExport;
        this.onManager = onManager;
        this.onClear = onClear;
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        tryOnAllButton = CreateButton("Feature.BetterGlamourManagement.TryOnAll", onTryOnAll);
        saveButton = CreateButton("Feature.BetterGlamourManagement.Save", onSave);
        exportButton = CreateButton("Feature.BetterGlamourManagement.Export", onExport);
        managerButton = CreateButton("Feature.BetterGlamourManagement.OpenSaved", onManager);
        clearButton = CreateButton("Feature.BetterGlamourManagement.Clear", onClear);
        ResizeContent();
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon) => ResizeContent();

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        tryOnAllButton = null;
        saveButton = null;
        exportButton = null;
        managerButton = null;
        clearButton = null;
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

    private void ResizeContent()
    {
        const float gap = 6f;
        var width = MathF.Max(80f, (ContentSize.X - gap) / 2f);
        var x = ContentStartPosition.X;
        var y = ContentStartPosition.Y;
        PositionButton(tryOnAllButton, x, y, width);
        PositionButton(saveButton, x + width + gap, y, width);
        PositionButton(exportButton, x, y + 34f, width);
        PositionButton(managerButton, x + width + gap, y + 34f, width);
        PositionButton(clearButton, x, y + 68f, ContentSize.X);
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
}
