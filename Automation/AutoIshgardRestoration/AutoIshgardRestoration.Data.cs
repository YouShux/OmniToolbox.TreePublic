using System.Globalization;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private static RecipeOption[]? RecipeCache;

    private readonly List<ScripProduct> scripProducts = [];

    private string scripCatalogError = string.Empty;

    private bool scripCatalogAttempted;

    private bool scripCatalogLoaded;

    private static RecipeOption[] Recipes => RecipeCache ??= LoadRecipes();

    private static RecipeOption[] LoadRecipes() => RecipeIDs.Select(id =>
    {
        if (!LuminaGetter.TryGetRow<Recipe>(id, out var row))
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get("Feature.AutoIshgardRestoration.UI.RecipeUnavailable"), id));
        return ReadRecipeOption(row);
    }).OrderByDescending(recipe => recipe.Level)
      .ThenByDescending(recipe => recipe.IsExpert)
      .ToArray();

    private static RecipeOption ReadRecipeOption(Recipe row)
    {
        var item = row.ItemResult.Value;
        var level = row.RecipeLevelTable.Value.ClassJobLevel;
        var jobID = row.CraftType.RowId + 8;
        if (item.RowId == 0 || level == 0 || jobID is < 8 or > 15)
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get("Feature.AutoIshgardRestoration.UI.RecipeInvalid"), row.RowId));
        foreach (var supply in LuminaGetter.Get<HWDCrafterSupply>())
        {
            foreach (var entry in supply.HWDCrafterSupplyParams)
            {
                if (entry.ItemTradeIn.RowId != item.RowId) continue;
                var rewards = new ScripRewards(
                    entry.BaseCollectableRating, entry.BaseCollectableReward.Value.ScriptRewardAmount,
                    entry.MidCollectableRating, entry.MidCollectableReward.Value.ScriptRewardAmount,
                    entry.HighCollectableRating, entry.HighCollectableReward.Value.ScriptRewardAmount);
                return new RecipeOption(row.RowId, item.RowId, jobID, level, row.IsExpert, item.Name.ExtractText(), rewards);
            }
        }

        throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get("Feature.AutoIshgardRestoration.UI.RecipeInvalid"), row.RowId));
    }

    private void EnsureScripCatalog()
    {
        if (scripCatalogLoaded || scripCatalogAttempted) return;
        scripCatalogAttempted = true;

        var products = new List<ScripProduct>();
        foreach (var id in ScripShopIDs)
        {
            if (!LuminaGetter.TryGetRow<SpecialShop>(id, out var shop))
            {
                scripCatalogError = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ScripCatalogUnavailable");
                return;
            }

            products.AddRange(ReadScripProducts(shop));
        }

        scripProducts.AddRange(products.OrderBy(x => x.ShopID).ThenBy(x => x.Name));
        scripCatalogLoaded = true;
        scripCatalogError  = string.Empty;
    }

    private static List<ScripProduct> ReadScripProducts(SpecialShop shop)
    {
        var result = new List<ScripProduct>();
        foreach (var entry in shop.Item)
        {
            var rewards = entry.ReceiveItems.Where(x => x.Item.RowId != 0).ToArray();
            var costs = entry.ItemCosts.Where(x => x.ItemCost.RowId != 0 || x.CurrencyCost != 0).ToArray();
            if (rewards.Length != 1 || rewards[0].ReceiveCount != 1 || rewards[0].ReceiveHq ||
                costs.Length != 1 || costs[0].ItemCost.RowId != SKYBUILDERS_SCRIP_ITEM_ID ||
                costs[0].CurrencyCost is 0 or > 10000 || costs[0].CostType != 0 || costs[0].CollectabilityCost != 0)
                continue;
            if (!LuminaGetter.TryGetRow<Item>(rewards[0].Item.RowId, out var value) || value.StackSize == 0) continue;
            result.Add(new ScripProduct(shop.RowId, value.RowId, value.Name.ExtractText(),
                (int)costs[0].CurrencyCost, (int)value.StackSize, value.IsUnique));
        }
        return result;
    }

    private static RecipeOption? FindRecipe(uint recipeID)
    {
        foreach (var recipe in Recipes)
        {
            if (recipe.RecipeID == recipeID)
            {
                return recipe;
            }
        }

        return null;
    }

    private static string GetJobName(uint jobID) =>
        LuminaGetter.TryGetRow<ClassJob>(jobID, out var job)
            ? job.Name.ExtractText()
            : string.Format(CultureInfo.InvariantCulture, OmniLoc.Get("Feature.AutoIshgardRestoration.UI.UnknownJob"), jobID);

    private static string FormatRecipe(RecipeOption recipe) =>
        string.Format(CultureInfo.InvariantCulture, OmniLoc.Get("Feature.AutoIshgardRestoration.UI.RecipeFormat"),
            recipe.Level, recipe.IsExpert ? OmniLoc.Get("Feature.AutoIshgardRestoration.UI.ExpertRecipe") : string.Empty, recipe.ItemName) +
        (recipe.Level == 80 ? OmniLoc.Get("Feature.AutoIshgardRestoration.UI.RecipeVoucher") : string.Empty);

    private readonly record struct RecipeOption
    (
        uint         RecipeID,
        uint         ItemID,
        uint         JobID,
        int          Level,
        bool         IsExpert,
        string       ItemName,
        ScripRewards Rewards
    );

    private readonly record struct ScripRewards
    (
        ushort BaseRating,
        ushort BaseScrips,
        ushort MidRating,
        ushort MidScrips,
        ushort HighRating,
        ushort HighScrips
    )
    {
        public int GetScrips(uint collectability)
        {
            if (collectability >= HighRating) return HighScrips;
            if (collectability >= MidRating) return MidScrips;
            return collectability >= BaseRating ? BaseScrips : 0;
        }
    }

    private sealed record ScripProduct
    (
        uint   ShopID,
        uint   ItemID,
        string Name,
        int    Price,
        int    StackSize,
        bool   Unique
    );

    #region 预置数据

    private static readonly uint[] RecipeIDs =
    [
        34433, 34441, 34449, 34457, 34465, 34473,
        34434, 34442, 34450, 34458, 34466, 34474,
        34435, 34443, 34451, 34459, 34467, 34475,
        34436, 34444, 34452, 34460, 34468, 34476,
        34437, 34445, 34453, 34461, 34469, 34477,
        34438, 34446, 34454, 34462, 34470, 34478,
        34439, 34447, 34455, 34463, 34471, 34479,
        34440, 34448, 34456, 34464, 34472, 34480,
    ];

    private static readonly uint[] ScripShopIDs = [1770041, 1770281, 1770301];

    #endregion
}
