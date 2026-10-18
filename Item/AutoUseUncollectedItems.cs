using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;
using OmniToolbox.Collections;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe class AutoUseUncollectedItems : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AutoUseUncollectedItemsTitle"),
        Description = OmniLoc.Get("AutoUseUncollectedItemsDescription"),
        Category = ModuleCategory.Item
    };

    private readonly Config config;
    private readonly CollectionSourceService sourceService;
    private readonly Dictionary<uint, CollectionType> itemTypes = [];
    private TaskHelper? taskHelper;

    public override bool HasSettings => true;

    public AutoUseUncollectedItems(
        TreeHouseConfig configStore,
        Func<CollectionType, IReadOnlyList<CollectionEntry>> getCollections,
        CollectionSourceService sourceService)
    {
        config = configStore.GetOrCreate<Config>(ModuleName);
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

    protected override void OnEnable()
    {
        taskHelper = new()
        {
            RetryIntervalMS = 100,
            TimeoutMS = 10_000
        };
        FrameworkManager.Instance().Reg(OnUpdate, 500);
    }

    protected override void OnDisable()
    {
        FrameworkManager.Instance().Unreg(OnUpdate);
        taskHelper?.Dispose();
        taskHelper = null;
    }

    public override bool DrawSettings()
    {
        using var table = ImRaii.Table("AutoUseUncollectedItemsCategories", 4, ImGuiTableFlags.SizingStretchSame);
        if (!table)
            return false;

        foreach (var type in SupportedTypes)
        {
            ImGui.TableNextColumn();
            var enabled = config.EnabledTypes.Contains(type);
            if (OmniControls.Checkbox(OmniLoc.Get($"Collection.Category.{type}"), ref enabled))
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
                OmniControls.HelpIcon(OmniLoc.Get("Feature.AutoUseUncollectedItems.BlindBoxHint"));
            }
        }

        ImGui.TableNextColumn();
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.AutoUseUncollectedItems.SecretRecipeBooks"),
                ref config.UseSecretRecipeBooks))
            SaveHostConfig?.Invoke();

        ImGui.TableNextColumn();
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.AutoUseUncollectedItems.FolkloreTomes"),
                ref config.UseFolkloreTomes))
            SaveHostConfig?.Invoke();

        return false;
    }

    private void OnUpdate(IFramework framework)
    {
        if (DalamudServices.IsUnloading || taskHelper is not { IsBusy: false } helper)
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
        if (actionManager == null || inventoryContext == null || actionManager->AnimationLock > 0)
            return;

        if (!Inventories.Player.TryGetFirstItem(CanUseCollectible, out var slot) &&
            (!config.EnabledTypes.Contains(CollectionType.BlindBox) || Inventories.Player.IsFull() ||
             !Inventories.Player.TryGetFirstItem(CanOpenBlindBox, out slot)))
            return;

        var itemID = slot->GetItemId();
        var container = slot->Container;
        var slotIndex = slot->GetSlot();
        var quantity = slot->GetQuantity();
        if (inventoryContext->UseItem(itemID, container, slotIndex) != 0)
            return;

        helper.Enqueue(() =>
            DalamudServices.IsUnloading || !service.ClientState.IsLoggedIn ||
            !InventorySlotAccess.TryGet(container, slotIndex, out var currentSlot) ||
            currentSlot->GetItemId() != itemID || currentSlot->GetQuantity() != quantity);
    }

    private bool CanUseCollectible(InventoryItem slot)
    {
        var itemID = slot.GetBaseItemId();
        if (!LuminaGetter.TryGetRow<Item>(itemID, out var item) || !item.ItemAction.IsValid)
            return false;

        var enabled = (ItemActionKind)item.ItemAction.Value.Action.RowId switch
        {
            ItemActionKind.SecretRecipeBook => config.UseSecretRecipeBooks,
            ItemActionKind.FolkloreTome => config.UseFolkloreTomes,
            _ => itemTypes.TryGetValue(itemID, out var type) && config.EnabledTypes.Contains(type)
        };
        return enabled &&
               DalamudServices.UnlockState.IsItemUnlockable(item) &&
               !DalamudServices.UnlockState.IsItemUnlocked(item) &&
               ActionManager.Instance()->GetActionStatus(ActionType.Item, slot.GetItemId()) == 0;
    }

    private bool CanOpenBlindBox(InventoryItem slot)
    {
        var pool = sourceService.GetBlindBoxDropPool(slot.GetBaseItemId());
        if (pool.Count == 0 ||
            ActionManager.Instance()->GetActionStatus(ActionType.Item, slot.GetItemId()) != 0)
            return false;

        foreach (var itemID in pool)
        {
            if (LuminaGetter.TryGetRow<Item>(itemID, out var item) &&
                DalamudServices.UnlockState.IsItemUnlockable(item) &&
                !DalamudServices.UnlockState.IsItemUnlocked(item))
                return true;
        }

        return false;
    }

    private sealed class Config
    {
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<CollectionType> EnabledTypes = [.. SupportedTypes];

        public bool UseSecretRecipeBooks = true;
        public bool UseFolkloreTomes = true;
    }

    private enum ItemActionKind : uint
    {
        SecretRecipeBook = 2136,
        FolkloreTome = 4107
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
