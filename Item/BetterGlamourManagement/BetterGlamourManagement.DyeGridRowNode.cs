using KamiToolKit.Nodes;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourDyeGridRowNode : ResNode
    {
        public static float ItemHeight => 30f;

        private readonly BetterGlamourDyeSwatchNode[] swatches = new BetterGlamourDyeSwatchNode[12];

        public BetterGlamourDyeGridRowNode()
        {
            for (var index = 0; index < swatches.Length; index++)
            {
                swatches[index] = new(index);
                swatches[index].AttachNode(this);
            }
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            if (Width <= 0f || Height <= 0f)
                return;
            var start = MathF.Max(0f, (Width - swatches.Length * ItemHeight) / 2f);
            for (var index = 0; index < swatches.Length; index++)
            {
                swatches[index].Position = new(start + index * ItemHeight, MathF.Max(0f, (Height - ItemHeight) / 2f));
                swatches[index].Size = new(ItemHeight, ItemHeight);
            }
        }

        public void SetData(BetterGlamourDyeGridRow itemData)
        {
            for (var index = 0; index < swatches.Length; index++)
            {
                swatches[index].SetOption(
                    index < itemData.Options.Count ? itemData.Options[index] : null,
                    itemData);
            }

            OnSizeChanged();
        }

        public void ClearHover()
        {
            foreach (var swatch in swatches)
                swatch.ClearHover();
        }
    }

}
