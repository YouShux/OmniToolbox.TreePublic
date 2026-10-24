using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourItemSearchResultNode : ListItemNode<BetterGlamourItemSearchResult>, IListItemNode
    {
        public static float ItemHeight => 48f;

        private readonly BetterGlamourItemIconNode iconNode;
        private readonly TextNode nameNode;
        private readonly TextNode levelNode;

        public BetterGlamourItemSearchResultNode()
        {
            iconNode = new(false);
            iconNode.AttachNode(this);
            nameNode = new()
            {
                AlignmentType = AlignmentType.Left,
                TextFlags = TextFlags.Ellipsis
            };
            nameNode.AttachNode(this);
            levelNode = new()
            {
                AlignmentType = AlignmentType.Left,
                TextColor = ColorHelper.GetColor(3)
            };
            levelNode.AttachNode(this);
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            iconNode.Position = new(4f, 2f);
            iconNode.Size = new(44f, 44f);
            nameNode.Position = new(56f, levelNode.IsVisible ? 0f : 12f);
            nameNode.Size = new(MathF.Max(20f, Width - 60f), 24f);
            levelNode.Position = new(56f, 24f);
            levelNode.Size = new(MathF.Max(20f, Width - 60f), 20f);
        }

        protected override void SetNodeData(BetterGlamourItemSearchResult itemData)
        {
            iconNode.IconID = itemData.IconID;
            nameNode.String = itemData.Name;
            this.SetTextTooltip(itemData.Name);
            levelNode.IsVisible = itemData.EquipLevel > 0;
            levelNode.String = itemData.EquipLevel > 0
                ? string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.ItemPicker.Level"), itemData.EquipLevel)
                : string.Empty;
            OnSizeChanged();
        }
    }

}
