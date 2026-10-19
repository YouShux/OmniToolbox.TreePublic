using Item = Lumina.Excel.Sheets.Item;
using System.Drawing;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using Lumina.Text.ReadOnly;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.UI;
using KernelTexture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

#pragma warning disable KTKExperimental_TextureImageNode

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourItemSearchResultNode : ListItemNode<BetterGlamourItemSearchResult>, IListItemNode
    {
        public static float ItemHeight => 28f;

        private readonly BetterGlamourItemIconNode iconNode;
        private readonly TextNode nameNode;

        public BetterGlamourItemSearchResultNode()
        {
            iconNode = new();
            iconNode.AttachNode(this);
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
            iconNode.Position = new(4f, 2f);
            iconNode.Size = new(24f, 24f);
            nameNode.Position = new(34f, 0f);
            nameNode.Size = new(MathF.Max(20f, Width - 34f), Height);
        }

        protected override void SetNodeData(BetterGlamourItemSearchResult itemData)
        {
            iconNode.IconID = itemData.IconID;
            nameNode.String = itemData.Name;
            nameNode.TextTooltip = itemData.Name;
            OnSizeChanged();
        }
    }

}