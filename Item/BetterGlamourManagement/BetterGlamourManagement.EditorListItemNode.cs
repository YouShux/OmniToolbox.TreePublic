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
    internal sealed class BetterGlamourEditorListItemNode : ListItemNode<BetterGlamourEditorRow>, IListItemNode
    {
        public static float ItemHeight => 48f;

        private readonly TextNode partNode;
        private readonly BetterGlamourItemIconNode itemIconNode;
        private readonly TextNode itemNameNode;
        private readonly TextInputNode itemSearchInputNode;
        private readonly string itemSearchPlaceholder = OmniLoc.Get("Feature.BetterGlamourManagement.ItemSearch.Placeholder");
        private readonly BetterGlamourDyeButtonNode dyeNode;
        private readonly TextNode noDyeNode;
        private BetterGlamourEditorRow? row;
        private bool isSettingNodeData;

        public BetterGlamourEditorListItemNode()
        {
            partNode = CreateTextNode();
            partNode.AlignmentType = AlignmentType.Center;
            itemIconNode = new();
            itemIconNode.AttachNode(this);
            itemNameNode = CreateTextNode();
            itemSearchInputNode = new()
            {
                ShowLimitText = false,
                PlaceholderString = itemSearchPlaceholder,
                OnFocused = () => row?.OnSearchFocused?.Invoke(this),
                OnInputReceived = OnItemSearchInput,
                OnUnfocused = () => row?.OnSearchClosed?.Invoke(),
                OnFocusLost = () => row?.OnSearchClosed?.Invoke(),
                OnEscapeEntered = () => row?.OnSearchClosed?.Invoke()
            };
            itemSearchInputNode.AttachNode(this);
            dyeNode = CreateDyeButton();
            noDyeNode = CreateTextNode();
            noDyeNode.AlignmentType = AlignmentType.Center;
            noDyeNode.String = OmniLoc.Get("Feature.BetterGlamourManagement.None");
            DisableInteractions();
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            var columns = GetColumns(Width);
            PositionNode(partNode, 0f, columns.PartWidth);
            itemIconNode.Position = new(columns.ItemColumnX, (ItemHeight - columns.ItemIconWidth) / 2f);
            itemIconNode.Size = new(columns.ItemIconWidth, columns.ItemIconWidth);
            itemNameNode.Position = new(columns.ItemNameX, 0f);
            itemNameNode.Size = new(columns.ItemNameWidth, 20f);
            itemSearchInputNode.Position = new(columns.ItemSearchX, 20f);
            itemSearchInputNode.Size = new(columns.ItemSearchWidth, 28f);
            dyeNode.Position = new(columns.DyeX, (ItemHeight - 28f) / 2f);
            dyeNode.Size = new(columns.DyeWidth, 28f);
            PositionNode(noDyeNode, columns.DyeX, columns.DyeWidth);
        }

        protected override void SetNodeData(BetterGlamourEditorRow itemData)
        {
            row = itemData;
            partNode.String = itemData.PartName;
            itemIconNode.IconID = itemData.Kind switch
            {
                BetterGlamourEditorItemKind.Hairstyle => BetterGlamourManagement.GetHairstyleIcon(itemData.Preset.HairstyleID),
                BetterGlamourEditorItemKind.Glasses => BetterGlamourManagement.GetGlassesIcon(itemData.Preset.GlassesID),
                _ => LuminaWrapper.GetItemIconID(itemData.Item!.ItemID)
            };
            itemIconNode.ItemTooltip = itemData.Kind == BetterGlamourEditorItemKind.Item
                ? itemData.Item!.ItemID
                : 0;
            itemNameNode.String = GetCurrentItemName();
            isSettingNodeData = true;
            try
            {
                itemSearchInputNode.String = string.Empty;
            }
            finally
            {
                isSettingNodeData = false;
            }

            var canDye = itemData.Kind == BetterGlamourEditorItemKind.Item;
            dyeNode.IsVisible = canDye;
            noDyeNode.IsVisible = !canDye;
            if (canDye)
            {
                dyeNode.IsSelected = itemData.IsDyeSelected;
                dyeNode.SetStains(itemData.Item!.Stain0, itemData.Item.Stain1);
            }

            OnSizeChanged();
        }

        private TextNode CreateTextNode()
        {
            var node = new TextNode
            {
                AlignmentType = AlignmentType.Left,
                TextFlags = TextFlags.Ellipsis
            };
            node.AttachNode(this);
            return node;
        }

        private BetterGlamourDyeButtonNode CreateDyeButton()
        {
            var node = new BetterGlamourDyeButtonNode();
            node.CollisionNode.AddEvent(AtkEventType.MouseClick, () => row?.OpenDye?.Invoke());
            node.AttachNode(this);
            return node;
        }

        internal float ItemSearchWidth => itemSearchInputNode.Width;

        internal unsafe Vector2 ItemSearchInputScreenSize => ((AtkResNode*)itemSearchInputNode)->GetSize();

        internal unsafe Vector2 ItemSearchInputScreenPosition => ((AtkResNode*)itemSearchInputNode)->GetPosition();

        internal List<BetterGlamourItemSearchResult> Search(string query) => row?.Kind switch
        {
            BetterGlamourEditorItemKind.Hairstyle => BetterGlamourManagement.SearchHairstyles(query),
            BetterGlamourEditorItemKind.Glasses => BetterGlamourManagement.SearchGlasses(query),
            BetterGlamourEditorItemKind.Item when row.Item is { } item =>
                BetterGlamourManagement.SearchItems(query).FindAll(result =>
                    LuminaGetter.TryGetRow<Item>(result.ItemID, out var itemRow) &&
                    BetterGlamourManagement.FitsSlot(itemRow, item.Slot)),
            _ => []
        };

        internal void SetItemSearchResult(BetterGlamourItemSearchResult result)
        {
            if (row is null)
            {
                return;
            }

            switch (row.Kind)
            {
                case BetterGlamourEditorItemKind.Hairstyle:
                    row.Preset.HairstyleID = result.ItemID;
                    itemIconNode.IconID = BetterGlamourManagement.GetHairstyleIcon(result.ItemID);
                    break;
                case BetterGlamourEditorItemKind.Glasses:
                    row.Preset.GlassesID = (ushort)Math.Min(result.ItemID, (uint)ushort.MaxValue);
                    itemIconNode.IconID = BetterGlamourManagement.GetGlassesIcon(row.Preset.GlassesID);
                    break;
                case BetterGlamourEditorItemKind.Item when row.Item is { } item:
                    if (!LuminaGetter.TryGetRow<Item>(result.ItemID, out var itemRow) ||
                        !BetterGlamourManagement.FitsSlot(itemRow, item.Slot))
                        return;

                    item.ItemID = result.ItemID;
                    itemIconNode.IconID = LuminaWrapper.GetItemIconID(item.ItemID);
                    itemIconNode.ItemTooltip = item.ItemID;
                    break;
            }

            itemNameNode.String = result.Name;
            row.OnChanged();
        }

        internal void ClearItemSearchText() => itemSearchInputNode.String = string.Empty;

        public override void Update()
        {
            base.Update();
            var placeholder = itemSearchInputNode.String.IsEmpty &&
                              itemSearchInputNode.CurrentTextNode.String.IsEmpty &&
                              !itemSearchInputNode.IsFocused
                ? itemSearchPlaceholder
                : string.Empty;
            if (itemSearchInputNode.PlaceholderString != placeholder)
            {
                itemSearchInputNode.PlaceholderString = placeholder;
            }
        }

        private void OnItemSearchInput(ReadOnlySeString value)
        {
            itemSearchInputNode.PlaceholderString = string.Empty;
            if (!isSettingNodeData)
            {
                row?.OnSearchChanged?.Invoke(this, value.ExtractText());
            }
        }

        private string GetCurrentItemName()
        {
            if (row is null)
            {
                return string.Empty;
            }

            return row.Kind switch
            {
                BetterGlamourEditorItemKind.Hairstyle => BetterGlamourManagement.GetHairstyleName(row.Preset.HairstyleID),
                BetterGlamourEditorItemKind.Glasses => BetterGlamourManagement.GetGlassesName(row.Preset.GlassesID),
                _ => BetterGlamourManagement.GetItemName(row.Item!.ItemID)
            };
        }

        internal static EditorColumns GetColumns(float width)
        {
            const float PART_WIDTH = 76f;
            const float ITEM_ICON_WIDTH = 44f;
            const float DYE_WIDTH = 148f;
            return new(
                PART_WIDTH,
                ITEM_ICON_WIDTH,
                MathF.Max(80f, width - PART_WIDTH - ITEM_ICON_WIDTH - DYE_WIDTH - GAP * 3f),
                DYE_WIDTH);
        }

        private static void PositionNode(NodeBase node, float x, float width)
        {
            node.Position = new(x, 0f);
            node.Size = new(width, ItemHeight);
        }

        internal readonly record struct EditorColumns(
            float PartWidth,
            float ItemIconWidth,
            float ItemNameWidth,
            float DyeWidth)
        {
            public float ItemColumnX => PartWidth + GAP;

            public float ItemNameX => ItemColumnX + ItemIconWidth + GAP;

            public float ItemSearchX => ItemNameX - 8f;

            public float ItemSearchWidth => ItemNameWidth + 8f;

            public float DyeX => ItemNameX + ItemNameWidth + GAP;

            public float ItemWidth => ItemIconWidth + ItemNameWidth + GAP;

            public float DyeHeaderX => PartWidth + ItemWidth + GAP * 2f - 16f;
        }

        #region 常量

        internal const float GAP = 6f;

        #endregion
    }

}