using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Collections;
using OmniToolbox.Items;
using Item = Lumina.Excel.Sheets.Item;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed partial class BetterGlamourItemPickerNativeUI : NativeAddon
    {
        private static ItemSortMode              LastSortMode            = ItemSortMode.ID;
        private static bool                      LastDescending          = true;
        private readonly ItemPatchMap            itemPatchMap;
        private readonly CollectionSourceService collectionSourceService;
        private readonly Func<uint, bool>        isInGlamourDresser;
        private readonly Func<uint, bool>        isInArmoire;
        private readonly Func<long>              getInventoryRevision;

        private BetterGlamourEditorItemKind                                                 kind;
        private int                                                                         targetSlot;
        private BetterGlamourItemSearchResult[]                                             candidates    = [];
        private List<BetterGlamourItemSearchResult>                                         results       = [];
        private Action<BetterGlamourItemSearchResult>?                                      onSelected;
        private TextInputNode?                                                              searchNode;
        private DropDownNode<ItemSortMode>?                                                 sortNode;
        private DropDownNode<int>?                                                          filterNode;
        private CircleButtonNode?                                                           directionNode;
        private ListNode<BetterGlamourItemSearchResult, BetterGlamourItemSearchResultNode>? resultsNode;
        private ItemSortMode                                                                sortMode;
        private bool                                                                        descending;
        private uint                                                                        selectedID;
        private string                                                                      query         = string.Empty;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public BetterGlamourItemPickerNativeUI
        (
            ItemPatchMap            itemPatchMap,
            CollectionSourceService collectionSourceService,
            Func<uint, bool>        isInGlamourDresser,
            Func<uint, bool>        isInArmoire,
            Func<long>              getInventoryRevision
        )
        {
            this.itemPatchMap            = itemPatchMap;
            this.collectionSourceService = collectionSourceService;
            this.isInGlamourDresser      = isInGlamourDresser;
            this.isInArmoire             = isInArmoire;
            this.getInventoryRevision    = getInventoryRevision;
            InternalName                 = "OmniBetterGlamourItemPicker";
            Title                        = OmniLoc.Get("Feature.BetterGlamourManagement.ItemPicker.Title");
            Subtitle                     = string.Empty;
            Size                         = new(300f, 516f);
            ContentPadding               = new(10f, 8f);
            RememberClosePosition        = true;
            CreateWindowNode             = static () => new WindowNode { ShowCloseButton = true };
        }

        public void OpenFor
        (
            BetterGlamourEditorItemKind           itemKind,
            int                                   slot,
            uint                                  currentID,
            CustomizeData?                        hairAppearance,
            Action<BetterGlamourItemSearchResult> selected
        )
        {
            kind       = itemKind;
            targetSlot = slot;
            selectedID = currentID;
            onSelected = selected;
            query      = string.Empty;
            descending = LastDescending;
            sortMode   = IsSortModeAvailable(LastSortMode) ? LastSortMode : ItemSortMode.ID;

            var options = kind switch
            {
                BetterGlamourEditorItemKind.Hairstyle => hairAppearance is { } appearance
                    ? SearchHairstyles(string.Empty, appearance)
                    : [],
                BetterGlamourEditorItemKind.Glasses => SearchGlasses(string.Empty),
                _ => SearchItems(string.Empty)
            };
            candidates = [.. options.Where(option => option.ItemID != 0 &&
                (kind != BetterGlamourEditorItemKind.Item ||
                 LuminaGetter.TryGetRow<Item>(option.ItemID, out var item) && FitsSlot(item, slot)))];
            BuildCandidateMetadata();

            if (kind == BetterGlamourEditorItemKind.Hairstyle)
            {
                if (selectedID == 0 && hairAppearance is { } appearance)
                    selectedID = ResolveInspectHairstyleID(appearance);
                if (!candidates.Any(option => option.ItemID == selectedID) &&
                    LuminaGetter.TryGetRow<CharaMakeCustomize>(selectedID, out var selectedHair))
                {
                    selectedID = candidates.FirstOrDefault(option =>
                        LuminaGetter.TryGetRow<CharaMakeCustomize>(option.ItemID, out var hairstyle) &&
                        (selectedHair.UnlockLink != 0
                            ? hairstyle.UnlockLink == selectedHair.UnlockLink
                            : hairstyle.FeatureID == selectedHair.FeatureID)).ItemID;
                }
            }

            if (!IsOpen)
                Open();
            ResizeContent();

            if (searchNode is not null)
                searchNode.String = string.Empty;
            if (sortNode is not null)
            {
                sortNode.Collapse(false);
                sortNode.Options        = [.. Enum.GetValues<ItemSortMode>().Where(IsSortModeAvailable)];
                sortNode.SelectedOption = sortMode;
            }
            UpdateFilterControls();
            RebuildResults(true);
        }

        protected override unsafe void OnSetup
        (
            AtkUnitBase*   addon,
            Span<AtkValue> atkValueSpan
        )
        {
            searchNode = new()
            {
                MaxCharacters = 0,
                ShowLimitText = false,
                PlaceholderString = OmniLoc.Get("Feature.BetterGlamourManagement.ItemPicker.Search"),
                OnInputReceived = value =>
                {
                    var newQuery = value.ExtractText().Trim();
                    if (query == newQuery)
                        return;

                    query = newQuery;
                    RebuildResults();
                },
                OnEscapeEntered = Close
            };
            searchNode.Component->SetMaxChar(0);
            searchNode.Component->SetMaxByte(0);
            searchNode.Data->Nodes[15]                        = 0;
            searchNode.Component->ComponentTextData.Nodes[15] = 0;
            searchNode.Component->AvailableLinesTextNode      = null;
            searchNode.Component->AvailableCharsTextNode      = null;
            searchNode.AttachNode(this);

            sortNode = new()
            {
                Options = [.. Enum.GetValues<ItemSortMode>().Where(IsSortModeAvailable)],
                MaxListOptions = 7,
                GetLabelFunction = mode => OmniLoc.Get(mode switch
                {
                    ItemSortMode.Name => "Feature.BetterGlamourManagement.ItemPicker.SortName",
                    ItemSortMode.Level => "Feature.BetterGlamourManagement.ItemPicker.SortLevel",
                    ItemSortMode.Patch => "Collection.Filter.Patch",
                    ItemSortMode.Source => "Collection.Filter.Source",
                    ItemSortMode.GlamourDresser => "Collection.Filter.GlamourDresser",
                    ItemSortMode.Armoire => "Collection.Filter.Armoire",
                    _ => "Feature.BetterGlamourManagement.ItemPicker.SortID"
                }),
                SelectedOption = sortMode,
                OnOptionSelected = SelectSort,
                OnUncollapsed = () => filterNode?.Collapse(false),
                OnCollapsed = () =>
                {
                    if (sortNode is not null)
                        sortNode.SelectedOption = sortMode;
                }
            };
            sortNode.AttachNode(this);

            filterNode = new()
            {
                PlaceholderString = string.Empty,
                MaxListOptions = 8,
                GetLabelFunction = value => sortMode switch
                {
                    ItemSortMode.Patch => value == 1 ? "1.0" : $"{value}.x",
                    ItemSortMode.Source => OmniLoc.Get($"Collection.Source.{(CollectionSourceCategory)value}"),
                    _ => string.Empty
                },
                OnOptionSelected = SelectFilter,
                OnUncollapsed = () => sortNode?.Collapse(false)
            };
            filterNode.AttachNode(this);
            directionNode = new()
            {
                Icon = CircleButtonIcon.Sort,
                OnClick = () =>
                {
                    descending ^= true;
                    LastDescending = descending;
                    UpdateDirectionButton();
                    RebuildResults(true);
                }
            };
            directionNode.AttachNode(this);

            resultsNode                          = new()
            {
                ItemSpacing = 0f,
                ShowNoResultsPlaceholder = true,
                AutoResetScroll = true,
                OptionsList = results,
                OnItemSelected = SelectResult
            };
            resultsNode.NoResultsTextNode.String = OmniLoc.Get("Feature.BetterGlamourManagement.ItemPicker.NoResults");
            resultsNode.AttachNode(this);

            SetWindowSize(new(Size.X, Size.Y - ContentSize.Y + 66f + VISIBLE_ITEM_COUNT * BetterGlamourItemSearchResultNode.ItemHeight + 4f));
            ResizeContent();
            UpdateFilterControls();
            RebuildResults(true);
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            ResizeContent();
            if (sortMode == ItemSortMode.Source && sourceRevision != collectionSourceService.Revision)
                RebuildResults();
            if (sortMode is ItemSortMode.GlamourDresser or ItemSortMode.Armoire && inventoryRevision != getInventoryRevision())
                RebuildResults();
            resultsNode?.Update();
        }

        protected override unsafe void OnHide(AtkUnitBase* addon)
        {
            resultsNode?.HideTooltip();
            onSelected = null;
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            searchNode    = null;
            sortNode      = null;
            filterNode    = null;
            directionNode = null;
            resultsNode   = null;
            onSelected    = null;
            candidates    = [];
            candidateMetadata.Clear();
            results.Clear();
            query = string.Empty;
        }

        private void SelectSort(ItemSortMode mode)
        {
            if (!IsSortModeAvailable(mode) || mode == sortMode)
                return;

            sortMode       = mode;
            LastSortMode   = mode;
            descending     = true;
            LastDescending = descending;
            UpdateFilterControls();
            RebuildResults(true);
        }

        private void RebuildResults(bool revealSelection = false)
        {
            resultsNode?.HideTooltip();
            results = SearchItems(query, candidates);
            FilterResults();
            results.Sort((left, right) =>
            {
                if (descending)
                    (left, right) = (right, left);
                var comparison = sortMode switch
                {
                    ItemSortMode.Name => StringComparer.Ordinal.Compare(left.Name, right.Name),
                    ItemSortMode.Level => left.EquipLevel.CompareTo(right.EquipLevel),
                    ItemSortMode.Patch => candidateMetadata[left.ItemID].Patch.CompareTo(candidateMetadata[right.ItemID].Patch),
                    _ => left.ItemID.CompareTo(right.ItemID)
                };
                return comparison != 0 ? comparison : left.ItemID.CompareTo(right.ItemID);
            });

            if (resultsNode is null)
                return;

            resultsNode.NoResultsTextNode.String = OmniLoc.Get(sortMode is ItemSortMode.GlamourDresser or ItemSortMode.Armoire
                ? "Feature.BetterGlamourManagement.ItemPicker.NoStoredResults"
                : "Feature.BetterGlamourManagement.ItemPicker.NoResults");
            resultsNode.OptionsList              = results;
            UpdateScrollThumb();
            resultsNode.ClearSelection();
            var selected = results.FindIndex(result => result.ItemID == selectedID);
            if (selected >= 0)
            {
                resultsNode.SelectedItems.Add(results[selected]);
                if (revealSelection)
                {
                    var visibleCount = resultsNode.OptionNodes.Count;
                    var firstVisible = Math.Clamp(selected - visibleCount / 2, 0, Math.Max(0, results.Count - visibleCount));
                    var position = (int)(firstVisible * (BetterGlamourItemSearchResultNode.ItemHeight + resultsNode.ItemSpacing));
                    resultsNode.ScrollBarNode.ScrollPosition = position;
                    resultsNode.ScrollBarNode.OnValueChanged?.Invoke(position);
                }
            }
        }

        private void SelectResult(BetterGlamourItemSearchResult result)
        {
            var selected = onSelected;
            if (selected is null)
                return;

            selected(result);
            selectedID = result.ItemID;
            if (resultsNode is not null)
            {
                resultsNode.ClearSelection();
                if (result.ItemID != 0)
                    resultsNode.SelectedItems.Add(result);
            }
        }

        public void ClearSelection
        (
            BetterGlamourEditorItemKind itemKind,
            int                         slot
        )
        {
            if (!IsOpen || kind != itemKind || targetSlot != slot)
                return;
            selectedID = 0;
            resultsNode?.ClearSelection();
        }

        private void ResizeContent()
        {
            var x = ContentStartPosition.X;
            var y = ContentStartPosition.Y;
            var width = ContentSize.X;
            var listPosition = new Vector2(x, y + 66f);
            var listHeight = VISIBLE_ITEM_COUNT * BetterGlamourItemSearchResultNode.ItemHeight;
            var listSize = new Vector2(width, listHeight);
            if (resultsNode is null || resultsNode.Position == listPosition && resultsNode.Size == listSize)
                return;

            sortNode?.Collapse(false);
            filterNode?.Collapse(false);
            if (searchNode is not null)
            {
                searchNode.Position = new(x, y);
                searchNode.Size     = new(width, 28f);
            }
            if (sortNode is not null)
            {
                sortNode.Position = new(x, y + 34f);
                sortNode.Size     = new((width - 40f) / 2f, 28f);
            }
            if (filterNode is not null)
            {
                filterNode.Position = new(x + (width - 40f) / 2f + 4f, y + 34f);
                filterNode.Size     = new((width - 40f) / 2f, 28f);
            }
            if (directionNode is not null)
            {
                directionNode.Position = new(x + width - 28f, y + 34f);
                directionNode.Size     = new(28f, 28f);
            }
            resultsNode.Position = listPosition;
            resultsNode.Size     = listSize;
            UpdateScrollThumb();
        }

        private unsafe void UpdateScrollThumb()
        {
            if (resultsNode is null)
                return;

            var scrollBar = resultsNode.ScrollBarNode;
            if (scrollBar.ScrollMaxPosition <= 0)
                return;

            var thumbHeight = MathF.Min(scrollBar.Height, MathF.Max(24f, scrollBar.ForegroundButtonNode.Height));
            scrollBar.ForegroundButtonNode.Height = thumbHeight;
            scrollBar.Component->EmptyLength      = scrollBar.Component->ScrollbarLength - (int)thumbHeight;
            scrollBar.ScrollPosition              = scrollBar.ScrollPosition;
        }

        private enum ItemSortMode
        {
            Name,
            ID,
            Level,
            Patch,
            Source,
            GlamourDresser,
            Armoire
        }

        #region 常量

        private const int VISIBLE_ITEM_COUNT = 8;

        #endregion
    }
}
