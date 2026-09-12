using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node.Simple;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

internal sealed class BattlehornSettingsNativeUI : NativeAddon
{
    private readonly BetterBattlehornSettings feature;
    private ListNode<BattlehornRow, BattlehornRowNode>? list;
    private TextInputNode? nameInput;
    private TextNode? emptyNode;
    private TextButtonNode? saveButton;
    private TextButtonNode? overwriteButton;
    private TextButtonNode? renameButton;
    private TextButtonNode? deleteButton;
    private readonly List<IconImageNode> icons = [];
    private readonly List<TextNode> petNames = [];
    private readonly List<TextNode> petProgress = [];
    private long nextPreviewUpdate;
    private BattlehornPreset? selected;
    private int revision = -1;
    private bool disposed;
    private bool ready;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public BattlehornSettingsNativeUI(BetterBattlehornSettings feature)
    {
        this.feature = feature;
        InternalName = "OmniBattlehornSettings";
        Title = feature.Info.Title;
        Subtitle = string.Empty;
        Size = new(370f, 420f);
        ContentPadding = new(6f, 4f);
        RememberClosePosition = false;
        DisableCloseTransition = true;
        CreateWindowNode = static () => new BattlehornWindowNode { ShowCloseButton = true };
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        var x = ContentStartPosition.X;
        var y = ContentStartPosition.Y;
        var width = ContentSize.X;
        nameInput = new()
        {
            Position = new(x, y), Size = new(width - 88f, 28f), MaxCharacters = 64,
            ShowLimitText = false,
            PlaceholderString = OmniLoc.Get("Feature.Battlehorn.Name")
        };
        nameInput.AttachNode(this);
        ((SimpleNineGridNode)nameInput.BackgroundNode).TexturePath = "ui/uld/img01/textinputa.tex";
        ((SimpleNineGridNode)nameInput.FocusBorderNode).TexturePath = "ui/uld/img01/textinputa.tex";
        nameInput.CurrentTextNode.TextColor = BattlehornWindowNode.Ink;
        nameInput.CurrentTextNode.TextOutlineColor = Vector4.Zero;
        nameInput.CurrentTextNode.TextFlags &= ~TextFlags.Emboss;
        nameInput.PlaceholderTextNode.TextColor = BattlehornWindowNode.Ink;
        nameInput.PlaceholderTextNode.TextOutlineColor = Vector4.Zero;
        nameInput.PlaceholderTextNode.TextFlags = TextFlags.Ellipsis;
        saveButton = Button("Feature.Battlehorn.SaveNew", new(x + width - 82f, y), new(64f, 28f),
            () => feature.SavePreset(nameInput.String.ExtractText(), null));
        var listHeight = MathF.Max(80f, ContentSize.Y - 180f);
        list = new()
        {
            Position = new(x, y + 36f), Size = new(width, listHeight),
            OptionsList = [], ItemSpacing = 2f,
            OnItemSelected = row =>
            {
                selected = row?.Preset;
                nameInput.String = selected?.Name ?? string.Empty;
                RefreshPreview();
            }
        };
        list.AttachNode(this);
        emptyNode = new()
        {
            Position = list.Position, Size = list.Size,
            AlignmentType = AlignmentType.Center,
            TextColor = BattlehornWindowNode.Ink, TextOutlineColor = Vector4.Zero,
            String = OmniLoc.Get("Feature.Battlehorn.Empty")
        };
        emptyNode.AttachNode(this);
        var previewY = y + 40f + listHeight;
        for (var i = 0; i < 3; i++)
        {
            var icon = new IconImageNode { Position = new(x, previewY + i * 34f), Size = new(32f), FitTexture = true, IsVisible = false };
            icon.AttachNode(this);
            icons.Add(icon);
            var label = new TextNode
            {
                Position = new(x + 38f, previewY + i * 34f), Size = new(width - 198f, 32f),
                TextColor = BattlehornWindowNode.Ink, TextOutlineColor = Vector4.Zero,
                TextFlags = TextFlags.Ellipsis
            };
            label.AttachNode(this);
            petNames.Add(label);
            var progress = new TextNode
            {
                Position = new(x + width - 156f, previewY + i * 34f), Size = new(156f, 32f),
                AlignmentType = AlignmentType.Right, FontSize = 12,
                TextFlags = TextFlags.Ellipsis,
                TextColor = BattlehornWindowNode.Ink, TextOutlineColor = Vector4.Zero
            };
            progress.AttachNode(this);
            petProgress.Add(progress);
        }
        var buttonY = ContentStartPosition.Y + ContentSize.Y - 28f;
        var buttonWidth = (width - 12f) / 3f;
        overwriteButton = Button("Feature.Battlehorn.OverwriteShort", new(x, buttonY), new(buttonWidth, 28f), () =>
        {
            if (selected != null)
            {
                feature.SavePreset(selected.Name, selected);
            }
        });
        renameButton = Button("Feature.Battlehorn.Rename", new(x + buttonWidth + 6f, buttonY), new(buttonWidth, 28f), () =>
        {
            if (selected != null)
            {
                feature.RenamePreset(selected, nameInput.String.ExtractText());
            }
        });
        deleteButton = Button("Feature.Battlehorn.Delete", new(x + (buttonWidth + 6f) * 2f, buttonY), new(buttonWidth, 28f), () =>
        {
            if (selected != null)
            {
                feature.DeletePreset(selected);
            }
        });
        revision = -1;
        RefreshData();
        RefreshPreview();
        ready = true;
    }

    protected override unsafe void OnUpdate(AtkUnitBase* addon)
    {
        if (disposed || !ready || !feature.IsRunning)
        {
            return;
        }
        RefreshData();
        nameInput!.ShowLimitText = false;
        if (Environment.TickCount64 >= nextPreviewUpdate)
        {
            RefreshPreview();
            nextPreviewUpdate = Environment.TickCount64 + 250;
        }
        var canEdit = feature.CanEdit;
        saveButton!.IsEnabled = canEdit;
        overwriteButton!.IsEnabled = canEdit && selected != null;
        renameButton!.IsEnabled = selected != null;
        deleteButton!.IsEnabled = selected != null;
        list?.Update();
    }

    protected override unsafe void OnDraw(AtkUnitBase* addon)
    {
        if (disposed || !ready || !feature.IsRunning)
        {
            return;
        }
        (WindowNode as BattlehornWindowNode)?.LayoutHeader();
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        ready = false;
        list = null;
        nameInput = null;
        emptyNode = null;
        saveButton = overwriteButton = renameButton = deleteButton = null;
        icons.Clear();
        petNames.Clear();
        petProgress.Clear();
        selected = null;
    }

    public override void Dispose()
    {
        if (disposed)
        {
            return;
        }
        // 原生窗口延迟关闭，先阻止迟到的回调访问已释放的插件服务。
        disposed = true;
        ready = false;
        base.Dispose();
    }

    internal void ResetSelection()
    {
        selected = null;
        if (nameInput != null)
        {
            nameInput.String = string.Empty;
        }
        revision = -1;
        RefreshPreview();
    }

    internal unsafe void SyncTitleColor(AtkUnitBase* notebook)
    {
        if (!ready || disposed || WindowNode is not BattlehornWindowNode window || notebook->WindowNode == null)
        {
            return;
        }
        var component = notebook->WindowNode->Component;
        if (component == null || component->GetComponentType() != ComponentType.Window)
        {
            return;
        }
        var title = component->GetTextNodeById((uint)((AtkComponentWindow*)component)->NodeIds[0]);
        if (title != null)
        {
            window.TitleNode.Node->TextColor = title->TextColor;
            window.TitleNode.Node->EdgeColor = title->EdgeColor;
        }
    }

    private void RefreshData()
    {
        if (list == null || revision == feature.Revision)
        {
            return;
        }
        revision = feature.Revision;
        var presets = feature.Presets;
        if (selected != null && !presets.Contains(selected))
        {
            selected = null;
        }
        var rows = new List<BattlehornRow>();
        foreach (var preset in presets)
        {
            rows.Add(new(preset, () =>
            {
                selected = preset;
                nameInput!.String = preset.Name;
                feature.Apply(preset);
                RefreshPreview();
            }, () => feature.CanEdit));
        }
        list.OptionsList = rows;
        emptyNode!.IsVisible = rows.Count == 0;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var pets = selected?.Pets ?? feature.CurrentPets;
        for (var i = 0; i < icons.Count; i++)
        {
            var pet = pets is { Length: 3 } ? pets[i] : (byte)0;
            var entry = feature.GetEntry(pet);
            icons[i].IsVisible = entry != null;
            if (entry != null)
            {
                if (icons[i].IconId != entry.IconID)
                {
                    icons[i].IconId = entry.IconID;
                }
            }
            var name = $"{i + 1}  {entry?.Name ?? OmniLoc.Get("Feature.Battlehorn.EmptySlot")}";
            if (petNames[i].String.ExtractText() != name)
            {
                petNames[i].String = name;
            }
            var progress = feature.GetPetProgress(pet);
            if (petProgress[i].String.ExtractText() != progress)
            {
                petProgress[i].String = progress;
            }
        }
    }

    private TextButtonNode Button(string key, Vector2 position, Vector2 size, Action action)
    {
        var button = new BattlehornButtonNode { String = OmniLoc.Get(key), Position = position, Size = size, OnClick = action };
        button.AttachNode(this);
        return button;
    }
}

internal sealed record BattlehornRow(BattlehornPreset Preset, Action Apply, Func<bool> CanApply);

internal sealed class BattlehornButtonNode : TextButtonNode
{
    public unsafe BattlehornButtonNode()
    {
        BackgroundNode.PartsList[0]->LoadTexture("ui/uld/ButtonA.tex", false);
        AddColor = new(32f / 255f, 16f / 255f, 0f);
        LabelNode.TextColor = Vector4.One;
    }
}

internal sealed class BattlehornRowNode : ListItemNode<BattlehornRow>, IListItemNode
{
    public static float ItemHeight => 30f;
    private readonly TextNode label;
    private readonly TextButtonNode apply;

    public BattlehornRowNode()
    {
        label = new()
        {
            AlignmentType = AlignmentType.Left, TextFlags = TextFlags.Ellipsis,
            TextColor = BattlehornWindowNode.Ink, TextOutlineColor = Vector4.Zero
        };
        label.AttachNode(this);
        apply = new BattlehornButtonNode { String = OmniLoc.Get("Feature.Battlehorn.Apply"), OnClick = () => ItemData?.Apply() };
        apply.AttachNode(this);
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        label.Position = new(6f, 0f);
        label.Size = new(MathF.Max(0f, Width - 78f), Height);
        apply.Position = new(MathF.Max(0f, Width - 66f), 2f);
        apply.Size = new(64f, Height - 4f);
    }

    protected override void SetNodeData(BattlehornRow itemData) => label.String = itemData.Preset.Name;

    public override void Update() => apply.IsEnabled = ItemData?.CanApply() == true;
}
