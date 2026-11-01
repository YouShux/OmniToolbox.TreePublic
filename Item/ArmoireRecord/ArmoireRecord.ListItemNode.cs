using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;

namespace OmniToolbox.TreePublic;

public sealed partial class ArmoireRecord
{
    internal sealed class ArmoireRecordListItemNode : ListItemNode<ArmoireRecordItem>, IListItemNode
    {
        public static float ItemHeight => 54f;

        private readonly IconNode iconNode;
        private readonly TextNode nameNode;
        private readonly TextNode locationNode;
        private readonly TextNode statusNode;

        public ArmoireRecordListItemNode()
        {
            iconNode = new()
            {
                IconId = 60072
            };
            iconNode.CollisionNode.RemoveNodeFlags(NodeFlags.HasCollision);
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
            iconNode.Size     = new(48f);
            iconNode.Scale    = new(42f / 48f);

            const float TEXT_X = 50f;
            const float STATUS_WIDTH = 120f;
            nameNode.Position     = new(TEXT_X, 4f);
            nameNode.Size         = new(MathF.Max(40f, Width - TEXT_X - STATUS_WIDTH - 8f), 22f);
            statusNode.Position   = new(MathF.Max(TEXT_X, Width - STATUS_WIDTH), 4f);
            statusNode.Size       = new(STATUS_WIDTH, 22f);
            locationNode.Position = new(TEXT_X, 28f);
            locationNode.Size     = new(MathF.Max(40f, Width - TEXT_X - 6f), 20f);
        }

        protected override void SetNodeData(ArmoireRecordItem itemData)
        {
            iconNode.IconId = itemData.IconID;
            ItemIconDyeIndicators.Apply(iconNode, itemData.ItemID, itemData.Stain0, itemData.Stain1);
            nameNode.String      = $"{itemData.Name} ×{itemData.Quantity}";
            locationNode.String  = itemData.LocationsText;
            statusNode.String    = OmniLoc.Get(itemData.CanDirectStore
                ? "Feature.ArmoireRecord.Status.DirectStore"
                : "Feature.ArmoireRecord.Status.MoveToBackpack");
            statusNode.TextColor = ColorHelper.GetColor(itemData.CanDirectStore ? 45u : 3u);
            OnSizeChanged();
        }
    }

}
