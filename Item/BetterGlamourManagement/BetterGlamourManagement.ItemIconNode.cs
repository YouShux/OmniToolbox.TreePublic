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
using KernelTexture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

#pragma warning disable KTKExperimental_TextureImageNode

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourItemIconNode : SimpleComponentNode
    {
        private readonly IconNode iconNode;

        public BetterGlamourItemIconNode(bool interactive = true)
        {
            iconNode = new();
            iconNode.AttachNode(this);
            if (!interactive)
            {
                CollisionNode.RemoveNodeFlags(NodeFlags.HasCollision);
                iconNode.CollisionNode.RemoveNodeFlags(NodeFlags.HasCollision);
            }
        }

        public uint IconID
        {
            get => iconNode.IconId;
            set
            {
                iconNode.IconId = value;
                ItemIconDyeIndicators.Apply(iconNode, 0);
            }
        }

        public void SetItem(uint itemID, byte stain0 = 0, byte stain1 = 0)
        {
            iconNode.IconId = LuminaWrapper.GetItemIconID(itemID);
            ItemIconDyeIndicators.Apply(iconNode, itemID, stain0, stain1);
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            var scale = MathF.Max(0.01f, MathF.Min(Width, Height) / 48f);
            iconNode.Size = new(48f, 48f);
            iconNode.Scale = new(scale, scale);
            iconNode.Position = new((Width - 48f * scale) / 2f, (Height - 48f * scale) / 2f);
        }
    }

}
