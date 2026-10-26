using TreeToolKit;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Collections;
using OmniToolbox.Items;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed partial class BetterGlamourItemPickerNativeUI
    {
        private static int LastPatchMajor = 7;
        private static CollectionSourceCategory LastSource = CollectionSourceCategory.Gil;
        private static Dictionary<uint, uint>? GlassesUnlockItems;
        private readonly Dictionary<uint, CandidateMetadata> candidateMetadata = [];
        private long sourceRevision;
        private long inventoryRevision;

        private bool IsSortModeAvailable(ItemSortMode mode) =>
            kind == BetterGlamourEditorItemKind.Item ||
            mode is not (ItemSortMode.Level or ItemSortMode.GlamourDresser or ItemSortMode.Armoire);

        private void BuildCandidateMetadata()
        {
            candidateMetadata.Clear();
            if (kind == BetterGlamourEditorItemKind.Glasses && GlassesUnlockItems is null)
            {
                GlassesUnlockItems = [];
                foreach (var item in LuminaGetter.Get<Item>())
                {
                    if (item.ItemAction.ValueNullable is { } action && action.Action.RowId == GLASSES_ACTION_ID)
                        GlassesUnlockItems.TryAdd(item.AdditionalData.RowId, item.RowId);
                }
            }

            foreach (var candidate in candidates)
            {
                var itemID = kind switch
                {
                    BetterGlamourEditorItemKind.Hairstyle =>
                        LuminaGetter.TryGetRow<CharaMakeCustomize>(candidate.ItemID, out var hair)
                            ? HairstyleData.GetUnlockItem(hair)?.RowId ?? 0
                            : 0,
                    BetterGlamourEditorItemKind.Glasses => GlassesUnlockItems?.GetValueOrDefault(candidate.ItemID) ?? 0,
                    _ => candidate.ItemID
                };
                candidateMetadata[candidate.ItemID] = new(itemID, itemPatchMap.Get(itemID));
            }
        }

        private void UpdateFilterControls()
        {
            if (filterNode is null)
                return;

            filterNode.Collapse(false);
            filterNode.Options = sortMode switch
            {
                ItemSortMode.Patch => [7, 6, 5, 4, 3, 2, 1],
                ItemSortMode.Source => [.. SourceCategories.Select(category => (int)category)],
                _ => []
            };
            filterNode.IsEnabled = sortMode is ItemSortMode.Patch or ItemSortMode.Source;
            filterNode.Alpha = filterNode.IsEnabled ? 1f : 0.4f;
            filterNode.SelectedOption = sortMode == ItemSortMode.Patch ? LastPatchMajor : (int)LastSource;
            UpdateDirectionButton();
        }

        private void UpdateDirectionButton()
        {
            if (directionNode is null)
                return;

            directionNode.CollisionNode.SetTextTooltip(OmniLoc.Get(descending
                ? "Feature.BetterGlamourManagement.ItemPicker.Descending"
                : "Feature.BetterGlamourManagement.ItemPicker.Ascending"));
        }

        private void SelectFilter(int value)
        {
            if (sortMode == ItemSortMode.Patch)
                LastPatchMajor = value;
            else if (sortMode == ItemSortMode.Source)
                LastSource = (CollectionSourceCategory)value;
            else
                return;

            RebuildResults(true);
        }

        private void FilterResults()
        {
            sourceRevision = collectionSourceService.Revision;
            inventoryRevision = getInventoryRevision();
            if (sortMode == ItemSortMode.Patch)
            {
                results.RemoveAll(result =>
                    candidateMetadata[result.ItemID].Patch <= 0 ||
                    decimal.ToInt32(decimal.Truncate(candidateMetadata[result.ItemID].Patch)) != LastPatchMajor);
            }
            else if (sortMode == ItemSortMode.Source)
            {
                results.RemoveAll(result =>
                {
                    var itemID = candidateMetadata[result.ItemID].ItemID;
                    return !collectionSourceService.GetItemCategories(itemID).Contains(LastSource) &&
                           !collectionSourceService.GetItemSources(itemID).Any(source => source.Category == LastSource);
                });
            }
            else if (sortMode == ItemSortMode.GlamourDresser)
            {
                results.RemoveAll(result => !isInGlamourDresser(result.ItemID));
            }
            else if (sortMode == ItemSortMode.Armoire)
            {
                results.RemoveAll(result => !isInArmoire(result.ItemID));
            }
        }

        private readonly record struct CandidateMetadata(uint ItemID, decimal Patch);

        private static readonly CollectionSourceCategory[] SourceCategories =
        [
            CollectionSourceCategory.Gil,
            CollectionSourceCategory.MGP,
            CollectionSourceCategory.Duty,
            CollectionSourceCategory.Event,
            CollectionSourceCategory.Quest,
            CollectionSourceCategory.PvP,
            CollectionSourceCategory.BeastTribes,
            CollectionSourceCategory.MogStation,
            CollectionSourceCategory.Tomestones,
            CollectionSourceCategory.Achievement,
            CollectionSourceCategory.CompanySeals,
            CollectionSourceCategory.Scrips,
            CollectionSourceCategory.Fate,
            CollectionSourceCategory.TheHunt,
            CollectionSourceCategory.Crafting,
            CollectionSourceCategory.RestorationZone,
            CollectionSourceCategory.TreasureHunts,
            CollectionSourceCategory.DeepDungeon,
            CollectionSourceCategory.FieldOperations,
            CollectionSourceCategory.IslandSanctuary,
            CollectionSourceCategory.Submarine,
            CollectionSourceCategory.Container,
            CollectionSourceCategory.Shop,
            CollectionSourceCategory.Other
        ];

        #region 常量

        private const uint GLASSES_ACTION_ID = 37312;

        #endregion
    }
}
