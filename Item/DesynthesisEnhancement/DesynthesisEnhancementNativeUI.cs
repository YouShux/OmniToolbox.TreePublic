using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Tooltips;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class DesynthesisEnhancementNativeUI : IDisposable
{
    private const string AddonName = "SalvageItemSelector";
    private const uint SkillColumnID = 91_001;
    private const ushort SkillColumnWidth = 120;
    private static readonly CompSig PopulateItemSignature = new(
        "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 49 8B 38");

    private readonly DesynthesisEnhancementConfig config;
    private readonly HookRegistry hookRegistry;
    private readonly AddonEventRegistry addonEvents;
    private readonly FeatureLifetime lifetime = new();
    private readonly HashSet<uint> gearsetItemIds = [];
    private readonly HashSet<uint> customLockItemIds = [];
    private readonly List<InjectedNode> injectedNodes = [];
    private readonly List<(nint Address, ushort Width)> widthSnapshots = [];
    private readonly List<(nint Address, short X)> xSnapshots = [];
    private readonly ListItemLockStateCache lockVisuals = new();
    private Hook<PopulateItemDelegate>? populateItemHook;
    private AtkUnitBase* salvageAddon;
    private AtkComponentList* salvageList;
    private short rowColumnX;
    private bool initialRowsReady;
    private long lastErrorLogTick;

    private delegate void PopulateItemDelegate(
        AddonSalvageItemSelector* addon,
        int index,
        AtkResNode** nodes,
        AtkComponentListItemRenderer* listItemRenderer);

    public DesynthesisEnhancementNativeUI(DesynthesisEnhancementConfig config, HookRegistry hookRegistry)
    {
        this.config = config;
        this.hookRegistry = hookRegistry;
        addonEvents = new(DalamudServices.AddonLifecycle);

        lifetime.Add(RestoreSalvageWindow);
        lifetime.Add(ReleaseHook);
        lifetime.Add(addonEvents.Dispose);

        try
        {
            populateItemHook = hookRegistry.Register(PopulateItemSignature, (PopulateItemDelegate)PopulateItemDetour);
            addonEvents.Register(AddonEvent.PostSetup, AddonName, OnSalvageSetup);
            addonEvents.Register(AddonEvent.PostDraw, AddonName, OnSalvageDraw);
            addonEvents.Register(AddonEvent.PreFinalize, AddonName, OnSalvageFinalize);
            RefreshCaches();

            if (AddonHelper.TryGetByName(AddonName, out AtkUnitBase* addon) && addon->IsAddonAndNodesReady())
            {
                SetupSalvageWindow(addon);
                initialRowsReady = RefreshVisibleRows();
            }
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    public void RefreshSettings()
    {
        RefreshCaches();

        if (salvageAddon == null &&
            AddonHelper.TryGetByName(AddonName, out AtkUnitBase* addon) &&
            addon->IsAddonAndNodesReady())
        {
            SetupSalvageWindow(addon);
        }

        initialRowsReady = RefreshVisibleRows();
    }

    public void Dispose() => lifetime.Dispose();

    private void PopulateItemDetour(
        AddonSalvageItemSelector* addon,
        int index,
        AtkResNode** nodes,
        AtkComponentListItemRenderer* listItemRenderer)
    {
        try
        {
            UpdateListItem(listItemRenderer, index);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "update desynthesis list item");
        }

        populateItemHook!.Original(addon, index, nodes, listItemRenderer);
    }

    private bool UpdateListItem(AtkComponentListItemRenderer* renderer, int index)
    {
        if (renderer == null || index < 0)
        {
            return false;
        }

        var skillNode = EnsureSkillNode(renderer);
        var agent = AgentSalvage.Instance();
        if (agent == null)
        {
            ClearRow(renderer, skillNode, index);
            return false;
        }

        if ((uint)index >= agent->ItemCount)
        {
            ClearRow(renderer, skillNode, index);
            return false;
        }

        var entry = agent->ItemList + index;
        var container = InventoryManager.Instance()->GetInventoryContainer(entry->InventoryType);
        var inventoryItem = container == null ? null : container->GetInventorySlot((int)entry->InventorySlot);
        if (inventoryItem == null ||
            inventoryItem->ItemId == 0 ||
            !LuminaGetter.TryGetRow<Item>(inventoryItem->ItemId, out var item) ||
            item.Desynth == 0 ||
            item.ClassJobRepair.RowId == 0)
        {
            ClearRow(renderer, skillNode, index);
            return false;
        }

        var uiState = UIState.Instance();
        if (uiState == null)
        {
            ClearRow(renderer, skillNode, index);
            return false;
        }

        var level = DesynthesisEnhancementState.Calculate(
            uiState->PlayerState.GetDesynthesisLevel(item.ClassJobRepair.RowId),
            item.LevelItem.RowId);
        if (skillNode != null)
        {
            skillNode->TextColor = GetStatusColor(level.Status);
            skillNode->SetText($"{level.Recommended}/{level.Current}");
        }

        var isLocked = config.LockGearsetItems && gearsetItemIds.Contains(GetGearsetItemID(inventoryItem));
        if (config.LockCustomItems &&
            customLockItemIds.Contains(ItemUtil.GetBaseId(inventoryItem->ItemId).ItemId))
        {
            isLocked = true;
        }

        if (salvageList != null && index < salvageList->ListLength)
        {
            if (isLocked && salvageList->SelectedItemIndex == index)
            {
                salvageList->DeselectItem();
            }

            salvageList->SetItemDisabledState(index, isLocked);
        }

        lockVisuals.Apply(renderer, isLocked);
        return skillNode != null;
    }

    private void ClearRow(AtkComponentListItemRenderer* renderer, AtkTextNode* skillNode, int index)
    {
        if (skillNode != null)
        {
            skillNode->SetText(string.Empty);
        }

        if (salvageList != null && index >= 0 && index < salvageList->ListLength)
        {
            salvageList->SetItemDisabledState(index, false);
        }

        lockVisuals.Apply(renderer, false);
    }

    private void OnSalvageSetup(AddonEvent _, AddonArgs args)
    {
        try
        {
            RefreshCaches();
            SetupSalvageWindow((AtkUnitBase*)args.Addon.Address);
            initialRowsReady = RefreshVisibleRows();
        }
        catch (Exception ex)
        {
            LogFailure(ex, "setup desynthesis window");
            RestoreSalvageWindow();
        }
    }

    private void OnSalvageDraw(AddonEvent _, AddonArgs args)
    {
        if (initialRowsReady)
        {
            return;
        }

        try
        {
            var addon = (AtkUnitBase*)args.Addon.Address;
            if (salvageAddon != addon)
            {
                RefreshCaches();
                SetupSalvageWindow(addon);
            }

            initialRowsReady = RefreshVisibleRows();
        }
        catch (Exception ex)
        {
            LogFailure(ex, "initialize desynthesis rows");
        }
    }

    private void OnSalvageFinalize(AddonEvent _, AddonArgs args)
    {
        if (salvageAddon == (AtkUnitBase*)args.Addon.Address)
        {
            ForgetSalvageWindow();
        }
    }

    private void SetupSalvageWindow(AtkUnitBase* addon)
    {
        if (addon == null || salvageAddon == addon)
        {
            return;
        }

        if (salvageAddon != null)
        {
            RestoreSalvageWindow();
        }

        var headerContainer = addon->GetNodeById(7);
        var separator = addon->GetNodeById(11);
        var windowNode = addon->GetNodeById(14);
        var listNode = addon->GetNodeById(12);
        if (headerContainer == null ||
            separator == null ||
            windowNode == null ||
            listNode == null)
        {
            return;
        }

        var lastHeader = headerContainer->ChildNode;
        if (lastHeader == null)
        {
            return;
        }

        for (var node = lastHeader; node != null; node = node->PrevSiblingNode)
        {
            if (node->Type == NodeType.Text && node->GetXShort() > lastHeader->GetXShort())
            {
                lastHeader = node;
            }
        }

        var headerTemplate = lastHeader->GetAsAtkTextNode();
        var windowComponent = ((AtkComponentNode*)windowNode)->Component;
        var listComponent = ((AtkComponentNode*)listNode)->Component;
        if (headerTemplate == null ||
            windowComponent == null ||
            listComponent == null)
        {
            return;
        }

        var emptyStateText = FindEmptyStateText(listComponent);
        salvageAddon = addon;
        salvageList = (AtkComponentList*)listComponent;
        var columnX = (short)(lastHeader->GetXShort() + lastHeader->GetWidth());
        rowColumnX = columnX;
        CreateTextNode(
            SkillColumnID,
            OmniLoc.Get("Feature.DesynthesisEnhancement.Column.Skill"),
            SkillColumnWidth,
            columnX,
            lastHeader->GetYShort(),
            lastHeader->GetHeight(),
            headerTemplate,
            headerTemplate->AlignmentType,
            lastHeader,
            &addon->UldManager);

        CaptureWidth(headerContainer);
        headerContainer->SetWidth((ushort)(columnX + SkillColumnWidth));
        CaptureWidth(separator);
        separator->SetWidth((ushort)(columnX + SkillColumnWidth + 6));
        IncreaseWidth(addon->RootNode, SkillColumnWidth);
        IncreaseWidth(windowNode, SkillColumnWidth);
        IncreaseWidth(listNode, SkillColumnWidth);

        IncreaseWidths(windowComponent, SkillColumnWidth);
        MoveNodes(windowComponent, (short)SkillColumnWidth);

        var listNodes = listComponent->UldManager.NodeList;
        var listNodeCount = listComponent->UldManager.NodeListCount;
        if (listNodes == null)
        {
            return;
        }

        for (var index = 0; index < listNodeCount; index++)
        {
            var node = listNodes[index];
            if (node == null)
            {
                continue;
            }

            if (node->NodeId == 5)
            {
                MoveX(node, (short)SkillColumnWidth);
                continue;
            }

            if (node != (AtkResNode*)emptyStateText)
            {
                IncreaseWidth(node, SkillColumnWidth);
            }

            if (node->NodeId == 4 || node->NodeId is > 41_000 and < 41_100)
            {
                var renderer = (AtkComponentListItemRenderer*)((AtkComponentNode*)node)->Component;
                if (renderer != null)
                {
                    EnsureSkillNode(renderer);
                }
            }
        }
    }

    private AtkTextNode* EnsureSkillNode(AtkComponentListItemRenderer* renderer)
    {
        var skill = FindTextNodeByID(renderer, SkillColumnID);
        var template = renderer->GetTextNodeById(6);
        var root = renderer->UldManager.RootNode;
        if (skill == null && template != null && root != null && rowColumnX != 0)
        {
            skill = CreateTextNode(
                SkillColumnID,
                string.Empty,
                SkillColumnWidth,
                rowColumnX,
                template->AtkResNode.GetYShort(),
                template->AtkResNode.GetHeight(),
                template,
                AlignmentType.Center,
                root,
                &renderer->UldManager);
        }

        return skill;
    }

    private static AtkTextNode* FindTextNodeByID(AtkComponentListItemRenderer* renderer, uint nodeID)
    {
        var root = renderer->UldManager.RootNode;
        if (root == null)
        {
            return null;
        }

        while (root->PrevSiblingNode != null)
        {
            root = root->PrevSiblingNode;
        }

        for (var node = root; node != null; node = node->NextSiblingNode)
        {
            var found = FindTextNodeByID(node, nodeID, 0);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static AtkTextNode* FindTextNodeByID(AtkResNode* node, uint nodeID, int depth)
    {
        if (node == null || depth > ListItemLockStateCache.MaxDepth)
        {
            return null;
        }

        if (node->NodeId == nodeID && node->Type == NodeType.Text)
        {
            return (AtkTextNode*)node;
        }

        for (var child = node->ChildNode; child != null; child = child->PrevSiblingNode)
        {
            var found = FindTextNodeByID(child, nodeID, depth + 1);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private AtkTextNode* CreateTextNode(
        uint nodeID,
        string text,
        ushort width,
        short x,
        short y,
        ushort height,
        AtkTextNode* template,
        AlignmentType alignment,
        AtkResNode* target,
        AtkUldManager* uldManager)
    {
        var node = IMemorySpace.GetUISpace()->Create<AtkTextNode>();
        node->AtkResNode.Type = NodeType.Text;
        node->AtkResNode.NodeId = nodeID;
        node->SetText(text);
        node->AtkResNode.SetWidth(width);
        node->AtkResNode.SetHeight(height);
        node->AtkResNode.SetXShort(x);
        node->AtkResNode.SetYShort(y);
        node->SetFont(template->FontType);
        node->SetAlignment(alignment);
        node->LineSpacing = template->LineSpacing;
        node->TextColor = template->TextColor;
        node->EdgeColor = template->EdgeColor;
        node->BackgroundColor = template->BackgroundColor;
        node->TextFlags = template->TextFlags;
        node->AtkResNode.DrawFlags = template->AtkResNode.DrawFlags;
        node->AtkResNode.NodeFlags = template->AtkResNode.NodeFlags;
        node->FontSize = template->FontSize;
        node->AtkResNode.ToggleVisibility(true);

        var previousNode = target->PrevSiblingNode;
        node->AtkResNode.ParentNode = target->ParentNode;
        node->AtkResNode.PrevSiblingNode = previousNode;
        node->AtkResNode.NextSiblingNode = target;
        target->PrevSiblingNode = (AtkResNode*)node;
        if (previousNode != null)
        {
            previousNode->NextSiblingNode = (AtkResNode*)node;
        }

        injectedNodes.Add(new((nint)node, (nint)uldManager));
        uldManager->UpdateDrawNodeList();
        return node;
    }

    private static void RemoveInjectedNode(InjectedNode injectedNode)
    {
        var node = (AtkResNode*)injectedNode.Address;
        if (node->ParentNode != null && node->ParentNode->ChildNode == node)
        {
            node->ParentNode->ChildNode = node->PrevSiblingNode;
        }

        if (node->PrevSiblingNode != null)
        {
            node->PrevSiblingNode->NextSiblingNode = node->NextSiblingNode;
        }

        if (node->NextSiblingNode != null)
        {
            node->NextSiblingNode->PrevSiblingNode = node->PrevSiblingNode;
        }

        ((AtkUldManager*)injectedNode.UldManager)->UpdateDrawNodeList();
        node->Destroy(true);
    }

    private static AtkTextNode* FindEmptyStateText(AtkComponentBase* component)
    {
        AtkTextNode* result = null;
        for (var index = 0; index < component->UldManager.NodeListCount; index++)
        {
            var node = component->UldManager.NodeList[index];
            if (node == null || node->Type != NodeType.Text)
            {
                continue;
            }

            if (result != null && node->GetWidth() <= result->AtkResNode.GetWidth())
            {
                continue;
            }

            result = (AtkTextNode*)node;
        }

        return result;
    }

    private void IncreaseWidths(AtkComponentBase* component, ushort amount)
    {
        ReadOnlySpan<uint> nodeIds = [2, 8, 9, 10, 11, 12, 13];
        for (var index = 0; index < nodeIds.Length; index++)
        {
            var node = component->UldManager.SearchNodeById(nodeIds[index]);
            if (node != null)
            {
                IncreaseWidth(node, amount);
            }
        }
    }

    private void MoveNodes(AtkComponentBase* component, short amount)
    {
        ReadOnlySpan<uint> nodeIds = [5, 6, 7];
        for (var index = 0; index < nodeIds.Length; index++)
        {
            var node = component->UldManager.SearchNodeById(nodeIds[index]);
            if (node != null)
            {
                MoveX(node, amount);
            }
        }
    }

    private void IncreaseWidth(AtkResNode* node, ushort amount)
    {
        CaptureWidth(node);
        node->SetWidth((ushort)(node->GetWidth() + amount));
    }

    private void CaptureWidth(AtkResNode* node) => widthSnapshots.Add(((nint)node, node->GetWidth()));

    private void MoveX(AtkResNode* node, short amount)
    {
        xSnapshots.Add(((nint)node, node->GetXShort()));
        node->SetXShort((short)(node->GetXShort() + amount));
    }

    private void RefreshCaches()
    {
        customLockItemIds.Clear();
        for (var index = 0; index < config.CustomLockItemIds.Count; index++)
        {
            var itemID = ItemUtil.GetBaseId(config.CustomLockItemIds[index]).ItemId;
            if (itemID != 0)
            {
                customLockItemIds.Add(itemID);
            }
        }

        gearsetItemIds.Clear();
        var gearsetModule = RaptureGearsetModule.Instance();
        if (!config.LockGearsetItems || gearsetModule == null)
        {
            return;
        }

        for (var gearsetIndex = 0; gearsetIndex < 101; gearsetIndex++)
        {
            var gearset = gearsetModule->GetGearset(gearsetIndex);
            if (gearset == null || gearset->Id != gearsetIndex ||
                (gearset->Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0)
            {
                continue;
            }

            foreach (var item in gearset->Items)
            {
                if (item.ItemId != 0)
                {
                    gearsetItemIds.Add(item.ItemId);
                }
            }
        }
    }

    private static uint GetGearsetItemID(InventoryItem* item) =>
        (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0
            ? item->ItemId + 1_000_000
            : item->ItemId;

    private static ByteColor GetStatusColor(DesynthesisStatus status)
    {
        var stage = AtkStage.Instance();
        if (stage == null || stage->AtkUIColorHolder == null)
        {
            return new() { A = 0xFF, R = 0xFF, G = 0xFF, B = 0xFF };
        }

        var color = stage->AtkUIColorHolder->GetColor(true, DesynthesisEnhancementState.GetColorKey(status));
        return new()
        {
            R = (byte)(color & 0xFF),
            G = (byte)((color >> 8) & 0xFF),
            B = (byte)((color >> 16) & 0xFF),
            A = (byte)((color >> 24) & 0xFF)
        };
    }

    private void ReleaseHook()
    {
        hookRegistry.Release(populateItemHook);
        populateItemHook = null;
    }

    private bool RefreshVisibleRows()
    {
        var agent = AgentSalvage.Instance();
        if (salvageList == null || agent == null || agent->ItemCount == 0)
        {
            return false;
        }

        var firstIndex = Math.Max(salvageList->FirstVisibleItemIndex, 0);
        var endIndex = Math.Min(
            firstIndex + salvageList->NumVisibleItems,
            Math.Min(salvageList->ListLength, (int)agent->ItemCount));
        if (firstIndex >= endIndex)
        {
            return false;
        }

        for (var index = firstIndex; index < endIndex; index++)
        {
            var renderer = salvageList->GetItemRenderer(index);
            if (renderer == null)
            {
                return false;
            }

            if (!UpdateListItem(renderer, index))
            {
                return false;
            }
        }

        return true;
    }

    private void RestoreSalvageWindow()
    {
        if (salvageAddon == null)
        {
            return;
        }

        var addon = salvageAddon;
        if (salvageList != null)
        {
            for (var index = 0; index < salvageList->ListLength; index++)
            {
                salvageList->SetItemDisabledState(index, false);
            }
        }

        lockVisuals.Restore();
        for (var index = injectedNodes.Count - 1; index >= 0; index--)
        {
            RemoveInjectedNode(injectedNodes[index]);
        }

        for (var index = widthSnapshots.Count - 1; index >= 0; index--)
        {
            ((AtkResNode*)widthSnapshots[index].Address)->SetWidth(widthSnapshots[index].Width);
        }

        for (var index = xSnapshots.Count - 1; index >= 0; index--)
        {
            ((AtkResNode*)xSnapshots[index].Address)->SetXShort(xSnapshots[index].X);
        }

        ForgetSalvageWindow();
        addon->UldManager.UpdateDrawNodeList();
        var agent = AgentSalvage.Instance();
        if (agent != null)
        {
            agent->ItemListRefresh(agent->IsSalvageResultAddonOpen);
        }
    }

    private void ForgetSalvageWindow()
    {
        salvageAddon = null;
        salvageList = null;
        rowColumnX = 0;
        initialRowsReady = false;
        injectedNodes.Clear();
        widthSnapshots.Clear();
        xSnapshots.Clear();
        lockVisuals.Forget();
    }

    private void LogFailure(Exception exception, string operation)
    {
        var now = Environment.TickCount64;
        if (lastErrorLogTick != 0 && now - lastErrorLogTick < 60_000)
        {
            return;
        }

        lastErrorLogTick = now;
        DalamudServices.PluginLog.Error(exception, "Failed to {Operation}.", operation);
    }

    private readonly record struct InjectedNode(nint Address, nint UldManager);
}
