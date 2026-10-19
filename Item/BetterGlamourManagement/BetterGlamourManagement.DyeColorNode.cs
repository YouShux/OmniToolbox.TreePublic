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
    internal class BetterGlamourDyeColorNode : SimpleComponentNode
    {
        private readonly ColorImageNode colorNode;
        private readonly TextNode noDyeLineNode;
        private readonly SimpleClippingMaskNode clipNode;
        private readonly SimpleImageNode highlightNode;
        private readonly SimpleImageNode lowlightNode;
        private readonly float colorInset;
        private readonly float highlightInset;
        private readonly float lowlightInset;
        private Action<BetterGlamourDyeColorNode, string>? showTooltip;
        private Action? hideTooltip;
        private string tooltipText = string.Empty;
        private bool isSelected;
        private bool showBorder = true;

        public BetterGlamourDyeColorNode(
            float colorInset = 2f,
            float highlightInset = 1f,
            float? lowlightInset = null)
        {
            this.colorInset = colorInset;
            this.highlightInset = highlightInset;
            this.lowlightInset = lowlightInset ?? colorInset;
            colorNode = new();
            colorNode.AttachNode(this);
            noDyeLineNode = new()
            {
                AlignmentType = AlignmentType.Center,
                String = "/",
                TextColor = KnownColor.Black.Vector(),
                TextFlags = TextFlags.None,
                IsVisible = false,
            };
            noDyeLineNode.AttachNode(this);
            clipNode = new()
            {
                TextureCoordinates = Vector2.Zero,
                TextureSize = new(32f, 32f),
                TexturePath = "ui/uld/BgPartsMask.tex"
            };
            clipNode.AttachNode(this);

            highlightNode = CreateBorder(new(69f, 1f));
            lowlightNode = CreateBorder(new(141f, 1f));
            CollisionNode.AddEvent(AtkEventType.MouseOver, () =>
            {
                if (tooltipText.Length != 0)
                {
                    showTooltip?.Invoke(this, tooltipText);
                }
            });
            CollisionNode.AddEvent(AtkEventType.MouseOut, () => hideTooltip?.Invoke());
        }

        public override Vector4 Color
        {
            get => colorNode.Color;
            set => colorNode.Color = value;
        }

        public bool IsSelected
        {
            set
            {
                isSelected = value;
                UpdateBorderVisibility();
            }
        }

        public bool ShowBorder
        {
            set
            {
                showBorder = value;
                UpdateBorderVisibility();
            }
        }

        public void SetDye(byte stainID, Vector4 color)
        {
            Color = stainID == 0 ? KnownColor.LightGray.Vector() : color;
            noDyeLineNode.IsVisible = stainID == 0;
        }

        public void SetFixedTooltip(
            string text,
            Action<BetterGlamourDyeColorNode, string>? show,
            Action? hide)
        {
            tooltipText = text;
            showTooltip = show;
            hideTooltip = hide;
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            colorNode.Position = new(colorInset, colorInset);
            colorNode.Size = new(
                MathF.Max(1f, Width - colorInset * 2f),
                MathF.Max(1f, Height - colorInset * 2f));
            noDyeLineNode.Position = colorNode.Position;
            noDyeLineNode.Size = colorNode.Size;
            noDyeLineNode.FontSize = (uint)Math.Clamp(
                (int)MathF.Round(MathF.Min(colorNode.Width, colorNode.Height) * 0.8f),
                12,
                24);
            clipNode.Position = colorNode.Position;
            clipNode.Size = colorNode.Size;

            highlightNode.Position = new(highlightInset, highlightInset);
            highlightNode.Size = new(
                MathF.Max(1f, Width - highlightInset * 2f),
                MathF.Max(1f, Height - highlightInset * 2f));
            lowlightNode.Position = new(lowlightInset, lowlightInset);
            lowlightNode.Size = new(
                MathF.Max(1f, Width - lowlightInset * 2f),
                MathF.Max(1f, Height - lowlightInset * 2f));
        }

        private void UpdateBorderVisibility()
        {
            highlightNode.IsVisible = showBorder && isSelected;
            lowlightNode.IsVisible = showBorder && !isSelected;
        }

        private SimpleImageNode CreateBorder(Vector2 coordinates)
        {
            var node = new SimpleImageNode
            {
                TextureCoordinates = coordinates,
                TextureSize = new(36f, 36f),
                TexturePath = "ui/uld/BgParts.tex"
            };
            node.AttachNode(this);
            return node;
        }
    }

}