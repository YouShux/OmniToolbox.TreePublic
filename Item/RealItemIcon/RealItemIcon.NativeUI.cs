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
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using OmniToolbox.Config;
using OmniToolbox.Items;

namespace OmniToolbox.TreePublic;

public sealed partial class RealItemIcon
{
    internal sealed unsafe class RealItemIconNativeUI : IDisposable
    {
        private static readonly Vector2      CollectedCheckTextureCoordinates = new(60f, 28f);
        private static readonly Vector2      CollectedCheckTextureSize        = new(28f, 24f);
        private static readonly Vector2      CollectedCheckOffset             = new(8.5f, 6f);
        private static readonly AddonEvent[] ObservedEvents                   =
        [
            AddonEvent.PostSetup,
            AddonEvent.PostRequestedUpdate,
            AddonEvent.PostRefresh,
            AddonEvent.PostDraw,
            AddonEvent.PreFinalize
        ];
        private static readonly string[]     InventoryAddonNames              =
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

        private readonly RealItemIconConfig                 config;
        private readonly RealItemIconReplacementResolver    resolver       = new();
        private readonly RealItemIconInventoryIndex         inventoryIndex;
        private readonly AddonEventRegistry                 addonEvents;
        private readonly Dictionary<nint, ReplacementEntry> replacements   = [];
        private readonly Dictionary<nint, OverlayEntry>     overlays       = [];
        private readonly List<nint>                         keysToRemove   = [];
        private readonly Dictionary<nint, Dictionary<nint, CachedIconTarget>> targetCaches = [];
        private bool                                        disposed;

        public RealItemIconNativeUI
        (
            RealItemIconConfig       config,
            IInventorySnapshotReader inventoryService
        )
        {
            this.config    = config;
            inventoryIndex = new(inventoryService);
            addonEvents    = new(DalamudServices.AddonLifecycle);
            try
            {
                for (var eventIndex = 0; eventIndex < ObservedEvents.Length; eventIndex++)
                {
                    addonEvents.Register(ObservedEvents[eventIndex], GLASSES_ADDON_NAME, OnAddon);
                    addonEvents.Register(ObservedEvents[eventIndex], ITEM_DETAIL_ADDON_NAME, OnAddon);
                    for (var addonIndex = 0; addonIndex < InventoryAddonNames.Length; addonIndex++)
                    {
                        addonEvents.Register(
                            ObservedEvents[eventIndex],
                            InventoryAddonNames[addonIndex],
                            OnAddon);
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
            resolver.InvalidateUnlocks();
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

        private void OnAddon
        (
            AddonEvent eventType,
            AddonArgs  args
        )
        {
            var addonAddress = args.Addon.Address;
            if (addonAddress == nint.Zero)
            {
                return;
            }

            if (eventType != AddonEvent.PostDraw)
            {
                resolver.InvalidateUnlocks();
                if (targetCaches.TryGetValue(addonAddress, out var targets))
                    targets.Clear();
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

            resolver.PrepareUnlocks();
            switch (args.AddonName)
            {
                case GLASSES_ADDON_NAME:
                    ApplyToGlassesAddon((AtkUnitBase*)addonAddress);
                    break;
                case ITEM_DETAIL_ADDON_NAME:
                    ApplyToItemDetailAddon((AtkUnitBase*)addonAddress);
                    break;
                default:
                    ApplyToInventoryAddon((AtkUnitBase*)addonAddress, args.AddonName);
                    break;
            }
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

                var target = ResolveIconTarget((nint)addon, &renderer->UldManager, renderer);
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

        private uint ResolveGlassesID
        (
            AtkComponentListItemRenderer* renderer,
            int                           visibleIndex
        )
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

        private bool TryFindGlassesIDInTextNodes
        (
            AtkUldManager* uld,
            int            depth,
            out uint       glassesID
        )
        {
            glassesID = 0;
            if (uld == null || depth > MAX_NODE_DEPTH || uld->NodeList == null)
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

        private static bool TryParseDigits
        (
            string   text,
            out uint value
        )
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
            var primaryTarget = CreateTarget(*(AtkComponentIcon**)((byte*)addon + ITEM_DETAIL_ICON_OFFSET));
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

            var fallbackTarget = ResolveIconTarget((nint)addon, &addon->UldManager);
            if (!fallbackTarget.IsEmpty &&
                fallbackTarget.Address != primaryTarget.Address &&
                ApplyReplacement((nint)addon, fallbackTarget, replacement.IconID))
            {
                RemoveOverlay(fallbackTarget.Node);
            }
        }

        private void ApplyToInventoryAddon(AtkUnitBase* addon, string addonName)
        {
            if (addon == null)
            {
                return;
            }

            var itemOrderModule = ItemOrderModule.Instance();
            var sorter = itemOrderModule == null ? null : GetInventorySorter(addon, addonName, itemOrderModule);
            if (sorter == null)
            {
                RestoreAddon((nint)addon);
                return;
            }

            if (inventoryIndex.Refresh())
                resolver.InvalidateUnlocks();
            if (IsRetainerInventoryParent(addonName))
            {
                ApplyToRetainerInventoryParent(addon, addonName, sorter);
                return;
            }

            ApplyToInventorySlots(addon, addonName, sorter);
        }

        private void ApplyToRetainerInventoryParent
        (
            AtkUnitBase*           addon,
            string                 addonName,
            ItemOrderModuleSorter* sorter
        )
        {
            var parentTab = GetInventoryTab(addon);
            switch (addonName)
            {
                case "InventoryRetainer":
                    ApplyToChildInventorySlots(((AddonInventoryRetainer*)addon)->AddonControl, sorter, parentTab);
                    break;
                case "InventoryRetainerLarge":
                    ApplyToChildInventorySlots(((AddonInventoryRetainerLarge*)addon)->AddonControl, sorter, parentTab);
                    break;
            }
        }

        private void ApplyToChildInventorySlots
        (
            AtkAddonControl        addonControl,
            ItemOrderModuleSorter* sorter,
            int                    parentTab
        )
        {
            foreach (var child in addonControl.ChildAddons)
            {
                if (child.Value == null || child.Value->AtkUnitBase == null)
                {
                    continue;
                }

                var childAddon = child.Value->AtkUnitBase;
                var childName = childAddon->NameString;
                if (IsRetainerGridAddon(childName))
                {
                    ApplyToInventorySlots(childAddon, childName, sorter, parentTab);
                }
            }
        }

        private void ApplyToInventorySlots
        (
            AtkUnitBase*           addon,
            string                 addonName,
            ItemOrderModuleSorter* sorter,
            int                    parentTabOverride = int.MinValue
        )
        {
            var slotCount = addonName is "InventoryBuddy" or "InventoryBuddy2" ? 70 : 35;
            var parentTab = parentTabOverride == int.MinValue ? GetParentInventoryTab(addon) : parentTabOverride;
            var holderID = GetInventoryHolderID(addonName);
            for (var index = 0; index < slotCount; index++)
            {
                var dragDrop = GetInventorySlot(addon, addonName, index);
                if (dragDrop == null)
                {
                    continue;
                }

                var target = CreateTarget(dragDrop);
                if (!TryGetInventoryItemID(addonName, sorter, index, parentTab, holderID, out var itemId) ||
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

        private bool TryGetInventoryItemID
        (
            string                 addonName,
            ItemOrderModuleSorter* sorter,
            int                    visibleSlot,
            int                    parentTab,
            ulong                  holderID,
            out uint               itemID
        )
        {
            itemID = 0;
            var sortedIndex = RealItemIconInventoryIndex.GetSortedIndex(
                addonName,
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

        private static AtkComponentDragDrop* GetInventorySlot
        (
            AtkUnitBase* addon,
            string       addonName,
            int          index
        )
        {
            var slotsOffset = addonName is "InventoryBuddy" or "InventoryBuddy2"
                ? 0x248
                : 0x238;
            return ((Pointer<AtkComponentDragDrop>*)((byte*)addon + slotsOffset))[index].Value;
        }

        private static ItemOrderModuleSorter* GetInventorySorter
        (
            AtkUnitBase*     addon,
            string           addonName,
            ItemOrderModule* itemOrderModule
        ) =>
            addonName switch
            {
                _ when IsInventoryGridAddon(addonName) => itemOrderModule->InventorySorter,
                "InventoryBuddy" or "InventoryBuddy2" => ((AddonInventoryBuddy*)addon)->TabIndex switch
                {
                    0 => itemOrderModule->SaddleBagSorter,
                    1 => itemOrderModule->PremiumSaddleBagSorter,
                    _ => null,
                },
                _ when IsRetainerGridAddon(addonName) || IsRetainerInventoryParent(addonName) =>
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

        private static int GetInventoryTab(AtkUnitBase* addon)
        {
            ReadOnlySpan<byte> addonName = addon->Name;
            addonName = addonName[..addonName.IndexOf((byte)0)];
            return addonName switch
            {
                _ when addonName.SequenceEqual("Inventory"u8) => ((AddonInventory*)addon)->TabIndex,
                _ when addonName.SequenceEqual("InventoryLarge"u8) => ((AddonInventoryLarge*)addon)->TabIndex,
                _ when addonName.SequenceEqual("InventoryExpansion"u8) => ((AddonInventoryExpansion*)addon)->TabIndex,
                _ when addonName.SequenceEqual("InventoryRetainer"u8) => ((AddonInventoryRetainer*)addon)->TabIndex,
                _ when addonName.SequenceEqual("InventoryRetainerLarge"u8) => ((AddonInventoryRetainerLarge*)addon)->TabIndex,
                _ when addonName.SequenceEqual("InventoryBuddy"u8) || addonName.SequenceEqual("InventoryBuddy2"u8) =>
                    ((AddonInventoryBuddy*)addon)->TabIndex,
                _ => 0,
            };
        }

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

        private static AtkComponentList* FindFirstListComponent
        (
            AtkUldManager* uld,
            int            depth
        )
        {
            if (uld == null || depth > MAX_NODE_DEPTH || uld->NodeList == null)
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

        private IconTarget ResolveIconTarget(nint addonAddress, AtkUldManager* uld,
            AtkComponentListItemRenderer* renderer = null)
        {
            if (!targetCaches.TryGetValue(addonAddress, out var targets))
                targetCaches.Add(addonAddress, targets = []);
            if (!targets.TryGetValue((nint)uld, out var cached))
                targets.Add((nint)uld, cached = new());
            var dragDrop = renderer is null ? null : renderer->DragDropComponent;
            var rendererTarget = CreateTarget(dragDrop);
            var rendererState = rendererTarget.IsEmpty ? default :
                CreateIconNodeState(rendererTarget.Node, (AtkComponentBase*)dragDrop, ComponentType.DragDrop, rendererTarget);
            var changed = !cached.Parsed || cached.RendererState != rendererState;
            var count = 0;
            CaptureIconNodes(uld, 0, cached.Nodes, ref count, ref changed);
            if (count < cached.Nodes.Count)
            {
                cached.Nodes.RemoveRange(count, cached.Nodes.Count - count);
                changed = true;
            }
            if (changed)
            {
                cached.Target = renderer is null ? FindBestDetailIconTarget(uld) : FindBestRendererIconTarget(renderer);
                cached.RendererState = rendererState;
                cached.Parsed = true;
            }
            return cached.Target;
        }

        private static void CaptureIconNodes(AtkUldManager* uld, int depth, List<IconNodeState> nodes,
            ref int count, ref bool changed)
        {
            if (uld is null || depth > MAX_NODE_DEPTH || uld->NodeList is null)
                return;
            for (var index = 0; index < uld->NodeListCount; index++)
            {
                var node = uld->NodeList[index];
                if (node is null)
                    continue;
                var component = node->Type == NodeType.Component ? node->GetAsAtkComponentNode()->Component : null;
                var info = component is null ? null : (AtkUldComponentInfo*)component->UldManager.Objects;
                var componentType = info is null ? default : info->ComponentType;
                var target = default(IconTarget);
                if (node->Type == NodeType.Image)
                    target = CreateTarget((AtkImageNode*)node);
                else if (component is not null && componentType == ComponentType.Icon)
                    target = CreateTarget((AtkComponentIcon*)component);
                else if (component is not null && componentType == ComponentType.DragDrop)
                    target = CreateTarget((AtkComponentDragDrop*)component);
                var state = CreateIconNodeState(node, component, componentType, target);
                if (count == nodes.Count)
                {
                    nodes.Add(state);
                    changed = true;
                }
                else if (nodes[count] != state)
                {
                    nodes[count] = state;
                    changed = true;
                }
                count++;
                if (component is not null)
                    CaptureIconNodes(&component->UldManager, depth + 1, nodes, ref count, ref changed);
            }
        }

        private static IconNodeState CreateIconNodeState(AtkResNode* node, AtkComponentBase* component,
            ComponentType componentType, IconTarget target)
        {
            var anchor = target.IsEmpty ? node : target.Node;
            var text = node->Type == NodeType.Text ? (AtkTextNode*)node : null;
            var visible = (!target.IsEmpty || text is not null) && anchor->IsVisible();
            var hasContent = false;
            var width = 0f;
            var height = 0f;
            var x = 0f;
            if (visible)
            {
                hasContent = !target.IsEmpty ? GetCurrentIconID(target) != 0 :
                    text->NodeText.StringPtr.HasValue && *text->NodeText.StringPtr.Value != 0;
                if (hasContent)
                {
                    x = anchor->GetXFloat();
                    if (!target.IsEmpty)
                    {
                        width = anchor->GetWidth();
                        height = anchor->GetHeight();
                    }
                }
            }
            return new((nint)node, (nint)component, (nint)target.Icon, (nint)target.Node, node->Type, componentType,
                width, height, x, visible, hasContent);
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

        private static void FindBestTarget
        (
            AtkUldManager* uld,
            TargetSearch   search,
            float          firstTextX,
            int            depth,
            ref IconTarget best,
            ref int        bestScore
        )
        {
            if (uld == null || depth > MAX_NODE_DEPTH || uld->NodeList == null)
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

        private static void ConsiderTarget
        (
            IconTarget     candidate,
            TargetSearch   search,
            float          firstTextX,
            ref IconTarget best,
            ref int        bestScore
        )
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
                best      = candidate;
                bestScore = score;
            }
        }

        private static float FindFirstTextX
        (
            AtkUldManager* uld,
            int            depth
        )
        {
            if (uld == null || depth > MAX_NODE_DEPTH || uld->NodeList == null)
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

        private bool ApplyReplacement
        (
            nint       addonAddress,
            IconTarget target,
            uint       replacementIconID
        )
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

        private void RestoreTarget
        (
            nint       addonAddress,
            IconTarget target
        )
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
            targetCaches.Remove(addonAddress);

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
            targetCaches.Remove(addonAddress);
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
            targetCaches.Clear();
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

        private void ApplyOverlay
        (
            AtkResNode* iconNode,
            bool        unlocked,
            nint        addonAddress
        )
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
                entry         = new(node, addonAddress);
                overlays[key] = entry;
            }

            var overlay = entry.Node;
            overlay.NodeFlags          = NodeFlags.Visible | NodeFlags.Enabled;
            var stage = AtkStage.Instance();
            var textureTheme = stage->AtkUIColorHolder->ActiveColorThemeType;
            var textureScale = stage->AtkTextureResourceManager->DefaultTextureScale;
            if (entry.TextureTheme != textureTheme || entry.TextureScale != textureScale ||
                !overlay.PartsList[0]->IsTextureReady)
            {
                overlay.TexturePath = COLLECTED_CHECK_TEXTURE_PATH;
                overlays[key] = entry with { TextureTheme = textureTheme, TextureScale = textureScale };
            }
            overlay.TextureCoordinates = CollectedCheckTextureCoordinates;
            overlay.TextureSize        = CollectedCheckTextureSize;
            var width = Math.Max(16f, iconNode->GetWidth() * iconNode->GetScaleX());
            var height = Math.Max(16f, iconNode->GetHeight() * iconNode->GetScaleY());
            var overlayWidth = Math.Clamp(Math.Min(width, height) * 0.70f, 20f, 36f);
            var overlayHeight = overlayWidth * CollectedCheckTextureSize.Y / CollectedCheckTextureSize.X;
            overlay.Size      = new(overlayWidth, overlayHeight);
            overlay.Position  = new(
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

        private static bool LoadIcon
        (
            IconTarget target,
            uint       iconID
        )
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

                    var part = &target.Image->PartsList->Parts[target.Image->PartId];
                    if (part->UldAsset == null)
                    {
                        return false;
                    }

                    part->LoadIcon(iconID);
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
            public IconTarget
            (
                TargetKind            kind,
                nint                  address,
                AtkResNode*           node,
                AtkComponentDragDrop* dragDrop,
                AtkComponentIcon*     icon,
                AtkImageNode*         image
            )
            {
                Kind     = kind;
                Address  = address;
                Node     = node;
                DragDrop = dragDrop;
                Icon     = icon;
                Image    = image;
            }

            public TargetKind Kind { get; }

            public nint Address { get; }

            public AtkResNode* Node { get; }

            public AtkComponentDragDrop* DragDrop { get; }

            public AtkComponentIcon* Icon { get; }

            public AtkImageNode* Image { get; }

            public bool IsEmpty => Kind == TargetKind.None || Address == nint.Zero || Node == null;
        }

        private sealed class CachedIconTarget
        {
            public readonly List<IconNodeState> Nodes = [];
            public IconTarget Target;
            public IconNodeState RendererState;
            public bool Parsed;
        }

        private readonly record struct IconNodeState(nint Address, nint Component, nint Icon, nint TargetNode, NodeType Type,
            ComponentType ComponentType, float Width, float Height, float X, bool Visible, bool HasContent);

        private readonly record struct ReplacementEntry
        (
            nint       AddonAddress,
            TargetKind Kind,
            nint       TargetAddress,
            nint       NodeAddress,
            uint       OriginalIconID,
            uint       ReplacementIconID
        );

        private readonly record struct OverlayEntry
        (
            SimpleImageNode Node,
            nint            AddonAddress,
            byte            TextureTheme = 0,
            int             TextureScale = 0
        );

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

        #region 常量

        private const string GLASSES_ADDON_NAME = "TryonGlassesSelect";

        private const string ITEM_DETAIL_ADDON_NAME = "ItemDetail";

        private const int ITEM_DETAIL_ICON_OFFSET = 0x3C8;

        private const int MAX_NODE_DEPTH = 12;

        private const string COLLECTED_CHECK_TEXTURE_PATH = "ui/uld/RecipeNoteBook.tex";

        #endregion
    }

}
