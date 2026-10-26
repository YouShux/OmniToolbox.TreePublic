using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed partial class ChocoboColorPreview
{
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

}