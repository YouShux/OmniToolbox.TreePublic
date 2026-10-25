using TreeToolKit;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourDyeSwatchNode : SimpleComponentNode
    {
        private BetterGlamourDyeGridRow? row;
        private readonly ResNode colorGroup;
        private readonly SimpleImageNode colorNode;
        private readonly SimpleImageNode metallicShadowNode;
        private readonly SimpleImageNode metallicHighlightNode;
        private readonly SimpleImageNode selectedNode;
        private readonly SimpleImageNode selectedOutlineNode;
        private readonly SimpleImageNode hoveredNode;
        private readonly SimpleImageNode hoveredOutlineNode;
        private readonly SimpleImageNode selectedCheckNode;
        private readonly SimpleImageNode missingDyeNode;

        public unsafe BetterGlamourDyeSwatchNode(int index)
        {
            colorGroup = new()
            {
                Position = new(-5f),
                Size = new(40f)
            };
            colorGroup.AttachNode(this);
            colorNode = new()
            {
                TexturePath = "ui/uld/ListColorChooser.tex",
                TextureCoordinates = new(0f, 48f),
                TextureSize = new(40f, 40f),
                Size = new(40f, 40f),
                WrapMode = WrapMode.Stretch
            };
            colorNode.AttachNode(colorGroup);
            metallicShadowNode = CreateMetallicLayer(false);
            metallicShadowNode.AttachNode(colorGroup);
            metallicHighlightNode = CreateMetallicLayer(true);
            metallicHighlightNode.AttachNode(colorGroup);
            selectedNode = new()
            {
                TexturePath = "ui/uld/ListColorChooser.tex",
                TextureCoordinates = new(40f, 48f),
                TextureSize = new(40f, 40f),
                Position = new(-5f, -5f),
                Size = new(40f, 40f),
                WrapMode = WrapMode.Stretch,
                IsVisible = false
            };
            selectedNode.AttachNode(this);
            selectedOutlineNode = new()
            {
                TexturePath = "ui/uld/ListColorChooser.tex",
                TextureCoordinates = new(85f, 53f),
                TextureSize = new(30f),
                Size = new(30f),
                WrapMode = WrapMode.Stretch,
                IsVisible = false
            };
            selectedOutlineNode.AttachNode(this);
            hoveredNode = new()
            {
                TexturePath = "ui/uld/ListColorChooser.tex",
                TextureCoordinates = new(40f, 48f),
                TextureSize = new(40f),
                Position = new(-5f),
                Size = new(40f),
                WrapMode = WrapMode.Stretch,
                IsVisible = false
            };
            hoveredNode.AttachNode(this);
            hoveredOutlineNode = new()
            {
                TexturePath = "ui/uld/ListColorChooser.tex",
                TextureCoordinates = new(85f, 53f),
                TextureSize = new(30f),
                Size = new(30f),
                WrapMode = WrapMode.Stretch,
                IsVisible = false
            };
            hoveredOutlineNode.AttachNode(this);
            selectedCheckNode = new()
            {
                TexturePath = "ui/uld/ListColorChooser.tex",
                TextureCoordinates = new(120f, 64f),
                TextureSize = new(16f, 16f),
                Size = new(16f, 16f),
                WrapMode = WrapMode.Stretch,
                IsVisible = false
            };
            selectedCheckNode.AttachNode(this);
            missingDyeNode = new()
            {
                TexturePath = "ui/uld/ListColorChooser.tex",
                TextureCoordinates = new(120f, 48f),
                TextureSize = new(16f, 16f),
                Size = new(16f, 16f),
                WrapMode = WrapMode.Stretch,
                IsVisible = false
            };
            missingDyeNode.AttachNode(this);
            CollisionNode.AddEvent(AtkEventType.MouseOver, () =>
            {
                if (row is not null && index < row.Options.Count)
                    SetHovered(true);
            });
            CollisionNode.AddEvent(AtkEventType.MouseOut, ClearHover);
            CollisionNode.AddEvent(AtkEventType.MouseClick, (_, _, _, _, eventData) =>
            {
                if (row is null || index >= row.Options.Count || eventData is null)
                    return;
                if (eventData->IsLeftClick)
                {
                    ClearHover();
                    row.OnSelected(row.Options[index]);
                }
                else if (eventData->IsRightClick)
                {
                    ClearHover();
                    row.ContextRequested?.Invoke(row.Options[index]);
                }
            });
            CollisionNode.ShowClickableCursor = true;
        }

        public void SetOption(
            GlamourDyeOption? option,
            BetterGlamourDyeGridRow itemRow)
        {
            ClearHover();
            row = itemRow;
            IsVisible = option is not null;
            if (option is not { } dye)
            {
                CollisionNode.SetTextTooltip(string.Empty);
                return;
            }

            CollisionNode.AddNodeFlags(NodeFlags.HasCollision);
            var hasStain = SetDyeAppearance(dye, out var stain);
            CollisionNode.SetTextTooltip(dye.Name);
            selectedNode.IsVisible = selectedOutlineNode.IsVisible = dye.ID == itemRow.SelectedID;
            selectedCheckNode.IsVisible = dye.ID == itemRow.OriginalID;
            missingDyeNode.IsVisible = !hasStain || !HasDye(stain);
        }

        public void SetPreview(byte stainID)
        {
            ClearHover();
            row = null;
            CollisionNode.RemoveNodeFlags(NodeFlags.HasCollision);
            var dye = DyeOptions.Find(option => option.ID == stainID);
            SetDyeAppearance(dye, out _);
            selectedNode.IsVisible = selectedOutlineNode.IsVisible = selectedCheckNode.IsVisible = missingDyeNode.IsVisible = false;
        }

        public void SetHovered(bool hovered)
        {
            if (!hovered)
            {
                ClearHover();
                return;
            }
            hoveredNode.IsVisible = hoveredOutlineNode.IsVisible = !selectedNode.IsVisible;
        }

        public void ClearHover()
        {
            hoveredNode.IsVisible = hoveredOutlineNode.IsVisible = false;
            CollisionNode.HideTooltip();
        }

        private bool SetDyeAppearance(GlamourDyeOption dye, out Stain stain)
        {
            var hasStain = LuminaGetter.TryGetRow(dye.ID, out stain);
            colorNode.TextureCoordinates = dye.ID == 0 ? new(88f, 88f) : new(0f, 48f);
            colorGroup.MultiplyColor = dye.ID == 0 ? Vector3.Zero : new(dye.Color.X, dye.Color.Y, dye.Color.Z);
            metallicShadowNode.IsVisible = metallicHighlightNode.IsVisible = hasStain && stain.IsMetallic;
            return hasStain;
        }

        private static unsafe SimpleImageNode CreateMetallicLayer(bool highlight)
        {
            var flags = highlight
                ? (ImageNodeFlags)0x20 | ImageNodeFlags.FlipH | ImageNodeFlags.FlipV
                : (ImageNodeFlags)0x40;
            var native = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonByName("ColorantColoring");
            var preview = native is null ? null : native->GetComponentByNodeId(40);
            var image = preview is null ? null : preview->UldManager.SearchNodeById(highlight ? 8U : 9U);
            if (image is not null && image->Type == NodeType.Image)
                flags = ((AtkImageNode*)image)->Flags;
            return new()
            {
                TexturePath = "ui/uld/ListColorChooser.tex",
                TextureCoordinates = new(48f, 88f),
                TextureSize = new(40f),
                Size = new(40f),
                WrapMode = WrapMode.Stretch,
                ImageNodeFlags = flags,
                AddColor = new(highlight ? 0f : -128f / 255f),
                Alpha = 191f / 255f,
                IsVisible = false
            };
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            if (Width <= 0f || Height <= 0f || colorNode is null ||
                selectedNode is null || selectedCheckNode is null || missingDyeNode is null)
                return;
            selectedCheckNode.Position = new(16f, -2f);
            missingDyeNode.Position = new(0f, 14f);
        }

        private static unsafe bool HasDye(Stain stain)
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null)
                return false;
            foreach (var dye in stain.Item)
            {
                if (dye.RowId != 0 && inventory->GetInventoryItemCount(dye.RowId, checkEquipped: false, checkArmory: false) > 0)
                    return true;
            }
            return false;
        }
    }

}
