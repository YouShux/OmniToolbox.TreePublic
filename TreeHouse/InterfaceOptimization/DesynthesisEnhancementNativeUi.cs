using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.TreeHouse;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed unsafe class DesynthesisEnhancementNativeUi : IDisposable
{
    private const string AddonName = "SalvageItemSelector";
    private const string TooltipKeyword = "建议分解技能";
    private const uint SkillColumnId = 91_001;
    private const uint LockColumnId = 91_002;
    private const ushort SkillColumnWidth = 120;
    private const ushort LockColumnWidth = 60;
    private const ushort TotalColumnWidth = SkillColumnWidth + LockColumnWidth;
    private static readonly CompSig PopulateItemSignature = new(
        "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 49 8B 38");

    private readonly DesynthesisEnhancementConfig config;
    private readonly HookRegistry hookRegistry;
    private readonly TooltipManager tooltipManager;
    private readonly AddonEventRegistry addonEvents;
    private readonly FeatureLifetime lifetime = new();
    private readonly TooltipManager.ItemTooltipUpdateDelegate tooltipHandler;
    private readonly HashSet<uint> gearsetItemIds = [];
    private readonly HashSet<uint> customLockItemIds = [];
    private readonly Dictionary<nint, RowNodes> rowNodes = [];
    private readonly List<TextNode> injectedNodes = [];
    private readonly List<(nint Address, ushort Width)> widthSnapshots = [];
    private readonly List<(nint Address, short X)> xSnapshots = [];
    private readonly ListItemLockStateCache lockVisuals = new();
    private Hook<PopulateItemDelegate>? populateItemHook;
    private AtkUnitBase* salvageAddon;
    private bool tooltipRegistered;
    private long lastErrorLogTick;

    private delegate void PopulateItemDelegate(
        AddonSalvageItemSelector* addon,
        int index,
        AtkResNode** nodes,
        AtkComponentListItemRenderer* listItemRenderer);

    public DesynthesisEnhancementNativeUi(DesynthesisEnhancementConfig config, HookRegistry hookRegistry)
    {
        this.config = config;
        this.hookRegistry = hookRegistry;
        tooltipManager = TooltipManager.Instance();
        addonEvents = new(DalamudServices.AddonLifecycle);
        tooltipHandler = (_, itemId, ref modifications) => OnItemTooltip(itemId, ref modifications);

        lifetime.Add(RestoreSalvageWindow);
        lifetime.Add(ReleaseHook);
        lifetime.Add(addonEvents.Dispose);
        lifetime.Add(() =>
        {
            UnregisterTooltip();
            tooltipManager.TriggerItemDetailUpdate();
        });

        try
        {
            populateItemHook = hookRegistry.Register(PopulateItemSignature, (PopulateItemDelegate)PopulateItemDetour);
            addonEvents.Register(AddonEvent.PostSetup, AddonName, OnSalvageSetup);
            addonEvents.Register(AddonEvent.PreFinalize, AddonName, OnSalvageFinalize);
            RefreshCaches();
            UpdateTooltipRegistration();

            if (AddonHelper.TryGetByName(AddonName, out AtkUnitBase* addon) && addon->IsAddonAndNodesReady())
            {
                SetupSalvageWindow(addon);
                RefreshSalvageList();
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
        UpdateTooltipRegistration();
        tooltipManager.TriggerItemDetailUpdate();

        if (salvageAddon == null &&
            AddonHelper.TryGetByName(AddonName, out AtkUnitBase* addon) &&
            addon->IsAddonAndNodesReady())
        {
            SetupSalvageWindow(addon);
        }

        RefreshSalvageList();
    }

    public void Dispose() => lifetime.Dispose();

    private void OnItemTooltip(uint itemId, ref List<TooltipItemModification> modifications)
    {
        if (!config.ShowTooltipText ||
            itemId == 0 ||
            !LuminaGetter.TryGetRow<Item>(itemId, out var item) ||
            item.Desynth == 0 ||
            item.ClassJobRepair.RowId == 0)
        {
            return;
        }

        var uiState = UIState.Instance();
        if (uiState == null)
        {
            return;
        }

        var level = DesynthesisEnhancementState.Calculate(
            uiState->PlayerState.GetDesynthesisLevel(item.ClassJobRepair.RowId),
            item.LevelItem.RowId);
        var jobName = LuminaGetter.TryGetRow<ClassJob>(item.ClassJobRepair.RowId, out var job)
            ? job.Name.ExtractText()
            : string.Empty;
        if (!TryBuildTooltipText(
                tooltipManager.GetOriginalItemTooltipText(TooltipItemType.GearAbilityInfo),
                level,
                jobName,
                out var text))
        {
            return;
        }

        modifications.Add(new()
        {
            Target = TooltipItemType.GearAbilityInfo,
            Type = TooltipModificationType.Contribute,
            Text = text
        });
    }

    private static bool TryBuildTooltipText(
        ReadOnlySeString original,
        DesynthesisLevelInfo level,
        string jobName,
        out ReadOnlySeString result)
    {
        result = default;
        if (!original.ExtractText().Contains(TooltipKeyword, StringComparison.Ordinal))
        {
            return false;
        }

        using var rented = new RentedSeStringBuilder();
        var replaced = false;
        foreach (var payload in original)
        {
            if (replaced || payload.Type != ReadOnlySePayloadType.Text)
            {
                rented.Builder.Append(payload);
                continue;
            }

            var text = payload.ToString();
            var keywordIndex = text.IndexOf(TooltipKeyword, StringComparison.Ordinal);
            if (keywordIndex < 0)
            {
                rented.Builder.Append(payload);
                continue;
            }

            var lineStart = text.LastIndexOf('\n', keywordIndex);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            var lineEnd = text.IndexOf('\n', keywordIndex);
            if (lineStart > 0)
            {
                rented.Builder.Append(text.AsSpan(0, lineStart));
            }

            rented.Builder
                .Append(OmniLoc.Get("Feature.DesynthesisEnhancement.TooltipPrefix"))
                .PushColorType(DesynthesisEnhancementState.GetColorKey(level.Status))
                .Append(DesynthesisEnhancementState.FormatTooltipValue(level, jobName))
                .PopColorType();
            if (lineEnd >= 0)
            {
                rented.Builder.Append(text.AsSpan(lineEnd));
            }

            replaced = true;
        }

        if (!replaced)
        {
            return false;
        }

        result = rented.Builder.ToReadOnlySeString();
        return true;
    }

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

    private void UpdateListItem(AtkComponentListItemRenderer* renderer, int index)
    {
        if (renderer == null || index < 0 || !rowNodes.TryGetValue((nint)renderer, out var nodes))
        {
            return;
        }

        var agent = AgentSalvage.Instance();
        if (agent == null || (uint)index >= agent->ItemCount)
        {
            ClearRow(renderer, nodes);
            return;
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
            ClearRow(renderer, nodes);
            return;
        }

        var uiState = UIState.Instance();
        if (uiState == null)
        {
            ClearRow(renderer, nodes);
            return;
        }

        var level = DesynthesisEnhancementState.Calculate(
            uiState->PlayerState.GetDesynthesisLevel(item.ClassJobRepair.RowId),
            item.LevelItem.RowId);
        nodes.Skill.Node->TextColor = GetStatusColor(level.Status);
        nodes.Skill.Node->SetText($"{level.Recommended}/{level.Current}");

        var isLocked = config.LockGearsetItems && gearsetItemIds.Contains(GetGearsetItemId(inventoryItem));
        if (config.LockCustomItems && customLockItemIds.Contains(inventoryItem->ItemId))
        {
            isLocked = true;
        }

        nodes.Lock.Node->SetText(isLocked ? $"{(char)SeIconChar.BoxedStar}" : string.Empty);
        lockVisuals.Apply(renderer, isLocked);
    }

    private void ClearRow(AtkComponentListItemRenderer* renderer, RowNodes nodes)
    {
        nodes.Skill.Node->SetText(string.Empty);
        nodes.Lock.Node->SetText(string.Empty);
        lockVisuals.Apply(renderer, false);
    }

    private void OnSalvageSetup(AddonEvent _, AddonArgs args)
    {
        try
        {
            RefreshCaches();
            SetupSalvageWindow((AtkUnitBase*)args.Addon.Address);
            RefreshSalvageList();
        }
        catch (Exception ex)
        {
            LogFailure(ex, "setup desynthesis window");
            RestoreSalvageWindow();
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
            windowNode->Type != NodeType.Component ||
            listNode == null ||
            listNode->Type != NodeType.Component)
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
            listComponent == null ||
            listComponent->UldManager.NodeList == null)
        {
            return;
        }

        salvageAddon = addon;
        var columnX = (short)(lastHeader->GetXShort() + lastHeader->GetWidth());
        var skillHeader = CreateTextNode(
            SkillColumnId,
            OmniLoc.Get("Feature.DesynthesisEnhancement.Column.Skill"),
            SkillColumnWidth,
            columnX,
            lastHeader->GetYShort(),
            lastHeader->GetHeight(),
            headerTemplate,
            headerTemplate->AlignmentType,
            lastHeader);
        CreateTextNode(
            LockColumnId,
            OmniLoc.Get("Feature.DesynthesisEnhancement.Column.Lock"),
            LockColumnWidth,
            (short)(columnX + SkillColumnWidth),
            lastHeader->GetYShort(),
            lastHeader->GetHeight(),
            headerTemplate,
            headerTemplate->AlignmentType,
            (AtkResNode*)skillHeader.Node);

        CaptureWidth(headerContainer);
        headerContainer->SetWidth((ushort)(columnX + TotalColumnWidth));
        CaptureWidth(separator);
        separator->SetWidth((ushort)(columnX + TotalColumnWidth + 6));
        IncreaseWidth(addon->RootNode, TotalColumnWidth);
        IncreaseWidth(windowNode, TotalColumnWidth);
        IncreaseWidth(listNode, TotalColumnWidth);

        IncreaseWidths(windowComponent, TotalColumnWidth);
        MoveNodes(windowComponent, (short)TotalColumnWidth);

        var rendererNodes = new List<nint>(100);
        var listNodes = listComponent->UldManager.NodeList;
        var listNodeCount = listComponent->UldManager.NodeListCount;
        for (var index = 0; index < listNodeCount; index++)
        {
            var node = listNodes[index];
            if (node == null)
            {
                continue;
            }

            if (node->NodeId == 5)
            {
                MoveX(node, (short)TotalColumnWidth);
                continue;
            }

            IncreaseWidth(node, TotalColumnWidth);
            if (node->Type == NodeType.Component &&
                (node->NodeId == 4 || node->NodeId is > 41_000 and < 41_100))
            {
                rendererNodes.Add((nint)node);
            }
        }

        for (var index = 0; index < rendererNodes.Count; index++)
        {
            var rendererNode = (AtkComponentNode*)rendererNodes[index];
            var renderer = rendererNode->Component;
            var template = renderer == null ? null : renderer->GetTextNodeById(6);
            if (renderer == null || template == null || renderer->UldManager.RootNode == null)
            {
                continue;
            }

            var skill = CreateTextNode(
                SkillColumnId,
                string.Empty,
                SkillColumnWidth,
                columnX,
                template->AtkResNode.GetYShort(),
                template->AtkResNode.GetHeight(),
                template,
                AlignmentType.Center,
                renderer->UldManager.RootNode);
            var locked = CreateTextNode(
                LockColumnId,
                string.Empty,
                LockColumnWidth,
                (short)(columnX + SkillColumnWidth),
                template->AtkResNode.GetYShort(),
                template->AtkResNode.GetHeight(),
                template,
                AlignmentType.Center,
                renderer->UldManager.RootNode);
            rowNodes[(nint)renderer] = new(skill, locked);
        }
    }

    private TextNode CreateTextNode(
        uint nodeId,
        string text,
        ushort width,
        short x,
        short y,
        ushort height,
        AtkTextNode* template,
        AlignmentType alignment,
        AtkResNode* target)
    {
        var node = new TextNode { NodeId = nodeId };
        injectedNodes.Add(node);
        node.Node->SetText(text);
        node.Node->AtkResNode.SetWidth(width);
        node.Node->AtkResNode.SetHeight(height);
        node.Node->AtkResNode.SetXShort(x);
        node.Node->AtkResNode.SetYShort(y);
        node.Node->SetFont(template->FontType);
        node.Node->SetAlignment(alignment);
        node.Node->LineSpacing = template->LineSpacing;
        node.Node->TextColor = template->TextColor;
        node.Node->EdgeColor = template->EdgeColor;
        node.Node->BackgroundColor = template->BackgroundColor;
        node.Node->TextFlags = template->TextFlags;
        node.Node->AtkResNode.DrawFlags = template->AtkResNode.DrawFlags;
        node.Node->AtkResNode.NodeFlags = template->AtkResNode.NodeFlags;
        node.Node->FontSize = template->FontSize;
        node.AttachNode(target, NodePosition.AfterTarget);
        return node;
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
            var itemId = DesynthesisEnhancementState.NormalizeItemId(config.CustomLockItemIds[index]);
            if (itemId != 0)
            {
                customLockItemIds.Add(itemId);
            }
        }

        gearsetItemIds.Clear();
        var gearsetModule = RaptureGearsetModule.Instance();
        if (!config.LockGearsetItems || gearsetModule == null)
        {
            return;
        }

        for (var gearsetIndex = 0; gearsetIndex < 100; gearsetIndex++)
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

    private static uint GetGearsetItemId(InventoryItem* item) =>
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

    private void UpdateTooltipRegistration()
    {
        if (config.ShowTooltipText == tooltipRegistered)
        {
            return;
        }

        if (config.ShowTooltipText)
        {
            tooltipManager.RegItem(tooltipHandler);
            tooltipRegistered = true;
        }
        else
        {
            UnregisterTooltip();
        }
    }

    private void UnregisterTooltip()
    {
        if (!tooltipRegistered)
        {
            return;
        }

        tooltipManager.Unreg(tooltipHandler);
        tooltipRegistered = false;
    }

    private void ReleaseHook()
    {
        hookRegistry.Release(populateItemHook);
        populateItemHook = null;
    }

    private void RefreshSalvageList()
    {
        var agent = AgentSalvage.Instance();
        if (salvageAddon == null || agent == null)
        {
            return;
        }

        agent->ItemListRefresh(agent->IsSalvageResultAddonOpen);
    }

    private void RestoreSalvageWindow()
    {
        if (salvageAddon == null)
        {
            return;
        }

        var addon = salvageAddon;
        lockVisuals.Restore();
        for (var index = injectedNodes.Count - 1; index >= 0; index--)
        {
            injectedNodes[index].Dispose();
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
        rowNodes.Clear();
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

    private readonly record struct RowNodes(TextNode Skill, TextNode Lock);

}
