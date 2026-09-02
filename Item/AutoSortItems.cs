using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;

namespace OmniToolbox.TreePublic;

public sealed unsafe class AutoSortItems(AutoSortItemsConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AutoSortItemsTitle"),
        Description = OmniLoc.Get("AutoSortItemsDescription"),
        Category = ModuleCategory.Item,
        RequiresPrivateProvider = true,
        Commands =
        [
            new ModuleCommand("Feature.AutoSortItems.CommandDescription", "/omni 自动整理")
        ]
    };

    private const int SortTimeoutMs = 60_000;
    private static readonly InventoryType[] InventoryContainers =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4
    ];
    private static readonly InventoryType[] RetainerContainers =
    [
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7
    ];
    private static readonly InventoryType[] SaddlebagContainers =
    [
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2
    ];
    private static readonly InventoryType[] PremiumSaddlebagContainers =
    [
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2
    ];
    internal static readonly RuleOption[] Categories =
    [
        new("inventory", 257, "Feature.AutoSortItems.Category.Inventory"),
        new("retainer", 261, "Feature.AutoSortItems.Category.Retainer"),
        new("armoury", 259, "Feature.AutoSortItems.Category.Armoury"),
        new("saddlebag", 467, "Feature.AutoSortItems.Category.Saddlebag"),
        new("rightsaddlebag", 469, "Feature.AutoSortItems.Category.PremiumSaddlebag"),
        new("mh", 26, "Feature.AutoSortItems.Category.MainHand"),
        new("oh", 28, "Feature.AutoSortItems.Category.OffHand"),
        new("head", 37, "Feature.AutoSortItems.Category.Head"),
        new("body", 41, "Feature.AutoSortItems.Category.Body"),
        new("hands", 47, "Feature.AutoSortItems.Category.Hands"),
        new("legs", 45, "Feature.AutoSortItems.Category.Legs"),
        new("feet", 49, "Feature.AutoSortItems.Category.Feet"),
        new("neck", 53, "Feature.AutoSortItems.Category.Neck"),
        new("ears", 285, "Feature.AutoSortItems.Category.Ears"),
        new("wrists", 287, "Feature.AutoSortItems.Category.Wrists"),
        new("rings", 289, "Feature.AutoSortItems.Category.Rings"),
        new("soul", 291, "Feature.AutoSortItems.Category.Soul")
    ];
    internal static readonly RuleOption[] Conditions =
    [
        new("id", 271, "Feature.AutoSortItems.Condition.ID"),
        new("spiritbond", 275, "Feature.AutoSortItems.Condition.Spiritbond"),
        new("category", 263, "Feature.AutoSortItems.Condition.Category"),
        new("lv", 265, "Feature.AutoSortItems.Condition.Level"),
        new("ilv", 267, "Feature.AutoSortItems.Condition.ItemLevel"),
        new("stack", 269, "Feature.AutoSortItems.Condition.Stack"),
        new("hq", 277, "Feature.AutoSortItems.Condition.HQ"),
        new("materia", 279, "Feature.AutoSortItems.Condition.Materia"),
        new("pdamage", 293, "Feature.AutoSortItems.Condition.PhysicalDamage"),
        new("mdamage", 295, "Feature.AutoSortItems.Condition.MagicalDamage"),
        new("delay", 297, "Feature.AutoSortItems.Condition.Delay"),
        new("autoattack", 299, "Feature.AutoSortItems.Condition.AutoAttack"),
        new("blockrate", 301, "Feature.AutoSortItems.Condition.BlockRate"),
        new("blockstrength", 303, "Feature.AutoSortItems.Condition.BlockStrength"),
        new("defense", 305, "Feature.AutoSortItems.Condition.Defense"),
        new("mdefense", 307, "Feature.AutoSortItems.Condition.MagicalDefense"),
        new("str", 309, "Feature.AutoSortItems.Condition.Strength"),
        new("dex", 311, "Feature.AutoSortItems.Condition.Dexterity"),
        new("vit", 313, "Feature.AutoSortItems.Condition.Vitality"),
        new("int", 315, "Feature.AutoSortItems.Condition.Intelligence"),
        new("mnd", 317, "Feature.AutoSortItems.Condition.Mind"),
        new("craftsmanship", 321, "Feature.AutoSortItems.Condition.Craftsmanship"),
        new("control", 323, "Feature.AutoSortItems.Condition.Control"),
        new("gathering", 325, "Feature.AutoSortItems.Condition.Gathering"),
        new("perception", 327, "Feature.AutoSortItems.Condition.Perception")
    ];
    internal static readonly RuleOption[] Orders =
    [
        new("des", 283, "Feature.AutoSortItems.Order.Descending"),
        new("asc", 281, "Feature.AutoSortItems.Order.Ascending")
    ];

    private readonly HashSet<string> queuedCategories = new(StringComparer.Ordinal);
    private TaskHelper? taskHelper;
    private AddonEventRegistry? addonEvents;

    public override bool HasSettings => true;

    public override bool DrawSettings() => AutoSortItemsPanel.Draw(config);

    public void RequestSort()
    {
        EnsureRules();
        taskHelper?.Abort();
        ResetQueue();
        if (IsCategoryVisible("retainer"))
        {
            QueueCategory("retainer", true);
            return;
        }

        if (IsCategoryVisible("saddlebag"))
        {
            if (IsCategoryVisible("inventory"))
            {
                QueueCategory("inventory");
            }

            QueueSaddlebags(true);
            return;
        }

        if (GameState.ContentFinderCondition != 0)
        {
            QueueCategory("inventory", true, allowInDuty: true);
            return;
        }

        QueueAllEnabled(true);
    }

    protected override void OnEnable()
    {
        EnsureRules();
        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = SortTimeoutMs,
            TimeoutAction = ResetQueue,
            ExceptionAction = ResetQueue
        };
        addonEvents = new(DalamudServices.AddonLifecycle);
        RegisterAutoSortAddon("ArmouryBoard", () => QueueArmoury());
        RegisterAutoSortAddon("Inventory", () => QueueCategory("inventory"));
        RegisterAutoSortAddon("InventoryLarge", () => QueueCategory("inventory"));
        RegisterAutoSortAddon("InventoryExpansion", () => QueueCategory("inventory"));
        RegisterAutoSortAddon("InventoryRetainer", () => QueueCategory("retainer"));
        RegisterAutoSortAddon("InventoryRetainerLarge", () => QueueCategory("retainer"));
        RegisterAutoSortAddon("InventoryBuddy", () => QueueSaddlebags());
        RegisterAutoSortAddon("InventoryBuddy2", () => QueueSaddlebags());
        DService.Instance().ClientState.ClassJobChanged += OnClassJobChanged;
    }

    protected override void OnDisable()
    {
        DService.Instance().ClientState.ClassJobChanged -= OnClassJobChanged;
        addonEvents?.Dispose();
        addonEvents = null;
        taskHelper?.Abort();
        taskHelper?.Dispose();
        taskHelper = null;
        queuedCategories.Clear();
    }

    protected override bool OnInterruptAutomation()
    {
        if (taskHelper?.IsBusy != true && queuedCategories.Count == 0)
        {
            return false;
        }

        taskHelper?.Abort();
        ResetQueue();
        return true;
    }

    private void RegisterAutoSortAddon(string addonName, System.Action queueAction) =>
        addonEvents!.Register(AddonEvent.PostShow, addonName, (_, _) => queueAction());

    private void OnClassJobChanged(uint _)
    {
        if (config.SortArmouryOnJobChange && IsAddonVisible("ArmouryBoard"))
        {
            QueueArmoury();
        }
    }

    private void QueueAllEnabled(bool notify)
    {
        EnsureRules();
        var queued = false;
        foreach (var category in config.Rules!
                     .Where(rule => rule.Category is not null)
                     .Select(rule => rule.Category!)
                     .Distinct(StringComparer.Ordinal)
                     .ToArray())
        {
            if (IsCategoryEnabled(category) &&
                (category == "inventory" || IsArmouryCategory(category) || IsCategoryVisible(category)))
            {
                QueueCategory(
                    category,
                    waitForMergeContainers: config.AutoMerge && category is "saddlebag" or "rightsaddlebag");
                queued = true;
            }
        }

        if (notify && queued && taskHelper is not null)
        {
            taskHelper.Enqueue(NotifySortCompleted, "Notify sort completed");
        }
    }

    private void QueueArmoury(bool notify = false)
    {
        EnsureRules();
        foreach (var category in config.Rules!
                     .Where(rule => rule.Category is not null && IsArmouryCategory(rule.Category))
                     .Select(rule => rule.Category!)
                     .Distinct(StringComparer.Ordinal)
                     .Where(IsCategoryEnabled))
        {
            QueueCategory(category, notify);
        }
    }

    private void QueueSaddlebags(bool notify = false)
    {
        EnsureRules();
        var queued = new HashSet<string>(StringComparer.Ordinal);
        foreach (var category in config.Rules!
                     .Where(rule => rule.Category is "saddlebag" or "rightsaddlebag")
                     .Select(rule => rule.Category!)
                     .Distinct(StringComparer.Ordinal)
                     .Where(IsCategoryEnabled))
        {
            QueueCategory(category);
            queued.Add(category);
        }

        if (queued.Add("saddlebag"))
        {
            QueueCategory("saddlebag");
        }

        if (queued.Add("rightsaddlebag"))
        {
            QueueCategory("rightsaddlebag");
        }

        if (notify && taskHelper is not null &&
            (queuedCategories.Contains("saddlebag") || queuedCategories.Contains("rightsaddlebag")))
        {
            taskHelper.Enqueue(NotifySortCompleted, "Notify sort completed");
        }
    }

    private void QueueCategory(
        string category,
        bool notify = false,
        bool waitForMergeContainers = false,
        bool allowInDuty = false)
    {
        EnsureRules();
        var playerState = PlayerState.Instance();
        if (category == "rightsaddlebag" &&
            (playerState is null || !playerState->HasPremiumSaddlebag))
        {
            return;
        }

        if (taskHelper is null)
        {
            return;
        }

        if (!queuedCategories.Add(category))
        {
            return;
        }

        var rules = config.Rules!
            .Where(rule => string.Equals(rule.Category, category, StringComparison.Ordinal))
            .ToArray();
        if (rules.Length > 0 && !rules.Any(rule => rule.Enabled))
        {
            queuedCategories.Remove(category);
            return;
        }

        var shouldMerge = config.AutoMerge && GetMergeContainers(category) is not null;
        if (rules.Length == 0 && !shouldMerge)
        {
            queuedCategories.Remove(category);
            return;
        }

        if (shouldMerge)
        {
            taskHelper.Enqueue(
                () => MergeNext(category, waitForMergeContainers, allowInDuty),
                $"Merge stacks {category}");
        }

        if (rules.Length > 0)
        {
            taskHelper.Enqueue(() => StartSort(category, rules, allowInDuty), $"Start sort {category}");
        }

        taskHelper.Enqueue(() => FinishSort(category, notify), $"Finish sort {category}");
    }

    private bool StartSort(string category, IReadOnlyList<AutoSortItemsRule> rules, bool allowInDuty)
    {
        if (!CanSort(category, allowInDuty))
        {
            return false;
        }

        var categoryCommand = GetCommand(Categories, category);
        if (string.IsNullOrEmpty(categoryCommand))
        {
            return true;
        }

        var command = $"/itemsort clear {categoryCommand}";
        ChatManager.Instance().SendMessage(command);
        foreach (var rule in rules)
        {
            var condition = GetCommand(Conditions, rule.Condition);
            var order = GetCommand(Orders, rule.Order);
            if (!string.IsNullOrEmpty(condition) && !string.IsNullOrEmpty(order))
            {
                command = $"/itemsort condition {categoryCommand} {condition} {order}";
                ChatManager.Instance().SendMessage(command);
            }
        }

        if (IsCategoryTabbed(category))
        {
            var tab = GetCommand(273);
            if (!string.IsNullOrEmpty(tab))
            {
                command = $"/itemsort condition {categoryCommand} {tab}";
                ChatManager.Instance().SendMessage(command);
            }
        }

        command = $"/itemsort execute {categoryCommand}";
        ChatManager.Instance().SendMessage(command);
        return true;
    }

    private bool NotifySortCompleted()
    {
        if (config.SendChat)
        {
            OmniNotifier.Chat(OmniLoc.Get("Feature.AutoSortItems.Completed"));
        }

        if (config.SendNotification)
        {
            OmniNotifier.Popup(
                OmniLoc.Get("AutoSortItemsTitle"),
                OmniLoc.Get("Feature.AutoSortItems.Completed"));
        }

        return true;
    }

    private bool MergeNext(string category, bool waitForContainers, bool allowInDuty)
    {
        if (!CanSort(category, allowInDuty))
        {
            return false;
        }

        var containers = GetMergeContainers(category);
        if (containers is null)
        {
            return true;
        }

        var manager = InventoryManager.Instance();
        if (manager is null)
        {
            return true;
        }

        if (waitForContainers)
        {
            for (var index = 0; index < containers.Length; index++)
            {
                var container = manager->GetInventoryContainer(containers[index]);
                if (container is null || !container->IsLoaded)
                {
                    return false;
                }
            }
        }

        var hasMergeCandidate = false;
        for (var sourceIndex = 0; sourceIndex < containers.Length; sourceIndex++)
        {
            var sourceContainer = manager->GetInventoryContainer(containers[sourceIndex]);
            if (sourceContainer is null || !sourceContainer->IsLoaded)
            {
                continue;
            }

            for (var sourceSlotIndex = 0; sourceSlotIndex < sourceContainer->Size; sourceSlotIndex++)
            {
                var source = sourceContainer->GetInventorySlot(sourceSlotIndex);
                if (!TryGetMergeSource(source, out var stackSize))
                {
                    continue;
                }

                for (var targetIndex = 0; targetIndex < containers.Length; targetIndex++)
                {
                    var targetContainer = manager->GetInventoryContainer(containers[targetIndex]);
                    if (targetContainer is null || !targetContainer->IsLoaded)
                    {
                        continue;
                    }

                    for (var targetSlotIndex = 0; targetSlotIndex < targetContainer->Size; targetSlotIndex++)
                    {
                        var target = targetContainer->GetInventorySlot(targetSlotIndex);
                        if (target is null ||
                            (sourceContainer == targetContainer && source->Slot == target->Slot) ||
                            !IsSameStack(source, target) ||
                            target->Quantity >= stackSize)
                        {
                            continue;
                        }

                        hasMergeCandidate = true;
                        var result = manager->MoveItemSlot(
                                containers[sourceIndex],
                                (ushort)source->Slot,
                                containers[targetIndex],
                                (ushort)target->Slot,
                                true);
                        if (result == 0)
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return !hasMergeCandidate;
    }

    private bool FinishSort(string category, bool notify)
    {
        queuedCategories.Remove(category);
        if (notify)
        {
            NotifySortCompleted();
        }

        return true;
    }

    private static bool TryGetMergeSource(InventoryItem* item, out uint stackSize)
    {
        stackSize = 0;
        if (item is null ||
            item->ItemId == 0 ||
            item->Flags.HasFlag(InventoryItem.ItemFlags.Collectable) ||
            !LuminaGetter.TryGetRow<Item>(item->GetBaseItemId(), out var itemData))
        {
            return false;
        }

        stackSize = itemData.StackSize;
        return stackSize > 1 && item->Quantity < stackSize;
    }

    private static bool IsSameStack(InventoryItem* left, InventoryItem* right) =>
        right is not null &&
        right->ItemId != 0 &&
        !right->Flags.HasFlag(InventoryItem.ItemFlags.Collectable) &&
        left->ItemId == right->ItemId &&
        left->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) ==
        right->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality);

    private static bool CanSort(string category, bool allowInDuty)
    {
        var services = DService.Instance();
        if (!GameState.IsLoggedIn ||
            !services.ClientState.IsLoggedIn ||
            services.ObjectTable.LocalPlayer is null)
        {
            return false;
        }

        if (!UIModule.IsScreenReady())
        {
            return false;
        }

        var isVisibleInventoryInteraction = category is "inventory" or "retainer" or "saddlebag" or "rightsaddlebag" &&
                                            IsCategoryVisible(category);
        if (!isVisibleInventoryInteraction && services.Condition.IsOccupiedInEvent)
        {
            return false;
        }

        if (!allowInDuty && !isVisibleInventoryInteraction && !services.Condition.IsIdle)
        {
            return false;
        }

        if (!IsInValidZone(allowInDuty))
        {
            return false;
        }

        if (category is "retainer" or "saddlebag" or "rightsaddlebag" &&
            !IsCategoryVisible(category))
        {
            return false;
        }

        if (category is "saddlebag" or "rightsaddlebag" && !AreContainersLoaded(category))
        {
            return false;
        }

        var module = ItemOrderModule.Instance();
        if (module is null)
        {
            return false;
        }

        if (category is "armoury" or "mh" or "oh" or "head" or "body" or "hands" or "legs" or "feet" or "neck" or "ears" or "wrists" or "rings" or "soul")
        {
            if (module->IsSavePending)
            {
                return false;
            }

            if (IsBusy(module->ArmouryMainHandSorter) ||
                IsBusy(module->ArmouryHeadSorter) ||
                IsBusy(module->ArmouryBodySorter) ||
                IsBusy(module->ArmouryHandsSorter) ||
                IsBusy(module->ArmouryLegsSorter) ||
                IsBusy(module->ArmouryFeetSorter) ||
                IsBusy(module->ArmouryOffHandSorter) ||
                IsBusy(module->ArmouryEarsSorter) ||
                IsBusy(module->ArmouryNeckSorter) ||
                IsBusy(module->ArmouryWristsSorter) ||
                IsBusy(module->ArmouryRingsSorter) ||
                IsBusy(module->ArmourySoulCrystalSorter))
            {
                return false;
            }

            return true;
        }

        var sorter = category switch
        {
            "inventory" => module->InventorySorter,
            "retainer" => GetActiveRetainerSorter(module),
            "saddlebag" => module->SaddleBagSorter,
            "rightsaddlebag" => module->PremiumSaddleBagSorter,
            _ => null
        };
        if (sorter is null)
        {
            return false;
        }

        if (sorter->SortFunctionIndex != -1)
        {
            return false;
        }

        return true;
    }

    private static ItemOrderModuleSorter* GetActiveRetainerSorter(ItemOrderModule* itemOrderModule)
    {
        var retainerID = itemOrderModule->ActiveRetainerId;
        return retainerID != 0 &&
               itemOrderModule->RetainerSorter.TryGetValue(retainerID, out var sorter, false)
            ? sorter.Value
            : null;
    }

    private static bool IsBusy(ItemOrderModuleSorter* sorter) =>
        sorter is not null && sorter->SortFunctionIndex != -1;

    private static bool AreContainersLoaded(string category)
    {
        var containers = GetMergeContainers(category);
        var manager = InventoryManager.Instance();
        if (containers is null || manager is null)
        {
            return false;
        }

        for (var index = 0; index < containers.Length; index++)
        {
            var container = manager->GetInventoryContainer(containers[index]);
            if (container is null || !container->IsLoaded)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInValidZone(bool allowInDuty) =>
        GameState.Map != 0 &&
        GameState.TerritoryType != 0 &&
        !GameState.IsInPVPArea &&
        (allowInDuty || GameState.ContentFinderCondition == 0);

    private static bool IsAddonVisible(string addonName)
    {
        var addon = AddonHelper.GetByName(addonName);
        return addon is not null && addon->IsVisible;
    }

    private static bool IsCategoryVisible(string category) => category switch
    {
        "retainer" => IsAddonVisible("InventoryRetainer") || IsAddonVisible("InventoryRetainerLarge"),
        "saddlebag" or "rightsaddlebag" =>
            IsAddonVisible("InventoryBuddy") || IsAddonVisible("InventoryBuddy2"),
        "inventory" =>
            IsAddonVisible("Inventory") || IsAddonVisible("InventoryLarge") || IsAddonVisible("InventoryExpansion"),
        _ => IsAddonVisible(GetAddonName(category))
    };

    private static string GetAddonName(string category) => category switch
    {
        "armoury" or "mh" or "oh" or "head" or "body" or "hands" or "legs" or "feet" or "neck" or "ears" or "wrists" or "rings" or "soul" => "ArmouryBoard",
        "retainer" => "InventoryRetainer",
        "saddlebag" or "rightsaddlebag" => "InventoryBuddy",
        _ => "Inventory"
    };

    private static bool IsArmouryCategory(string category) => category is
        "armoury" or "mh" or "oh" or "head" or "body" or "hands" or "legs" or "feet" or
        "neck" or "ears" or "wrists" or "rings" or "soul";

    internal static bool SupportsTab(string category) => category is
        "inventory" or "retainer" or "saddlebag" or "rightsaddlebag";

    private static InventoryType[]? GetMergeContainers(string category) => category switch
    {
        "inventory" => InventoryContainers,
        "retainer" => RetainerContainers,
        "saddlebag" => SaddlebagContainers,
        "rightsaddlebag" => PremiumSaddlebagContainers,
        _ => null
    };

    private static string GetCommand(IEnumerable<RuleOption> options, string? key)
    {
        var option = options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.Ordinal));
        if (option is null)
        {
            return string.Empty;
        }

        return GetCommand(option.RowID);
    }

    private static string GetCommand(uint rowID) => LuminaGetter.TryGetRow<TextCommandParam>(rowID, out var row)
            ? row.Param.ToString().ToLowerInvariant()
            : string.Empty;

    private void ResetQueue() => queuedCategories.Clear();

    private bool IsCategoryTabbed(string category) =>
        SupportsTab(category) &&
        (config.CategoryHeaders?.FirstOrDefault(header => header.Category == category)?.Tab ??
         category is "inventory" or "saddlebag" or "rightsaddlebag");

    private bool IsCategoryEnabled(string category) =>
        config.Rules!.Any(rule =>
            rule.Enabled && string.Equals(rule.Category, category, StringComparison.Ordinal));

    internal static void EnsureRules(AutoSortItemsConfig config)
    {
        config.Rules ??= CreateDefaultRules();
        if (config.CategoryHeaders is null)
        {
            config.CategoryHeaders = CreateDefaultCategoryHeaders();
            return;
        }

        if (!config.CategoryHeaders.Any(header => header.Category == "inventory"))
        {
            config.CategoryHeaders.Add(new("inventory", true));
        }

        if (!config.CategoryHeaders.Any(header => header.Category == "saddlebag"))
        {
            config.CategoryHeaders.Add(new("saddlebag", true));
        }

        if (!config.CategoryHeaders.Any(header => header.Category == "rightsaddlebag"))
        {
            config.CategoryHeaders.Add(new("rightsaddlebag", true));
        }
    }

    private void EnsureRules() => EnsureRules(config);

    private static List<AutoSortItemsRule> CreateDefaultRules() =>
    [
        new(true, "armoury", "id", "asc"),
        new(true, "armoury", "lv", "asc"),
        new(true, "armoury", "category", "asc"),
        new(true, "inventory", "hq", "asc"),
        new(true, "inventory", "id", "asc"),
        new(true, "inventory", "lv", "asc"),
        new(true, "inventory", "category", "asc"),
        new(true, "saddlebag", "hq", "asc"),
        new(true, "saddlebag", "id", "asc"),
        new(true, "saddlebag", "lv", "asc"),
        new(true, "saddlebag", "category", "asc")
    ];

    private static List<AutoSortItemsCategoryHeader> CreateDefaultCategoryHeaders() =>
    [
        new("inventory", true),
        new("saddlebag", true),
        new("rightsaddlebag", true)
    ];

    internal sealed record RuleOption(string Key, uint RowID, string LocalizationKey);
}

[Serializable]
public sealed class AutoSortItemsConfig
{
    public List<AutoSortItemsRule>? Rules { get; set; }

    public List<AutoSortItemsCategoryHeader>? CategoryHeaders { get; set; }

    public bool SortArmouryOnJobChange { get; set; } = true;

    public bool AutoMerge { get; set; } = true;

    public int ArmouryCategory { get; set; }

    public int ArmouryChestID { get; set; }

    public int ArmouryItemLevel { get; set; }

    public int InventoryCategory { get; set; }

    public int InventoryHQ { get; set; }

    public int InventoryID { get; set; }

    public int InventoryItemLevel { get; set; }

    public int InventoryTab { get; set; } = 1;

    public bool SendChat { get; set; }

    public bool SendNotification { get; set; } = true;
}

[Serializable]
public sealed record AutoSortItemsRule(
    bool Enabled = true,
    string? Category = null,
    string? Condition = null,
    string? Order = null);

[Serializable]
public sealed record AutoSortItemsCategoryHeader(
    string Category,
    bool Tab = false);

internal static class AutoSortItemsPanel
{
    public static bool Draw(AutoSortItemsConfig config)
    {
        AutoSortItems.EnsureRules(config);
        var changed = false;
        using (var optionsTable = ImRaii.Table(
                   "##autoSortItemsOptions",
                   4,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (optionsTable)
            {
                ImGui.TableSetupColumn("##sortArmouryOnJobChange", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##autoMerge", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##sendChat", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##sendNotification", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var sortArmouryOnJobChange = config.SortArmouryOnJobChange;
                if (OmniControls.Checkbox(
                        OmniLoc.Get("Feature.AutoSortItems.SortArmouryOnJobChange"),
                        ref sortArmouryOnJobChange))
                {
                    config.SortArmouryOnJobChange = sortArmouryOnJobChange;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var autoMerge = config.AutoMerge;
                if (OmniControls.Checkbox(OmniLoc.Get("Feature.AutoSortItems.AutoMerge"), ref autoMerge))
                {
                    config.AutoMerge = autoMerge;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var sendChat = config.SendChat;
                if (OmniControls.Checkbox(OmniLoc.Get("Feature.AutoSortItems.SendChat"), ref sendChat))
                {
                    config.SendChat = sendChat;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var sendNotification = config.SendNotification;
                if (OmniControls.Checkbox(
                        OmniLoc.Get("Feature.AutoSortItems.SendNotification"),
                        ref sendNotification))
                {
                    config.SendNotification = sendNotification;
                    changed = true;
                }
            }
        }

        var rowContentHeight = MathF.Max(
            OmniTheme.CheckboxSize(),
            MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight()));
        var deleteLabel = OmniLoc.Get("Feature.AutoSortItems.DeleteRule");
        var deleteSize = OmniControls.CompactButtonSize(deleteLabel);
        var tabLabel = OmniLoc.Get("Feature.AutoSortItems.Tab");
        var tabWidth = OmniTheme.CheckboxSize() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(tabLabel).X;
        var rules = config.Rules!;
        var enableColumnWidth = OmniTheme.CheckboxSize() + ImGui.GetStyle().CellPadding.X * 2f;
        var visibleRowCount = Math.Min(
            6,
            rules.Count + rules
                .Where(rule => rule.Category is not null)
                .Select(rule => rule.Category!)
                .Distinct(StringComparer.Ordinal)
                .Count());
        var tableHeight = OmniTheme.SmallButtonSize().Y +
                          rowContentHeight * visibleRowCount +
                          ImGui.GetStyle().CellPadding.Y * (visibleRowCount + 1) * 2f +
                          ImGui.GetStyle().FrameBorderSize * 2f;
        using var table = ImRaii.Table(
            "##autoSortItemsRules",
            4,
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, tableHeight));
        if (table)
        {
            ImGui.TableSetupColumn("##enabled", ImGuiTableColumnFlags.WidthFixed, enableColumnWidth);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.AutoSortItems.Column.Category"),
                ImGuiTableColumnFlags.WidthStretch,
                1f);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.AutoSortItems.Column.Condition"),
                ImGuiTableColumnFlags.WidthStretch,
                1.6f);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.AutoSortItems.Column.Actions"),
                ImGuiTableColumnFlags.WidthFixed,
                MathF.Max(deleteSize.X, tabWidth) + ImGui.GetStyle().CellPadding.X * 2f);
            ImGui.TableSetupScrollFreeze(0, 1);
            OmniControls.BeginTableHeaderRow();
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
            var allEnabled = rules.Any(rule => rule.Category is not null) &&
                             rules.Where(rule => rule.Category is not null).All(rule => rule.Enabled);
            OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), OmniTheme.SmallButtonSize().Y);
            if (OmniControls.Checkbox("##allEnabled", ref allEnabled))
            {
                SetAllCategoriesEnabled(config, allEnabled);
                changed = true;
            }
            OmniControls.TableHeader(OmniLoc.Get("Feature.AutoSortItems.Column.Category"));
            OmniControls.TableHeader(OmniLoc.Get("Feature.AutoSortItems.Column.Condition"));
            OmniControls.TableHeader(OmniLoc.Get("Feature.AutoSortItems.Column.Actions"));

            var removeIndex = -1;
            var drawnRules = new HashSet<int>();
            foreach (var category in rules
                         .Where(rule => rule.Category is not null)
                         .Select(rule => rule.Category!)
                         .Distinct(StringComparer.Ordinal)
                         .ToArray())
            {
                DrawCategoryHeader(config, category, rowContentHeight, ref changed);
                for (var index = 0; index < rules.Count; index++)
                {
                    if (rules[index].Category == category)
                    {
                        DrawRule(config, index, rowContentHeight, deleteSize, ref removeIndex, ref changed);
                        drawnRules.Add(index);
                    }
                }
            }

            for (var index = 0; index < rules.Count; index++)
            {
                if (!drawnRules.Contains(index))
                {
                    DrawRule(config, index, rowContentHeight, deleteSize, ref removeIndex, ref changed);
                }
            }

            if (removeIndex >= 0)
            {
                rules.RemoveAt(removeIndex);
                changed = true;
            }
        }

        if (OmniControls.SmallButton(OmniLoc.Get("Feature.AutoSortItems.AddRule"), false))
        {
            config.Rules!.Add(new());
            changed = true;
        }

        return changed;
    }

    private static void DrawCategoryHeader(
        AutoSortItemsConfig config,
        string category,
        float rowContentHeight,
        ref bool changed)
    {
        ImGui.PushID(category);
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
        ImGui.TableSetColumnIndex(0);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        var categoryLabel = GetRuleOptionLabel(category, AutoSortItems.Categories);
        var enabled = IsCategoryEnabled(config, category);
        OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
        if (OmniControls.Checkbox($"##{category}Enabled", ref enabled))
        {
            SetCategoryEnabled(config, category, enabled);
            changed = true;
        }

        ImGui.TableSetColumnIndex(1);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        OmniControls.CenterTableItem(
            new Vector2(ImGui.CalcTextSize(categoryLabel).X, ImGui.GetFrameHeight()),
            rowContentHeight);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(categoryLabel);

        ImGui.TableSetColumnIndex(2);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

        ImGui.TableSetColumnIndex(3);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        if (AutoSortItems.SupportsTab(category))
        {
            var tabLabel = OmniLoc.Get("Feature.AutoSortItems.Tab");
            var tab = GetCategoryHeader(config, category).Tab;
            OmniControls.CenterTableItem(
                new Vector2(
                    OmniTheme.CheckboxSize() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(tabLabel).X,
                    OmniTheme.CheckboxSize()),
                rowContentHeight);
            if (OmniControls.Checkbox($"{tabLabel}##tab", ref tab))
            {
                SetCategoryTab(config, category, tab);
                changed = true;
            }
        }

        ImGui.PopID();
    }

    private static void DrawRule(
        AutoSortItemsConfig config,
        int index,
        float rowContentHeight,
        Vector2 deleteSize,
        ref int removeIndex,
        ref bool changed)
    {
        var rule = config.Rules![index];
        ImGui.PushID(index);
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        var categoryWidth = ImGui.GetContentRegionAvail().X;
        OmniControls.CenterTableItem(new Vector2(categoryWidth, ImGui.GetFrameHeight()), rowContentHeight);
        var rowChanged = DrawRuleCombo("category", rule.Category, AutoSortItems.Categories, categoryWidth, out var category);

        ImGui.TableNextColumn();
        var conditionSpacing = ImGui.GetStyle().ItemSpacing.X;
        var conditionWidth = MathF.Max(1f, (ImGui.GetContentRegionAvail().X - conditionSpacing) * 0.5f);
        OmniControls.CenterTableItem(
            new Vector2(conditionWidth * 2f + conditionSpacing, ImGui.GetFrameHeight()),
            rowContentHeight);
        rowChanged |= DrawRuleCombo("condition", rule.Condition, AutoSortItems.Conditions, conditionWidth, out var condition);
        ImGui.SameLine(0f, conditionSpacing);
        rowChanged |= DrawRuleCombo("order", rule.Order, AutoSortItems.Orders, conditionWidth, out var order);
        if (rowChanged)
        {
            config.Rules[index] = rule with
            {
                Enabled = string.Equals(category, rule.Category, StringComparison.Ordinal)
                    ? rule.Enabled
                    : IsCategoryEnabled(config, category),
                Category = category,
                Condition = condition,
                Order = order
            };
            changed = true;
        }

        ImGui.TableNextColumn();
        OmniControls.CenterTableItem(deleteSize, rowContentHeight);
        if (OmniControls.SmallButton(
                $"{OmniLoc.Get("Feature.AutoSortItems.DeleteRule")}##delete",
                false,
                deleteSize))
        {
            removeIndex = index;
        }

        ImGui.PopID();
    }

    private static AutoSortItemsCategoryHeader GetCategoryHeader(AutoSortItemsConfig config, string category) =>
        config.CategoryHeaders!.FirstOrDefault(header => header.Category == category) ??
        new(category, category is "inventory" or "saddlebag" or "rightsaddlebag");

    private static bool IsCategoryEnabled(AutoSortItemsConfig config, string? category) =>
        category is null ||
        !config.Rules!.Any(rule => string.Equals(rule.Category, category, StringComparison.Ordinal)) ||
        config.Rules!.Any(rule =>
            rule.Enabled && string.Equals(rule.Category, category, StringComparison.Ordinal));

    private static void SetCategoryEnabled(AutoSortItemsConfig config, string category, bool enabled)
    {
        for (var index = 0; index < config.Rules!.Count; index++)
        {
            if (string.Equals(config.Rules[index].Category, category, StringComparison.Ordinal))
            {
                config.Rules[index] = config.Rules[index] with { Enabled = enabled };
            }
        }
    }

    private static void SetAllCategoriesEnabled(AutoSortItemsConfig config, bool enabled)
    {
        for (var index = 0; index < config.Rules!.Count; index++)
        {
            if (config.Rules[index].Category is not null)
            {
                config.Rules[index] = config.Rules[index] with { Enabled = enabled };
            }
        }
    }

    private static void SetCategoryTab(AutoSortItemsConfig config, string category, bool tab)
    {
        for (var index = 0; index < config.CategoryHeaders!.Count; index++)
        {
            if (config.CategoryHeaders[index].Category == category)
            {
                config.CategoryHeaders[index] = new(category, tab);
                return;
            }
        }

        config.CategoryHeaders.Add(new(category, tab));
    }

    private static string GetRuleOptionLabel(string category, IReadOnlyList<AutoSortItems.RuleOption> options) =>
        options.FirstOrDefault(option => option.Key == category) is { } option
            ? OmniLoc.Get(option.LocalizationKey)
            : OmniLoc.Get("Feature.AutoSortItems.Unset");

    private static bool DrawRuleCombo(
        string id,
        string? current,
        IReadOnlyList<AutoSortItems.RuleOption> options,
        float width,
        out string? selected)
    {
        selected = current;
        var preview = options.FirstOrDefault(option => option.Key == current) is { } selectedOption
            ? OmniLoc.Get(selectedOption.LocalizationKey)
            : OmniLoc.Get("Feature.AutoSortItems.Unset");
        if (!OmniControls.BeginCombo($"##{id}", preview, width))
        {
            return false;
        }

        var changed = false;
        foreach (var option in options)
        {
            if (ImGui.Selectable(OmniLoc.Get(option.LocalizationKey), current == option.Key))
            {
                selected = option.Key;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

}
