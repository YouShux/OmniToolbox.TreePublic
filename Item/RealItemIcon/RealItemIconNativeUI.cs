using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Extensions;
using KamiToolKit.Premade.Node.Simple;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class RealItemIconNativeUI : IDisposable
{
    private const string GlassesAddonName = "TryonGlassesSelect";
    private const string ItemDetailAddonName = "ItemDetail";
    private const int ItemDetailIconOffset = 0x3C8;
    private const int MaxNodeDepth = 12;
    private const string CollectedCheckTexturePath = "ui/uld/RecipeNoteBook.tex";

    private static readonly Vector2 CollectedCheckTextureCoordinates = new(60f, 28f);
    private static readonly Vector2 CollectedCheckTextureSize = new(28f, 24f);
    private static readonly Vector2 CollectedCheckOffset = new(8.5f, 6f);
    private static readonly AddonEvent[] ObservedEvents =
    [
        AddonEvent.PostSetup,
        AddonEvent.PostRequestedUpdate,
        AddonEvent.PostRefresh,
        AddonEvent.PostDraw,
        AddonEvent.PreFinalize
    ];
    private static readonly string[] InventoryAddonNames =
    [
        "InventoryGrid",
        "InventoryGrid0",
        "InventoryGrid1",
        "InventoryGrid0E",
        "InventoryGrid1E",
        "InventoryGrid2E",
        "InventoryGrid3E",
        "InventoryBuddy",
        "InventoryBuddy2",
        "InventoryRetainer",
        "InventoryRetainerLarge",
        "RetainerGrid",
        "RetainerGrid0",
        "RetainerGrid1",
        "RetainerGrid2",
        "RetainerGrid3",
        "RetainerGrid4"
    ];

    private readonly RealItemIconConfig config;
    private readonly RealItemIconReplacementResolver resolver = new();
    private readonly RealItemIconInventoryIndex inventoryIndex;
    private readonly AddonEventRegistry addonEvents;
    private readonly Dictionary<nint, ReplacementEntry> replacements = [];
    private readonly Dictionary<nint, OverlayEntry> overlays = [];
    private readonly List<nint> keysToRemove = [];
    private bool disposed;

    public RealItemIconNativeUI(
        RealItemIconConfig config,
        PlayerInventoryService inventoryService)
    {
        this.config = config;
        inventoryIndex = new(inventoryService);
        addonEvents = new(DalamudServices.AddonLifecycle);
        try
        {
            for (var eventIndex = 0; eventIndex < ObservedEvents.Length; eventIndex++)
            {
                addonEvents.Register(ObservedEvents[eventIndex], GlassesAddonName, OnGlassesAddon);
                addonEvents.Register(ObservedEvents[eventIndex], ItemDetailAddonName, OnItemDetailAddon);
                for (var addonIndex = 0; addonIndex < InventoryAddonNames.Length; addonIndex++)
                {
                    addonEvents.Register(
                        ObservedEvents[eventIndex],
                        InventoryAddonNames[addonIndex],
                        OnInventoryAddon);
                }
            }
        }
        catch
        {
            addonEvents.Dispose();
            throw;
        }
    }

    public void OnConfigurationChanged()
    {
        if (!CanApply)
        {
            RestoreAll();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            addonEvents.Dispose();
        }
        finally
        {
            RestoreAll();
            keysToRemove.Clear();
        }
    }

    private bool CanApply => config.FacewearGlasses;

    private void OnGlassesAddon(AddonEvent eventType, AddonArgs args)
    {
        var addonAddress = args.Addon.Address;
        if (addonAddress == nint.Zero)
        {
            return;
        }

        if (eventType == AddonEvent.PreFinalize)
        {
            ForgetAddon(addonAddress);
            return;
        }

        if (!CanApply)
        {
            RestoreAddon(addonAddress);
            return;
        }

        ApplyToGlassesAddon((AtkUnitBase*)addonAddress);
    }

    private void OnItemDetailAddon(AddonEvent eventType, AddonArgs args)
    {
        var addonAddress = args.Addon.Address;
        if (addonAddress == nint.Zero)
        {
            return;
        }

        if (eventType == AddonEvent.PreFinalize)
        {
            ForgetAddon(addonAddress);
            return;
        }

        if (!CanApply)
        {
            RestoreAddon(addonAddress);
            return;
        }

        ApplyToItemDetailAddon((AtkUnitBase*)addonAddress);
    }

    private void OnInventoryAddon(AddonEvent eventType, AddonArgs args)
    {
        var addonAddress = args.Addon.Address;
        if (addonAddress == nint.Zero)
        {
            return;
        }

        if (eventType == AddonEvent.PreFinalize)
        {
            ForgetAddon(addonAddress);
            return;
        }

        if (!CanApply)
        {
            RestoreAddon(addonAddress);
            return;
        }

        ApplyToInventoryAddon((AtkUnitBase*)addonAddress);
    }

    private void ApplyToGlassesAddon(AtkUnitBase* addon)
    {
        var list = addon == null ? null : FindFirstListComponent(&addon->UldManager, 0);
        if (list == null || list->ListLength <= 0 || list->ItemRendererList == null)
        {
            RestoreAddon((nint)addon);
            return;
        }

        for (var index = 0; index < list->ListLength; index++)
        {
            var renderer = list->ItemRendererList[index].AtkComponentListItemRenderer;
            if (renderer == null)
            {
                continue;
            }

            var target = FindBestRendererIconTarget(renderer);
            if (target.IsEmpty)
            {
                continue;
            }

            var glassesID = ResolveGlassesID(renderer, index);
            if (glassesID == 0 || !resolver.TryGetByGlassesID(glassesID, out var replacement))
            {
                RestoreTarget((nint)addon, target);
                RemoveOverlay(target.Node);
                continue;
            }

            if (ApplyReplacement((nint)addon, target, replacement.IconID))
            {
                ApplyOverlay(target.Node, resolver.IsGlassesUnlocked(glassesID), (nint)addon);
                continue;
            }

            RestoreTarget((nint)addon, target);
            RemoveOverlay(target.Node);
        }
    }

    private uint ResolveGlassesID(AtkComponentListItemRenderer* renderer, int visibleIndex)
    {
        if (renderer->ListItemIndex >= 0 &&
            resolver.TryGetByGlassesID((uint)(renderer->ListItemIndex + 1), out var byItemIndex))
        {
            return byItemIndex.GlassesID;
        }

        if (resolver.TryGetByGlassesID((uint)(visibleIndex + 1), out var byVisibleIndex))
        {
            return byVisibleIndex.GlassesID;
        }

        return TryFindGlassesIDInTextNodes(&renderer->UldManager, 0, out var glassesId)
            ? glassesId
            : 0;
    }

    private bool TryFindGlassesIDInTextNodes(AtkUldManager* uld, int depth, out uint glassesID)
    {
        glassesID = 0;
        if (uld == null || depth > MaxNodeDepth || uld->NodeList == null)
        {
            return false;
        }

        for (var index = 0; index < uld->NodeListCount; index++)
        {
            var node = uld->NodeList[index];
            if (node == null)
            {
                continue;
            }

            if (node->Type == NodeType.Text &&
                TryParseDigits(((AtkTextNode*)node)->NodeText.ToString(), out var parsed) &&
                resolver.TryGetByGlassesID(parsed, out _))
            {
                glassesID = parsed;
                return true;
            }

            if (node->Type != NodeType.Component)
            {
                continue;
            }

            var component = node->GetAsAtkComponentNode()->Component;
            if (component != null &&
                TryFindGlassesIDInTextNodes(&component->UldManager, depth + 1, out glassesID))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseDigits(string text, out uint value)
    {
        value = 0;
        var digitCount = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is < '0' or > '9')
            {
                continue;
            }

            digitCount++;
            if (digitCount > 6)
            {
                value = 0;
                return false;
            }

            value = value * 10 + (uint)(text[index] - '0');
        }

        return digitCount > 0 && value > 0;
    }

    private void ApplyToItemDetailAddon(AtkUnitBase* addon)
    {
        var agent = AgentItemDetail.Instance();
        if (addon == null ||
            agent == null ||
            !resolver.TryGetByItemID(agent->ItemId, out var replacement))
        {
            RestoreAddon((nint)addon);
            return;
        }

        ClearOverlaysForAddon((nint)addon);
        var primaryTarget = CreateTarget(*(AtkComponentIcon**)((byte*)addon + ItemDetailIconOffset));
        if (!primaryTarget.IsEmpty)
        {
            if (ApplyReplacement((nint)addon, primaryTarget, replacement.IconID))
            {
                RemoveOverlay(primaryTarget.Node);
                return;
            }

            RestoreTarget((nint)addon, primaryTarget);
            RemoveOverlay(primaryTarget.Node);
        }

        var fallbackTarget = FindBestDetailIconTarget(&addon->UldManager);
        if (!fallbackTarget.IsEmpty &&
            fallbackTarget.Address != primaryTarget.Address &&
            ApplyReplacement((nint)addon, fallbackTarget, replacement.IconID))
        {
            RemoveOverlay(fallbackTarget.Node);
        }
    }

    private void ApplyToInventoryAddon(AtkUnitBase* addon)
    {
        if (addon == null)
        {
            return;
        }

        var itemOrderModule = ItemOrderModule.Instance();
        var sorter = itemOrderModule == null ? null : GetInventorySorter(addon, itemOrderModule);
        if (sorter == null)
        {
            RestoreAddon((nint)addon);
            return;
        }

        inventoryIndex.Refresh();
        if (IsRetainerInventoryParent(addon->NameString))
        {
            ApplyToRetainerInventoryParent(addon, sorter);
            return;
        }

        ApplyToInventorySlots(addon, sorter);
    }

    private void ApplyToRetainerInventoryParent(AtkUnitBase* addon, ItemOrderModuleSorter* sorter)
    {
        var parentTab = GetInventoryTab(addon);
        switch (addon->NameString)
        {
            case "InventoryRetainer":
                ApplyToChildInventorySlots(((AddonInventoryRetainer*)addon)->AddonControl, sorter, parentTab);
                break;
            case "InventoryRetainerLarge":
                ApplyToChildInventorySlots(((AddonInventoryRetainerLarge*)addon)->AddonControl, sorter, parentTab);
                break;
        }
    }

    private void ApplyToChildInventorySlots(
        AtkAddonControl addonControl,
        ItemOrderModuleSorter* sorter,
        int parentTab)
    {
        foreach (var child in addonControl.ChildAddons)
        {
            if (child.Value == null || child.Value->AtkUnitBase == null)
            {
                continue;
            }

            var childAddon = child.Value->AtkUnitBase;
            if (IsRetainerGridAddon(childAddon->NameString))
            {
                ApplyToInventorySlots(childAddon, sorter, parentTab);
            }
        }
    }

    private void ApplyToInventorySlots(
        AtkUnitBase* addon,
        ItemOrderModuleSorter* sorter,
        int parentTabOverride = int.MinValue)
    {
        var slots = GetInventorySlots(addon);
        for (var index = 0; index < slots.Length; index++)
        {
            var dragDrop = slots[index].Value;
            if (dragDrop == null)
            {
                continue;
            }

            var target = CreateTarget(dragDrop);
            if (!TryGetInventoryItemID(addon, sorter, index, parentTabOverride, out var itemId) ||
                !resolver.TryGetByItemID(itemId, out var replacement))
            {
                RestoreTarget((nint)addon, target);
                RemoveOverlay(target.Node);
                continue;
            }

            if (ApplyReplacement((nint)addon, target, replacement.IconID))
            {
                ApplyOverlay(
                    target.Node,
                    resolver.IsGlassesUnlocked(replacement.GlassesID),
                    (nint)addon);
                continue;
            }

            RestoreTarget((nint)addon, target);
            RemoveOverlay(target.Node);
        }
    }

    private bool TryGetInventoryItemID(
        AtkUnitBase* addon,
        ItemOrderModuleSorter* sorter,
        int visibleSlot,
        int parentTabOverride,
        out uint itemID)
    {
        itemID = 0;
        var parentTab = parentTabOverride == int.MinValue
            ? GetParentInventoryTab(addon)
            : parentTabOverride;
        var sortedIndex = RealItemIconInventoryIndex.GetSortedIndex(
            addon->NameString,
            parentTab,
            visibleSlot);
        if (sortedIndex < 0 || sortedIndex >= sorter->Items.LongCount)
        {
            return false;
        }

        var entry = sorter->Items[sortedIndex].Value;
        if (entry == null)
        {
            return false;
        }

        var holderID = GetInventoryHolderID(addon->NameString);
        return holderID != 0 && inventoryIndex.TryGetItemID(
            holderID,
            (InventoryType)((uint)sorter->InventoryType + entry->Page),
            (ushort)entry->Slot,
            out itemID);
    }

    private static ulong GetInventoryHolderID(string addonName)
    {
        if (!IsRetainerGridAddon(addonName) && !IsRetainerInventoryParent(addonName))
        {
            return DalamudServices.PlayerState.ContentId;
        }

        var retainerManager = RetainerManager.Instance();
        var retainer = retainerManager == null ? null : retainerManager->GetActiveRetainer();
        return retainer == null ? 0 : retainer->RetainerId;
    }

    private static Span<Pointer<AtkComponentDragDrop>> GetInventorySlots(AtkUnitBase* addon) =>
        addon->NameString switch
        {
            "InventoryBuddy" or "InventoryBuddy2" => ((AddonInventoryBuddy*)addon)->Slots,
            _ when IsInventoryGridAddon(addon->NameString) || IsRetainerGridAddon(addon->NameString) =>
                ((AddonInventoryGrid*)addon)->Slots,
            _ => [],
        };

    private static ItemOrderModuleSorter* GetInventorySorter(
        AtkUnitBase* addon,
        ItemOrderModule* itemOrderModule) =>
        addon->NameString switch
        {
            _ when IsInventoryGridAddon(addon->NameString) => itemOrderModule->InventorySorter,
            "InventoryBuddy" or "InventoryBuddy2" => ((AddonInventoryBuddy*)addon)->TabIndex switch
            {
                0 => itemOrderModule->SaddleBagSorter,
                1 => itemOrderModule->PremiumSaddleBagSorter,
                _ => null,
            },
            _ when IsRetainerGridAddon(addon->NameString) || IsRetainerInventoryParent(addon->NameString) =>
                GetActiveRetainerSorter(itemOrderModule),
            _ => null,
        };

    private static ItemOrderModuleSorter* GetActiveRetainerSorter(ItemOrderModule* itemOrderModule)
    {
        var retainerID = itemOrderModule->ActiveRetainerId;
        return retainerID != 0 &&
               itemOrderModule->RetainerSorter.TryGetValue(retainerID, out var sorter, false)
            ? sorter.Value
            : null;
    }

    private static int GetInventoryTab(AtkUnitBase* addon) =>
        addon->NameString switch
        {
            "Inventory" => ((AddonInventory*)addon)->TabIndex,
            "InventoryLarge" => ((AddonInventoryLarge*)addon)->TabIndex,
            "InventoryExpansion" => ((AddonInventoryExpansion*)addon)->TabIndex,
            "InventoryRetainer" => ((AddonInventoryRetainer*)addon)->TabIndex,
            "InventoryRetainerLarge" => ((AddonInventoryRetainerLarge*)addon)->TabIndex,
            "InventoryBuddy" or "InventoryBuddy2" => ((AddonInventoryBuddy*)addon)->TabIndex,
            _ => 0,
        };

    private static int GetParentInventoryTab(AtkUnitBase* addon)
    {
        if (addon->ParentId == 0)
        {
            return 0;
        }

        var unitManager = RaptureAtkUnitManager.Instance();
        var parent = unitManager == null ? null : unitManager->GetAddonById(addon->ParentId);
        return parent == null ? 0 : GetInventoryTab(parent);
    }

    private static bool IsInventoryGridAddon(string addonName) =>
        addonName is
            "InventoryGrid" or
            "InventoryGrid0" or
            "InventoryGrid1" or
            "InventoryGrid0E" or
            "InventoryGrid1E" or
            "InventoryGrid2E" or
            "InventoryGrid3E";

    private static bool IsRetainerInventoryParent(string addonName) =>
        addonName is "InventoryRetainer" or "InventoryRetainerLarge";

    private static bool IsRetainerGridAddon(string addonName) =>
        addonName is
            "RetainerGrid" or
            "RetainerGrid0" or
            "RetainerGrid1" or
            "RetainerGrid2" or
            "RetainerGrid3" or
            "RetainerGrid4";

    private static AtkComponentList* FindFirstListComponent(AtkUldManager* uld, int depth)
    {
        if (uld == null || depth > MaxNodeDepth || uld->NodeList == null)
        {
            return null;
        }

        for (var index = 0; index < uld->NodeListCount; index++)
        {
            var node = uld->NodeList[index];
            if (node == null || node->Type != NodeType.Component)
            {
                continue;
            }

            var component = node->GetAsAtkComponentNode()->Component;
            if (component == null)
            {
                continue;
            }

            var info = (AtkUldComponentInfo*)component->UldManager.Objects;
            if (info != null && info->ComponentType == ComponentType.List)
            {
                return (AtkComponentList*)component;
            }

            var nested = FindFirstListComponent(&component->UldManager, depth + 1);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static IconTarget FindBestRendererIconTarget(AtkComponentListItemRenderer* renderer)
    {
        var best = default(IconTarget);
        var bestScore = int.MinValue;
        var firstTextX = FindFirstTextX(&renderer->UldManager, 0);
        if (renderer->DragDropComponent != null)
        {
            ConsiderTarget(
                CreateTarget(renderer->DragDropComponent),
                TargetSearch.Renderer,
                firstTextX,
                ref best,
                ref bestScore);
        }

        FindBestTarget(
            &renderer->UldManager,
            TargetSearch.Renderer,
            firstTextX,
            0,
            ref best,
            ref bestScore);
        return best;
    }

    private static IconTarget FindBestDetailIconTarget(AtkUldManager* uld)
    {
        var best = default(IconTarget);
        var bestScore = int.MinValue;
        FindBestTarget(
            uld,
            TargetSearch.Detail,
            float.PositiveInfinity,
            0,
            ref best,
            ref bestScore);
        return best;
    }

    private static void FindBestTarget(
        AtkUldManager* uld,
        TargetSearch search,
        float firstTextX,
        int depth,
        ref IconTarget best,
        ref int bestScore)
    {
        if (uld == null || depth > MaxNodeDepth || uld->NodeList == null)
        {
            return;
        }

        for (var index = 0; index < uld->NodeListCount; index++)
        {
            var node = uld->NodeList[index];
            if (node == null)
            {
                continue;
            }

            if (node->Type == NodeType.Image)
            {
                ConsiderTarget(
                    CreateTarget((AtkImageNode*)node),
                    search,
                    firstTextX,
                    ref best,
                    ref bestScore);
            }

            if (node->Type != NodeType.Component)
            {
                continue;
            }

            var component = node->GetAsAtkComponentNode()->Component;
            if (component == null)
            {
                continue;
            }

            var info = (AtkUldComponentInfo*)component->UldManager.Objects;
            if (info != null)
            {
                if (info->ComponentType == ComponentType.DragDrop)
                {
                    ConsiderTarget(
                        CreateTarget((AtkComponentDragDrop*)component),
                        search,
                        firstTextX,
                        ref best,
                        ref bestScore);
                }
                else if (info->ComponentType == ComponentType.Icon)
                {
                    ConsiderTarget(
                        CreateTarget((AtkComponentIcon*)component),
                        search,
                        firstTextX,
                        ref best,
                        ref bestScore);
                }
            }

            FindBestTarget(
                &component->UldManager,
                search,
                firstTextX,
                depth + 1,
                ref best,
                ref bestScore);
        }
    }

    private static void ConsiderTarget(
        IconTarget candidate,
        TargetSearch search,
        float firstTextX,
        ref IconTarget best,
        ref int bestScore)
    {
        var node = candidate.Node;
        if (candidate.IsEmpty || node == null || !node->IsVisible() || GetCurrentIconID(candidate) == 0)
        {
            return;
        }

        var width = node->GetWidth();
        var height = node->GetHeight();
        if (search == TargetSearch.Renderer)
        {
            if (width < 24 || height < 24 || width > 64 || height > 64 ||
                !float.IsPositiveInfinity(firstTextX) && node->GetXFloat() >= firstTextX - 2f)
            {
                return;
            }
        }
        else if (width < 16 || height < 16 || width > 96 || height > 96)
        {
            return;
        }

        var area = width * height;
        var score = search == TargetSearch.Renderer
            ? 1_000 + (candidate.Kind switch
            {
                TargetKind.Icon => 300,
                TargetKind.DragDrop => 260,
                TargetKind.Image => 80,
                _ => 0,
            })
            : candidate.Kind switch
            {
                TargetKind.Icon => 200_000 + area,
                TargetKind.DragDrop => 180_000 + area,
                TargetKind.Image => (int)area,
                _ => int.MinValue,
            };
        if (search == TargetSearch.Renderer)
        {
            score -= (int)(Math.Abs(width - height) * 6f);
            score -= (int)(Math.Abs(area - 1_600f) / 12f);
            if (!float.IsPositiveInfinity(firstTextX))
            {
                score += (int)Math.Clamp(firstTextX - node->GetXFloat(), 0f, 160f);
            }
        }

        if (score > bestScore)
        {
            best = candidate;
            bestScore = score;
        }
    }

    private static float FindFirstTextX(AtkUldManager* uld, int depth)
    {
        if (uld == null || depth > MaxNodeDepth || uld->NodeList == null)
        {
            return float.PositiveInfinity;
        }

        var firstTextX = float.PositiveInfinity;
        for (var index = 0; index < uld->NodeListCount; index++)
        {
            var node = uld->NodeList[index];
            if (node == null)
            {
                continue;
            }

            if (node->Type == NodeType.Text &&
                node->IsVisible() &&
                ((AtkTextNode*)node)->NodeText.StringPtr.HasValue &&
                *(((AtkTextNode*)node)->NodeText.StringPtr.Value) != 0)
            {
                firstTextX = Math.Min(firstTextX, node->GetXFloat());
            }

            if (node->Type != NodeType.Component)
            {
                continue;
            }

            var component = node->GetAsAtkComponentNode()->Component;
            if (component != null)
            {
                firstTextX = Math.Min(
                    firstTextX,
                    FindFirstTextX(&component->UldManager, depth + 1));
            }
        }

        return firstTextX;
    }

    private bool ApplyReplacement(nint addonAddress, IconTarget target, uint replacementIconID)
    {
        if (target.IsEmpty || replacementIconID == 0)
        {
            return false;
        }

        var currentIconID = GetCurrentIconID(target);
        if (currentIconID == 0)
        {
            return false;
        }

        var originalIconID = currentIconID;
        if (replacements.TryGetValue(target.Address, out var existing))
        {
            if (existing.AddonAddress == addonAddress && currentIconID == existing.ReplacementIconID)
            {
                if (currentIconID == replacementIconID)
                {
                    return true;
                }

                originalIconID = existing.OriginalIconID;
            }
            else
            {
                replacements.Remove(target.Address);
            }
        }

        if (currentIconID == replacementIconID)
        {
            return true;
        }

        if (!LoadIcon(target, replacementIconID))
        {
            return false;
        }

        replacements[target.Address] = new(
            addonAddress,
            target.Kind,
            target.Address,
            (nint)target.Node,
            originalIconID,
            replacementIconID);
        return true;
    }

    private void RestoreTarget(nint addonAddress, IconTarget target)
    {
        if (target.IsEmpty || !replacements.Remove(target.Address, out var entry))
        {
            return;
        }

        if (entry.AddonAddress == addonAddress)
        {
            RestoreEntry(entry);
        }
    }

    private void RestoreAddon(nint addonAddress)
    {
        if (addonAddress == nint.Zero)
        {
            return;
        }

        keysToRemove.Clear();
        foreach (var pair in replacements)
        {
            if (pair.Value.AddonAddress == addonAddress)
            {
                RestoreEntry(pair.Value);
                keysToRemove.Add(pair.Key);
            }
        }

        RemoveReplacementKeys();
        ClearOverlaysForAddon(addonAddress);
    }

    private void ForgetAddon(nint addonAddress)
    {
        keysToRemove.Clear();
        foreach (var pair in replacements)
        {
            if (pair.Value.AddonAddress == addonAddress)
            {
                keysToRemove.Add(pair.Key);
            }
        }

        RemoveReplacementKeys();
        ClearOverlaysForAddon(addonAddress);
    }

    private void RemoveReplacementKeys()
    {
        for (var index = 0; index < keysToRemove.Count; index++)
        {
            replacements.Remove(keysToRemove[index]);
        }

        keysToRemove.Clear();
    }

    private void RestoreAll()
    {
        foreach (var entry in replacements.Values)
        {
            RestoreEntry(entry);
        }

        replacements.Clear();
        foreach (var entry in overlays.Values)
        {
            entry.Node.Dispose();
        }

        overlays.Clear();
    }

    private static void RestoreEntry(ReplacementEntry entry)
    {
        var unitManager = RaptureAtkUnitManager.Instance();
        if (entry.NodeAddress == nint.Zero ||
            unitManager == null ||
            unitManager->GetAddonByNode((AtkResNode*)entry.NodeAddress) != (AtkUnitBase*)entry.AddonAddress)
        {
            return;
        }

        var target = CreateTarget(entry);
        if (GetCurrentIconID(target) == entry.ReplacementIconID)
        {
            LoadIcon(target, entry.OriginalIconID);
        }
    }

    private void ApplyOverlay(AtkResNode* iconNode, bool unlocked, nint addonAddress)
    {
        if (iconNode == null)
        {
            return;
        }

        if (!unlocked)
        {
            RemoveOverlay(iconNode);
            return;
        }

        var key = (nint)iconNode;
        if (overlays.TryGetValue(key, out var existing) && existing.AddonAddress != addonAddress)
        {
            existing.Node.Dispose();
            overlays.Remove(key);
        }

        if (!overlays.TryGetValue(key, out var entry))
        {
            var node = new SimpleImageNode
            {
                NodeFlags = NodeFlags.Visible | NodeFlags.Enabled,
                WrapMode = WrapMode.Stretch,
            };
            node.AttachNode(iconNode, NodePosition.AfterTarget);
            entry = new(node, addonAddress);
            overlays[key] = entry;
        }

        var overlay = entry.Node;
        overlay.NodeFlags = NodeFlags.Visible | NodeFlags.Enabled;
        overlay.TexturePath = CollectedCheckTexturePath;
        overlay.TextureCoordinates = CollectedCheckTextureCoordinates;
        overlay.TextureSize = CollectedCheckTextureSize;
        var width = Math.Max(16f, iconNode->GetWidth() * iconNode->GetScaleX());
        var height = Math.Max(16f, iconNode->GetHeight() * iconNode->GetScaleY());
        var overlayWidth = Math.Clamp(Math.Min(width, height) * 0.70f, 20f, 36f);
        var overlayHeight = overlayWidth * CollectedCheckTextureSize.Y / CollectedCheckTextureSize.X;
        overlay.Size = new(overlayWidth, overlayHeight);
        overlay.Position = new(
            iconNode->GetXFloat() + width - overlayWidth + CollectedCheckOffset.X,
            iconNode->GetYFloat() + height - overlayHeight + CollectedCheckOffset.Y);
        overlay.IsVisible = true;
        overlay.MarkDirty();
    }

    private void RemoveOverlay(AtkResNode* iconNode)
    {
        if (iconNode != null && overlays.Remove((nint)iconNode, out var entry))
        {
            entry.Node.Dispose();
        }
    }

    private void ClearOverlaysForAddon(nint addonAddress)
    {
        keysToRemove.Clear();
        foreach (var pair in overlays)
        {
            if (pair.Value.AddonAddress == addonAddress)
            {
                pair.Value.Node.Dispose();
                keysToRemove.Add(pair.Key);
            }
        }

        for (var index = 0; index < keysToRemove.Count; index++)
        {
            overlays.Remove(keysToRemove[index]);
        }

        keysToRemove.Clear();
    }

    private static IconTarget CreateTarget(AtkComponentDragDrop* dragDrop) =>
        dragDrop == null
            ? default
            : new(
                TargetKind.DragDrop,
                (nint)dragDrop,
                GetDragDropOverlayTarget(dragDrop),
                dragDrop,
                dragDrop->AtkComponentIcon,
                null);

    private static IconTarget CreateTarget(AtkComponentIcon* icon) =>
        icon == null
            ? default
            : new(
                TargetKind.Icon,
                (nint)icon,
                GetIconOverlayTarget(icon),
                null,
                icon,
                null);

    private static IconTarget CreateTarget(AtkImageNode* image) =>
        image == null
            ? default
            : new(
                TargetKind.Image,
                (nint)image,
                &image->AtkResNode,
                null,
                null,
                image);

    private static IconTarget CreateTarget(ReplacementEntry entry) =>
        entry.Kind switch
        {
            TargetKind.DragDrop => CreateTarget((AtkComponentDragDrop*)entry.TargetAddress),
            TargetKind.Icon => CreateTarget((AtkComponentIcon*)entry.TargetAddress),
            TargetKind.Image => CreateTarget((AtkImageNode*)entry.TargetAddress),
            _ => default,
        };

    private static AtkResNode* GetIconOverlayTarget(AtkComponentIcon* icon)
    {
        if (icon == null)
        {
            return null;
        }

        return icon->IconImage == null
            ? icon->OuterResNode
            : &icon->IconImage->AtkResNode;
    }

    private static AtkResNode* GetDragDropOverlayTarget(AtkComponentDragDrop* dragDrop)
    {
        if (dragDrop == null)
        {
            return null;
        }

        var iconTarget = GetIconOverlayTarget(dragDrop->AtkComponentIcon);
        return iconTarget != null
            ? iconTarget
            : dragDrop->OwnerNode == null
                ? null
                : &dragDrop->OwnerNode->AtkResNode;
    }

    private static uint GetCurrentIconID(IconTarget target)
    {
        if (target.Kind == TargetKind.DragDrop && target.DragDrop != null)
        {
            if (target.Icon != null && target.Icon->IconId > 0)
            {
                return target.Icon->IconId;
            }

            var iconID = target.DragDrop->GetIconId();
            return iconID > 0 ? (uint)iconID : 0;
        }

        return target.Kind switch
        {
            TargetKind.Icon when target.Icon != null => target.Icon->IconId,
            TargetKind.Image when target.Image != null => GetImageIconID(target.Image),
            _ => 0,
        };
    }

    private static uint GetImageIconID(AtkImageNode* image)
    {
        if (image == null ||
            image->PartsList == null ||
            image->PartsList->Parts == null ||
            image->PartId >= image->PartsList->PartCount)
        {
            return 0;
        }

        var asset = image->PartsList->Parts[image->PartId].UldAsset;
        return asset == null ||
               asset->AtkTexture.TextureType != TextureType.Resource ||
               asset->AtkTexture.Resource == null
            ? 0
            : asset->AtkTexture.Resource->IconId;
    }

    private static bool LoadIcon(IconTarget target, uint iconID)
    {
        if (iconID == 0)
        {
            return false;
        }

        switch (target.Kind)
        {
            case TargetKind.DragDrop when target.DragDrop != null:
                var loaded = target.DragDrop->LoadIcon(iconID);
                if (target.Icon != null)
                {
                    loaded = target.Icon->LoadIcon(iconID) || loaded;
                    MarkIconDirty(target.Icon);
                }

                MarkDragDropDirty(target.DragDrop);
                return loaded;
            case TargetKind.Icon when target.Icon != null:
                if (!target.Icon->LoadIcon(iconID))
                {
                    return false;
                }

                MarkIconDirty(target.Icon);
                return true;
            case TargetKind.Image when target.Image != null:
                if (target.Image->PartsList == null ||
                    target.Image->PartsList->Parts == null ||
                    target.Image->PartId >= target.Image->PartsList->PartCount)
                {
                    return false;
                }

                target.Image->LoadIconTexture(
                    iconID,
                    (int)AtkUldPartExtensions.GetIconSubFolder(iconID));
                MarkNodeDirty(&target.Image->AtkResNode);
                return true;
            default:
                return false;
        }
    }

    private static void MarkDragDropDirty(AtkComponentDragDrop* dragDrop)
    {
        if (dragDrop->OwnerNode != null)
        {
            MarkNodeDirty(&dragDrop->OwnerNode->AtkResNode);
        }

        MarkIconDirty(dragDrop->AtkComponentIcon);
    }

    private static void MarkIconDirty(AtkComponentIcon* icon)
    {
        if (icon == null)
        {
            return;
        }

        if (icon->AtkComponentBase.OwnerNode != null)
        {
            MarkNodeDirty(&icon->AtkComponentBase.OwnerNode->AtkResNode);
        }

        MarkNodeDirty(icon->OuterResNode);
        MarkNodeDirty(icon->FrameContainer);
        MarkNodeDirty(icon->ComboBorder);
        MarkNodeDirty(icon->Frame);
        if (icon->IconImage != null)
        {
            MarkNodeDirty(&icon->IconImage->AtkResNode);
        }

        if (icon->FrameIcon != null)
        {
            MarkNodeDirty(&icon->FrameIcon->AtkResNode);
        }

        if (icon->QuantityText != null)
        {
            MarkNodeDirty(&icon->QuantityText->AtkResNode);
        }
    }

    private static void MarkNodeDirty(AtkResNode* node)
    {
        if (node != null)
        {
            node->DrawFlags |= 0x1;
        }
    }

    private readonly struct IconTarget
    {
        public IconTarget(
            TargetKind kind,
            nint address,
            AtkResNode* node,
            AtkComponentDragDrop* dragDrop,
            AtkComponentIcon* icon,
            AtkImageNode* image)
        {
            Kind = kind;
            Address = address;
            Node = node;
            DragDrop = dragDrop;
            Icon = icon;
            Image = image;
        }

        public TargetKind Kind { get; }

        public nint Address { get; }

        public AtkResNode* Node { get; }

        public AtkComponentDragDrop* DragDrop { get; }

        public AtkComponentIcon* Icon { get; }

        public AtkImageNode* Image { get; }

        public bool IsEmpty => Kind == TargetKind.None || Address == nint.Zero || Node == null;
    }

    private readonly record struct ReplacementEntry(
        nint AddonAddress,
        TargetKind Kind,
        nint TargetAddress,
        nint NodeAddress,
        uint OriginalIconID,
        uint ReplacementIconID);

    private readonly record struct OverlayEntry(SimpleImageNode Node, nint AddonAddress);

    private enum TargetKind
    {
        None,
        DragDrop,
        Icon,
        Image
    }

    private enum TargetSearch
    {
        Renderer,
        Detail
    }
}
