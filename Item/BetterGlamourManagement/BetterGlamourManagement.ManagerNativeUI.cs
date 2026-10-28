using Item = Lumina.Excel.Sheets.Item;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using OmenTools.Interop.Game.Lumina;
using OmenTools;
using OmniToolbox.Collections;
using OmniToolbox.Items;
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
        private readonly Action onCreate;
        private readonly BetterGlamourPortraitPreview portraitPreview;
        private readonly ItemPatchMap itemPatchMap;
        private readonly CollectionSourceService collectionSourceService;
        private readonly Func<uint, bool> isInGlamourDresser;
        private readonly Func<uint, bool> isInArmoire;
        private readonly Func<long> getInventoryRevision;
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
            (37, 2425, new(48, 112)),
            (38, 3271, new(24, 136)),
            (39, 3270, new(48, 136))
        ];
        private Vector2 previewLastMouse;
        private nint boundPreviewTexture;
        private readonly List<BetterGlamourEditorPair> editorRows = [];
        private readonly List<GearsetOption> gearsetOptions = [];
        private readonly Dictionary<BetterGlamourItem, uint> previousItems = [];
        private readonly Dictionary<(BetterGlamourItem Item, int Channel), byte> previousDyes = [];
        private List<BetterGlamourPreset> presets = [];
        private BetterGlamourPreset? selectedPreset;
        private bool pendingEditorRefresh;
        private bool pendingGearsetRefresh;
        private ulong gearsetContentID;
        private ListNode<BetterGlamourPreset, BetterGlamourPresetListItemNode>? presetListNode;
        private VerticalLineNode? separatorNode;
        private TextNode? emptyNode;
        private TextNode? nameLabelNode;
        private TextInputNode? nameInputNode;
        private TextNode? gearsetLabelNode;
        private StringDropDownNode? gearsetDropDownNode;
        private ListNode<BetterGlamourEditorPair, BetterGlamourEditorListItemNode>? itemListNode;
        private TextButtonNode? applyButton;
        private TextButtonNode? exportButton;
        private TextButtonNode? deleteButton;
        private TextButtonNode? importButton;
        private TextButtonNode? createButton;
        private BetterGlamourDyePickerNativeUI? dyePicker;
        private BetterGlamourItemPickerNativeUI? itemPicker;
        private BetterGlamourHairColorNativeUI? hairColorPicker;
        private CustomizeData? editorHairAppearance;

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
            Action onCreate,
            BetterGlamourPortraitPreview portraitPreview,
            ItemPatchMap itemPatchMap,
            CollectionSourceService collectionSourceService,
            Func<uint, bool> isInGlamourDresser,
            Func<uint, bool> isInArmoire,
            Func<long> getInventoryRevision)
        {
            InternalName = "OmniBetterGlamourManager";
            Title = OmniLoc.Get("Feature.BetterGlamourManagement.WindowTitle");
            Subtitle = FormatPresetCount(0);
            Size = new(1100f, 600f);
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
            this.onCreate = onCreate;
            this.portraitPreview = portraitPreview;
            this.itemPatchMap = itemPatchMap;
            this.collectionSourceService = collectionSourceService;
            this.isInGlamourDresser = isInGlamourDresser;
            this.isInArmoire = isInArmoire;
            this.getInventoryRevision = getInventoryRevision;
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
                MaxCharacters = 40,
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
                OnOptionSelected = SelectGearset,
                TextTooltip = OmniLoc.Get("Feature.BetterGlamourManagement.BindGearsetHint")
            };
            gearsetDropDownNode.AttachNode(this);

            itemListNode = new()
            {
                ItemSpacing = 0f,
                ShowNoResultsPlaceholder = false,
                AutoResetScroll = false,
                OptionsList = editorRows
            };
            itemListNode.AttachNode(this);

            applyButton = CreateButton("Feature.BetterGlamourManagement.Apply", () => Invoke(onApply));
            exportButton = CreateButton("Feature.BetterGlamourManagement.SharePreset", () => Invoke(onExport));
            importButton = CreateButton("Feature.BetterGlamourManagement.ImportClipboard", onImport);
            createButton = CreateButton("Feature.BetterGlamourManagement.NewPreset", onCreate);
            deleteButton = CreateButton("Feature.BetterGlamourManagement.Delete", () => Invoke(onDelete));

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
                    TextTooltip = DService.Instance().SeStringEvaluator.Evaluate(LuminaWrapper.GetAddonTextSeString(buttonData.TooltipID)),
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
            var contentID = DService.Instance().ClientState.IsLoggedIn ? DService.Instance().PlayerState.ContentId : 0;
            if (pendingGearsetRefresh || contentID != gearsetContentID)
            {
                pendingGearsetRefresh = false;
                gearsetContentID = contentID;
                RefreshGearsets();
                presetListNode?.FullRebuild();
            }
            UpdatePortrait();
            UpdatePreviewMouse();
            for (var i = 0; i < previewButtonRings.Count; i++)
                previewButtonRings[i].IsVisible = portraitPreview.NativeButtonChecked(PreviewButtonData[i].NodeID);
            presetListNode?.Update();
            if (presetListNode is not null)
            {
                var rowWidth = presetListNode.ScrollBarNode.IsEnabled
                    ? presetListNode.ScrollBarNode.X - 4f
                    : presetListNode.Width - 2f;
                foreach (var row in presetListNode.OptionNodes)
                {
                    if (row.Width != rowWidth)
                        row.Width = rowWidth;
                }
            }
            itemListNode?.Update();
            if (itemListNode is not null)
            {
                var rowWidth = itemListNode.ScrollBarNode.IsEnabled
                    ? itemListNode.ScrollBarNode.X - 4f
                    : itemListNode.Width - 2f;
                foreach (var row in itemListNode.OptionNodes)
                {
                    if (row.Width != rowWidth)
                        row.Width = rowWidth;
                }
            }
            var appearance = GetHairAppearance();
            if (appearance?.Tribe != editorHairAppearance?.Tribe || appearance?.Sex != editorHairAppearance?.Sex)
                ClosePickers();
            if (appearance?.Tribe != editorHairAppearance?.Tribe ||
                appearance?.Sex != editorHairAppearance?.Sex ||
                appearance?.HairColor != editorHairAppearance?.HairColor ||
                appearance?.HighlightsColor != editorHairAppearance?.HighlightsColor ||
                appearance?.Highlights != editorHairAppearance?.Highlights)
                pendingEditorRefresh = true;
            if (pendingEditorRefresh)
            {
                pendingEditorRefresh = false;
                if (selectedPreset is not null)
                {
                    BuildEditorRows();
                    if (itemListNode is not null)
                        itemListNode.OptionsList = editorRows;
                }
            }
        }

        protected override unsafe void OnHide(AtkUnitBase* addon)
        {
            ClosePickers();
            previousItems.Clear();
            previousDyes.Clear();
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
            ClosePickers();
            dyePicker?.Dispose();
            itemPicker?.Dispose();
            hairColorPicker?.Dispose();
            dyePicker = null;
            itemPicker = null;
            hairColorPicker = null;
            editorHairAppearance = null;
            pendingEditorRefresh = false;
            pendingGearsetRefresh = false;
            previousItems.Clear();
            previousDyes.Clear();
            presetListNode = null;
            separatorNode = null;
            emptyNode = null;
            nameLabelNode = null;
            nameInputNode = null;
            gearsetLabelNode = null;
            gearsetDropDownNode = null;
            itemListNode = null;
            applyButton = null;
            exportButton = null;
            deleteButton = null;
            importButton = null;
            createButton = null;
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
            previousItems.Clear();
            previousDyes.Clear();
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
            previousItems.Clear();
            previousDyes.Clear();
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
                pendingGearsetRefresh = true;
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
            ClosePickers();
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
            RefreshGearsets();
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
            gearsetOptions.Add(new(-1, OmniLoc.Get("Feature.BetterGlamourManagement.ClearGearsetBindings")));
            var boundIndices = selectedPreset?.GetGearsetIndices(DService.Instance().PlayerState.ContentId);
            for (var index = 0; index < 100; index++)
            {
                if (BetterGlamourManagement.TryGetGearsetName(index, out var name))
                {
                    var mark = boundIndices?.Contains(index) == true ? "[x]" : "[ ]";
                    gearsetOptions.Add(new(index, $"{mark} {index + 1}. {name}"));
                }
            }
        }

        private void RefreshGearsets()
        {
            if (gearsetDropDownNode is null || selectedPreset is not { } preset)
                return;
            BuildGearsetOptions();
            gearsetDropDownNode.Options = gearsetOptions.ConvertAll(static option => option.Label);
            var contentID = DService.Instance().ClientState.IsLoggedIn ? DService.Instance().PlayerState.ContentId : 0;
            gearsetDropDownNode.IsEnabled = contentID != 0;
            var indices = preset.GetGearsetIndices(contentID);
            gearsetDropDownNode.SelectedOption = indices.Count > 0
                ? string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.BoundGearsets"), string.Join(", ", indices.Select(static index => index + 1)))
                : OmniLoc.Get("Feature.BetterGlamourManagement.Unbound");
            gearsetDropDownNode.TextTooltip = preset.GearsetIndex >= 0
                ? string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.LegacyGearsetHint"), preset.GearsetIndex + 1)
                : OmniLoc.Get("Feature.BetterGlamourManagement.BindGearsetHint");
            if (indices.Count > 0)
                gearsetDropDownNode.TextTooltip += $"\n{string.Join("\n", gearsetOptions.Where(option => indices.Contains(option.Index)).Select(static option => option.Label))}";
        }

        private void BuildEditorRows()
        {
            editorRows.Clear();
            editorHairAppearance = GetHairAppearance();
            if (selectedPreset is not { } owner)
                return;
            var hair = new BetterGlamourEditorRow(
                OmniLoc.Get("Feature.BetterGlamourManagement.Hairstyle"),
                owner,
                null,
                BetterGlamourEditorItemKind.Hairstyle,
                () => OpenItemPicker(owner, BetterGlamourEditorItemKind.Hairstyle, null),
                _ => OpenHairColor(owner),
                enabled =>
                {
                    if (!IsCurrentPreset(owner))
                        return;
                    owner.HighlightsEnabled = enabled;
                    NotifyPresetChanged(owner);
                },
                editorHairAppearance,
                ClearItem: () => ClearItemSelection(owner, BetterGlamourEditorItemKind.Hairstyle, null));
            var glasses = new BetterGlamourEditorRow(
                OmniLoc.Get("Feature.BetterGlamourManagement.Part.Glasses"),
                owner,
                null,
                BetterGlamourEditorItemKind.Glasses,
                () => OpenItemPicker(owner, BetterGlamourEditorItemKind.Glasses, null),
                ClearItem: () => ClearItemSelection(owner, BetterGlamourEditorItemKind.Glasses, null));
            editorRows.Add(new(hair, glasses));
            editorRows.Add(new(
                CreateItemRow(owner, "Feature.BetterGlamourManagement.MainHand", 0),
                CreateItemRow(owner, "Feature.BetterGlamourManagement.OffHand", 1)));
            for (var index = 0; index < 5; index++)
                editorRows.Add(new(
                    CreateItemRow(owner, Parts[index].TextKey, Parts[index].Slot),
                    CreateItemRow(owner, Parts[index + 5].TextKey, Parts[index + 5].Slot)));
        }

        private BetterGlamourEditorRow CreateItemRow(BetterGlamourPreset owner, string textKey, int slot)
        {
            var item = GetOrCreateItem(owner, slot);
            return new(
                OmniLoc.Get(textKey),
                owner,
                item,
                BetterGlamourEditorItemKind.Item,
                () => OpenItemPicker(owner, BetterGlamourEditorItemKind.Item, item),
                channel => OpenDyePicker(owner, item, channel),
                ClearDye: channel => SetDye(owner, item, channel, 0),
                ClearItem: () => ClearItemSelection(owner, BetterGlamourEditorItemKind.Item, item),
                UndoItem: () => UndoItem(owner, item),
                UndoDye: channel => UndoDye(owner, item, channel),
                SwapDyes: () => SwapDyes(owner, item));
        }

        private CustomizeData? GetHairAppearance()
        {
            if (selectedPreset is not { } preset || !portraitPreview.TryGetHairAppearance(out var appearance))
                return null;
            appearance.HairColor = preset.HairColor ?? appearance.HairColor;
            appearance.HighlightsColor = preset.HighlightColor ?? appearance.HighlightsColor;
            appearance.Highlights = preset.HighlightsEnabled ?? appearance.Highlights;
            return appearance;
        }

        private bool IsCurrentPreset(BetterGlamourPreset owner) =>
            IsOpen && ReferenceEquals(owner, selectedPreset) && presets.Contains(owner);

        private void NotifyPresetChanged(BetterGlamourPreset owner)
        {
            if (!IsCurrentPreset(owner))
                return;
            onChanged(owner);
            pendingEditorRefresh = true;
            if (hairColorPicker?.IsOpen == true && GetHairAppearance() is { } appearance)
                hairColorPicker.RefreshAppearance(appearance.HairColor, appearance.HighlightsColor);
        }

        private void OpenItemPicker(BetterGlamourPreset owner, BetterGlamourEditorItemKind kind, BetterGlamourItem? item)
        {
            if (!IsCurrentPreset(owner))
                return;
            dyePicker?.Close();
            hairColorPicker?.Close();
            itemPicker ??= new(itemPatchMap, collectionSourceService, isInGlamourDresser, isInArmoire, getInventoryRevision);
            var currentID = kind switch
            {
                BetterGlamourEditorItemKind.Hairstyle => owner.HairstyleID,
                BetterGlamourEditorItemKind.Glasses => owner.GlassesID,
                _ => item?.ItemID ?? 0
            };
            itemPicker.OpenFor(kind, item?.Slot ?? -1, currentID, GetHairAppearance(),
                result => SetItemSelection(owner, kind, item, result.ItemID));
        }

        private void SetItemSelection(BetterGlamourPreset owner, BetterGlamourEditorItemKind kind, BetterGlamourItem? item, uint itemID)
        {
            if (!IsCurrentPreset(owner))
                return;
            switch (kind)
            {
                case BetterGlamourEditorItemKind.Hairstyle:
                    owner.HairstyleID = itemID;
                    break;
                case BetterGlamourEditorItemKind.Glasses:
                    owner.GlassesID = (ushort)itemID;
                    break;
                case BetterGlamourEditorItemKind.Item when item is not null:
                    if (itemID != 0 &&
                        (!LuminaGetter.TryGetRow<Item>(itemID, out var itemRow) || !FitsSlot(itemRow, item.Slot)))
                        return;
                    if (item.ItemID == itemID)
                        return;
                    previousItems[item] = item.ItemID;
                    item.ItemID = itemID;
                    break;
            }
            NotifyPresetChanged(owner);
        }

        private void ClearItemSelection(BetterGlamourPreset owner, BetterGlamourEditorItemKind kind, BetterGlamourItem? item)
        {
            if (!IsCurrentPreset(owner))
                return;
            SetItemSelection(owner, kind, item, 0);
            itemPicker?.ClearSelection(kind, item?.Slot ?? -1);
        }

        private void OpenDyePicker(BetterGlamourPreset owner, BetterGlamourItem item, int channel)
        {
            if (!IsCurrentPreset(owner))
                return;
            itemPicker?.Close();
            hairColorPicker?.Close();
            dyePicker ??= new();
            dyePicker.OpenFor(item, channel,
                (selectedChannel, stainID) => SetDye(owner, item, selectedChannel, stainID),
                selectedChannel => UndoDye(owner, item, selectedChannel),
                () => SwapDyes(owner, item),
                selectedChannel => previousDyes.ContainsKey((item, selectedChannel)));
        }

        private void UndoItem(BetterGlamourPreset owner, BetterGlamourItem item)
        {
            if (!IsCurrentPreset(owner) || !previousItems.Remove(item, out var itemID))
                return;
            item.ItemID = itemID;
            itemPicker?.Close();
            NotifyPresetChanged(owner);
        }

        private void SetDye(BetterGlamourPreset owner, BetterGlamourItem item, int channel, byte stainID)
        {
            if (!IsCurrentPreset(owner))
                return;
            var previous = channel == 0 ? item.Stain0 : item.Stain1;
            if (previous == stainID)
                return;
            previousDyes[(item, channel)] = previous;
            if (channel == 0)
                item.Stain0 = stainID;
            else
                item.Stain1 = stainID;
            dyePicker?.RefreshItem(item);
            NotifyPresetChanged(owner);
        }

        private void UndoDye(BetterGlamourPreset owner, BetterGlamourItem item, int channel)
        {
            if (!IsCurrentPreset(owner) || !previousDyes.Remove((item, channel), out var stainID))
                return;
            if (channel == 0)
                item.Stain0 = stainID;
            else
                item.Stain1 = stainID;
            dyePicker?.RefreshItem(item);
            NotifyPresetChanged(owner);
        }

        private void SwapDyes(BetterGlamourPreset owner, BetterGlamourItem item)
        {
            if (!IsCurrentPreset(owner) || item.Stain0 == item.Stain1)
                return;
            previousDyes[(item, 0)] = item.Stain0;
            previousDyes[(item, 1)] = item.Stain1;
            (item.Stain0, item.Stain1) = (item.Stain1, item.Stain0);
            dyePicker?.RefreshItem(item);
            NotifyPresetChanged(owner);
        }

        private void OpenHairColor(BetterGlamourPreset owner)
        {
            if (!IsCurrentPreset(owner) || GetHairAppearance() is not { } appearance ||
                !TryGetHairPalette(appearance.Tribe, appearance.Sex, false, out var hairColors) ||
                !TryGetHairPalette(appearance.Tribe, appearance.Sex, true, out var highlightColors) ||
                !portraitPreview.TryGetHairAppearance(out var originalAppearance))
                return;
            itemPicker?.Close();
            dyePicker?.Close();
            hairColorPicker ??= new();
            hairColorPicker.OpenFor(hairColors, highlightColors, appearance.HairColor, appearance.HighlightsColor,
                originalAppearance.HairColor, originalAppearance.HighlightsColor,
                (channel, color) =>
                {
                    if (!IsCurrentPreset(owner))
                        return;
                    if (channel == 0)
                        owner.HairColor = color;
                    else
                        owner.HighlightColor = color;
                    NotifyPresetChanged(owner);
                });
        }

        private void ClosePickers()
        {
            itemPicker?.Close();
            dyePicker?.Close();
            hairColorPicker?.Close();
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
            const float PRESET_WIDTH = 214f;
            const float SEPARATOR_GAP = 4f;
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
                separatorNode.Position = new(contentX + PRESET_WIDTH, contentY);
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
            var buttonY = contentY + ContentSize.Y - 28f;
            if (itemListNode is not null)
            {
                itemListNode.Position = new(detailX, headerY);
                itemListNode.Size = new(detailWidth, MathF.Max(100f, buttonY - headerY - 10f));
            }

            var buttonWidth = (detailX + detailWidth - contentX - BUTTON_GAP * 4f) / 5f;
            PositionButton(createButton, contentX, buttonY, buttonWidth);
            PositionButton(importButton, contentX + buttonWidth + BUTTON_GAP, buttonY, buttonWidth);
            PositionButton(exportButton, contentX + (buttonWidth + BUTTON_GAP) * 2f, buttonY, buttonWidth);
            PositionButton(applyButton, contentX + (buttonWidth + BUTTON_GAP) * 3f, buttonY, buttonWidth);
            PositionButton(deleteButton, contentX + (buttonWidth + BUTTON_GAP) * 4f, buttonY, buttonWidth);
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
