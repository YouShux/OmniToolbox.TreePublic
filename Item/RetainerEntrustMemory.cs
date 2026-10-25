using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Helpers;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using TreeTools.Lifecycle;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class RetainerEntrustMemory(
    RetainerEntrustMemory.Settings config,
    System.Action saveConfig) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Item/RetainerEntrustMemory-1.png"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "RetainerEntrustMemoryTitle",
            ["Description"] = "RetainerEntrustMemoryDescription"
        });

    private readonly Dictionary<uint, bool> itemStates = [];
    private AddonEventRegistry? addonEvents;
    private Dictionary<string, uint>? itemIDsByName;

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => addonEvents?.Dispose());

        var events = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        addonEvents = events;
        events.Register(AddonEvent.PostSetup, ADDON_NAME, OnPostSetup);
        events.Register(AddonEvent.PostUpdate, ADDON_NAME, OnPostUpdate);
        events.Register(AddonEvent.PreFinalize, ADDON_NAME, OnPreFinalize);
        if (AddonHelper.TryGetByName(ADDON_NAME, out var addon) && addon->IsAddonAndNodesReady())
            ApplyExcludedItems((AddonRetainerItemTransferList*)addon);
    }

    protected override void OnStop()
    {
        addonEvents = null;
        itemStates.Clear();
    }

    private void OnPostSetup(AddonEvent eventType, AddonArgs args) =>
        ApplyExcludedItems((AddonRetainerItemTransferList*)args.Addon.Address);

    private void ApplyExcludedItems(AddonRetainerItemTransferList* addon)
    {
        var agent = AgentRetainerItemTransfer.Instance();
        var data = agent == null ? null : agent->Data;
        if (data == null || addon == null)
        {
            return;
        }

        for (var index = 0; index < data->ItemCount && index < 140; index++)
        {
            ref var item = ref data->DuplicateItems[index];
            if (!item.Exists)
            {
                continue;
            }

            if (config.ExcludedItemIDs.Contains(ResolveItemID(ref item)))
            {
                item.IsEnabled = false;
                if (index < addon->ListItems.Length)
                {
                    addon->ListItems[index] = 0;
                }
            }
        }

        CaptureItemStates();
    }

    private void OnPostUpdate(AddonEvent eventType, AddonArgs args) => SaveChangedItemStates();

    private void SaveChangedItemStates()
    {
        var agent = AgentRetainerItemTransfer.Instance();
        var data = agent == null ? null : agent->Data;
        if (data == null || itemStates.Count == 0)
        {
            return;
        }

        var changed = false;
        for (var index = 0; index < data->ItemCount && index < 140; index++)
        {
            ref var item = ref data->DuplicateItems[index];
            if (!item.Exists)
            {
                continue;
            }

            var itemID = ResolveItemID(ref item);
            if (itemID == 0)
            {
                continue;
            }

            if (!itemStates.TryGetValue(itemID, out var wasEnabled))
            {
                itemStates[itemID] = item.IsEnabled;
                continue;
            }

            if (wasEnabled == item.IsEnabled)
            {
                continue;
            }

            itemStates[itemID] = item.IsEnabled;
            changed |= SetExcluded(config.ExcludedItemIDs, itemID, item.IsEnabled);
        }

        if (changed)
        {
            saveConfig();
        }
    }

    private void OnPreFinalize(AddonEvent eventType, AddonArgs args)
    {
        SaveChangedItemStates();
        itemStates.Clear();
    }

    private void CaptureItemStates()
    {
        itemStates.Clear();
        var agent = AgentRetainerItemTransfer.Instance();
        var data = agent == null ? null : agent->Data;
        if (data == null)
        {
            return;
        }

        for (var index = 0; index < data->ItemCount && index < 140; index++)
        {
            ref var item = ref data->DuplicateItems[index];
            if (!item.Exists)
            {
                continue;
            }

            var itemID = ResolveItemID(ref item);
            if (itemID == 0)
            {
                continue;
            }

            itemStates[itemID] = item.IsEnabled;
        }
    }

    private uint ResolveItemID(ref AgentRetainerItemTransferData.DuplicateItemEntry item)
    {
        var name = GetItemName(ref item);
        if (name.Length != 0)
        {
            itemIDsByName ??= BuildItemIDsByName();
            var itemID = itemIDsByName.GetValueOrDefault(name);
            if (itemID != 0)
            {
                return itemID;
            }
        }

        if (item.ItemId == 0)
        {
            return 0;
        }

        var rawItemID = ItemUtil.GetBaseId(item.ItemId).ItemId;
        return name.Length == 0 ||
               LuminaGetter.TryGetRow<Item>(rawItemID, out var itemRow) && itemRow.Name.ToString() == name
            ? rawItemID
            : 0;
    }

    private static string GetItemName(ref AgentRetainerItemTransferData.DuplicateItemEntry item) =>
        SeString.Parse(item.Name.AsSpan()).TextValue;

    private static Dictionary<string, uint> BuildItemIDsByName()
    {
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var item in LuminaGetter.Get<Item>())
        {
            var name = item.Name.ToString();
            if (name.Length == 0)
            {
                continue;
            }

            if (!result.TryAdd(name, item.RowId))
            {
                result[name] = 0;
            }
        }

        return result;
    }

    internal static bool SetExcluded(HashSet<uint> excludedItemIDs, uint itemID, bool enabled) =>
        enabled ? excludedItemIDs.Remove(itemID) : excludedItemIDs.Add(itemID);

    #region 常量

    private const string ADDON_NAME = "RetainerItemTransferList";

    #endregion

    public sealed class Settings
    {
        public HashSet<uint> ExcludedItemIDs { get; set; } = [];
    }
}
