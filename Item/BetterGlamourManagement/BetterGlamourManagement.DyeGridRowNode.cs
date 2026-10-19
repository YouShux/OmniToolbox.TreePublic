using System.Drawing;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourDyeGridRowNode : ListItemNode<BetterGlamourDyeGridRow>, IListItemNode
    {
        public static float ItemHeight => 40f;

        private readonly BetterGlamourDyeSwatchNode[] swatches = new BetterGlamourDyeSwatchNode[8];

        public BetterGlamourDyeGridRowNode()
        {
            for (var index = 0; index < swatches.Length; index++)
            {
                swatches[index] = new(index);
                swatches[index].AttachNode(this);
            }

            DisableInteractions();
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            var size = MathF.Min(36f, MathF.Max(20f, (Width - (swatches.Length - 1) * 6f) / swatches.Length));
            for (var index = 0; index < swatches.Length; index++)
            {
                swatches[index].Position = new(index * (size + 6f), (Height - size) / 2f);
                swatches[index].Size = new(size, size);
            }
        }

        protected override void SetNodeData(BetterGlamourDyeGridRow itemData)
        {
            for (var index = 0; index < swatches.Length; index++)
            {
                swatches[index].SetOption(index < itemData.Options.Count ? itemData.Options[index] : null, itemData);
            }

            OnSizeChanged();
        }
    }

}