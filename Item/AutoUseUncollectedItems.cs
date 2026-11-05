using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.Exd;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading;
using OmniToolbox.Collections;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Items;
using TreeTools.Threading;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class AutoUseUncollectedItems : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "AutoUseUncollectedItemsTitle",
            ["Description"] = "AutoUseUncollectedItemsDescription",
            ["Collection.Category.FieldNote"] = "Collection.Category.FieldNote",
            ["Collection.Category.SoulShard"] = "Collection.Category.SoulShard",
            ["Feature.AutoUseUncollectedItems.BlindBoxHint"] = "Feature.AutoUseUncollectedItems.BlindBoxHint",
            ["Feature.AutoUseUncollectedItems.FolkloreTomes"] = "Feature.AutoUseUncollectedItems.FolkloreTomes",
            ["Feature.AutoUseUncollectedItems.SecretRecipeBooks"] = "Feature.AutoUseUncollectedItems.SecretRecipeBooks"
        });

    private readonly Config                           config;
    private readonly CollectionSourceService          sourceService;
    private readonly Dictionary<uint, CollectionType> itemTypes         = [];
    private readonly Dictionary<uint, long>           blindBoxRetryAt   = [];
    private readonly Throttler<ulong>                 fieldNoteRequests = new();
    private readonly Dictionary<uint, (Item Row, ItemActionKind Action, int JobID)?> collectibleMetadata = [];
    private readonly Dictionary<uint, bool> collectibleChecks = [];
    private readonly Dictionary<uint, bool> blindBoxChecks = [];
    private readonly List<(InventoryType Container, int Slot, uint ItemID, uint BaseItemID)> blindBoxCandidates = [];
    private TaskHelper?                               taskHelper;

    public AutoUseUncollectedItems
    (
        TreeHouseConfig                                      configStore,
        Func<CollectionType, IReadOnlyList<CollectionEntry>> getCollections,
        CollectionSourceService                              sourceService
    )
    {
        config             = configStore.GetOrCreate<Config>(ModuleName);
        this.sourceService = sourceService;
        foreach (var type in SupportedTypes)
        {
            if (type == CollectionType.BlindBox)
                continue;

            foreach (var entry in getCollections(type))
            {
                if (entry.ItemID is { } itemID)
                    itemTypes.TryAdd(itemID, type);
            }
        }
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnUpdate));

        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = 10_000
        };
        activation.Add(taskHelper);
        activation.OnStopping(taskHelper.Abort);
        if (!FrameworkManager.Instance().Reg(OnUpdate, 500))
            throw new InvalidOperationException("Uncollected item use update registration failed.");
    }

    protected override void OnStop()
    {
        taskHelper = null;
        collectibleMetadata.Clear();
        collectibleChecks.Clear();
        blindBoxChecks.Clear();
        blindBoxCandidates.Clear();
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        using var table = ImRaii.Table("AutoUseUncollectedItemsCategories", 4, ImGuiTableFlags.SizingStretchSame);
        if (!table)
            return false;

        foreach (var type in SupportedTypes)
        {
            ImGui.TableNextColumn();
            var enabled = config.EnabledTypes.Contains(type);
            var typeLabel = OmniLoc.Get($"Collection.Category.{type}");
            if (OmniControls.Checkbox(typeLabel, ref enabled))
            {
                if (enabled)
                    config.EnabledTypes.Add(type);
                else
                    config.EnabledTypes.Remove(type);

                SaveHostConfig?.Invoke();
            }

            if (type == CollectionType.BlindBox)
            {
                ImGui.SameLine();
                OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.AutoUseUncollectedItems.BlindBoxHint"), referenceText: typeLabel);
            }
        }

        ImGui.TableNextColumn();
        if (OmniControls.Checkbox(
                OmniLoc.Get(this, "Feature.AutoUseUncollectedItems.SecretRecipeBooks"),
                ref config.UseSecretRecipeBooks))
            SaveHostConfig?.Invoke();

        ImGui.TableNextColumn();
        if (OmniControls.Checkbox(
                OmniLoc.Get(this, "Feature.AutoUseUncollectedItems.FolkloreTomes"),
                ref config.UseFolkloreTomes))
            SaveHostConfig?.Invoke();

        ImGui.TableNextColumn();
        if (OmniControls.Checkbox(
                OmniLoc.Get("Collection.Category.FieldNote"),
                ref config.UseFieldNotes))
            SaveHostConfig?.Invoke();

        ImGui.TableNextColumn();
        if (OmniControls.Checkbox(
                OmniLoc.Get("Collection.Category.SoulShard"),
                ref config.UseSoulShards))
            SaveHostConfig?.Invoke();

        return false;
    }

    private void OnUpdate(IFramework framework)
    {
        if (Activation.IsStopping || DalamudServices.IsUnloading || taskHelper is not { IsBusy: false } helper)
            return;

        var service = DService.Instance();
        var condition = service.Condition;
        if (!service.ClientState.IsLoggedIn || !DalamudServices.PlayerState.IsLoaded ||
            service.ObjectTable.LocalPlayer is null ||
            !condition.IsIdle || condition.IsOnMount ||
            condition[ConditionFlag.Jumping] || condition[ConditionFlag.UsingFashionAccessory] ||
            LocalPlayerState.Instance().IsMoving)
            return;

        var actionManager = ActionManager.Instance();
        var inventoryContext = AgentInventoryContext.Instance();
        var inventoryManager = InventoryManager.Instance();
        if (actionManager == null || inventoryContext == null || inventoryManager == null ||
            actionManager->AnimationLock > 0)
            return;

        if (!TryFindUsableItem(inventoryManager, out var slot, out var isBlindBox))
            return;

        var baseItemID = slot->GetBaseItemId();
        var itemID = slot->GetItemId();
        if (Activation.IsStopping || DalamudServices.IsUnloading || helper.IsBusy ||
            (isBlindBox ? !IsBlindBoxAvailable(baseItemID, itemID) : !IsCollectibleAvailable(*slot)) ||
            actionManager->GetActionStatus(ActionType.Item, itemID) != 0)
            return;
        var quantity = inventoryManager->GetInventoryItemCount(baseItemID, false, false, false);
        if (inventoryContext->UseItem(itemID) != 0)
        {
            DelayBlindBox(baseItemID);
            return;
        }

        helper.Enqueue(() =>
            {
                if (DalamudServices.IsUnloading || !service.ClientState.IsLoggedIn)
                    return true;

                var currentManager = InventoryManager.Instance();
                return currentManager != null &&
                       currentManager->GetInventoryItemCount(baseItemID, false, false, false) < quantity;
            },
            "AutoUseUncollectedItems.WaitUse",
            timeoutMS: 5_000,
            timeoutAction: () => DelayBlindBox(baseItemID));
    }

    private bool TryFindUsableItem(InventoryManager* manager, out InventoryItem* result, out bool isBlindBox)
    {
        collectibleChecks.Clear();
        blindBoxChecks.Clear();
        blindBoxCandidates.Clear();
        result = null;
        isBlindBox = false;
        var searchBlindBoxes = config.EnabledTypes.Contains(CollectionType.BlindBox);
        var searchItems = true;
        var hasEmptySlot = false;
        foreach (var type in Inventories.Player)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
            {
                if (!searchBlindBoxes || hasEmptySlot)
                    break;
                searchItems = false;
                continue;
            }

            for (var index = 0; index < container->Size; index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot == null)
                    continue;
                if (slot->ItemId == 0)
                {
                    hasEmptySlot = true;
                    continue;
                }
                if (!searchItems)
                    continue;
                if (CanUseCollectible(*slot))
                {
                    result = slot;
                    return true;
                }
                if (searchBlindBoxes)
                    blindBoxCandidates.Add((type, index, slot->GetItemId(), slot->GetBaseItemId()));
            }
            if (!searchItems && hasEmptySlot)
                break;
        }

        if (!searchBlindBoxes || !hasEmptySlot)
            return false;

        foreach (var candidate in blindBoxCandidates)
        {
            if (!CanOpenBlindBox(candidate.BaseItemID, candidate.ItemID))
                continue;

            var container = manager->GetInventoryContainer(candidate.Container);
            if (container == null || !container->IsLoaded || candidate.Slot >= container->Size)
                continue;
            var slot = container->GetInventorySlot(candidate.Slot);
            if (slot == null || slot->GetItemId() != candidate.ItemID || slot->GetBaseItemId() != candidate.BaseItemID)
                continue;

            result = slot;
            isBlindBox = true;
            return true;
        }

        return false;
    }

    private bool CanUseCollectible(InventoryItem slot)
    {
        var itemID = slot.GetItemId();
        if (collectibleChecks.TryGetValue(itemID, out var result))
            return result;

        result = IsCollectibleAvailable(slot);
        collectibleChecks.Add(itemID, result);
        return result;
    }

    private bool IsCollectibleAvailable(InventoryItem slot)
    {
        var itemID = slot.GetBaseItemId();
        if (!collectibleMetadata.TryGetValue(itemID, out var metadata) &&
            LuminaGetter.TryGetRow<Item>(itemID, out var row))
        {
            if (row.ItemAction.IsValid)
            {
                var action = row.ItemAction.Value;
                metadata = (row, (ItemActionKind)action.Action.RowId, action.Data[0]);
            }
            collectibleMetadata.Add(itemID, metadata);
        }
        if (metadata is not { } info)
            return false;

        var actionKind = info.Action;
        var enabled = actionKind switch
        {
            ItemActionKind.BuddyEquip => config.EnabledTypes.Contains(CollectionType.Barding),
            ItemActionKind.SecretRecipeBook => config.UseSecretRecipeBooks,
            ItemActionKind.FolkloreTome => config.UseFolkloreTomes,
            ItemActionKind.FieldNote => config.UseFieldNotes,
            ItemActionKind.SoulShard => config.UseSoulShards,
            _ => itemTypes.TryGetValue(itemID, out var type) && config.EnabledTypes.Contains(type)
        };
        if (!enabled)
            return false;

        if (actionKind == ItemActionKind.FieldNote)
        {
            if (!IsFieldNoteUncollected(itemID))
                return false;
        }
        else if (actionKind == ItemActionKind.SoulShard)
        {
            var crescent = PublicContentOccultCrescent.GetInstance();
            if (crescent == null || !crescent->StateLoaded)
                return false;

            var state = PublicContentOccultCrescent.GetState();
            var jobID = info.JobID;
            if (state == null || jobID >= state->SupportJobLevels.Length || state->SupportJobLevels[jobID] != 0)
                return false;
        }
        else if (!DalamudServices.UnlockState.IsItemUnlockable(info.Row) ||
                 DalamudServices.UnlockState.IsItemUnlocked(info.Row))
        {
            return false;
        }

        return ActionManager.Instance()->GetActionStatus(ActionType.Item, slot.GetItemId()) == 0;
    }

    private bool IsFieldNoteUncollected(uint itemID)
    {
        var uiState = UIState.Instance();
        var row = ExdModule.GetItemRowById(itemID);
        if (uiState == null || row == null)
            return false;

        var unlockState = uiState->IsItemActionUnlocked(row);
        if (unlockState is not (1 or 2) && fieldNoteRequests.Throttle(DalamudServices.PlayerState.ContentId, 30_000))
            BozjaRequestCommand.RequestWarResultNotebook();

        return unlockState == 2;
    }

    private bool CanOpenBlindBox(uint baseItemID, uint itemID)
    {
        if (blindBoxChecks.TryGetValue(itemID, out var result))
            return result;

        result = IsBlindBoxAvailable(baseItemID, itemID);
        blindBoxChecks.Add(itemID, result);
        return result;
    }

    private bool IsBlindBoxAvailable(uint blindBoxItemID, uint itemID)
    {
        if (blindBoxRetryAt.TryGetValue(blindBoxItemID, out var retryAt) &&
            Environment.TickCount64 < retryAt)
            return false;
        blindBoxRetryAt.Remove(blindBoxItemID);

        var pool = sourceService.GetBlindBoxDropPool(blindBoxItemID);
        if (pool.Count == 0 ||
            ActionManager.Instance()->GetActionStatus(ActionType.Item, itemID) != 0)
            return false;

        // 收录页的盲盒池包含无法由 UnlockState 标记的普通奖励，按奖励池实际的解锁状态判断。
        foreach (var rewardItemID in pool)
        {
            if (LuminaGetter.TryGetRow<Item>(rewardItemID, out var item) &&
                !DalamudServices.UnlockState.IsItemUnlocked(item))
                return true;
        }

        return false;
    }

    private void DelayBlindBox(uint itemID)
    {
        if (sourceService.GetBlindBoxDropPool(itemID).Count != 0)
            blindBoxRetryAt[itemID] = Environment.TickCount64 + 60_000;
    }

    private sealed class Config
    {
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<CollectionType> EnabledTypes = [.. SupportedTypes];

        public bool UseSecretRecipeBooks = true;
        public bool UseFolkloreTomes     = true;
        public bool UseFieldNotes        = true;
        public bool UseSoulShards        = true;
    }

    private enum ItemActionKind : uint
    {
        BuddyEquip = 1013,
        SecretRecipeBook = 2136,
        FolkloreTome = 4107,
        FieldNote = 19743,
        SoulShard = 43142
    }

    private static readonly CollectionType[] SupportedTypes =
    [
        CollectionType.Mount,
        CollectionType.Minion,
        CollectionType.Emote,
        CollectionType.Hairstyle,
        CollectionType.TripleTriadCard,
        CollectionType.Barding,
        CollectionType.Orchestrion,
        CollectionType.Portrait,
        CollectionType.FashionAccessory,
        CollectionType.Glasses,
        CollectionType.RidingMap,
        CollectionType.BlindBox
    ];
}
