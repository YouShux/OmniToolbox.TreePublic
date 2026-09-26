using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.Dalamud;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Info.Game.Packets.Upstream;
using GameEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;
using GameEventHandlerContent = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandlerContent;
using GameEventID = FFXIVClientStructs.FFXIV.Client.Game.Event.EventId;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private static readonly uint[] RecipeIDs = [34433, 34441, 34449, 34457, 34465, 34473, 34434, 34442, 34450, 34458, 34466, 34474, 34435, 34443, 34451, 34459, 34467, 34475, 34436, 34444, 34452, 34460, 34468, 34476, 34437, 34445, 34453, 34461, 34469, 34477, 34438, 34446, 34454, 34462, 34470, 34478, 34439, 34447, 34455, 34463, 34471, 34479, 34440, 34448, 34456, 34464, 34472, 34480];

    private static RecipeOption[]? recipeCache;

    private static RecipeOption[] Recipes => recipeCache ??= LoadRecipes();

    private static RecipeOption[] LoadRecipes() => RecipeIDs.Select(id =>
    {
        if (!LuminaGetter.TryGetRow<Recipe>(id, out var row))
            throw new InvalidOperationException($"无法读取生产配方 #{id}。");
        return ReadRecipeOption(row);
    }).ToArray();

    private static RecipeOption ReadRecipeOption(Recipe row)
    {
        var item = row.ItemResult.Value;
        var level = row.RecipeLevelTable.Value.ClassJobLevel;
        // CraftType indexes crafting classes from zero; ClassJob starts at carpenter (8).
        var jobID = row.CraftType.RowId + 8;
        if (item.RowId == 0 || level == 0 || jobID is < 8 or > 15)
            throw new InvalidOperationException($"生产配方 #{row.RowId} 数据无效。");
        return new RecipeOption(row.RowId, item.RowId, jobID, level, row.IsExpert, item.Name.ExtractText());
    }

    private static readonly uint[] ScripShopIDs = [1770041, 1770281, 1770301];

    private readonly List<ScripProduct> scripProducts = [];

    private string scripCatalogError = string.Empty;

    private bool scripCatalogLoaded;

    private sealed record ScripProduct(uint ShopID, uint ItemID, string Name, int Price, int StackSize, bool Unique);

    private void EnsureScripCatalog()
    {
        if (scripCatalogLoaded) return;
        try
        {
            var products = new List<ScripProduct>();
            foreach (var id in ScripShopIDs)
            {
                if (!LuminaGetter.TryGetRow<SpecialShop>(id, out var shop))
                    throw new InvalidOperationException("无法读取振兴票商店数据。");
                products.AddRange(ReadScripProducts(shop, itemID => LuminaGetter.GetRow<Item>(itemID)));
            }

            scripProducts.AddRange(products.OrderBy(x => x.ShopID).ThenBy(x => x.Name));
            scripCatalogLoaded = true;
            scripCatalogError = string.Empty;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to load scrip products.");
            scripCatalogError = "振兴票商品数据暂不可用，请重新打开模块后重试。";
        }
    }

    private static List<ScripProduct> ReadScripProducts(SpecialShop shop, Func<uint, Item?> getItem)
    {
        var result = new List<ScripProduct>();
        foreach (var entry in shop.Item)
        {
            var rewards = entry.ReceiveItems.Where(x => x.Item.RowId != 0).ToArray();
            var costs = entry.ItemCosts.Where(x => x.ItemCost.RowId != 0 || x.CurrencyCost != 0).ToArray();
            // Only expose one-item, one-currency recipes that this buyer can verify exactly.
            if (rewards.Length != 1 || rewards[0].ReceiveCount != 1 || rewards[0].ReceiveHq ||
                costs.Length != 1 || costs[0].ItemCost.RowId != SKYBUILDERS_SCRIP_ITEM_ID ||
                costs[0].CurrencyCost is 0 or > 10000 || costs[0].CostType != 0 || costs[0].CollectabilityCost != 0)
                continue;
            var item = getItem(rewards[0].Item.RowId);
            if (item is not { } value || value.StackSize == 0) continue;
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
            : $"职业 #{jobID}";

    private static string FormatRecipe(RecipeOption recipe) =>
        $"{recipe.Level}级{(recipe.IsExpert ? "高难度" : string.Empty)} · {recipe.ItemName}" +
        (recipe.Level == 80 ? "（可以获得库啵好运章）" : string.Empty);

    private readonly record struct RecipeOption(
        uint RecipeID,
        uint ItemID,
        uint JobID,
        int Level,
        bool IsExpert,
        string ItemName);
}
