using Dalamud.Game.ClientState.Keys;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using TreeToolKit;
using Item = Lumina.Excel.Sheets.Item;
using Stain = Lumina.Excel.Sheets.Stain;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourDyePickerNativeUI : NativeAddon
    {
        private static readonly Vector4[] CategoryColors =
        [
            new(244f / 255f, 239f / 255f, 1f, 1f),
            new(201f / 255f, 17f / 255f, 25f / 255f, 1f),
            new(161f / 255f, 97f / 255f, 49f / 255f, 1f),
            new(229f / 255f, 206f / 255f, 48f / 255f, 1f),
            new(137f / 255f, 191f / 255f, 59f / 255f, 1f),
            new(59f / 255f, 114f / 255f, 245f / 255f, 1f),
            new(156f / 255f, 80f / 255f, 231f / 255f, 1f)
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
            "Feature.BetterGlamourManagement.DyeCategory.Special"
        ];

        private readonly List<DyeCategoryNode>             categoryNodes         = [];
        private readonly List<BetterGlamourDyeGridRowNode> rowNodes              = [];
        private readonly DyeChannelNode?[]                 channelNodes          = new DyeChannelNode?[2];
        private readonly DyeColorPreviewNode?[,]           previews              = new DyeColorPreviewNode?[2, 2];
        private readonly TextNode?[,]                      previewLabels         = new TextNode?[2, 2];
        private readonly SimpleNineGridNode?[]             comparisonBackgrounds = new SimpleNineGridNode?[2];
        private readonly TextureButtonNode?[]              comparisonButtons     = new TextureButtonNode?[2];
        private readonly TextNode?[]                       comparisonTitles      = new TextNode?[2];
        private readonly DyeSourceNode?[]                  dyeSources            = new DyeSourceNode?[2];
        private readonly byte[]                            originalStains        = new byte[2];
        private readonly byte[]                            candidateStains       = new byte[2];
        private BetterGlamourItem?                         item;
        private Action<int, byte>?                         onSelected;
        private Action<int>?                               onUndo;
        private Action?                                    onSwap;
        private Func<int, bool>?                           canUndo;
        private TextNode?                                  equipmentLabel;
        private SimpleNineGridNode?                        equipmentBackground;
        private SimpleNineGridNode?                        paletteBackground;
        private SimpleNineGridNode?                        selectionBackground;
        private SimpleNineGridNode?                        categoryBackground;
        private SimpleNineGridNode?                        channelFrame;
        private SimpleNineGridNode?                        categoryLine;
        private SimpleNineGridNode?                        channelIndicator;
        private BetterGlamourItemIconNode?                 equipmentIcon;
        private TextNode?                                  equipmentName;
        private TextNode?                                  categoryLabel;
        private TextNode?                                  colorLabel;
        private TextNode?                                  sourceLabel;
        private HorizontalLineNode?                        footerLine;
        private TextButtonNode?                            backButton;
        private TextureButtonNode?                         undoButton;
        private TextureButtonNode?                         redoButton;
        private TextureButtonNode?                         swapButton;
        private byte?                                      redoStain;
        private int                                        redoChannel;
        private int                                        selectedStain;
        private int                                        selectedCategory;
        private int                                        dyeCount              = 2;
        private int                                        visibleRows;
        private bool                                       pendingRowsRebuild;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public BetterGlamourDyePickerNativeUI()
        {
            InternalName          = "OmniBetterGlamourDyePicker";
            Title                 = OmniLoc.Get("Feature.BetterGlamourManagement.Dye");
            Subtitle              = string.Empty;
            Size                  = new(416f, 700f);
            ContentPadding        = new(12f, 8f);
            RememberClosePosition = true;
            CreateWindowNode      = static () => new WindowNode { ShowCloseButton = true };
        }

        public void OpenFor
        (
            BetterGlamourItem glamourItem,
            int               channel,
            Action<int, byte> selected,
            Action<int>       undo,
            Action            swap,
            Func<int, bool>   undoAvailable
        )
        {
            if (!IsOpen || !ReferenceEquals(item, glamourItem))
            {
                originalStains[0] = glamourItem.Stain0;
                originalStains[1] = glamourItem.Stain1;
                redoStain         = null;
            }
            item             = glamourItem;
            onSelected       = selected;
            onUndo           = undo;
            onSwap           = swap;
            canUndo          = undoAvailable;
            dyeCount         = LuminaGetter.TryGetRow<Item>(ItemUtil.GetBaseId(item.ItemID).ItemId, out var equipment)
                ? Math.Clamp((int)equipment.DyeCount, 1, 2)
                : 2;
            selectedStain    = Math.Clamp(channel, 0, dyeCount - 1);
            selectedCategory = GetCategory(selectedStain == 0 ? item.Stain0 : item.Stain1);
            if (!IsOpen)
                Open();
            RebuildRows();
        }

        public void RefreshItem(BetterGlamourItem changedItem)
        {
            if (ReferenceEquals(item, changedItem))
            {
                redoStain          = null;
                pendingRowsRebuild = true;
            }
        }

        protected override unsafe void OnSetup
        (
            AtkUnitBase*   addon,
            Span<AtkValue> atkValueSpan
        )
        {
            var native = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonByName("ColorantColoring");
            if (native is not null && native->IsVisible)
                addon->SetScale(native->Scale / AtkUnitBase.GetGlobalUIScale(), true);
            equipmentBackground = CreateBackground("ui/uld/BgParts.tex", new(34f, 38f), new(26f), 8f);
            paletteBackground   = CreateBackground("ui/uld/BgParts.tex", new(34f, 38f), new(26f), 12f);
            selectionBackground = CreateBackground("ui/uld/BgParts.tex", new(34f, 38f), new(26f), 12f);
            categoryBackground  = CreateBackground("ui/uld/BgParts.tex", new(34f, 38f), new(26f), 8f);
            equipmentLabel      = CreateLabel(LuminaWrapper.GetAddonText(4702));
            equipmentIcon       = new(false) { Size = new(48f) };
            equipmentIcon.AttachNode(this);
            equipmentName = CreateLabel(string.Empty, 14, 2);
            categoryLabel = CreateLabel(LuminaWrapper.GetAddonText(4703));
            colorLabel    = CreateLabel(LuminaWrapper.GetAddonText(15992));
            sourceLabel   = CreateLabel(string.Empty);
            categoryLine  = new()
            {
                TexturePath = "ui/uld/ColorantBgBase.tex",
                TextureCoordinates = new(0f, 10f),
                TextureSize = new(28f, 4f),
                LeftOffset = 8f,
                RightOffset = 8f
            };
            categoryLine.AttachNode(this);
            channelIndicator = new()
            {
                TexturePath = "ui/uld/ColorantBgBase.tex",
                TextureCoordinates = new(0f, 2f),
                TextureSize = new(14f, 4f)
            };
            channelIndicator.AttachNode(this);
            channelFrame = CreateBackground("ui/uld/ToggleButton.tex", new(124f, 40f), new(24f, 26f), 8f);

            for (var index = 0; index < channelNodes.Length; index++)
            {
                var node = new DyeChannelNode(index, () => SelectStain(dyeCount > 1 ? 1 - selectedStain : 0));
                node.AttachNode(this);
                channelNodes[index] = node;
            }
            for (var index = 0; index < CategoryTooltipKeys.Length; index++)
            {
                var category = index;
                var node = new DyeCategoryNode(index < CategoryColors.Length ? CategoryColors[index] : null,
                    () => SelectCategory(category));
                node.CollisionNode.SetTextTooltip(OmniLoc.Get(CategoryTooltipKeys[index]));
                node.AttachNode(this);
                categoryNodes.Add(node);
            }
            for (var index = 0; index < (DyeOptions.Count + SWATCHES_PER_ROW - 1) / SWATCHES_PER_ROW; index++)
            {
                var node = new BetterGlamourDyeGridRowNode { Height = BetterGlamourDyeGridRowNode.ItemHeight };
                node.AttachNode(this);
                rowNodes.Add(node);
            }

            for (var comparison = 0; comparison < 2; comparison++)
            {
                var original = comparison == 1;
                var background = CreateBackground("ui/uld/ConfigLogFilter.tex", new(48f, 0f), new(24f), 10f);
                background.Alpha                  = 63f / 255f;
                background.AddColor               = new(120f / 255f);
                comparisonBackgrounds[comparison] = background;
                comparisonTitles[comparison]      = CreateLabel(LuminaWrapper.GetAddonText(original ? 15989U : 15990U));
                comparisonButtons[comparison]     = CreateTextureButton(original ? 36f : 0f, LuminaWrapper.GetAddonText(original ? 15974U : 15991U),
                    () =>
                    {
                        var stains = original ? originalStains : candidateStains;
                        onSelected?.Invoke(0, stains[0]);
                        if (dyeCount > 1)
                            onSelected?.Invoke(1, stains[1]);
                        redoStain = null;
                    });
                for (var index = 0; index < 2; index++)
                {
                    var channel = index;
                    var preview = new DyeColorPreviewNode();
                    preview.CollisionNode.ShowClickableCursor = true;
                    preview.CollisionNode.AddEvent(AtkEventType.MouseClick, (_, _, _, _, eventData) =>
                    {
                        if (eventData is null || channel >= dyeCount)
                            return;
                        if (eventData->IsRightClick)
                        {
                            if (DalamudServices.KeyState[VirtualKey.CONTROL])
                                Undo(channel);
                        }
                        else if (eventData->IsLeftClick)
                        {
                            onSelected?.Invoke(channel, original ? originalStains[channel] : candidateStains[channel]);
                            SelectStain(channel);
                        }
                    });
                    preview.AttachNode(this);
                    previews[comparison, index] = preview;
                    var label = CreateLabel(LuminaWrapper.GetAddonText(index == 0 ? 15970U : 15971U), color: 2);
                    label.AlignmentType              = AlignmentType.Right;
                    previewLabels[comparison, index] = label;
                }
            }

            undoButton = CreateTextureButton(144f, OmniLoc.Get("Feature.BetterGlamourManagement.DyePicker.Undo"), () => Undo(selectedStain));
            redoButton = CreateTextureButton(108f, OmniLoc.Get("Feature.BetterGlamourManagement.DyePicker.Redo"), Redo);
            swapButton = CreateTextureButton(72f, LuminaWrapper.GetAddonText(15975), () =>
            {
                onSwap?.Invoke();
                redoStain = null;
            });
            for (var index = 0; index < dyeSources.Length; index++)
            {
                var node = new DyeSourceNode();
                node.AttachNode(this);
                dyeSources[index] = node;
            }
            footerLine = new();
            footerLine.AttachNode(this);
            backButton = new()
            {
                String = OmniLoc.Get("Feature.BetterGlamourManagement.DyePicker.Back"),
                OnClick = Close
            };
            backButton.AttachNode(this);
            RebuildRows();
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            if (pendingRowsRebuild)
            {
                pendingRowsRebuild = false;
                RebuildRows();
            }
            ResizeContent();
        }

        protected override unsafe void OnHide(AtkUnitBase* addon)
        {
            RootNode.HideTooltip();
            foreach (var row in rowNodes)
                row.ClearHover();
            item       = null;
            onSelected = null;
            onUndo     = null;
            onSwap     = null;
            canUndo    = null;
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            equipmentLabel      = null;
            equipmentBackground = paletteBackground = selectionBackground = categoryBackground = null;
            channelFrame        = null;
            categoryLine        = channelIndicator = null;
            equipmentIcon       = null;
            equipmentName       = categoryLabel = colorLabel = sourceLabel = null;
            footerLine          = null;
            backButton          = null;
            undoButton          = redoButton = swapButton = null;
            Array.Clear(channelNodes);
            Array.Clear(previews);
            Array.Clear(previewLabels);
            Array.Clear(comparisonBackgrounds);
            Array.Clear(comparisonButtons);
            Array.Clear(comparisonTitles);
            Array.Clear(dyeSources);
            categoryNodes.Clear();
            rowNodes.Clear();
            item               = null;
            onSelected         = null;
            onUndo             = null;
            onSwap             = null;
            canUndo            = null;
            redoStain          = null;
            pendingRowsRebuild = false;
        }

        private void SelectStain(int channel)
        {
            if (channel >= dyeCount || item is null)
                return;
            selectedStain    = channel;
            selectedCategory = GetCategory(channel == 0 ? item.Stain0 : item.Stain1);
            RebuildRows();
        }

        private void SelectCategory(int category)
        {
            selectedCategory = category;
            RebuildRows();
        }

        private void SelectDye(GlamourDyeOption option)
        {
            if (item is null)
                return;
            redoStain = null;
            onSelected?.Invoke(selectedStain, option.ID);
            pendingRowsRebuild = true;
        }

        private unsafe void OpenDyeContextMenu(GlamourDyeOption option)
        {
            if (item is not { } ownerItem || InternalAddon is null)
                return;
            RootNode.HideTooltip();
            foreach (var row in rowNodes)
                row.ClearHover();
            var channel = selectedStain;
            List<ContextMenuItem> entries = [];
            if (dyeCount > 1)
            {
                var otherChannel = 1 - channel;
                entries.Add(new()
                {
                    Name = LuminaWrapper.GetAddonText(otherChannel == 0 ? 15979U : 15980U),
                    OnClicked = _ =>
                    {
                        if (ReferenceEquals(item, ownerItem))
                            onSelected?.Invoke(otherChannel, option.ID);
                    }
                });
            }
            for (var index = 0; index < candidateStains.Length; index++)
            {
                var candidateChannel = index;
                entries.Add(new()
                {
                    Name = LuminaWrapper.GetAddonText(index == 0 ? 15977U : 15978U),
                    OnClicked = _ =>
                    {
                        if (!ReferenceEquals(item, ownerItem))
                            return;
                        candidateStains[candidateChannel] = option.ID;
                        pendingRowsRebuild                = true;
                    }
                });
            }
            entries.Add(new()
            {
                Name = LuminaWrapper.GetAddonText(15988),
                IsEnabled = (channel == 0 ? ownerItem.Stain0 : ownerItem.Stain1) != originalStains[channel],
                OnClicked = _ =>
                {
                    if (ReferenceEquals(item, ownerItem))
                        onSelected?.Invoke(channel, originalStains[channel]);
                }
            });
            var dyeItemID = LuminaGetter.TryGetRow<Stain>(option.ID, out var stain) ? stain.Item[0].RowId : 0;
            ContextMenuManager.Instance().Open(new()
            {
                Addon = InternalAddon,
                AddonName = InternalName,
                OwnerAddonID = InternalAddon->Id,
                TargetItemID = dyeItemID
            }, [new ContextMenuEntryInfo(nameof(BetterGlamourManagement), _ => entries, omitPrefix: true)]);
        }

        private void Undo(int channel)
        {
            if (item is null || canUndo?.Invoke(channel) != true)
                return;
            var stain = channel == 0 ? item.Stain0 : item.Stain1;
            onUndo?.Invoke(channel);
            redoChannel = channel;
            redoStain   = stain;
        }

        private void Redo()
        {
            if (redoStain is not { } stain)
                return;
            onSelected?.Invoke(redoChannel, stain);
            redoStain = null;
        }

        private void RebuildRows()
        {
            if (item is null || equipmentIcon is null || equipmentName is null || sourceLabel is null ||
                undoButton is null || redoButton is null)
                return;
            RootNode.HideTooltip();
            var selectedID = selectedStain == 0 ? item.Stain0 : item.Stain1;
            var options = DyeOptions.Where(option => GetCategory(option.ID) == selectedCategory)
                .OrderBy(option => LuminaGetter.TryGetRow<Stain>(option.ID, out var stain) ? stain.SubOrder : 0)
                .ToList();
            visibleRows = Math.Max(3, (options.Count + SWATCHES_PER_ROW - 1) / SWATCHES_PER_ROW);
            for (var index = 0; index < rowNodes.Count; index++)
            {
                var offset = index * SWATCHES_PER_ROW;
                rowNodes[index].IsVisible = offset < options.Count;
                if (offset < options.Count)
                    rowNodes[index].SetData(new(options.GetRange(offset, Math.Min(SWATCHES_PER_ROW, options.Count - offset)),
                        selectedID, SelectDye, OpenDyeContextMenu, originalStains[selectedStain]));
            }
            for (var index = 0; index < categoryNodes.Count; index++)
                categoryNodes[index].SetSelected(index == selectedCategory, index == GetCategory(originalStains[selectedStain]));
            for (var index = 0; index < 2; index++)
            {
                var stainID = index == 0 ? item.Stain0 : item.Stain1;
                channelNodes[index]?.SetDye(stainID, originalStains[index], index == selectedStain, dyeCount > 1);
                if (channelNodes[index] is { } channelNode)
                    channelNode.IsVisible = index < dyeCount;
                for (var comparison = 0; comparison < 2; comparison++)
                {
                    if (previews[comparison, index] is { } preview)
                    {
                        preview.Alpha = index < dyeCount ? 1f : 0.5f;
                        preview.SetDye(comparison == 0 ? candidateStains[index] : originalStains[index]);
                    }
                    if (previewLabels[comparison, index] is { } label)
                        label.Alpha = index < dyeCount ? 1f : 0.5f;
                }
                if (comparisonButtons[index] is { } button)
                {
                    var stains = index == 0 ? candidateStains : originalStains;
                    button.IsEnabled = item.Stain0 != stains[0] || dyeCount > 1 && item.Stain1 != stains[1];
                }
            }
            equipmentIcon.SetItem(item.ItemID, item.Stain0, item.Stain1);
            equipmentName.String = GetItemName(item.ItemID);
            sourceLabel.String   = string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.DyePicker.Source"), selectedStain + 1);
            undoButton.IsEnabled = canUndo?.Invoke(selectedStain) == true;
            redoButton.IsEnabled = redoStain.HasValue;
            if (swapButton is not null)
                swapButton.IsEnabled = dyeCount > 1 && item.Stain0 != item.Stain1;
            for (var index = 0; index < dyeSources.Length; index++)
            {
                var sourceID = LuminaGetter.TryGetRow<Stain>(selectedID, out var stain) ? stain.Item[index].RowId : 0;
                dyeSources[index]?.SetItem(sourceID);
            }
            ResizeContent();
        }

        private void ResizeContent()
        {
            if (equipmentLabel is null || equipmentBackground is null || paletteBackground is null || selectionBackground is null || categoryBackground is null || channelFrame is null ||
                categoryLine is null || channelIndicator is null ||
                equipmentIcon is null || equipmentName is null || categoryLabel is null || colorLabel is null ||
                sourceLabel is null || undoButton is null || redoButton is null || swapButton is null ||
                footerLine is null || backButton is null)
                return;
            var comparisonY = GRID_TOP + visibleRows * BetterGlamourDyeGridRowNode.ItemHeight;
            var sourceY = comparisonY + 116f;
            var footerY = sourceY + 140f;
            var targetSize = new Vector2(416f, Size.Y - ContentSize.Y + footerY + 38f);
            if (Vector2.DistanceSquared(Size, targetSize) > 0.5f)
                SetWindowSize(targetSize);
            var start = ContentStartPosition - new Vector2(0f, 2f);
            var width = ContentSize.X;
            equipmentLabel.Position      = start + new Vector2(4f, 0f);
            equipmentLabel.Size          = new(width - 8f, 16f);
            equipmentBackground.Position = start + new Vector2(0f, 16f);
            equipmentBackground.Size     = new(width, 84f);
            equipmentIcon.Position       = start + new Vector2(2f, 20f);
            equipmentName.Position       = start + new Vector2(52f, 33f);
            equipmentName.Size           = new(width - 60f, 24f);
            channelFrame.Position        = start + new Vector2(0f, 102f);
            channelFrame.Size            = new(dyeCount > 1 ? width + 1f : width / 2f + 1f, 29f);
            for (var index = 0; index < channelNodes.Length; index++)
            {
                if (channelNodes[index] is { } node)
                {
                    node.Position = start + new Vector2(index * width / 2f, 102f);
                    node.Size     = new(width / 2f, 28f);
                }
            }
            categoryBackground.Position = start + new Vector2(0f, 132f);
            categoryBackground.Size     = new(width, 24f);
            categoryLabel.Position      = start + new Vector2(6f, 132f);
            categoryLabel.Size          = new(width - 12f, 18f);
            categoryLine.Position       = start + new Vector2(0f, 148f);
            categoryLine.Size           = new(width, 4f);
            var indicatorWidth = dyeCount > 1 ? width / 2f : width;
            channelIndicator.Position           = start + new Vector2(selectedStain * indicatorWidth, 148f);
            channelIndicator.Size               = new(indicatorWidth, 4f);
            channelIndicator.TextureCoordinates = new(selectedStain == 0 ? 0f : 14f, 2f);
            channelIndicator.TextureSize        = new(dyeCount > 1 ? 14f : 28f, 4f);
            channelIndicator.LeftOffset         = selectedStain == 0 ? 12f : 0f;
            channelIndicator.RightOffset        = dyeCount == 1 || selectedStain == 1 ? 12f : 0f;
            for (var index = 0; index < categoryNodes.Count; index++)
            {
                categoryNodes[index].Position = start + new Vector2((width - 384f) / 2f + index * 48f, 152f);
                categoryNodes[index].Size     = new(48f);
            }
            paletteBackground.Position   = start + new Vector2(0f, 146f);
            paletteBackground.Size       = new(width, footerY - 146f);
            selectionBackground.Position = start + new Vector2(0f, 202f);
            selectionBackground.Size     = new(width, sourceY + 18f - 202f);
            colorLabel.Position          = start + new Vector2(8f, 202f);
            colorLabel.Size              = new(width - 12f, 18f);
            for (var index = 0; index < rowNodes.Count; index++)
            {
                rowNodes[index].Position = start + new Vector2(8f, GRID_TOP + index * BetterGlamourDyeGridRowNode.ItemHeight);
                rowNodes[index].Width    = SWATCHES_PER_ROW * BetterGlamourDyeGridRowNode.ItemHeight;
            }
            for (var comparison = 0; comparison < 2; comparison++)
            {
                var y = comparisonY + comparison * 44f;
                var inset = comparison * 12f;
                if (comparisonBackgrounds[comparison] is { } background)
                {
                    background.Position = start + new Vector2(80f, y + 12f + inset);
                    background.Size     = new(300f, 44f);
                }
                if (comparisonTitles[comparison] is { } title)
                {
                    title.Position = start + new Vector2(84f, y + inset);
                    title.Size     = new(296f, 18f);
                }
                if (comparisonButtons[comparison] is { } swap)
                {
                    swap.Position = start + new Vector2(84f, y + 16f + inset);
                    swap.Size     = new(36f);
                }
                for (var index = 0; index < 2; index++)
                {
                    if (previewLabels[comparison, index] is { } label && previews[comparison, index] is { } preview)
                    {
                        var x = 124f + index * 128f;
                        label.Position   = start + new Vector2(x, y + 26f + inset);
                        label.Size       = new(88f, 16f);
                        preview.Position = start + new Vector2(x + 92f, y + 18f + inset);
                        preview.Size     = new(30f);
                    }
                }
            }
            undoButton.Position  = start + new Vector2(8f, comparisonY + 16f);
            redoButton.Position  = start + new Vector2(44f, comparisonY + 16f);
            swapButton.Position  = start + new Vector2(8f, comparisonY + 72f);
            undoButton.Size      = redoButton.Size = swapButton.Size = new(36f);
            sourceLabel.Position = start + new Vector2(4f, sourceY);
            sourceLabel.Size     = new(width - 8f, 18f);
            for (var index = 0; index < dyeSources.Length; index++)
            {
                if (dyeSources[index] is { } source)
                {
                    source.Position = start + new Vector2(0f, sourceY + 16f + index * 60f);
                    source.Size     = new(width, 60f);
                }
            }
            footerLine.Position = start + new Vector2(0f, footerY);
            footerLine.Size     = new(width, 2f);
            backButton.Position = start + new Vector2(width - 160f, footerY + 10f);
            backButton.Size     = new(160f, 28f);
        }

        private TextNode CreateLabel
        (
            string text,
            uint   size = 12,
            uint   color = 3
        )
        {
            var node = new TextNode
            {
                String = text,
                FontSize = size,
                TextColor = ColorHelper.GetColor(color),
                TextFlags = TextFlags.Ellipsis | TextFlags.Emboss
            };
            node.AttachNode(this);
            return node;
        }

        private SimpleNineGridNode CreateBackground
        (
            string  path,
            Vector2 coordinates,
            Vector2 textureSize,
            float   inset
        )
        {
            var node = new SimpleNineGridNode
            {
                TexturePath = path,
                TextureCoordinates = coordinates,
                TextureSize = textureSize,
                LeftOffset = inset,
                RightOffset = inset,
                TopOffset = inset,
                BottomOffset = inset
            };
            node.AttachNode(this);
            return node;
        }

        private TextureButtonNode CreateTextureButton
        (
            float  u,
            string tooltip,
            Action clicked
        )
        {
            var node = new TextureButtonNode
            {
                TexturePath = "ui/uld/ColorantButton.tex",
                TextureCoordinates = new(u, 0f),
                TextureSize = new(36f),
                TextTooltip = tooltip,
                OnClick = clicked
            };
            node.AttachNode(this);
            return node;
        }

        private static int GetCategory(byte stainID) =>
            LuminaGetter.TryGetRow<Stain>(stainID, out var stain) && stain.Shade is >= 4 and <= 10 ? stain.Shade - 3 : 0;

        private sealed class DyeCategoryNode : SimpleComponentNode
        {
            private readonly SimpleImageNode fill;
            private readonly SimpleImageNode frame;
            private readonly SimpleImageNode selectedFrame;
            private readonly SimpleImageNode selectedCheck;

            public unsafe DyeCategoryNode
            (
                Vector4? color,
                Action   selected
            )
            {
                fill = CreateImage(color.HasValue ? Vector2.Zero : new(0f, 88f), new(48f));
                if (color is { } tint)
                    fill.Color = tint;
                frame                             = CreateImage(new(48f, 0f), new(48f));
                selectedFrame                     = CreateImage(new(96f, 0f), new(48f));
                selectedCheck                     = CreateImage(new(120f, 64f), new(16f));
                selectedCheck.Position            = new(29f, 4f);
                CollisionNode.ShowClickableCursor = true;
                CollisionNode.AddEvent(AtkEventType.MouseClick, (_, _, _, _, eventData) =>
                {
                    if (eventData is not null && eventData->IsLeftClick)
                        selected();
                });
            }

            public void SetSelected
            (
                bool selected,
                bool original
            )
            {
                selectedFrame.IsVisible = selected;
                selectedCheck.IsVisible = original;
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                fill.Size = frame.Size = selectedFrame.Size = Size;
            }

            private SimpleImageNode CreateImage
            (
                Vector2 coordinates,
                Vector2 size
            )
            {
                var node = new SimpleImageNode
                {
                    TexturePath = "ui/uld/ListColorChooser.tex",
                    TextureCoordinates = coordinates,
                    TextureSize = size,
                    Size = size,
                    WrapMode = WrapMode.Stretch
                };
                node.AttachNode(this);
                return node;
            }
        }

        private sealed class DyeColorPreviewNode : SimpleComponentNode
        {
            private readonly bool                       isChannel;
            private readonly BetterGlamourDyeSwatchNode swatch;

            public DyeColorPreviewNode(bool isChannel = false)
            {
                this.isChannel = isChannel;
                swatch         = new(0) { Size = new(30f) };
                swatch.CollisionNode.RemoveNodeFlags(NodeFlags.HasCollision);
                swatch.AttachNode(this);
                if (isChannel)
                    CollisionNode.RemoveNodeFlags(NodeFlags.HasCollision);
                else
                {
                    CollisionNode.AddEvent(AtkEventType.MouseOver, () => swatch.SetHovered(true));
                    CollisionNode.AddEvent(AtkEventType.MouseOut, swatch.ClearHover);
                }
            }

            public void SetDye(byte stainID)
            {
                swatch.SetPreview(stainID);
                if (!isChannel)
                    CollisionNode.SetTextTooltip(GetDyeName(stainID));
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                swatch.Scale    = Size / (isChannel ? 40f : 30f);
                swatch.Position = isChannel ? Size / 8f : Vector2.Zero;
            }
        }

        private sealed class DyeChannelNode : SimpleComponentNode
        {
            private readonly int                 channel;
            private readonly SimpleNineGridNode  background;
            private readonly SimpleImageNode     bucket;
            private readonly TextNode            label;
            private readonly DyeColorPreviewNode originalPreview;
            private readonly SimpleImageNode     changeArrow;
            private readonly DyeColorPreviewNode preview;

            public unsafe DyeChannelNode
            (
                int    channel,
                Action selected
            )
            {
                this.channel = channel;
                background   = new()
                {
                    TexturePath = "ui/uld/ToggleButton.tex",
                    TextureCoordinates = new(170f, 0f),
                    TextureSize = new(32f),
                    LeftOffset = 8f,
                    RightOffset = 8f,
                    TopOffset = 8f,
                    BottomOffset = 8f
                };
                background.AttachNode(this);
                bucket = new()
                {
                    TexturePath = "ui/uld/ToggleButton.tex",
                    TextureCoordinates = new(281f, 40f),
                    TextureSize = new(18f),
                    Size = new(18f),
                    Position = new(8f, 5f),
                    WrapMode = WrapMode.Stretch
                };
                bucket.AttachNode(this);
                label = new()
                {
                    String = LuminaWrapper.GetAddonText(channel == 0 ? 15970U : 15971U),
                    Position = new(28f, 6f),
                    FontSize = 12,
                    TextColor = ColorHelper.GetColor(50)
                };
                label.AttachNode(this);
                originalPreview = new(true);
                originalPreview.AttachNode(this);
                changeArrow = new()
                {
                    TexturePath = "ui/uld/ToggleButton.tex",
                    TextureCoordinates = new(152f, 116f),
                    TextureSize = new(8f, 10f),
                    Size = new(8f, 10f),
                    WrapMode = WrapMode.Stretch,
                    IsVisible = false
                };
                changeArrow.AttachNode(this);
                preview = new(true);
                preview.AttachNode(this);
                CollisionNode.ShowClickableCursor = true;
                CollisionNode.AddEvent(AtkEventType.MouseClick, (_, _, _, _, eventData) =>
                {
                    if (eventData is not null && eventData->IsLeftClick)
                        selected();
                });
            }

            public void SetDye
            (
                byte stainID,
                byte originalID,
                bool selected,
                bool dual
            )
            {
                background.TextureCoordinates = dual
                    ? new(channel == 0 ? 0f : 25f, selected ? 32f : 0f)
                    : new(selected ? 202f : 170f, 0f);
                background.TextureSize        = new(dual ? 25f : 32f, 32f);
                background.MultiplyColor      = new(selected ? 1f : 0.5f);
                bucket.TextureCoordinates     = new(selected ? 299f : 281f, 40f);
                label.Alpha                   = bucket.Alpha = selected ? 1f : 0.5f;
                originalPreview.SetDye(originalID);
                originalPreview.IsVisible     = changeArrow.IsVisible = stainID != originalID;
                originalPreview.MultiplyColor = preview.MultiplyColor = new(selected ? 1f : 0.5f);
                preview.SetDye(stainID);
                OnSizeChanged();
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                background.Position      = new(channel == 0 ? 2f : 0f, 3f);
                background.Size          = new(MathF.Max(1f, Width - 2f), 24f);
                label.Size               = new(114f, 18f);
                originalPreview.Position = new(Width - 64f, 4f);
                changeArrow.Position     = new(Width - 42f, 9f);
                preview.Position         = new(Width - (changeArrow.IsVisible ? 34f : 64f), 4f);
                originalPreview.Size     = preview.Size = new(20f);
            }
        }

        private sealed class DyeSourceNode : SimpleComponentNode
        {
            private readonly SimpleNineGridNode        background;
            private readonly BetterGlamourItemIconNode icon;
            private readonly SimpleImageNode           emptySlot;
            private readonly TextNode                  name;
            private readonly TextNode                  count;

            public DyeSourceNode()
            {
                background = new()
                {
                    TexturePath = "ui/uld/ListColorChooser.tex",
                    TextureCoordinates = new(80f, 48f),
                    TextureSize = new(40f),
                    Size = new(60f),
                    LeftOffset = 13f,
                    RightOffset = 13f,
                    TopOffset = 13f,
                    BottomOffset = 13f,
                    Alpha = 127f / 255f
                };
                background.AttachNode(this);
                icon = new(false) { Size = new(48f), Position = new(6f, 7f) };
                icon.AttachNode(this);
                emptySlot = new()
                {
                    TexturePath = "ui/uld/DragTargetA.tex",
                    TextureSize = new(44f),
                    Size = new(44f),
                    Position = new(8f, 9f),
                    WrapMode = WrapMode.Tile
                };
                emptySlot.AttachNode(this);
                name = new()
                {
                    Position = new(60f, 22f),
                    FontSize = 14,
                    TextFlags = TextFlags.Ellipsis | TextFlags.Emboss
                };
                name.AttachNode(this);
                count = new()
                {
                    Position = new(60f, 44f),
                    FontSize = 12,
                    TextFlags = TextFlags.MultiLine | TextFlags.WordWrap
                };
                count.AttachNode(this);
                CollisionNode.RemoveNodeFlags(NodeFlags.HasCollision);
            }

            public unsafe void SetItem(uint itemID)
            {
                var hasItem = itemID != 0;
                background.IsVisible = icon.IsVisible = name.IsVisible = count.IsVisible = hasItem;
                emptySlot.IsVisible  = !hasItem;
                if (!hasItem)
                    return;
                icon.SetItem(itemID);
                name.String    = GetItemName(itemID);
                name.TextColor = ColorHelper.GetColor(ItemUtil.GetItemRarityColorType(itemID));
                var inventory = InventoryManager.Instance();
                var quantity = inventory == null ? 0 : inventory->GetInventoryItemCount(itemID, checkEquipped: false, checkArmory: false);
                count.String    = quantity == 0
                    ? OmniLoc.Get("Feature.BetterGlamourManagement.DyePicker.MissingDye")
                    : string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.DyePicker.Quantity"), quantity);
                count.TextColor = quantity == 0 ? ColorHelper.GetColor(15) : ColorHelper.GetColor(2);
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                name.Size = count.Size = new(MathF.Max(1f, Width - 64f), 20f);
            }
        }

        #region 常量

        private const int   SWATCHES_PER_ROW = 12;
        private const float GRID_TOP         = 218f;

        #endregion
    }
}
