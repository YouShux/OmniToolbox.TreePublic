using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Quests;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    private CraftWork? craftWork;

    private static unsafe string? DriveDuty(Session current, QuestStep step)
    {
        current.Phase = QuestExecutionPhase.Duty;
        if (!LuminaGetter.TryGetRow<ContentFinderCondition>(step.Argument, out var content) || content.TerritoryType.RowId == 0)
            return "duty_id_invalid";
        var pi = DService.Instance().PI;
        var stopped = pi.GetIpcSubscriber<bool>("AutoDuty.IsStopped");
        var hasPath = pi.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath");
        if (!stopped.HasFunction || !hasPath.HasFunction)
            return "autoduty_unavailable";
        try
        {
            if (!stopped.InvokeFunc())
                return "plugin_busy";
            if (!hasPath.InvokeFunc(content.TerritoryType.RowId))
                return "duty_path_unsupported";
            if (!UIState.IsInstanceContentUnlocked(content.Content.RowId))
                return "duty_locked";
            if (DService.Instance().ObjectTable.LocalPlayer is not { } player)
                return "npc_player_state_unavailable";
            if (player.Level < content.ClassJobLevelRequired)
                return "level_insufficient";
            if (DService.Instance().PartyList.Length != 0)
                return "party_not_empty";
            return SelectNPCMode(content, out var error) is null ? error : "autoduty_incompatible";
        }
        catch (Exception exception)
        {
            DalamudServices.PluginLog.Warning(exception, "AutoQuest AutoDuty availability could not be checked.");
            return "autoduty_ipc_failed";
        }
    }

    private string? DriveCraft(Session current, QuestStep step)
    {
        current.Phase = QuestExecutionPhase.Crafting;
        if (step.Quantity <= 0 || step.Argument == 0)
            return "craft_target_invalid";
        if (shopWork is { } purchase)
            return DriveSupplyShop(current, purchase);
        var pi = DService.Instance().PI;
        var busy = pi.GetIpcSubscriber<bool>("Artisan.IsBusy");
        var start = pi.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
        var getStop = pi.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
        var setStop = pi.GetIpcSubscriber<bool, object>("Artisan.SetStopRequest");
        try
        {
            if (craftWork is { } work)
            {
                if (work.Owner != current.Operation.OperationID)
                    return "plugin_busy";
                if (!busy.HasFunction || !getStop.HasFunction || !setStop.HasAction)
                    return "artisan_unavailable";
                var isBusy = busy.InvokeFunc();
                if (work.StopRequested && !isBusy)
                {
                    RestoreCraftStop(work);
                    craftWork = null;
                    current.ActionSent = false;
                    return null;
                }
                if (CountItem(work.ItemID, work.RequireHQ) >= work.TargetQuantity)
                {
                    StopCraft(current);
                    return null;
                }
                work.ObservedBusy |= isBusy;
                if (work.ObservedBusy && !isBusy)
                    return "craft_result_incomplete";
                if (!work.ObservedBusy && Environment.TickCount64 - work.StartedAt > 30_000)
                    return "craft_start_unconfirmed";
                return Environment.TickCount64 - work.StartedAt > 600_000 ? "craft_timeout" : null;
            }
            if (CountItem(step.Argument, step.RequireHQ) >= step.Quantity)
                return null;
            if (!TryPrepareCraft(step.Argument, step.Quantity, step.RequireHQ, [], out var plan, out var reason, out var missing))
            {
                current.RelatedID = missing;
                return reason;
            }
            if (plan is null)
                return null;
            if (plan.Shop is { } shop)
            {
                shopWork = new(current.Operation.OperationID, plan.ItemID, plan.Quantity, shop);
                return DriveSupplyShop(current, shopWork);
            }
            if (plan.Recipe is not { } row)
                return "recipe_unavailable";
            if (!busy.HasFunction || !start.HasAction || !getStop.HasFunction || !setStop.HasAction)
                return "artisan_unavailable";
            if (busy.InvokeFunc())
                return "plugin_busy";
            if (row.CraftType.RowId + 8 != DalamudServices.PlayerState.ClassJob.RowId)
            {
                if (!LocalPlayerState.TryFindClassJobGearset(row.CraftType.RowId + 8, out var gearset) || !LocalPlayerState.SwitchGearset(gearset))
                    return "craft_job_or_recipe_unavailable";
                return null;
            }
            var crafts = (int)Math.Ceiling((plan.Quantity - CountItem(row.ItemResult.RowId, plan.RequireHQ)) / (double)row.AmountResult);
            craftWork = new(current.Operation.OperationID, row.ItemResult.RowId, plan.Quantity, plan.RequireHQ, getStop.InvokeFunc());
            setStop.InvokeAction(false);
            start.InvokeAction((ushort)row.RowId, crafts);
            current.ActionSent = true;
            return null;
        }
        catch (Exception exception)
        {
            DalamudServices.PluginLog.Warning(exception, "AutoQuest Artisan call failed.");
            return "artisan_ipc_failed";
        }
    }

    private void StopCraft(Session current)
    {
        if (craftWork is not { } work || work.Owner != current.Operation.OperationID)
            return;
        var stop = DService.Instance().PI.GetIpcSubscriber<bool, object>("Artisan.SetStopRequest");
        var getStop = DService.Instance().PI.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
        if (stop.HasAction && getStop.HasFunction && !getStop.InvokeFunc())
        {
            stop.InvokeAction(true);
            work.RestoreStop = true;
        }
        work.StopRequested = true;
    }

    private static unsafe bool TryPrepareCraft(uint itemID, int quantity, bool hq, HashSet<uint> visiting,
        out CraftPlan? plan, out string? error, out uint? missing)
    {
        plan = null;
        error = null;
        missing = null;
        if (CountItem(itemID, hq) >= quantity)
            return true;
        if (visiting.Count >= 32 || !visiting.Add(itemID))
        {
            error = "recipe_dependency_cycle";
            missing = itemID;
            return false;
        }
        try
        {
            Recipe? selectedRecipe = null;
            var state = PlayerState.Instance();
            foreach (var candidate in LuminaGetter.Get<Recipe>())
            {
                if (candidate.ItemResult.RowId != itemID || candidate.RowId > ushort.MaxValue || candidate.AmountResult == 0 ||
                    hq && !candidate.CanHq || state == null ||
                    !LuminaGetter.TryGetRow<ClassJob>(candidate.CraftType.RowId + 8, out var job) || job.ExpArrayIndex < 0 ||
                    job.ExpArrayIndex >= state->ClassJobLevels.Length || state->ClassJobLevels[job.ExpArrayIndex] < candidate.RecipeLevelTable.Value.ClassJobLevel ||
                    candidate.SecretRecipeBook.RowId != 0 || candidate.IsSpecializationRequired ||
                    candidate.Quest.RowId != 0 && !QuestManager.IsQuestComplete(candidate.Quest.RowId) ||
                    !LocalPlayerState.TryFindClassJobGearset(job.RowId, out _))
                    continue;
                if (selectedRecipe is null || candidate.CraftType.RowId + 8 == state->CurrentClassJobId &&
                    selectedRecipe.Value.CraftType.RowId + 8 != state->CurrentClassJobId ||
                    candidate.CraftType.RowId == selectedRecipe.Value.CraftType.RowId && candidate.RowId < selectedRecipe.Value.RowId)
                    selectedRecipe = candidate;
            }
            if (selectedRecipe is not { } recipe)
            {
                missing = itemID;
                if (!hq && TryPrepareShop(itemID, quantity, out plan, out error))
                    return true;
                error ??= "material_source_unsupported";
                return false;
            }
            var crafts = (int)Math.Ceiling((quantity - CountItem(itemID, hq)) / (double)recipe.AmountResult);
            var requirements = new Dictionary<uint, int>();
            for (var index = 0; index < recipe.Ingredient.Count; index++)
                if (recipe.AmountIngredient[index] > 0 && recipe.Ingredient[index].RowId != 0)
                {
                    var ingredient = recipe.Ingredient[index].RowId;
                    requirements[ingredient] = requirements.GetValueOrDefault(ingredient) + recipe.AmountIngredient[index] * crafts;
                }
            foreach (var (ingredient, required) in requirements)
            {
                if (CountItem(ingredient, false) >= required)
                    continue;
                if (!TryPrepareCraft(ingredient, required, false, visiting, out var dependency, out error, out missing))
                    return false;
                if (dependency is not null)
                {
                    plan = dependency;
                    return true;
                }
            }
            plan = new(recipe, itemID, quantity, hq);
            return true;
        }
        finally
        {
            visiting.Remove(itemID);
        }
    }

    private static void RestoreCraftStop(CraftWork work)
    {
        var pi = DService.Instance().PI;
        var stop = pi.GetIpcSubscriber<bool, object>("Artisan.SetStopRequest");
        var getStop = pi.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
        if (work.RestoreStop && stop.HasAction && getStop.HasFunction && getStop.InvokeFunc())
            stop.InvokeAction(work.PreviousStop);
    }

    private bool DrainPlugins(Session current)
    {
        var pi = DService.Instance().PI;
        if (!DrainSupplyShop(current))
            return false;
        if (craftWork is { } craft && craft.Owner == current.Operation.OperationID)
        {
            var busy = pi.GetIpcSubscriber<bool>("Artisan.IsBusy");
            if (!busy.HasFunction && pi.InstalledPlugins.Any(static plugin => plugin.IsLoaded && plugin.InternalName == "Artisan"))
                return false;
            if (busy.HasFunction && busy.InvokeFunc())
                return false;
            RestoreCraftStop(craft);
            craftWork = null;
        }
        return true;
    }

    private static unsafe int CountItem(uint itemID, bool hq)
    {
        if (itemID >= 2_000_000)
        {
            var manager = InventoryManager.Instance();
            var inventory = manager == null ? null : manager->GetInventoryContainer(InventoryType.KeyItems);
            if (inventory == null)
                return 0;
            var eventCount = 0;
            for (var index = 0; index < inventory->Size; index++)
            {
                var slot = inventory->GetInventorySlot(index);
                if (slot != null && slot->GetBaseItemId() == itemID)
                    eventCount += slot->Quantity;
            }
            return eventCount;
        }
        var count = 0;
        foreach (var container in new[] { GameInventoryType.Inventory1, GameInventoryType.Inventory2, GameInventoryType.Inventory3, GameInventoryType.Inventory4, GameInventoryType.Crystals })
            foreach (ref readonly var item in DService.Instance().GameInventory.GetInventoryItems(container))
                if (item.BaseItemId == itemID && (!hq || item.IsHq))
                    count += item.Quantity;
        return count;
    }

    private sealed class CraftWork(string owner, uint itemID, int targetQuantity, bool requireHQ, bool previousStop)
    {
        public string Owner { get; } = owner;
        public uint ItemID { get; } = itemID;
        public int TargetQuantity { get; } = targetQuantity;
        public bool RequireHQ { get; } = requireHQ;
        public bool PreviousStop { get; } = previousStop;
        public long StartedAt { get; } = Environment.TickCount64;
        public bool ObservedBusy { get; set; }
        public bool StopRequested { get; set; }
        public bool RestoreStop { get; set; }
    }

    private sealed record CraftPlan(Recipe? Recipe, uint ItemID, int Quantity, bool RequireHQ, QuestShopSource? Shop = null);
}
