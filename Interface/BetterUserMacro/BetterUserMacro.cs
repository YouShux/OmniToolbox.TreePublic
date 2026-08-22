using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace OmniToolbox.TreePublic;

public sealed unsafe class BetterUserMacro(BetterUserMacroConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BetterUserMacroTitle"),
        Description = OmniLoc.Get("BetterUserMacroDescription"),
        Category = ModuleCategory.Interface
    };

    private const uint MacroPageCount = 2;
    private const uint MacroSlotsPerPage = 100;
    private const int MacroLineCount = 15;
    private static readonly Vector2 MacroInputSizeOffset = new(20f, 150f);

    private readonly List<TextNode> lineNumberNodes = new(MacroLineCount);
    private readonly MacroDropTarget?[] macroDropTargets = new MacroDropTarget?[(int)MacroSlotsPerPage];
    private readonly Dictionary<string, uint> actionCache = new(StringComparer.OrdinalIgnoreCase);
    private FeatureLifetime? runtimeLifetime;
    private Hook<AddonActionBarBase.Delegates.ShowTooltip>? tooltipHook;
    private Hook<AtkDragDropManager.Delegates.Drop>? macroDropHook;
    private AddonMacro* lineNumberAddon;
    private bool macroInputRepositioned;
    private nint macroDropAddonAddress;
    private MacroSlot pendingSource;
    private MacroSlot pendingTarget;
    private uint pendingSourceIcon;
    private uint pendingTargetIcon;
    private int pendingRefreshFrames;

    public override bool HasSettings => true;

    public override bool DrawSettings() => BetterUserMacroPanel.Draw(config);

    public bool TrySetSelectedMacroIcon(uint iconID)
    {
        if (!IsEnabled || !config.CustomIcons || iconID == 0)
        {
            return false;
        }

        var shellModule = RaptureShellModule.Instance();
        if (shellModule is not null && shellModule->MacroLocked)
        {
            return false;
        }

        if (!TryGetSelectedMacroContext(out var agent, out var addon, out var set, out var index))
        {
            return false;
        }

        var macroModule = RaptureMacroModule.Instance();
        var macro = macroModule is null ? null : macroModule->GetMacro(set, index);
        if (macroModule is null || macro is null)
        {
            return false;
        }

        ApplyMacroIcon(agent, addon, macroModule, macro, set, index, iconID);
        return true;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            tooltipHook = DService.Instance().Hook.HookFromAddress<AddonActionBarBase.Delegates.ShowTooltip>(
                AddonActionBarBase.MemberFunctionPointers.ShowTooltip,
                OnShowTooltip);
            lifetime.Add(tooltipHook.Dispose);
            tooltipHook.Enable();

            macroDropHook = DService.Instance().Hook.HookFromAddress<AtkDragDropManager.Delegates.Drop>(
                AtkDragDropManager.MemberFunctionPointers.Drop,
                OnMacroDrop);
            lifetime.Add(macroDropHook.Dispose);
            macroDropHook.Enable();

            lifetime.Add(ClearMacroUI);
            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PostSetup, "Macro", OnMacroSetup);
            addonEvents.Register(AddonEvent.PostShow, "Macro", OnMacroRefresh);
            addonEvents.Register(AddonEvent.PreHide, "Macro", OnMacroFinalize);
            addonEvents.Register(AddonEvent.PreFinalize, "Macro", OnMacroFinalize);
            addonEvents.Register(AddonEvent.PreRefresh, "Macro", OnMacroPreRefresh);
            addonEvents.Register(AddonEvent.PostRefresh, "Macro", OnMacroRefresh);
            addonEvents.Register(AddonEvent.PostRequestedUpdate, "Macro", OnMacroRefresh);

            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 250))
            {
                throw new InvalidOperationException("Better user macro update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            runtimeLifetime = lifetime;
            OnFrameworkUpdate(DService.Instance().Framework);
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            tooltipHook = null;
            macroDropHook = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            tooltipHook = null;
            macroDropHook = null;
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (config.CustomIcons)
        {
            ScanSelectedMacroEditorIcon();
            ScanAllMacroCustomIcons();
        }

        if (!AddonHelper.TryGetByName("Macro", out AddonMacro* addon) || !IsMacroAddonVisible(addon))
        {
            return;
        }

        EnsureMacroLineNumbers(addon);
        EnsureMacroDropTargets(addon);
        RefreshPendingMacroSwap(addon);
    }

    private void OnShowTooltip(
        AddonActionBarBase* actionBar,
        AtkResNode* macroResNode,
        NumberArrayData* numberArray,
        StringArrayData* stringArray,
        int numberArrayIndex,
        int stringArrayIndex)
    {
        if (config.Tooltips &&
            TryShowMacroActionTooltip(macroResNode, stringArray, stringArrayIndex, numberArrayIndex))
        {
            return;
        }

        tooltipHook!.Original(
            actionBar,
            macroResNode,
            numberArray,
            stringArray,
            numberArrayIndex,
            stringArrayIndex);
    }

    private bool TryShowMacroActionTooltip(
        AtkResNode* macroResNode,
        StringArrayData* stringArray,
        int stringArrayIndex,
        int numberArrayIndex)
    {
        if (macroResNode is null || stringArray is null || stringArrayIndex < 0 || stringArrayIndex >= stringArray->Size)
        {
            return false;
        }

        var realSlotID = (numberArrayIndex - 15) % 16;
        var realHotbarID = (numberArrayIndex - 15) / 272;
        if (realSlotID < 0 || realSlotID >= 16 || realHotbarID < 0)
        {
            return false;
        }

        var hotbarModule = RaptureHotbarModule.Instance();
        if (hotbarModule is null || realHotbarID >= hotbarModule->Hotbars.Length)
        {
            return false;
        }

        var hotbarSlot = hotbarModule->Hotbars[realHotbarID].Slots[realSlotID];
        if (hotbarSlot.CommandType != RaptureHotbarModule.HotbarSlotType.Macro)
        {
            return false;
        }

        var actionID = hotbarSlot.ApparentSlotType == RaptureHotbarModule.HotbarSlotType.Action
            ? hotbarSlot.ApparentActionId
            : ResolveMacroTooltipActionID(hotbarSlot.CommandId);
        if (actionID == 0)
        {
            return false;
        }

        AtkStage.Instance()->ShowActionTooltip(
            macroResNode,
            actionID,
            stringArray->StringArray[stringArrayIndex].ToString());
        return true;
    }

    private uint ResolveMacroTooltipActionID(uint commandID)
    {
        var set = commandID / 256u;
        var index = commandID % 256u;
        if (set >= MacroPageCount || index >= MacroSlotsPerPage)
        {
            return 0;
        }

        var macroModule = RaptureMacroModule.Instance();
        var macro = macroModule is null ? null : macroModule->GetMacro(set, index);
        if (macro is null || !macro->IsNotEmpty())
        {
            return 0;
        }

        for (var lineIndex = 0; lineIndex < MacroLineCount; lineIndex++)
        {
            var line = BetterUserMacroParser.Decode(macro->Lines[lineIndex].AsSpan());
            if (!BetterUserMacroParser.TryParseActionName(line, out var actionName) ||
            !TryResolveAction(actionName, out var actionID))
            {
                continue;
            }

            return actionID;
        }

        return 0;
    }

    private bool TryResolveAction(string actionName, out uint actionID)
    {
        if (actionCache.TryGetValue(actionName, out var cached))
        {
            actionID = cached;
            return actionID > 0;
        }

        foreach (var action in LuminaGetter.Get<LuminaAction>())
        {
            if (!string.Equals(action.Name.ToString(), actionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            actionCache[actionName] = action.RowId;
            actionID = action.RowId;
            return actionID > 0;
        }

        actionCache[actionName] = default;
        actionID = 0;
        return false;
    }

    private void OnMacroSetup(AddonEvent _, AddonArgs args)
    {
        var addon = (AddonMacro*)args.Addon.Address;
        if (addon is null)
        {
            return;
        }

        EnsureMacroLineNumbers(addon);
        EnsureMacroDropTargets(addon);
    }

    private void OnMacroRefresh(AddonEvent _, AddonArgs args)
    {
        var addon = (AddonMacro*)args.Addon.Address;
        if (addon is null)
        {
            return;
        }

        EnsureMacroLineNumbers(addon);
        EnsureMacroDropTargets(addon);
    }

    private void OnMacroPreRefresh(AddonEvent _, AddonArgs unusedArgs) => ClearMacroDropTargets();

    private void OnMacroFinalize(AddonEvent _, AddonArgs unusedArgs) => ClearMacroUI();

    private static bool IsMacroAddonVisible(AddonMacro* addon) =>
        addon is not null &&
        addon->AtkUnitBase.IsVisible;

    private void EnsureMacroLineNumbers(AddonMacro* addon)
    {
        if (!config.LineNumbers || addon is null)
        {
            ClearMacroLineNumbers();
            return;
        }

        if (lineNumberAddon != addon)
        {
            ClearMacroLineNumbers();
        }

        if (lineNumberNodes.Count == MacroLineCount)
        {
            return;
        }

        var textInputNode = (AtkComponentNode*)addon->GetNodeById(119);
        if (textInputNode is null)
        {
            return;
        }

        RepositionMacroInputNode(textInputNode, MacroInputSizeOffset);
        lineNumberAddon = addon;
        macroInputRepositioned = true;
        try
        {
            for (var index = 0; index < MacroLineCount; index++)
            {
                var lineNumberNode = new TextNode
                {
                    Position = new Vector2(460f, 119f + index * 14f),
                    Size = new Vector2(MacroInputSizeOffset.X - 5f, 14f),
                    FontType = FontType.Axis,
                    FontSize = 12,
                    AlignmentType = AlignmentType.TopRight,
                };
                using var builder = new RentedSeStringBuilder();
                lineNumberNode.Node->SetText(builder.Builder.Append(index + 1).GetViewAsSpan());
                lineNumberNode.AttachNode((AtkUnitBase*)addon);
                lineNumberNodes.Add(lineNumberNode);
            }
        }
        catch
        {
            ClearMacroLineNumbers();
            throw;
        }
    }

    private void ClearMacroLineNumbers()
    {
        if (lineNumberAddon is not null && macroInputRepositioned)
        {
            var textInputNode = (AtkComponentNode*)lineNumberAddon->GetNodeById(119);
            if (textInputNode is not null)
            {
                RepositionMacroInputNode(textInputNode, -MacroInputSizeOffset);
            }
        }

        for (var index = lineNumberNodes.Count - 1; index >= 0; index--)
        {
            lineNumberNodes[index].Dispose();
        }

        lineNumberNodes.Clear();
        lineNumberAddon = null;
        macroInputRepositioned = false;
    }

    private void EnsureMacroDropTargets(AddonMacro* addon)
    {
        if (!config.DragSwap || addon is null)
        {
            ClearMacroDropTargets();
            return;
        }

        var addonAddress = (nint)addon;
        if (macroDropAddonAddress != addonAddress)
        {
            ClearMacroDropTargets();
            macroDropAddonAddress = addonAddress;
        }

        for (var index = 0u; index < MacroSlotsPerPage; index++)
        {
            var dragDrop = addon->DragDropComponent[(int)index].Value;
            if (dragDrop is null || dragDrop->OwnerNode is null)
            {
                continue;
            }

            if (macroDropTargets[(int)index] is { } target)
            {
                if (target.Component == dragDrop)
                {
                    dragDrop->AcceptedType = DragDropType.Macro;
                    continue;
                }

                RestoreMacroDropTarget(target);
            }

            macroDropTargets[(int)index] = new(dragDrop, dragDrop->AcceptedType);
            dragDrop->AcceptedType = DragDropType.Macro;
        }
    }

    private void ClearMacroDropTargets()
    {
        for (var index = 0; index < macroDropTargets.Length; index++)
        {
            if (macroDropTargets[index] is not { } target)
            {
                continue;
            }

            RestoreMacroDropTarget(target);
            macroDropTargets[index] = null;
        }

        macroDropAddonAddress = 0;
    }

    private static void RestoreMacroDropTarget(MacroDropTarget target)
    {
        target.Component->AcceptedType = target.OriginalAcceptedType;
    }

    private void OnMacroDrop(
        AtkDragDropManager* manager,
        AtkEventData.AtkDragDropData* data,
        AtkComponentNode* targetNode,
        DragDropType acceptedType,
        bool canNotAccept)
    {
        var addon = AddonHelper.GetByName<AddonMacro>("Macro");
        var source = default(MacroSlot);
        var target = default(MacroSlot);
        var targetResolved = addon is not null &&
                             data is not null &&
                             TryGetMacroSlot(addon, data->DragDropInterface, out source) &&
                             TryGetMacroDropTarget(addon, data, targetNode, source.Set, out target);

        if (!config.DragSwap || !targetResolved || !TrySwapMacro(manager, addon, source, target))
        {
            macroDropHook!.Original(manager, data, targetNode, acceptedType, canNotAccept);
            return;
        }

        ClearMacroDragDropTransientState(addon, source);
        ClearMacroDragDropTransientState(addon, target);
    }

    private static bool TryGetMacroSlot(AddonMacro* addon, AtkDragDropInterface* dragDropInterface, out MacroSlot slot)
    {
        slot = default;
        if (addon is null || dragDropInterface is null)
        {
            return false;
        }

        var payload = dragDropInterface->GetPayloadContainer();
        if (dragDropInterface->DragDropType == DragDropType.Macro &&
            payload is not null &&
            payload->Int1 is >= 0 and < (int)MacroPageCount &&
            payload->Int2 is >= 0 and < (int)MacroSlotsPerPage)
        {
            slot = new((uint)payload->Int1, (uint)payload->Int2);
            return true;
        }

        var set = GetCurrentMacroSet();
        if (set >= MacroPageCount)
        {
            return false;
        }

        for (var index = 0u; index < MacroSlotsPerPage; index++)
        {
            var dragDrop = addon->DragDropComponent[(int)index].Value;
            if (dragDrop is null || &dragDrop->AtkDragDropInterface != dragDropInterface)
            {
                continue;
            }

            slot = new(set, index);
            return true;
        }

        if (dragDropInterface->DragDropType != DragDropType.Macro)
        {
            return false;
        }

        if (payload is null)
        {
            return false;
        }

        if (payload->Int2 is >= 0 and < (int)MacroSlotsPerPage)
        {
            slot = new(set, (uint)payload->Int2);
            return true;
        }

        if (dragDropInterface->DragDropReferenceIndex is >= 0 and < (short)MacroSlotsPerPage)
        {
            slot = new(set, (uint)dragDropInterface->DragDropReferenceIndex);
            return true;
        }

        return false;
    }

    private static bool TryGetMacroDropTarget(
        AddonMacro* addon,
        AtkEventData.AtkDragDropData* data,
        AtkComponentNode* targetNode,
        uint set,
        out MacroSlot slot)
    {
        if (set >= MacroPageCount)
        {
            slot = default;
            return false;
        }

        if (TryGetMacroSlot(addon, targetNode, set, out slot))
        {
            return true;
        }

        var mouse = ImGui.GetIO().MousePos;
        var mouseX = (short)Math.Clamp(mouse.X, short.MinValue, short.MaxValue);
        var mouseY = (short)Math.Clamp(mouse.Y, short.MinValue, short.MaxValue);
        for (var index = 0u; index < MacroSlotsPerPage; index++)
        {
            var dragDrop = addon->DragDropComponent[(int)index].Value;
            if (dragDrop is null || dragDrop->OwnerNode is null)
            {
                continue;
            }

            var node = &dragDrop->OwnerNode->AtkResNode;
            if (!node->CheckCollisionAtCoords(mouseX, mouseY, true))
            {
                continue;
            }

            slot = new(set, index);
            return true;
        }

        return TryGetMacroSlot(addon, data->ComponentNode, set, out slot);
    }

    private static bool TryGetMacroSlot(
        AddonMacro* addon,
        AtkComponentNode* componentNode,
        uint set,
        out MacroSlot slot)
    {
        slot = default;
        if (addon is null || componentNode is null)
        {
            return false;
        }

        for (var index = 0u; index < MacroSlotsPerPage; index++)
        {
            var dragDrop = addon->DragDropComponent[(int)index].Value;
            if (dragDrop is null || dragDrop->OwnerNode != componentNode)
            {
                continue;
            }

            slot = new(set, index);
            return true;
        }

        return false;
    }

    private bool TrySwapMacro(
        AtkDragDropManager* manager,
        AddonMacro* addon,
        MacroSlot source,
        MacroSlot target)
    {
        if (source == target || source.Set >= MacroPageCount || target.Set >= MacroPageCount ||
            source.Index >= MacroSlotsPerPage || target.Index >= MacroSlotsPerPage)
        {
            return false;
        }

        var sourceIcon = GetCurrentMacroSlotIcon(addon, source);
        var targetIcon = GetCurrentMacroSlotIcon(addon, target);
        if (!SwapMacroData(source, target))
        {
            return false;
        }

        manager->CancelDragDrop(allowSoundEffect: false, suppressFlyBack: true);
        ReloadMacroSlot(source.Set, source.Index);
        ReloadMacroSlot(target.Set, target.Index);
        var refreshedSourceIcon = GetMacroDisplayIcon(addon, source, false);
        var refreshedTargetIcon = GetMacroDisplayIcon(addon, target, false);
        RefreshMacroAddonSlot(addon, source.Set, source.Index, refreshedSourceIcon > 0 ? refreshedSourceIcon : targetIcon);
        RefreshMacroAddonSlot(addon, target.Set, target.Index, refreshedTargetIcon > 0 ? refreshedTargetIcon : sourceIcon);
        QueuePendingMacroRefresh(source, target, targetIcon, sourceIcon);

        var agent = GetMacroAgent();
        if (agent is not null && (agent->SelectedMacroSet != target.Set || agent->SelectedMacroIndex != target.Index))
        {
            agent->OpenMacro(target.Set, target.Index);
        }

        return true;
    }

    private static bool SwapMacroData(MacroSlot sourceSlot, MacroSlot targetSlot)
    {
        var macroModule = RaptureMacroModule.Instance();
        if (macroModule is null)
        {
            return false;
        }

        var source = macroModule->GetMacro(sourceSlot.Set, sourceSlot.Index);
        var target = macroModule->GetMacro(targetSlot.Set, targetSlot.Index);
        if (source is null || target is null || !source->IsNotEmpty())
        {
            return false;
        }

        var sourceSnapshot = MacroSnapshot.Capture(source);
        var targetSnapshot = MacroSnapshot.Capture(target);
        sourceSnapshot.WriteTo(target);
        targetSnapshot.WriteTo(source);

        macroModule->SetSavePendingFlag(true, sourceSlot.Set);
        if (targetSlot.Set != sourceSlot.Set)
        {
            macroModule->SetSavePendingFlag(true, targetSlot.Set);
        }

        return true;
    }

    private void QueuePendingMacroRefresh(MacroSlot source, MacroSlot target, uint sourceIcon, uint targetIcon)
    {
        pendingSource = source;
        pendingTarget = target;
        pendingSourceIcon = sourceIcon;
        pendingTargetIcon = targetIcon;
        pendingRefreshFrames = 30;
    }

    private void RefreshPendingMacroSwap(AddonMacro* addon)
    {
        if (pendingRefreshFrames <= 0)
        {
            return;
        }

        var sourceIcon = GetMacroDisplayIcon(addon, pendingSource, false);
        var targetIcon = GetMacroDisplayIcon(addon, pendingTarget, false);
        RefreshMacroAddonSlot(
            addon,
            pendingSource.Set,
            pendingSource.Index,
            sourceIcon > 0 ? sourceIcon : pendingSourceIcon);
        RefreshMacroAddonSlot(
            addon,
            pendingTarget.Set,
            pendingTarget.Index,
            targetIcon > 0 ? targetIcon : pendingTargetIcon);
        pendingRefreshFrames--;
    }

    private void ScanSelectedMacroEditorIcon()
    {
        if (!TryGetSelectedMacroContext(out var agent, out var addon, out var set, out var index))
        {
            return;
        }

        var macroModule = RaptureMacroModule.Instance();
        var macro = macroModule is null ? null : macroModule->GetMacro(set, index);
        if (macroModule is null || macro is null ||
            !BetterUserMacroParser.TryFindCustomIcon(agent->RawMacroString.AsSpan(), out var iconID) ||
            macro->IconId == iconID)
        {
            return;
        }

        ApplyMacroIcon(agent, addon, macroModule, macro, set, index, iconID);
    }

    private void ScanAllMacroCustomIcons()
    {
        var macroModule = RaptureMacroModule.Instance();
        if (macroModule is null)
        {
            return;
        }

        var addon = AddonHelper.GetByName<AddonMacro>("Macro");
        var currentSet = GetCurrentMacroSet();
        if (!IsMacroAddonVisible(addon) || currentSet >= MacroPageCount)
        {
            addon = null;
        }

        var changed = false;
        for (var set = 0u; set < MacroPageCount; set++)
        {
            var changedPage = false;
            for (var index = 0u; index < MacroSlotsPerPage; index++)
            {
                var macro = macroModule->GetMacro(set, index);
                if (macro is null || !macro->IsNotEmpty() ||
                    !TryFindMacroCustomIcon(macro, out var iconID) || macro->IconId == iconID)
                {
                    continue;
                }

                macro->SetIcon(iconID);
                changedPage = true;
                changed = true;
                if (addon is not null && currentSet == set)
                {
                    RefreshMacroAddonSlot(addon, set, index, iconID);
                }
            }

            if (changedPage)
            {
                macroModule->SetSavePendingFlag(true, set);
            }
        }

        if (!changed)
        {
            return;
        }

        var hotbarModule = RaptureHotbarModule.Instance();
        if (hotbarModule is not null)
        {
            hotbarModule->ReloadAllMacroSlots();
        }
    }

    private static bool TryFindMacroCustomIcon(RaptureMacroModule.Macro* macro, out uint iconID)
    {
        iconID = 0;
        if (macro is null)
        {
            return false;
        }

        for (var index = 0; index < MacroLineCount; index++)
        {
            if (BetterUserMacroParser.TryParseCustomIcon(macro->Lines[index].AsSpan(), out iconID))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSelectedMacroContext(
        out AgentMacro* agent,
        out AddonMacro* addon,
        out uint set,
        out uint index)
    {
        agent = GetMacroAgent();
        addon = AddonHelper.GetByName<AddonMacro>("Macro");
        set = agent is null ? 0 : agent->SelectedMacroSet;
        index = agent is null ? 0 : agent->SelectedMacroIndex;
        return agent is not null && IsMacroAddonVisible(addon) && set < MacroPageCount && index < MacroSlotsPerPage;
    }

    private static AgentMacro* GetMacroAgent() => AgentMacro.Instance();

    private static uint GetCurrentMacroSet()
    {
        var agent = GetMacroAgent();
        return agent is null ? MacroPageCount : agent->SelectedMacroSet;
    }

    private static void ApplyMacroIcon(
        AgentMacro* agent,
        AddonMacro* addon,
        RaptureMacroModule* macroModule,
        RaptureMacroModule.Macro* macro,
        uint set,
        uint index,
        uint iconID)
    {
        macro->SetIcon(iconID);
        macroModule->SetSavePendingFlag(true, set);
        ReloadMacroSlot(set, index);
        RefreshMacroAddonSlot(addon, set, index, iconID);
        agent->OpenMacro(set, index);
        RefreshMacroAddonSlot(addon, set, index, iconID);
    }

    private static uint GetCurrentMacroSlotIcon(AddonMacro* addon, MacroSlot slot)
    {
        if (addon is null || GetCurrentMacroSet() != slot.Set ||
            slot.Index >= MacroSlotsPerPage)
        {
            return 0;
        }

        var iconID = addon->MacroSetIcon[(int)slot.Index];
        if (iconID > 0 && iconID != addon->DefaultIcon)
        {
            return (uint)iconID;
        }

        var dragDrop = addon->DragDropComponent[(int)slot.Index].Value;
        var iconComponent = dragDrop is null ? null : dragDrop->AtkComponentIcon;
        var componentIconID = iconComponent is null ? 0 : iconComponent->IconId;
        return componentIconID > 0 && componentIconID != addon->DefaultIcon ? componentIconID : 0;
    }

    private static uint GetMacroDisplayIcon(AddonMacro* addon, MacroSlot slot, bool allowCachedNativeIcon)
    {
        if (slot.Set >= MacroPageCount || slot.Index >= MacroSlotsPerPage)
        {
            return 0;
        }

        var macroModule = RaptureMacroModule.Instance();
        var macro = macroModule is null ? null : macroModule->GetMacro(slot.Set, slot.Index);
        if (macro is null || !macro->IsNotEmpty())
        {
            return 0;
        }

        if (macro->IconId > 0)
        {
            return macro->IconId;
        }

        var nativeIconID = ResolveNativeMacroIconID(macroModule, slot.Set, slot.Index);
        if (nativeIconID > 0)
        {
            return nativeIconID;
        }

        if (allowCachedNativeIcon && addon is not null &&
            GetCurrentMacroSet() == slot.Set)
        {
            var currentIconID = addon->MacroSetIcon[(int)slot.Index];
            if (currentIconID > 0 && currentIconID != addon->DefaultIcon)
            {
                return (uint)currentIconID;
            }
        }

        return 0;
    }

    private static uint ResolveNativeMacroIconID(
        RaptureMacroModule* macroModule,
        uint set,
        uint index)
    {
        if (macroModule is null || set >= MacroPageCount || index >= MacroSlotsPerPage)
        {
            return 0;
        }

        var uiModule = UIModule.Instance();
        var hotbarModule = RaptureHotbarModule.Instance();
        if (uiModule is null || hotbarModule is null)
        {
            return 0;
        }

        var slotType = RaptureHotbarModule.HotbarSlotType.Empty;
        var rowID = 0u;
        var itemID = 0u;
        if (!macroModule->TryResolveMacroIcon(uiModule, &slotType, &rowID, (int)set, index, &itemID))
        {
            return 0;
        }

        if (slotType == RaptureHotbarModule.HotbarSlotType.Empty || rowID == 0)
        {
            return 0;
        }

        var iconID = hotbarModule->ScratchSlot.GetIconIdForSlot(slotType, rowID);
        return iconID > 0 ? (uint)iconID : 0;
    }

    private static void RefreshMacroAddonSlot(
        AddonMacro* addon,
        uint set,
        uint index,
        uint forcedDisplayIconID = 0)
    {
        if (addon is null || GetCurrentMacroSet() != set ||
            index >= MacroSlotsPerPage)
        {
            return;
        }

        var macroModule = RaptureMacroModule.Instance();
        var macro = macroModule is null ? null : macroModule->GetMacro(set, index);
        var dragDrop = addon->DragDropComponent[(int)index].Value;
        if (macro is null || dragDrop is null)
        {
            return;
        }

        var isCreated = macro->IsNotEmpty();
        var displayIconID = forcedDisplayIconID > 0
            ? forcedDisplayIconID
            : GetMacroDisplayIcon(addon, new MacroSlot(set, index), true);
        addon->MacroCreated[(int)index] = isCreated;
        if (!isCreated)
        {
            addon->MacroSetIcon[(int)index] = 0;
        }
        else if (displayIconID > 0)
        {
            addon->MacroSetIcon[(int)index] = (int)displayIconID;
        }

        addon->MacroName[(int)index].SetString(CopyNullTerminated(macro->Name.AsSpan()));
        if (isCreated && displayIconID > 0)
        {
            ShowMacroDragDropIcon(dragDrop, displayIconID);
        }
        else if (!isCreated)
        {
            ClearMacroDragDropIcon(dragDrop);
        }

        MarkMacroDragDropDirty(dragDrop);
        if (addon->AtkUnitBase.RootNode is not null)
        {
            addon->AtkUnitBase.RootNode->DrawFlags |= 0x1;
        }
    }

    private static void ReloadMacroSlot(uint set, uint index)
    {
        if (set >= MacroPageCount || index >= MacroSlotsPerPage)
        {
            return;
        }

        var hotbarModule = RaptureHotbarModule.Instance();
        if (hotbarModule is not null)
        {
            hotbarModule->ReloadMacroSlots((byte)set, (byte)index);
        }
    }

    private static void ClearMacroDragDropState(AtkComponentDragDrop* dragDrop)
    {
        if (dragDrop is null)
        {
            return;
        }

        dragDrop->VisibilityFlags &= ~DragDropVisibilityFlag.HideAfterFlyBack;
        if (dragDrop->OwnerNode is not null)
        {
            dragDrop->OwnerNode->AtkResNode.ToggleVisibility(true);
        }

        var iconComponent = dragDrop->AtkComponentIcon;
        if (iconComponent is null)
        {
            return;
        }

        iconComponent->Flags &= ~(IconComponentFlags.IsBeingDragged | IconComponentFlags.IsDisabled);
        iconComponent->SetIconImageDisableState(false);
        if (iconComponent->AtkComponentBase.OwnerNode is not null)
        {
            iconComponent->AtkComponentBase.OwnerNode->AtkResNode.ToggleVisibility(true);
        }
    }

    private static void ClearMacroDragDropTransientState(AddonMacro* addon, MacroSlot slot)
    {
        if (addon is null || GetCurrentMacroSet() != slot.Set ||
            slot.Index >= MacroSlotsPerPage)
        {
            return;
        }

        ClearMacroDragDropState(addon->DragDropComponent[(int)slot.Index].Value);
    }

    private static void ClearMacroDragDropIcon(AtkComponentDragDrop* dragDrop)
    {
        if (dragDrop is null)
        {
            return;
        }

        dragDrop->SetQuantityText(string.Empty);
        dragDrop->SetIconDisableState(false);
        var iconComponent = dragDrop->AtkComponentIcon;
        if (iconComponent is null)
        {
            return;
        }

        iconComponent->UnloadIcon();
        iconComponent->IconId = 0;
        iconComponent->SetIsMacro(false);
        iconComponent->SetIconImageDisableState(false);
        if (iconComponent->IconImage is not null)
        {
            iconComponent->IconImage->AtkResNode.ToggleVisibility(false);
        }

        if (iconComponent->FrameIcon is not null)
        {
            iconComponent->FrameIcon->AtkResNode.ToggleVisibility(false);
        }

        if (iconComponent->QuantityText is not null)
        {
            iconComponent->QuantityText->AtkResNode.ToggleVisibility(false);
        }

        if (iconComponent->FrameContainer is not null)
        {
            iconComponent->FrameContainer->ToggleVisibility(false);
        }
        MarkMacroDragDropDirty(dragDrop);
    }

    private static void ShowMacroDragDropIcon(AtkComponentDragDrop* dragDrop, uint displayIconID)
    {
        if (dragDrop is null)
        {
            return;
        }

        dragDrop->VisibilityFlags &= ~DragDropVisibilityFlag.HideAfterFlyBack;
        dragDrop->LoadIcon(displayIconID);
        dragDrop->SetQuantityText(string.Empty);
        dragDrop->SetIconDisableState(false);
        if (dragDrop->OwnerNode is not null)
        {
            dragDrop->OwnerNode->AtkResNode.ToggleVisibility(true);
        }

        var iconComponent = dragDrop->AtkComponentIcon;
        if (iconComponent is null)
        {
            return;
        }

        iconComponent->Flags &= ~(IconComponentFlags.IsBeingDragged | IconComponentFlags.IsDisabled);
        iconComponent->LoadIcon(displayIconID);
        iconComponent->SetIsMacro(true);
        iconComponent->SetIconImageDisableState(false);
        iconComponent->UpdateIndicator();
        if (iconComponent->AtkComponentBase.OwnerNode is not null)
        {
            iconComponent->AtkComponentBase.OwnerNode->AtkResNode.ToggleVisibility(true);
        }

        if (iconComponent->OuterResNode is not null)
        {
            iconComponent->OuterResNode->ToggleVisibility(true);
        }

        if (iconComponent->IconImage is not null)
        {
            iconComponent->IconImage->AtkResNode.ToggleVisibility(true);
        }

        if (iconComponent->FrameIcon is not null)
        {
            iconComponent->FrameIcon->AtkResNode.ToggleVisibility(true);
        }

        if (iconComponent->QuantityText is not null)
        {
            iconComponent->QuantityText->AtkResNode.ToggleVisibility(false);
        }

        if (iconComponent->FrameContainer is not null)
        {
            iconComponent->FrameContainer->ToggleVisibility(true);
        }

        if (iconComponent->Frame is not null)
        {
            iconComponent->Frame->ToggleVisibility(true);
        }
        MarkMacroDragDropDirty(dragDrop);
    }

    private static void MarkMacroDragDropDirty(AtkComponentDragDrop* dragDrop)
    {
        if (dragDrop is null)
        {
            return;
        }

        if (dragDrop->OwnerNode is not null)
        {
            dragDrop->OwnerNode->AtkResNode.DrawFlags |= 0x1;
        }

        var iconComponent = dragDrop->AtkComponentIcon;
        if (iconComponent is null)
        {
            return;
        }

        if (iconComponent->AtkComponentBase.OwnerNode is not null)
        {
            iconComponent->AtkComponentBase.OwnerNode->AtkResNode.DrawFlags |= 0x1;
        }

        if (iconComponent->OuterResNode is not null)
        {
            iconComponent->OuterResNode->DrawFlags |= 0x1;
        }

        if (iconComponent->FrameContainer is not null)
        {
            iconComponent->FrameContainer->DrawFlags |= 0x1;
        }

        if (iconComponent->ComboBorder is not null)
        {
            iconComponent->ComboBorder->DrawFlags |= 0x1;
        }

        if (iconComponent->Frame is not null)
        {
            iconComponent->Frame->DrawFlags |= 0x1;
        }

        if (iconComponent->IconImage is not null)
        {
            iconComponent->IconImage->AtkResNode.DrawFlags |= 0x1;
        }

        if (iconComponent->FrameIcon is not null)
        {
            iconComponent->FrameIcon->AtkResNode.DrawFlags |= 0x1;
        }

        if (iconComponent->QuantityText is not null)
        {
            iconComponent->QuantityText->AtkResNode.DrawFlags |= 0x1;
        }
    }

    private static void RepositionMacroInputNode(AtkComponentNode* inputComponentNode, Vector2 offset)
    {
        var collisionNode = (AtkCollisionNode*)inputComponentNode->Component->UldManager.SearchNodeById(20);
        var backgroundNode = (AtkNineGridNode*)inputComponentNode->Component->UldManager.SearchNodeById(19);
        var borderNode = (AtkNineGridNode*)inputComponentNode->Component->UldManager.SearchNodeById(18);
        var remainingLineNode = (AtkTextNode*)inputComponentNode->Component->UldManager.SearchNodeById(17);
        var textInputNode = (AtkTextNode*)inputComponentNode->Component->UldManager.SearchNodeById(16);
        if (collisionNode is null || backgroundNode is null || borderNode is null ||
            textInputNode is null || remainingLineNode is null)
        {
            return;
        }

        var position = Vector2.Zero;
        inputComponentNode->GetPositionFloat(&position.X, &position.Y);
        inputComponentNode->SetPositionFloat(position.X + offset.X, position.Y);
        inputComponentNode->SetWidth((ushort)(inputComponentNode->GetWidth() - offset.X));
        inputComponentNode->SetHeight((ushort)(inputComponentNode->GetHeight() - offset.Y));
        collisionNode->SetWidth((ushort)(collisionNode->GetWidth() - offset.X));
        collisionNode->SetHeight((ushort)(collisionNode->GetHeight() - offset.Y));
        backgroundNode->SetWidth((ushort)(backgroundNode->GetWidth() - offset.X));
        backgroundNode->SetHeight((ushort)(backgroundNode->GetHeight() - offset.Y));
        borderNode->SetWidth((ushort)(borderNode->GetWidth() - offset.X));
        borderNode->SetHeight((ushort)(borderNode->GetHeight() - offset.Y));
        textInputNode->SetWidth((ushort)(textInputNode->GetWidth() - offset.X));
        textInputNode->SetHeight((ushort)(textInputNode->GetHeight() - offset.Y));
        remainingLineNode->GetPositionFloat(&position.X, &position.Y);
        remainingLineNode->SetPositionFloat(position.X, position.Y - offset.Y);
        remainingLineNode->SetWidth((ushort)(remainingLineNode->GetWidth() - offset.X));
    }

    private static byte[] CopyNullTerminated(ReadOnlySpan<byte> value)
    {
        var length = value.IndexOf((byte)0);
        if (length >= 0)
        {
            value = value[..length];
        }

        var result = new byte[value.Length + 1];
        value.CopyTo(result);
        return result;
    }

    private void ClearMacroUI()
    {
        ClearMacroDropTargets();
        ClearMacroLineNumbers();
        pendingRefreshFrames = 0;
        pendingSourceIcon = 0;
        pendingTargetIcon = 0;
    }

    private readonly record struct MacroSlot(uint Set, uint Index);

    private readonly struct MacroDropTarget
    {
        public MacroDropTarget(AtkComponentDragDrop* component, DragDropType originalAcceptedType)
        {
            Component = component;
            OriginalAcceptedType = originalAcceptedType;
        }

        public AtkComponentDragDrop* Component { get; }

        public DragDropType OriginalAcceptedType { get; }
    }

    private sealed class MacroSnapshot
    {
        private readonly uint iconID;
        private readonly byte[] name;
        private readonly byte[][] lines;

        private MacroSnapshot(uint iconID, byte[] name, byte[][] lines)
        {
            this.iconID = iconID;
            this.name = name;
            this.lines = lines;
        }

        public static MacroSnapshot Capture(RaptureMacroModule.Macro* macro)
        {
            var lines = new byte[MacroLineCount][];
            for (var index = 0; index < MacroLineCount; index++)
            {
                lines[index] = CopyNullTerminated(macro->Lines[index].AsSpan());
            }

            return new(macro->IconId, CopyNullTerminated(macro->Name.AsSpan()), lines);
        }

        public void WriteTo(RaptureMacroModule.Macro* macro)
        {
            macro->Clear();
            macro->SetIcon(iconID);
            macro->Name.SetString(name);
            for (var index = 0; index < lines.Length; index++)
            {
                macro->Lines[index].SetString(lines[index]);
            }
        }
    }
}

[Serializable]
public sealed class BetterUserMacroConfig
{
    public bool Tooltips { get; set; } = true;

    public bool LineNumbers { get; set; } = true;

    public bool DragSwap { get; set; } = true;

    public bool CustomIcons { get; set; } = true;
}

internal static class BetterUserMacroPanel
{
    private const string PreviewImageBaseURL =
        "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Interface/BetterUserMacro-";

    public static bool Draw(BetterUserMacroConfig config)
    {
        var changed = false;
        var style = ImGui.GetStyle();
        using var cellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y));
        using var itemSpacing = ImRaii.PushStyle(
            ImGuiStyleVar.ItemSpacing,
            new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
        using var table = ImRaii.Table(
            "##betterUserMacroSettings",
            4,
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##c0", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##c1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##c2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##c3", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var tooltips = config.Tooltips;
        if (DrawCheckbox(
                "Feature.BetterUserMacro.Tooltips",
                "tooltips",
                ref tooltips,
                $"{PreviewImageBaseURL}1.png"))
        {
            config.Tooltips = tooltips;
            changed = true;
        }

        ImGui.TableNextColumn();
        var lineNumbers = config.LineNumbers;
        if (DrawCheckbox(
                "Feature.BetterUserMacro.LineNumbers",
                "lineNumbers",
                ref lineNumbers,
                $"{PreviewImageBaseURL}2.png"))
        {
            config.LineNumbers = lineNumbers;
            changed = true;
        }

        ImGui.TableNextColumn();
        var dragSwap = config.DragSwap;
        if (DrawCheckbox("Feature.BetterUserMacro.DragSwap", "dragSwap", ref dragSwap))
        {
            config.DragSwap = dragSwap;
            changed = true;
        }

        ImGui.TableNextColumn();
        var customIcons = config.CustomIcons;
        if (DrawCheckbox(
                "Feature.BetterUserMacro.CustomIcons",
                "customIcons",
                ref customIcons,
                $"{PreviewImageBaseURL}3.png"))
        {
            config.CustomIcons = customIcons;
            changed = true;
        }

        return changed;
    }

    private static bool DrawCheckbox(
        string labelKey,
        string id,
        ref bool value,
        string? previewImageURL = null)
    {
        var changed = OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##betterUserMacro{id}", ref value);
        if (previewImageURL is not null)
        {
            OmniControls.PreviewImageIcon(previewImageURL);
        }
        else
        {
            ImGuiOm.HelpMarker(OmniLoc.Get($"{labelKey}.Help"));
        }

        return changed;
    }
}
