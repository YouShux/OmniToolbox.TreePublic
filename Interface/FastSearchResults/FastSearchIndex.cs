using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Utils.FuzzyMatcher;

namespace OmniToolbox.TreePublic;

internal sealed class FastSearchIndex : IDisposable
{
    private const int RecipeResultLimit = 3_000;

    private static readonly Comparison<MarketEntry> CompareMarketEntries =
        static (left, right) => left.ItemID.CompareTo(right.ItemID);

    private static readonly Comparison<RecipeEntry> CompareRecipeEntries =
        static (left, right) => left.RecipeIds[0].CompareTo(right.RecipeIds[0]);

    private readonly FuzzyMatcher<MarketEntry> marketMatcher;
    private readonly FuzzyMatcher<RecipeEntry> recipeMatcher;

    public FastSearchIndex()
    {
        var recipeIDsByItemID = new Dictionary<uint, List<uint>>();
        foreach (var recipe in LuminaGetter.Get<Recipe>())
        {
            if (recipe.RowId == 0 ||
                recipe.RecipeLevelTable.RowId == 0 ||
                recipe.ItemResult.RowId == 0)
            {
                continue;
            }

            if (!recipeIDsByItemID.TryGetValue(recipe.ItemResult.RowId, out var recipeIds))
            {
                recipeIds = [];
                recipeIDsByItemID.Add(recipe.ItemResult.RowId, recipeIds);
            }

            recipeIds.Add(recipe.RowId);
        }

        List<MarketEntry> marketEntries = [];
        List<RecipeEntry> recipeEntries = [];
        foreach (var item in LuminaGetter.Get<Item>())
        {
            if (item.RowId == 0 || item.Name.IsEmpty)
            {
                continue;
            }

            var name = item.Name.ExtractText();
            if (item.ItemSearchCategory.RowId != 0)
            {
                marketEntries.Add(new(item.RowId, name));
            }

            if (recipeIDsByItemID.Remove(item.RowId, out var recipeIds))
            {
                recipeEntries.Add(new(name, recipeIds.ToArray()));
            }
        }

        marketMatcher = new(
            marketEntries,
            static item => [([item.Name], FuzzySearchWeight.Name)]);
        try
        {
            recipeMatcher = new(
                recipeEntries,
                static item => [([item.Name], FuzzySearchWeight.Name)]);
        }
        catch
        {
            marketMatcher.Dispose();
            throw;
        }
    }

    public int CopyMarketItemIds(string query, Span<uint> destination)
    {
        var matches = marketMatcher.Search(
            query,
            CompareMarketEntries,
            Math.Min(destination.Length, 100));
        for (var index = 0; index < matches.Length; index++)
        {
            destination[index] = matches[index].ItemID;
        }

        return matches.Length;
    }

    public List<uint> SearchRecipeIds(string query)
    {
        var matches = recipeMatcher.Search(query, CompareRecipeEntries, RecipeResultLimit);
        var recipeIds = new List<uint>(Math.Min(matches.Length, RecipeResultLimit));
        for (var entryIndex = 0; entryIndex < matches.Length && recipeIds.Count < RecipeResultLimit; entryIndex++)
        {
            var entryRecipeIds = matches[entryIndex].RecipeIds;
            for (var recipeIndex = 0;
                 recipeIndex < entryRecipeIds.Length && recipeIds.Count < RecipeResultLimit;
                 recipeIndex++)
            {
                recipeIds.Add(entryRecipeIds[recipeIndex]);
            }
        }

        return recipeIds;
    }

    public void Dispose()
    {
        recipeMatcher.Dispose();
        marketMatcher.Dispose();
    }

    private readonly record struct MarketEntry(uint ItemID, string Name);

    private readonly record struct RecipeEntry(string Name, uint[] RecipeIds);
}
