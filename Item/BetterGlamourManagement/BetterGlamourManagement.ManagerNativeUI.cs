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
    internal sealed class BetterGlamourManagerNativeUI : NativeAddon
    {
        private readonly Action<BetterGlamourPreset> onSelected;
        private readonly Action<BetterGlamourPreset> onChanged;
        private readonly Action<BetterGlamourPreset, int> onGearsetChanged;
        private readonly Action<BetterGlamourPreset> onApply;
        private readonly Func<BetterGlamourPreset, bool> isTryingOn;
        private readonly Action<BetterGlamourPreset> onExport;
        private readonly Action<BetterGlamourPreset> onDelete;
        private readonly Action onImport;
        private readonly BetterGlamourPortraitPreview portraitPreview;
        private TextureImageNode? previewImage;
        private readonly List<TextureButtonNode> previewButtons = [];
        private readonly List<SimpleImageNode> previewButtonRings = [];
        private SimpleNineGridNode? previewBorder;
        private CollisionNode? previewInput;
        private bool previewDragLeft;
        private bool previewDragRight;
        private bool previewHovered;
        private static readonly (uint NodeID, uint TooltipID, Vector2 Coordinates)[] PreviewButtonData =
        [
            (32, 2424, new(96, 112)),
            (33, 2423, new(72, 112)),
            (34, 2416, new(120, 112)),
            (35, 2422, new(144, 112)),
            (37, 2419, new(48, 112)),
            (39, 3270, new(48, 136))
        ];
        private Vector2 previewLastMouse;
        private nint boundPreviewTexture;
        private readonly List<BetterGlamourEditorRow> editorRows = [];
        private readonly List<GearsetOption> gearsetOptions = [];
        private readonly List<BetterGlamourItemSearchResult> itemSearchResults = [];
        private List<BetterGlamourPreset> presets = [];
        private BetterGlamourPreset? selectedPreset;
        private BetterGlamourItem? selectedDyeItem;
        private bool pendingDyeSelectionRefresh;
        private ListNode<BetterGlamourPreset, BetterGlamourPresetListItemNode>? presetListNode;
        private VerticalLineNode? separatorNode;
        private TextNode? emptyNode;
        private TextNode? nameLabelNode;
        private TextInputNode? nameInputNode;
        private TextNode? gearsetLabelNode;
        private StringDropDownNode? gearsetDropDownNode;
        private TextNode? partHeaderNode;
        private TextNode? itemHeaderNode;
        private TextNode? dyeHeaderNode;
        private ListNode<BetterGlamourEditorRow, BetterGlamourEditorListItemNode>? itemListNode;
        private TextButtonNode? applyButton;
        private TextButtonNode? exportButton;
        private TextButtonNode? deleteButton;
        private TextButtonNode? importButton;
        private BetterGlamourDyePickerNativeUI? dyePicker;
        private SimpleNineGridNode? itemSearchBackgroundNode;
        private ListNode<BetterGlamourItemSearchResult, BetterGlamourItemSearchResultNode>? itemSearchResultsNode;
        private BetterGlamourEditorListItemNode? activeItemSearchRow;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public BetterGlamourManagerNativeUI(
            Action<BetterGlamourPreset> onSelected,
            Action<BetterGlamourPreset> onChanged,
            Action<BetterGlamourPreset, int> onGearsetChanged,
            Action<BetterGlamourPreset> onApply,
            Func<BetterGlamourPreset, bool> isTryingOn,
            Action<BetterGlamourPreset> onExport,
            Action<BetterGlamourPreset> onDelete,
            Action onImport,
            BetterGlamourPortraitPreview portraitPreview)
        {
            InternalName = "OmniBetterGlamourManager";
            Title = OmniLoc.Get("Feature.BetterGlamourManagement.WindowTitle");
            Subtitle = FormatPresetCount(0);
            Size = new(1050f, 650f);
            ContentPadding = new(10f, 8f);
            RememberClosePosition = true;
            CreateWindowNode = static () => new WindowNode { ShowCloseButton = true };
            this.onSelected = onSelected;
            this.onChanged = onChanged;
            this.onGearsetChanged = onGearsetChanged;
            this.onApply = onApply;
            this.isTryingOn = isTryingOn;
            this.onExport = onExport;
            this.onDelete = onDelete;
            this.onImport = onImport;
            this.portraitPreview = portraitPreview;
        }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
        {
            presetListNode = new()
            {
                ItemSpacing = 2f,
                ShowNoResultsPlaceholder = false,
                AutoResetScroll = false,
                OptionsList = presets,
                OnItemSelected = SelectPreset
            };
            presetListNode.AttachNode(this);

            separatorNode = new();
            separatorNode.AttachNode(this);

            emptyNode = new()
            {
                AlignmentType = AlignmentType.Center,
                TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
                String = OmniLoc.Get("Feature.BetterGlamourManagement.Empty")
            };
            emptyNode.AttachNode(this);

            nameLabelNode = CreateLabel("Feature.BetterGlamourManagement.PresetName");
            nameInputNode = new()
            {
                MaxCharacters = 80,
                ShowLimitText = true,
                OnInputReceived = value =>
                {
                    if (selectedPreset is not null)
                    {
                        selectedPreset.Name = value.ExtractText();
                    }
                },
                OnInputComplete = _ => SaveName(),
                OnFocusLost = SaveName
            };
            nameInputNode.AttachNode(this);
            if (WindowNode is WindowNode windowNode)
            {
                windowNode.SubtitleNode.FontType = FontType.MiedingerMed;
                windowNode.SubtitleNode.FontSize = 14;
                windowNode.SubtitleNode.Position = new(windowNode.SubtitleNode.X, 13f);
                windowNode.SubtitleNode.Size = new(64f, 19f);
            }

            gearsetLabelNode = CreateLabel("Feature.BetterGlamourManagement.BindGearset");
            gearsetDropDownNode = new()
            {
                Options = [],
                MaxListOptions = 8,
                OnOptionSelected = SelectGearset
            };
            gearsetDropDownNode.AttachNode(this);

            partHeaderNode = CreateHeader("Feature.BetterGlamourManagement.Column.Part");
            itemHeaderNode = CreateHeader("Feature.BetterGlamourManagement.Column.Item");
            dyeHeaderNode = CreateHeader("Feature.BetterGlamourManagement.Dye");

            itemListNode = new()
            {
                ItemSpacing = 1f,
                ShowNoResultsPlaceholder = false,
                AutoResetScroll = false,
                OptionsList = editorRows
            };
            itemListNode.AttachNode(this);

            applyButton = CreateButton("Feature.BetterGlamourManagement.Apply", () => Invoke(onApply));
            exportButton = CreateButton("Feature.BetterGlamourManagement.SharePreset", () => Invoke(onExport));
            importButton = CreateButton("Feature.BetterGlamourManagement.ImportClipboard", onImport);
            deleteButton = CreateButton("Feature.BetterGlamourManagement.Delete", () => Invoke(onDelete));

            itemSearchBackgroundNode = new()
            {
                TexturePath = "ui/uld/ListB.tex",
                TextureCoordinates = Vector2.Zero,
                TextureSize = new(32f, 32f),
                TopOffset = 10,
                BottomOffset = 12,
                LeftOffset = 10,
                RightOffset = 10
            };
            itemSearchBackgroundNode.AttachNode(this);
            itemSearchBackgroundNode.IsVisible = false;

            itemSearchResultsNode = new()
            {
                ItemSpacing = 1f,
                ShowNoResultsPlaceholder = false,
                AutoResetScroll = false,
                OptionsList = itemSearchResults,
                OnItemSelected = SelectItemSearchResult
            };
            itemSearchResultsNode.AttachNode(this);
            itemSearchResultsNode.IsVisible = false;

            previewBorder = new SimpleNineGridNode
            {
                TexturePath = "ui/uld/PreviewA.tex",
                TextureCoordinates = Vector2.Zero,
                TextureSize = new(36, 36),
                TopOffset = 14,
                BottomOffset = 14,
                LeftOffset = 14,
                RightOffset = 14
            };
            previewBorder.AttachNode(this);
            previewInput = new();
            previewInput.AddEvent(AtkEventType.MouseOver, () => previewHovered = true);
            previewInput.AddEvent(AtkEventType.MouseOut, () => previewHovered = false);
            previewInput.AddEvent(AtkEventType.MouseDown, StartPreviewDrag);
            previewInput.AddEvent(AtkEventType.MouseWheel, (listener, type, param, ev, data) =>
            {
                if (data != null && portraitPreview.Texture != 0)
                    portraitPreview.CameraPixels(0, 0, 0, Math.Sign(data->MouseData.WheelDirection));
            });
            previewInput.AttachNode(this);
            foreach (var buttonData in PreviewButtonData)
            {
                var nativeNode = buttonData.NodeID;
                var ring = new SimpleImageNode
                {
                    TexturePath = "ui/uld/CircleButtons.tex",
                    TextureCoordinates = new(84, 84),
                    TextureSize = new(28, 28),
                    Size = new(28, 28),
                    IsVisible = false
                };
                ring.AttachNode(this);
                previewButtonRings.Add(ring);
                var button = new TextureButtonNode
                {
                    TexturePath = "ui/uld/CircleButtons.tex",
                    TextureCoordinates = buttonData.Coordinates,
                    TextureSize = new(24, 24),
                    Size = new(24, 24),
                    TextTooltip = LuminaWrapper.GetAddonText(buttonData.TooltipID),
                    OnClick = () => portraitPreview.ClickNativeButton(nativeNode)
                };
                button.AttachNode(this);
                previewButtons.Add(button);
            }

            ResizeContent();
            ApplyData();
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            UpdatePortrait();
            UpdatePreviewMouse();
            for (var i = 0; i < previewButtonRings.Count; i++)
                previewButtonRings[i].IsVisible = portraitPreview.NativeButtonChecked(PreviewButtonData[i].NodeID);
            presetListNode?.Update();
            itemListNode?.Update();
            itemSearchResultsNode?.Update();
            UpdateItemSearchResults();
            if (pendingDyeSelectionRefresh)
            {
                pendingDyeSelectionRefresh = false;
                if (selectedPreset is not null)
                {
                    BuildEditorRows();
                    itemListNode?.FullRebuild();
                }
            }
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            ClearPreviewTexture();
            portraitPreview.CancelPreview();
            previewButtons.Clear();
            previewButtonRings.Clear();
            previewInput = null;
            previewBorder = null;
            previewDragLeft = false;
            previewDragRight = false;
            previewHovered = false;
            dyePicker?.Close();
            dyePicker?.Dispose();
            dyePicker = null;
            selectedDyeItem = null;
            pendingDyeSelectionRefresh = false;
            presetListNode = null;
            separatorNode = null;
            emptyNode = null;
            nameLabelNode = null;
            nameInputNode = null;
            gearsetLabelNode = null;
            gearsetDropDownNode = null;
            partHeaderNode = null;
            itemHeaderNode = null;
            dyeHeaderNode = null;
            itemListNode = null;
            itemSearchBackgroundNode = null;
            itemSearchResultsNode = null;
            activeItemSearchRow = null;
            itemSearchResults.Clear();
            applyButton = null;
            exportButton = null;
            deleteButton = null;
            importButton = null;
        }

        private unsafe void StartPreviewDrag()
        {
            var input = UIInputData.Instance();
            if (portraitPreview.Texture == 0 || input == null) return;
            var cursor = input->CursorInputs;
            if (!cursor.IsGameWindowFocused) return;
            var buttons = cursor.MouseButtonHeldFlags | cursor.MouseButtonPressedFlags;
            previewDragLeft = (buttons & MouseButtonFlags.LBUTTON) != 0;
            previewDragRight = (buttons & MouseButtonFlags.RBUTTON) != 0;
            previewLastMouse = new(cursor.PositionX, cursor.PositionY);
        }

        private unsafe void UpdatePreviewMouse()
        {
            var input = UIInputData.Instance();
            if (portraitPreview.Texture == 0 || input == null || !input->CursorInputs.IsGameWindowFocused)
            {
                previewDragLeft = false;
                previewDragRight = false;
                return;
            }
            var cursor = input->CursorInputs;
            if (previewHovered && (cursor.MouseButtonPressedFlags & (MouseButtonFlags.LBUTTON | MouseButtonFlags.RBUTTON)) != 0)
                StartPreviewDrag();
            if ((cursor.MouseButtonHeldFlags & MouseButtonFlags.LBUTTON) == 0) previewDragLeft = false;
            if ((cursor.MouseButtonHeldFlags & MouseButtonFlags.RBUTTON) == 0) previewDragRight = false;
            var position = new Vector2(cursor.PositionX, cursor.PositionY);
            if (previewDragLeft || previewDragRight)
            {
                var delta = position - previewLastMouse;
                portraitPreview.CameraPixels(previewDragLeft ? delta.X : 0,
                    previewDragRight ? delta.X : 0, previewDragRight ? -delta.Y : 0, 0);
            }
            previewLastMouse = position;
        }

        public unsafe void ClearPreviewTexture()
        {
            var oldImage = previewImage;
            var oldTexture = (KernelTexture*)boundPreviewTexture;
            previewImage = null;
            boundPreviewTexture = 0;
            try
            {
                oldImage?.Dispose();
            }
            finally
            {
                if (oldTexture != null)
                    oldTexture->DecRef();
            }
        }

        private unsafe void UpdatePortrait()
        {
            var texture = portraitPreview.Texture;
            if (texture != boundPreviewTexture)
            {
                ClearPreviewTexture();
                if (texture != 0)
                {
                    var nativeTexture = (KernelTexture*)texture;
                    nativeTexture->IncRef();
                    boundPreviewTexture = texture;
                    try
                    {
                        previewImage = new TextureImageNode
                        {
                            IsVisible = true,
                            TextureCoordinates = Vector2.Zero,
                            TextureSize = new(nativeTexture->ActualWidth, nativeTexture->ActualHeight),
                            FitTexture = true
                        };
                        previewImage.SetTexture(nativeTexture);
                        previewImage.AttachNode(this);
                    }
                    catch
                    {
                        ClearPreviewTexture();
                        throw;
                    }
                }
                ResizeContent();
            }
        }

        public void UpdateData(List<BetterGlamourPreset> newPresets, int selectedIndex)
        {
            presets = newPresets;
            selectedPreset = presets.Count == 0
                ? null
                : presets[Math.Clamp(selectedIndex, 0, presets.Count - 1)];
            ApplyData();
        }

        private TextNode CreateLabel(string textKey)
        {
            var node = new TextNode
            {
                AlignmentType = AlignmentType.Left,
                String = OmniLoc.Get(textKey)
            };
            node.AttachNode(this);
            return node;
        }

        private TextNode CreateHeader(string textKey)
        {
            var node = CreateLabel(textKey);
            node.AlignmentType = AlignmentType.Center;
            node.TextColor = ColorHelper.GetColor(50);
            return node;
        }

        private TextButtonNode CreateButton(string textKey, Action onClick)
        {
            var button = new TextButtonNode
            {
                String = OmniLoc.Get(textKey),
                OnClick = onClick
            };
            button.AttachNode(this);
            return button;
        }

        private void SelectPreset(BetterGlamourPreset? preset)
        {
            if (preset is null)
            {
                return;
            }

            selectedPreset = preset;
            selectedDyeItem = null;
            onSelected(preset);
            ApplySelection();
        }

        private void SelectGearset(string label)
        {
            if (selectedPreset is null)
            {
                return;
            }

            foreach (var option in gearsetOptions)
            {
                if (option.Label != label)
                {
                    continue;
                }

                onGearsetChanged(selectedPreset, option.Index);
                presetListNode?.FullRebuild();
                return;
            }
        }

        private void SaveName()
        {
            if (selectedPreset is null || nameInputNode is null)
            {
                return;
            }

            selectedPreset.Name = nameInputNode.String.ExtractText().Trim();
            nameInputNode.String = selectedPreset.Name;
            onChanged(selectedPreset);
            presetListNode?.FullRebuild();
        }

        private void Invoke(Action<BetterGlamourPreset> action)
        {
            if (selectedPreset is not null)
            {
                action(selectedPreset);
            }
        }

        private void ApplyData()
        {
            if (presetListNode is null)
            {
                return;
            }

            Subtitle = FormatPresetCount(presets.Count);
            presetListNode.OptionsList = presets;
            ApplySelection();
        }

        private void ApplySelection()
        {
            dyePicker?.Close();
            selectedDyeItem = null;
            CloseItemSearch();
            UpdateTryOnButton();
            var hasSelection = selectedPreset is not null;
            if (emptyNode is not null)
            {
                emptyNode.IsVisible = !hasSelection;
            }

            SetDetailVisibility(hasSelection);
            if (!hasSelection)
            {
                editorRows.Clear();
                if (itemListNode is not null)
                {
                    itemListNode.OptionsList = editorRows;
                }

                return;
            }

            nameInputNode!.String = selectedPreset!.Name;
            BuildGearsetOptions();
            gearsetDropDownNode!.Options = gearsetOptions.ConvertAll(static option => option.Label);
            gearsetDropDownNode.SelectedOption = gearsetOptions.Find(option => option.Index == selectedPreset.GearsetIndex).Label ??
                                                   gearsetOptions[0].Label;
            BuildEditorRows();
            itemListNode!.OptionsList = editorRows;
        }

        internal void UpdateTryOnButton()
        {
            if (applyButton is not null)
                applyButton.String = OmniLoc.Get(selectedPreset is not null && isTryingOn(selectedPreset)
                    ? "Feature.BetterGlamourManagement.ClearTryOn"
                    : "Feature.BetterGlamourManagement.Apply");
        }

        private void BuildGearsetOptions()
        {
            gearsetOptions.Clear();
            gearsetOptions.Add(new(-1, OmniLoc.Get("Feature.BetterGlamourManagement.Unbound")));
            for (var index = 0; index < 100; index++)
            {
                if (BetterGlamourManagement.TryGetGearsetName(index, out var name))
                {
                    gearsetOptions.Add(new(index, $"{index + 1}. {name}"));
                }
            }
        }

        private void BuildEditorRows()
        {
            editorRows.Clear();
            editorRows.Add(new(
                OmniLoc.Get("Feature.BetterGlamourManagement.Hairstyle"),
                selectedPreset!,
                null,
                false,
                true,
                false,
                null,
                () => onChanged(selectedPreset!),
                FocusItemSearch,
                SearchItems,
                CloseItemSearch));
            AddItemRow("Feature.BetterGlamourManagement.MainHand", 0);
            AddItemRow("Feature.BetterGlamourManagement.OffHand", 1);
            foreach (var part in BetterGlamourManagement.Parts)
            {
                AddItemRow(part.TextKey, part.Slot);
            }

            editorRows.Add(new(
                OmniLoc.Get("Feature.BetterGlamourManagement.Part.Glasses"),
                selectedPreset!,
                null,
                true,
                false,
                false,
                null,
                () => onChanged(selectedPreset!),
                FocusItemSearch,
                SearchItems,
                CloseItemSearch));
        }

        private void AddItemRow(string textKey, int slot)
        {
            var item = BetterGlamourManagement.GetOrCreateItem(selectedPreset!, slot);
            editorRows.Add(new(
                OmniLoc.Get(textKey),
                selectedPreset!,
                item,
                false,
                false,
                ReferenceEquals(item, selectedDyeItem),
                () => OpenDyePicker(item),
                () => onChanged(selectedPreset!),
                FocusItemSearch,
                SearchItems,
                CloseItemSearch));
        }

        private void FocusItemSearch(BetterGlamourEditorListItemNode row)
        {
            if (!ReferenceEquals(activeItemSearchRow, row))
            {
                CloseItemSearch();
                activeItemSearchRow = row;
            }
        }

        private void SearchItems(BetterGlamourEditorListItemNode row, string query)
        {
            if (query.Length == 0 && !ReferenceEquals(activeItemSearchRow, row))
            {
                return;
            }

            FocusItemSearch(row);
            itemSearchResults.Clear();
            itemSearchResults.AddRange(row.Search(query));
            if (itemSearchResultsNode is null)
            {
                return;
            }

            itemSearchResultsNode.OptionsList = itemSearchResults;
            itemSearchResultsNode.IsVisible = itemSearchResults.Count > 0;
            if (itemSearchBackgroundNode is not null)
            {
                itemSearchBackgroundNode.IsVisible = itemSearchResults.Count > 0;
            }
            itemSearchResultsNode.Size = new(
                row.ItemSearchWidth,
                BetterGlamourItemSearchResultNode.ItemHeight * Math.Min(6, itemSearchResults.Count) + 2f);
            PositionItemSearchResults();
        }

        private void SelectItemSearchResult(BetterGlamourItemSearchResult result)
        {
            if (activeItemSearchRow is { } row)
            {
                row.SetItemSearchResult(result);
            }

            CloseItemSearch();
        }

        private void UpdateItemSearchResults()
        {
            if (activeItemSearchRow is null)
            {
                return;
            }

            if (!activeItemSearchRow.IsVisible)
            {
                CloseItemSearch();
                return;
            }

            PositionItemSearchResults();
        }

        private unsafe void PositionItemSearchResults()
        {
            if (activeItemSearchRow is null || itemSearchResultsNode is null)
            {
                return;
            }

            AtkUnitBase* addon = this;
            var scale = addon->GetScale();
            var position = activeItemSearchRow.ItemSearchInputScreenPosition;
            var gap = 2f * scale;
            var resultSize = ((AtkResNode*)itemSearchResultsNode)->GetSize();
            var viewport = ImGui.GetMainViewport();
            var screenStart = viewport.WorkPos - viewport.Pos;
            var screenEnd = screenStart + viewport.WorkSize;
            var y = position.Y + activeItemSearchRow.ItemSearchInputScreenSize.Y + gap;
            if (y + resultSize.Y > screenEnd.Y - gap)
                y = position.Y - resultSize.Y - gap;
            var resultPosition = Vector2.Clamp(new(position.X, y), screenStart,
                Vector2.Max(screenStart, screenEnd - resultSize));
            itemSearchResultsNode.ScreenX = resultPosition.X;
            itemSearchResultsNode.ScreenY = resultPosition.Y;
            if (itemSearchBackgroundNode is not null)
            {
                itemSearchBackgroundNode.Size = new(
                    activeItemSearchRow.ItemSearchWidth,
                    itemSearchResultsNode.Height);
                itemSearchBackgroundNode.ScreenX = itemSearchResultsNode.ScreenX;
                itemSearchBackgroundNode.ScreenY = itemSearchResultsNode.ScreenY;
            }
        }

        private void CloseItemSearch()
        {
            var row = activeItemSearchRow;
            activeItemSearchRow = null;
            itemSearchResults.Clear();
            if (itemSearchResultsNode is not null)
            {
                itemSearchResultsNode.OptionsList = itemSearchResults;
                itemSearchResultsNode.IsVisible = false;
            }

            if (itemSearchBackgroundNode is not null)
            {
                itemSearchBackgroundNode.IsVisible = false;
            }

            row?.ClearItemSearchText();
        }

        private void OpenDyePicker(BetterGlamourItem item)
        {
            if (selectedPreset is not { } owner)
                return;

            dyePicker ??= new();
            selectedDyeItem = item;
            pendingDyeSelectionRefresh = true;
            dyePicker.OpenFor(item, () =>
            {
                if (presets.Contains(owner))
                    onChanged(owner);

                pendingDyeSelectionRefresh = true;
            });
        }

        private void SetDetailVisibility(bool visible)
        {
            if (nameLabelNode is not null)
                nameLabelNode.IsVisible = visible;
            if (nameInputNode is not null)
                nameInputNode.IsVisible = visible;
            if (gearsetLabelNode is not null)
                gearsetLabelNode.IsVisible = visible;
            if (gearsetDropDownNode is not null)
                gearsetDropDownNode.IsVisible = visible;
            if (partHeaderNode is not null)
                partHeaderNode.IsVisible = visible;
            if (itemHeaderNode is not null)
                itemHeaderNode.IsVisible = visible;
            if (dyeHeaderNode is not null)
                dyeHeaderNode.IsVisible = visible;
            if (itemListNode is not null)
                itemListNode.IsVisible = visible;
            if (applyButton is not null)
                applyButton.IsVisible = visible;
            if (exportButton is not null)
                exportButton.IsVisible = visible;
            if (deleteButton is not null)
                deleteButton.IsVisible = visible;
        }

        private void ResizeContent()
        {
            const float PRESET_WIDTH = 190f;
            const float SEPARATOR_GAP = 12f;
            const float LABEL_WIDTH = 72f;
            const float BUTTON_GAP = 8f;
            var contentX = ContentStartPosition.X;
            var contentY = ContentStartPosition.Y;
            var detailX = contentX + PRESET_WIDTH + SEPARATOR_GAP;
            var ratio = portraitPreview.AspectRatio;
            var imageHeight = MathF.Max(100f, ContentSize.Y - 44f);
            var previewWidth = MathF.Min(
                MathF.Max(280f, imageHeight * ratio + 8f),
                MathF.Max(280f, ContentSize.X - PRESET_WIDTH - SEPARATOR_GAP - 420f - 12f));
            var detailWidth = MathF.Max(420f, ContentSize.X - PRESET_WIDTH - SEPARATOR_GAP - previewWidth - 12f);
            var previewX = detailX + detailWidth + 12f;
            var width = MathF.Min(previewWidth - 8f, imageHeight * ratio);
            var imagePosition = new Vector2(previewX + (previewWidth - width) / 2f, contentY + 4f);
            var imageSize = new Vector2(width, width / ratio);
            if (previewImage is not null)
            {
                previewImage.Position = imagePosition;
                previewImage.Size = imageSize;
            }
            if (previewBorder is not null)
            {
                previewBorder.Position = imagePosition - new Vector2(4, 3);
                previewBorder.Size = imageSize + new Vector2(8, 8);
            }
            if (previewInput is not null)
            {
                previewInput.Position = imagePosition;
                previewInput.Size = imageSize;
            }
            var buttonStart = previewX + (previewWidth - previewButtons.Count * 28f) / 2f;
            for (var i = 0; i < previewButtons.Count; i++)
            {
                var buttonPosition = new Vector2(buttonStart + i * 28f, contentY + ContentSize.Y - 28f);
                previewButtons[i].Position = buttonPosition;
                previewButtons[i].Size = new(24, 24);
                previewButtonRings[i].Position = buttonPosition - new Vector2(2, 2);
            }

            if (presetListNode is not null)
            {
                presetListNode.Position = new(contentX, contentY);
                presetListNode.Size = new(PRESET_WIDTH, ContentSize.Y - 36f);
            }

            if (separatorNode is not null)
            {
                separatorNode.Position = new(contentX + PRESET_WIDTH + 4f, contentY);
                separatorNode.Size = new(4f, ContentSize.Y);
            }

            if (emptyNode is not null)
            {
                emptyNode.Position = new(detailX, contentY);
                emptyNode.Size = new(detailWidth, ContentSize.Y);
            }

            PositionLabeledControl(nameLabelNode, nameInputNode, detailX, contentY, detailWidth, LABEL_WIDTH);
            PositionLabeledControl(gearsetLabelNode, gearsetDropDownNode, detailX, contentY + 34f, detailWidth, LABEL_WIDTH);

            var headerY = contentY + 72f;
            var columns = BetterGlamourEditorListItemNode.GetColumns(detailWidth);
            PositionHeader(partHeaderNode, detailX, headerY, columns.PartWidth);
            PositionHeader(itemHeaderNode, detailX + columns.ItemColumnX, headerY, columns.ItemWidth);
            PositionHeader(dyeHeaderNode, detailX + columns.DyeHeaderX, headerY, columns.DyeWidth);

            var buttonY = contentY + ContentSize.Y - 28f;
            if (itemListNode is not null)
            {
                itemListNode.Position = new(detailX, headerY + 24f);
                itemListNode.Size = new(detailWidth, MathF.Max(100f, buttonY - headerY - 34f));
            }

            var buttonWidth = (detailX + detailWidth - contentX - BUTTON_GAP * 3f) / 4f;
            PositionButton(importButton, contentX, buttonY, buttonWidth);
            PositionButton(exportButton, contentX + buttonWidth + BUTTON_GAP, buttonY, buttonWidth);
            PositionButton(applyButton, contentX + (buttonWidth + BUTTON_GAP) * 2f, buttonY, buttonWidth);
            PositionButton(deleteButton, contentX + (buttonWidth + BUTTON_GAP) * 3f, buttonY, buttonWidth);
        }

        private static string FormatPresetCount(int presetCount) =>
            $"{presetCount}/{BetterGlamourManagement.MAX_PRESET_COUNT}";

        private static void PositionLabeledControl(
            TextNode? label,
            NodeBase? control,
            float x,
            float y,
            float width,
            float labelWidth)
        {
            if (label is not null)
            {
                label.Position = new(x, y + 2f);
                label.Size = new(labelWidth, 28f);
            }

            if (control is not null)
            {
                control.Position = new(x + labelWidth + 4f, y + 2f);
                control.Size = new(width - labelWidth - 4f, 28f);
            }
        }

        private static void PositionHeader(TextNode? node, float x, float y, float width)
        {
            if (node is null)
            {
                return;
            }

            node.Position = new(x, y);
            node.Size = new(width, 22f);
        }

        private static void PositionButton(TextButtonNode? button, float x, float y, float width)
        {
            if (button is null)
            {
                return;
            }

            button.Position = new(x, y);
            button.Size = new(width, 28f);
        }

        private readonly record struct GearsetOption(int Index, string Label);
    }

}