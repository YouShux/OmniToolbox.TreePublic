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
    internal sealed class BetterGlamourDyeButtonNode : TabBarRadioButtonNode
    {
        private readonly BetterGlamourDyeColorNode[] previewNodes = new BetterGlamourDyeColorNode[2];

        public BetterGlamourDyeButtonNode()
        {
            String = OmniLoc.Get("Feature.BetterGlamourManagement.Dye");
            for (var index = 0; index < previewNodes.Length; index++)
            {
                previewNodes[index] = new(0f, 0f, 0f)
                {
                    ShowBorder = false
                };
                previewNodes[index].CollisionNode.NodeFlags = 0;
                previewNodes[index].AttachNode(this);
            }
        }

        public void SetStains(byte stain0, byte stain1)
        {
            previewNodes[0].SetDye(stain0, GetStainColor(stain0));
            previewNodes[1].SetDye(stain1, GetStainColor(stain1));
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            const float PREVIEW_SIZE = 18f;
            const float LABEL_WIDTH = 40f;
            const float GAP = 5f;
            var contentX = MathF.Max(8f, (Width - LABEL_WIDTH - PREVIEW_SIZE * 2f - GAP * 2f) / 2f);
            previewNodes[0].Position = new(contentX, (Height - PREVIEW_SIZE) / 2f);
            previewNodes[1].Position = new(contentX + PREVIEW_SIZE + GAP, (Height - PREVIEW_SIZE) / 2f);
            LabelNode.Position = new(contentX + PREVIEW_SIZE * 2f + GAP * 2f, 0f);
            LabelNode.Size = new(LABEL_WIDTH, Height);
            previewNodes[0].Size = new(PREVIEW_SIZE, PREVIEW_SIZE);
            previewNodes[1].Size = new(PREVIEW_SIZE, PREVIEW_SIZE);
        }

        private static Vector4 GetStainColor(byte stainID)
        {
            var option = BetterGlamourManagement.DyeOptions.Find(option => option.ID == stainID);
            return option.ID == 0 ? KnownColor.White.Vector() : option.Color;
        }
    }

}