using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node.Simple;

namespace OmniToolbox.TreePublic;

internal sealed class BattlehornWindowNode : WindowNode
{
    internal static readonly Vector4 Ink = new(0.36f, 0.24f, 0.18f, 1f);
    private readonly List<SimpleImageNode> paper = [];

    public BattlehornWindowNode()
    {
        BackgroundNode.IsVisible = false;
        BackgroundImageNode.IsVisible = false;
        BorderNode.IsVisible = false;
        TitleNode.TextColor = new(0.46f, 0.31f, 0.08f, 1f);
        TitleNode.TextOutlineColor = Vector4.Zero;
        TitleNode.FontSize = 20;
        DividingLineNode.TexturePath = "ui/uld/img01/windowa_line.tex";
        CloseButtonNode.ImageNode.LoadTexture("ui/uld/WindowA_Button.tex", false);
        HeaderContainerNode.Height = 58f;

        // XBMMonsterBook.uld 第 19 组切片：四角保持原尺寸，仅拉伸边缘和中心。
        string[] names = ["TL", "TM", "TR", "ML", "MM", "MR", "BL", "BM", "BR"];
        for (var i = 0; i < names.Length; i++)
        {
            var row = i / 3;
            var column = i % 3;
            var image = new SimpleImageNode
            {
                TextureCoordinates = new(column == 1 && row == 1 ? 1f : 0f, row == 1 ? 1f : 0f),
                TextureSize = new(column == 0 ? 144f : column == 2 ? 145f : row == 1 ? 38f : 40f,
                    row == 0 ? 136f : row == 2 ? 135f : 38f),
                WrapMode = WrapMode.Stretch
            };
            image.LoadTexture($"ui/uld/XBMNoteWindow{names[i]}.tex", false);
            // 不插到组件根节点之前，否则原生更新会漏掉书页的位置和缩放。
            image.AttachNode(BackgroundImageNode, NodePosition.BeforeTarget);
            paper.Add(image);
        }
    }

    public override Vector2 ContentStartPosition => new(24f, 64f);
    public override Vector2 ContentSize => new(Width - 48f, Height - 92f);

    public override void SetTitle(string title, string? subtitle = null)
    {
        base.SetTitle(title, subtitle);
        LayoutHeader();
    }

    internal void LayoutHeader()
    {
        TitleNode.Position = new(32f, 28f);
        CloseButtonNode.Position = new(Width - 54f, 28f);
        DividingLineNode.Position = new(26f, 52f);
        DividingLineNode.Width = MathF.Max(0f, Width - 52f);
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        LayoutHeader();
        if (paper == null)
        {
            return;
        }
        for (var i = 0; i < paper.Count; i++)
        {
            var row = i / 3;
            var column = i % 3;
            paper[i].Position = new(column == 0 ? 0f : column == 1 ? 144f : Width - 145f,
                row == 0 ? 0f : row == 1 ? 136f : Height - 135f);
            paper[i].Size = new(column == 0 ? 144f : column == 2 ? 145f : MathF.Max(0f, Width - 289f),
                row == 0 ? 136f : row == 2 ? 135f : MathF.Max(0f, Height - 271f));
        }
    }
}
