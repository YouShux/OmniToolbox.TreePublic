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
    internal sealed class BetterGlamourDyePickerNativeUI : NativeAddon
    {
        private static readonly Vector4[] CategoryColors =
        [
            new(0.96f, 0.96f, 0.96f, 1f),
            new(0.88f, 0.08f, 0.12f, 1f),
            new(0.72f, 0.36f, 0.14f, 1f),
            new(0.96f, 0.84f, 0.16f, 1f),
            new(0.46f, 0.72f, 0.18f, 1f),
            new(0.16f, 0.42f, 0.92f, 1f),
            new(0.58f, 0.24f, 0.82f, 1f)
        ];

        private static readonly string[] CategoryTooltipKeys =
        [
            "Feature.BetterGlamourManagement.DyeCategory.Neutral",
            "Feature.BetterGlamourManagement.DyeCategory.Red",
            "Feature.BetterGlamourManagement.DyeCategory.Brown",
            "Feature.BetterGlamourManagement.DyeCategory.Yellow",
            "Feature.BetterGlamourManagement.DyeCategory.Green",
            "Feature.BetterGlamourManagement.DyeCategory.Blue",
            "Feature.BetterGlamourManagement.DyeCategory.Purple",
            "Feature.BetterGlamourManagement.DyeCategory.All"
        ];

        private readonly List<BetterGlamourDyeColorNode> categoryNodes = [];
        private readonly List<BetterGlamourDyeGridRow> rows = [];
        private readonly BetterGlamourDyeColorNode?[] stainPreviewNodes = new BetterGlamourDyeColorNode?[2];
        private BetterGlamourItem? item;
        private Action? onChanged;
        private TabBarNode? tabBarNode;
        private ListNode<BetterGlamourDyeGridRow, BetterGlamourDyeGridRowNode>? listNode;
        private int selectedStain;
        private int selectedCategory = ALL_CATEGORY;
        private bool pendingRowsRebuild;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public BetterGlamourDyePickerNativeUI()
        {
            InternalName = "OmniBetterGlamourDyePicker";
            Title = OmniLoc.Get("Feature.BetterGlamourManagement.SelectDye");
            Subtitle = string.Empty;
            Size = new(440f, 440f);
            ContentPadding = new(10f, 8f);
            RememberClosePosition = true;
            CreateWindowNode = static () => new WindowNode { ShowCloseButton = true };
        }

        public void OpenFor(BetterGlamourItem glamourItem, int channel, Action changed)
        {
            item = glamourItem;
            onChanged = changed;
            selectedStain = channel == 1 ? 1 : 0;
            if (!IsOpen)
            {
                Open();
            }

            tabBarNode?.SelectTab(OmniLoc.Get(selectedStain == 0
                ? "Feature.BetterGlamourManagement.Column.Stain0"
                : "Feature.BetterGlamourManagement.Column.Stain1"));
            RebuildRows();
        }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
        {
            tabBarNode = new()
            {
                Height = 28f
            };
            tabBarNode.AddTab(OmniLoc.Get("Feature.BetterGlamourManagement.Column.Stain0"), () => SelectStain(0));
            tabBarNode.AddTab(OmniLoc.Get("Feature.BetterGlamourManagement.Column.Stain1"), () => SelectStain(1));
            tabBarNode.AttachNode(this);

            for (var index = 0; index < stainPreviewNodes.Length; index++)
            {
                var stain = index;
                var label = OmniLoc.Get(index == 0
                    ? "Feature.BetterGlamourManagement.Column.Stain0"
                    : "Feature.BetterGlamourManagement.Column.Stain1");
                var node = new BetterGlamourDyeColorNode(0f, 0f, 0f) { ShowBorder = false };
                node.CollisionNode.AddEvent(AtkEventType.MouseClick, () =>
                {
                    tabBarNode?.SelectTab(label);
                    SelectStain(stain);
                });
                node.CollisionNode.ShowClickableCursor = true;
                node.AttachNode(tabBarNode);
                stainPreviewNodes[index] = node;
            }

            for (var index = 0; index <= ALL_CATEGORY; index++)
            {
                var category = index;
                var node = new BetterGlamourDyeColorNode(0f, 1f, 0f)
                {
                    Color = index == ALL_CATEGORY ? new(0.2f, 0.2f, 0.2f, 1f) : CategoryColors[index]
                };
                node.CollisionNode.AddEvent(AtkEventType.MouseClick, () => SelectCategory(category));
                node.CollisionNode.SetTextTooltip(OmniLoc.Get(CategoryTooltipKeys[index]));
                node.CollisionNode.ShowClickableCursor = true;
                node.AttachNode(this);
                categoryNodes.Add(node);
            }

            listNode = new()
            {
                ItemSpacing = 4f,
                ShowNoResultsPlaceholder = false,
                AutoResetScroll = false,
                OptionsList = rows
            };
            listNode.AttachNode(this);

            ResizeContent();
            tabBarNode.SelectTab(OmniLoc.Get(selectedStain == 0
                ? "Feature.BetterGlamourManagement.Column.Stain0"
                : "Feature.BetterGlamourManagement.Column.Stain1"));
            SelectCategory(ALL_CATEGORY);
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            ResizeContent();
            if (pendingRowsRebuild)
            {
                pendingRowsRebuild = false;
                RebuildRows();
            }

            listNode?.Update();
        }

        protected override unsafe void OnHide(AtkUnitBase* addon)
        {
            tabBarNode?.HideTooltip();
            item = null;
            onChanged = null;
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            tabBarNode = null;
            listNode = null;
            Array.Clear(stainPreviewNodes);
            categoryNodes.Clear();
            rows.Clear();
            item = null;
            onChanged = null;
            pendingRowsRebuild = false;
        }

        private void SelectStain(int stain)
        {
            selectedStain = stain;
            RebuildRows();
        }

        private void SelectCategory(int category)
        {
            selectedCategory = category;
            for (var index = 0; index < categoryNodes.Count; index++)
            {
                categoryNodes[index].IsSelected = index == category;
            }

            RebuildRows();
        }

        private void SelectDye(BetterGlamourManagement.GlamourDyeOption option)
        {
            if (item is null)
            {
                return;
            }

            if (selectedStain == 0)
            {
                item.Stain0 = option.ID;
            }
            else
            {
                item.Stain1 = option.ID;
            }

            onChanged?.Invoke();
            pendingRowsRebuild = true;
        }

        private void RebuildRows()
        {
            tabBarNode?.HideTooltip();
            UpdateStainPreviews();
            rows.Clear();
            var options = new List<BetterGlamourManagement.GlamourDyeOption>();
            foreach (var option in BetterGlamourManagement.DyeOptions)
            {
                if (selectedCategory == ALL_CATEGORY || GetCategory(option) == selectedCategory)
                {
                    options.Add(option);
                }
            }

            for (var index = 0; index < options.Count; index += SWATCHES_PER_ROW)
            {
                rows.Add(new(
                    options.GetRange(index, Math.Min(SWATCHES_PER_ROW, options.Count - index)),
                    item is null ? (byte)0 : selectedStain == 0 ? item.Stain0 : item.Stain1,
                    SelectDye));
            }

            if (listNode is not null)
            {
                listNode.OptionsList = rows;
                listNode.FullRebuild();
            }
        }

        private void ResizeContent()
        {
            if (tabBarNode is null || listNode is null)
            {
                return;
            }

            const float CATEGORY_WIDTH = 42f;
            const float GAP = 8f;
            var listPosition = new Vector2(ContentStartPosition.X + CATEGORY_WIDTH + GAP, ContentStartPosition.Y + 38f);
            var listSize = new Vector2(
                MathF.Max(120f, ContentSize.X - CATEGORY_WIDTH - GAP),
                MathF.Max(80f, ContentSize.Y - 38f));
            if (listNode.Position == listPosition && listNode.Size == listSize)
                return;

            tabBarNode.Position = ContentStartPosition;
            tabBarNode.Size = new(ContentSize.X, 28f);
            var tabWidth = ContentSize.X / stainPreviewNodes.Length;
            for (var index = 0; index < stainPreviewNodes.Length; index++)
            {
                if (stainPreviewNodes[index] is { } node)
                {
                    node.Position = new(tabWidth * (index + 0.5f) - 48f, 5f);
                    node.Size = new(18f, 18f);
                }
            }

            for (var index = 0; index < categoryNodes.Count; index++)
            {
                categoryNodes[index].Position = new(ContentStartPosition.X + 3f, ContentStartPosition.Y + 38f + index * 40f);
                categoryNodes[index].Size = new(36f, 36f);
            }

            listNode.Position = listPosition;
            listNode.Size = listSize;
        }

        private void UpdateStainPreviews()
        {
            for (var index = 0; index < stainPreviewNodes.Length; index++)
            {
                if (stainPreviewNodes[index] is not { } node)
                {
                    continue;
                }

                var stainID = item is null ? (byte)0 : index == 0 ? item.Stain0 : item.Stain1;
                var option = BetterGlamourManagement.DyeOptions.Find(option => option.ID == stainID);
                node.SetDye(stainID, option.ID == 0 ? KnownColor.White.Vector() : option.Color);
                node.IsSelected = index == selectedStain;
                node.CollisionNode.SetTextTooltip(option.Name ?? BetterGlamourManagement.GetDyeName(stainID));
            }
        }

        private static int GetCategory(BetterGlamourManagement.GlamourDyeOption option)
        {
            if (option.ID == 0)
            {
                return 0;
            }

            var color = ColorHelpers.RgbaToHsv(option.Color);
            if (color.S < 0.18f)
            {
                return 0;
            }

            var hue = color.H * 360f;
            return hue switch
            {
                < 15f or >= 345f => 1,
                < 45f => 2,
                < 75f => 3,
                < 165f => 4,
                < 255f => 5,
                _ => 6
            };
        }

        #region 常量

        private const int ALL_CATEGORY = 7;

        private const int SWATCHES_PER_ROW = 8;

        #endregion
    }

}
