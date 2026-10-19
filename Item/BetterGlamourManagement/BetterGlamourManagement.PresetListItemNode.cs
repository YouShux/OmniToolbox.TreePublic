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
    internal sealed class BetterGlamourPresetListItemNode : ListItemNode<BetterGlamourPreset>, IListItemNode
    {
        public static float ItemHeight => 28f;

        private readonly TextNode nameNode;

        public BetterGlamourPresetListItemNode()
        {
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
            nameNode.Position = new(8f, 0f);
            nameNode.Size = new(MathF.Max(20f, Width - 12f), Height);
        }

        protected override void SetNodeData(BetterGlamourPreset itemData)
        {
            var name = string.IsNullOrWhiteSpace(itemData.Name)
                ? OmniLoc.Get("Feature.BetterGlamourManagement.WindowTitle")
                : itemData.Name;
            nameNode.String = itemData.GearsetIndex >= 0
                ? string.Format(
                    OmniLoc.Get("Feature.BetterGlamourManagement.PresetList.Bound"),
                    name,
                    itemData.GearsetIndex + 1)
                : string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.PresetList.Unbound"), name);
            OnSizeChanged();
        }
    }

}