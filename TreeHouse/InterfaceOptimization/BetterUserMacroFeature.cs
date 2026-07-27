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
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed unsafe class BetterUserMacroFeature(BetterUserMacroConfig config) : TreeHouseFeatureBase(new(
    "better-user-macro",
    "Feature.BetterUserMacro.Title",
    "Feature.BetterUserMacro.Description",
    TreeHouseCategory.InterfaceOptimization,
    Commands:
    [
        new("Feature.BetterUserMacro.CustomIcons.Help", "/omni 宏图标 <图标ID>")
    ]))
{
    private const uint MacroPageCount = 2;
    private const uint MacroSlotsPerPage = 100;
    private const int MacroLineCount = 15;
    private static readonly Vector2 MacroInputSizeOffset = new(20f, 150f);

    private readonly List<TextNode> lineNumberNodes = new(MacroLineCount);
    private readonly List<MacroDragRegistration> dragRegistrations = new((int)MacroSlotsPerPage);
    private readonly Dictionary<string, uint> actionCache = new(StringComparer.OrdinalIgnoreCase);
    private FeatureLifetime? runtimeLifetime;
    private Hook<AddonActionBarBase.Delegates.ShowTooltip>? tooltipHook;
    private AddonMacro* lineNumberAddon;
    private bool macroInputRepositioned;
    private nint dragAddonAddress;
    private bool dragRegistrationAttempted;
    private MacroSlot? dragSource;
    private MacroSlot? dragTarget;
    private bool dragSwapHandled;
    private MacroSlot pendingSource;
    private MacroSlot pendingTarget;
    private uint pendingSourceIcon;
    private uint pendingTargetIcon;
    private int pendingRefreshFrames;

    public override bool HasSettings => true;

    public override bool DrawSettings() => BetterUserMacroPanel.Draw(config);

    public bool TrySetSelectedMacroIcon(uint iconId)
    {
        if (!IsEnabled || !config.CustomIcons || iconId == 0)
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

        ApplyMacroIcon(agent, addon, macroModule, macro, set, index, iconId);
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

            lifetime.Add(ClearMacroUi);
            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PostSetup, "Macro", OnMacroSetup);
            addonEvents.Register(AddonEvent.PreFinalize, "Macro", OnMacroFinalize);
            addonEvents.Register(AddonEvent.PreRefresh, "Macro", OnMacroPreRefresh);
            addonEvents.Register(AddonEvent.PreRequestedUpdate, "Macro", OnMacroPreRefresh);
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
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (config.CustomIcons)
        {
            ScanSelectedMacroEditorIcon();
            ScanAllMacroCustomIcons();
        }

        if (!AddonHelper.TryGetByName("Macro", out AddonMacro* addon) || addon is null)
        {
            ClearMacroUi();
            return;
        }

        EnsureMacroLineNumbers(addon);
        EnsureMacroDragRegistrations(addon);
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

        var realSlotId = (numberArrayIndex - 15) % 16;
        var realHotbarId = (numberArrayIndex - 15) / 272;
        if (realSlotId < 0 || realSlotId >= 16 || realHotbarId < 0)
        {
            return false;
        }

        var hotbarModule = RaptureHotbarModule.Instance();
        if (hotbarModule is null || realHotbarId >= hotbarModule->Hotbars.Length)
        {
            return false;
        }

        var hotbarSlot = hotbarModule->Hotbars[realHotbarId].Slots[realSlotId];
        if (hotbarSlot.CommandType != RaptureHotbarModule.HotbarSlotType.Macro)
        {
            return false;
        }

        var actionId = hotbarSlot.ApparentSlotType == RaptureHotbarModule.HotbarSlotType.Action
            ? hotbarSlot.ApparentActionId
            : ResolveMacroTooltipActionId(hotbarSlot.CommandId);
        if (actionId == 0)
        {
            return false;
        }

        AtkStage.Instance()->ShowActionTooltip(
            macroResNode,
            actionId,
            stringArray->StringArray[stringArrayIndex].ToString());
        return true;
    }

    private uint ResolveMacroTooltipActionId(uint commandId)
    {
        var set = commandId / 256u;
        var index = commandId % 256u;
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
                !TryResolveAction(actionName, out var actionId))
            {
                continue;
            }

            return actionId;
        }

        return 0;
    }

    private bool TryResolveAction(string actionName, out uint actionId)
    {
        if (actionCache.TryGetValue(actionName, out var cached))
        {
            actionId = cached;
            return actionId > 0;
        }

        foreach (var action in LuminaGetter.Get<LuminaAction>())
        {
            if (!string.Equals(action.Name.ToString(), actionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            actionCache[actionName] = action.RowId;
            actionId = action.RowId;
            return actionId > 0;
        }

        actionCache[actionName] = default;
        actionId = 0;
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
        EnsureMacroDragRegistrations(addon);
        RefreshVisibleMacroIcons(addon);
    }

    private void OnMacroRefresh(AddonEvent _, AddonArgs args)
    {
        var addon = (AddonMacro*)args.Addon.Address;
        if (addon is null)
        {
            return;
        }

        EnsureMacroLineNumbers(addon);
        EnsureMacroDragRegistrations(addon);
        RefreshVisibleMacroIcons(addon);
    }

    private void OnMacroPreRefresh(AddonEvent _, AddonArgs __) => ClearMacroDragRegistrations();

    private void OnMacroFinalize(AddonEvent _, AddonArgs __) => ClearMacroUi();

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

    private void EnsureMacroDragRegistrations(AddonMacro* addon)
    {
        if (!config.DragSwap || addon is null)
        {
            ClearMacroDragRegistrations();
            return;
        }

        var addonAddress = (nint)addon;
        if (dragAddonAddress != addonAddress)
        {
            ClearMacroDragRegistrations();
            dragAddonAddress = addonAddress;
        }

        if (dragRegistrationAttempted)
        {
            return;
        }

        dragRegistrationAttempted = true;
        for (var index = 0u; index < MacroSlotsPerPage; index++)
        {
            var dragDrop = addon->DragDropComponent[(int)index].Value;
            if (dragDrop is null || dragDrop->OwnerNode is null)
            {
                continue;
            }

            var node = &dragDrop->OwnerNode->AtkResNode;
            var originalAcceptedType = dragDrop->AcceptedType;
            var listener = new CustomEventListener((_, eventType, eventParam, atkEvent, atkEventData) =>
                OnMacroDragEvent(addon, eventParam, eventType, atkEvent, atkEventData));
            node->AddEvent(AtkEventType.DragDropBegin, index, listener, node, false);
            node->AddEvent(AtkEventType.DragDropInsert, index, listener, node, false);
            node->AddEvent(AtkEventType.DragDropEnd, index, listener, node, false);
            dragDrop->AcceptedType = DragDropType.Macro;
            dragRegistrations.Add(new(listener, node, dragDrop, originalAcceptedType, index));
        }
    }

    private void ClearMacroDragRegistrations()
    {
        for (var index = dragRegistrations.Count - 1; index >= 0; index--)
        {
            var registration = dragRegistrations[index];
            registration.Component->AcceptedType = registration.OriginalAcceptedType;
            registration.Node->RemoveEvent(AtkEventType.DragDropBegin, registration.Param, registration.Listener, false);
            registration.Node->RemoveEvent(AtkEventType.DragDropInsert, registration.Param, registration.Listener, false);
            registration.Node->RemoveEvent(AtkEventType.DragDropEnd, registration.Param, registration.Listener, false);
            registration.Listener.Dispose();
        }

        dragRegistrations.Clear();
        dragAddonAddress = 0;
        dragRegistrationAttempted = false;
        dragSource = null;
        dragTarget = null;
        dragSwapHandled = false;
    }

    private void OnMacroDragEvent(
        AddonMacro* addon,
        int slotIndex,
        AtkEventType eventType,
        AtkEvent* atkEvent,
        AtkEventData* atkEventData)
    {
        if (!config.DragSwap || addon is null || slotIndex < 0 || slotIndex >= MacroSlotsPerPage)
        {
            return;
        }

        switch (eventType)
        {
            case AtkEventType.DragDropBegin:
                dragSource = new MacroSlot(addon->SelectedPage, (uint)slotIndex);
                dragTarget = null;
                dragSwapHandled = false;
                break;

            case AtkEventType.DragDropInsert:
                if (dragSwapHandled)
                {
                    MarkDragDropAccepted(atkEvent);
                    break;
                }

                if (TryGetMacroDragSource(addon, atkEventData, out var insertSource))
                {
                    var insertTarget = new MacroSlot(addon->SelectedPage, (uint)slotIndex);
                    dragTarget = insertTarget;
                    if (TrySwapMacro(addon, insertSource, insertTarget))
                    {
                        MarkDragDropAccepted(atkEvent);
                        dragSwapHandled = true;
                    }
                }

                break;

            case AtkEventType.DragDropEnd:
                var endSource = dragSource;
                var endTarget = TryFindMacroSlotAtMouse(addon, out var mouseTarget) ? mouseTarget : dragTarget;
                if (!dragSwapHandled && endSource is { } swapSource && endTarget is { } swapTarget)
                {
                    dragSwapHandled = TrySwapMacro(addon, swapSource, swapTarget);
                }

                if (dragSwapHandled)
                {
                    MarkDragDropAccepted(atkEvent);
                    ClearDragDropState(atkEventData);
                    if (endTarget is { } completedTarget)
                    {
                        ClearMacroDragDropTransientState(addon, completedTarget);
                    }

                    if (endSource is { } completedSource)
                    {
                        ClearMacroDragDropTransientState(addon, completedSource);
                    }
                }

                dragSource = null;
                dragTarget = null;
                dragSwapHandled = false;
                break;
        }
    }

    private bool TrySwapMacro(AddonMacro* addon, MacroSlot source, MacroSlot target)
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
            !BetterUserMacroParser.TryFindCustomIcon(agent->RawMacroString.AsSpan(), out var iconId) ||
            macro->IconId == iconId)
        {
            return;
        }

        ApplyMacroIcon(agent, addon, macroModule, macro, set, index, iconId);
    }

    private void ScanAllMacroCustomIcons()
    {
        var macroModule = RaptureMacroModule.Instance();
        if (macroModule is null)
        {
            return;
        }

        var addon = AddonHelper.GetByName<AddonMacro>("Macro");
        var changed = false;
        for (var set = 0u; set < MacroPageCount; set++)
        {
            var changedPage = false;
            for (var index = 0u; index < MacroSlotsPerPage; index++)
            {
                var macro = macroModule->GetMacro(set, index);
                if (macro is null || !macro->IsNotEmpty() ||
                    !TryFindMacroCustomIcon(macro, out var iconId) || macro->IconId == iconId)
                {
                    continue;
                }

                macro->SetIcon(iconId);
                changedPage = true;
                changed = true;
                if (addon is not null && addon->SelectedPage == set)
                {
                    RefreshMacroAddonSlot(addon, set, index, iconId);
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

        if (addon is not null)
        {
            RefreshVisibleMacroIcons(addon);
        }
    }

    private static bool TryFindMacroCustomIcon(RaptureMacroModule.Macro* macro, out uint iconId)
    {
        iconId = 0;
        if (macro is null)
        {
            return false;
        }

        for (var index = 0; index < MacroLineCount; index++)
        {
            if (BetterUserMacroParser.TryParseCustomIcon(macro->Lines[index].AsSpan(), out iconId))
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
        return agent is not null && addon is not null && set < MacroPageCount && index < MacroSlotsPerPage;
    }

    private static AgentMacro* GetMacroAgent()
    {
        var agentModule = AgentModule.Instance();
        return agentModule is null ? null : (AgentMacro*)agentModule->GetAgentByInternalId(AgentId.Macro);
    }

    private static void ApplyMacroIcon(
        AgentMacro* agent,
        AddonMacro* addon,
        RaptureMacroModule* macroModule,
        RaptureMacroModule.Macro* macro,
        uint set,
        uint index,
        uint iconId)
    {
        macro->SetIcon(iconId);
        macroModule->SetSavePendingFlag(true, set);
        ReloadMacroSlot(set, index);
        RefreshMacroAddonSlot(addon, set, index, iconId);
        agent->OpenMacro(set, index);
        RefreshMacroAddonSlot(addon, set, index, iconId);
    }

    private void RefreshVisibleMacroIcons(AddonMacro* addon)
    {
        if (addon is null || addon->SelectedPage >= MacroPageCount)
        {
            return;
        }

        var set = addon->SelectedPage;
        var macroModule = RaptureMacroModule.Instance();
        if (macroModule is null)
        {
            return;
        }

        for (var index = 0u; index < MacroSlotsPerPage; index++)
        {
            var macro = macroModule->GetMacro(set, index);
            if (macro is not null && macro->IsNotEmpty())
            {
                RefreshMacroAddonSlot(addon, set, index);
            }
        }
    }

    private static uint GetCurrentMacroSlotIcon(AddonMacro* addon, MacroSlot slot)
    {
        if (addon is null || addon->SelectedPage != slot.Set || slot.Index >= MacroSlotsPerPage)
        {
            return 0;
        }

        var iconId = addon->MacroSetIcon[(int)slot.Index];
        if (iconId > 0 && iconId != addon->DefaultIcon)
        {
            return (uint)iconId;
        }

        var dragDrop = addon->DragDropComponent[(int)slot.Index].Value;
        var iconComponent = dragDrop is null ? null : dragDrop->AtkComponentIcon;
        var componentIconId = iconComponent is null ? 0 : iconComponent->IconId;
        return componentIconId > 0 && componentIconId != addon->DefaultIcon ? componentIconId : 0;
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

        var nativeIconId = ResolveNativeMacroIconId(macroModule, slot.Set, slot.Index);
        if (nativeIconId > 0)
        {
            return nativeIconId;
        }

        if (allowCachedNativeIcon && addon is not null && addon->SelectedPage == slot.Set)
        {
            var currentIconId = addon->MacroSetIcon[(int)slot.Index];
            if (currentIconId > 0 && currentIconId != addon->DefaultIcon)
            {
                return (uint)currentIconId;
            }
        }

        return 0;
    }

    private static uint ResolveNativeMacroIconId(
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
        var rowId = 0u;
        var itemId = 0u;
        if (!macroModule->TryResolveMacroIcon(uiModule, &slotType, &rowId, (int)set, index, &itemId))
        {
            return 0;
        }

        var iconId = hotbarModule->ScratchSlot.GetIconIdForSlot(slotType, rowId);
        return iconId > 0 ? (uint)iconId : 0;
    }

    private static void RefreshMacroAddonSlot(
        AddonMacro* addon,
        uint set,
        uint index,
        uint forcedDisplayIconId = 0)
    {
        if (addon is null || addon->SelectedPage != set || index >= MacroSlotsPerPage)
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
        var displayIconId = forcedDisplayIconId > 0
            ? forcedDisplayIconId
            : GetMacroDisplayIcon(addon, new MacroSlot(set, index), true);
        addon->MacroCreated[(int)index] = isCreated;
        if (!isCreated)
        {
            addon->MacroSetIcon[(int)index] = 0;
        }
        else if (displayIconId > 0)
        {
            addon->MacroSetIcon[(int)index] = (int)displayIconId;
        }

        addon->MacroName[(int)index].SetString(CopyNullTerminated(macro->Name.AsSpan()));
        if (isCreated && displayIconId > 0)
        {
            ShowMacroDragDropIcon(dragDrop, displayIconId);
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
        if (addon is null || addon->SelectedPage != slot.Set || slot.Index >= MacroSlotsPerPage)
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

    private static void ShowMacroDragDropIcon(AtkComponentDragDrop* dragDrop, uint displayIconId)
    {
        if (dragDrop is null)
        {
            return;
        }

        dragDrop->VisibilityFlags &= ~DragDropVisibilityFlag.HideAfterFlyBack;
        dragDrop->LoadIcon(displayIconId);
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
        iconComponent->LoadIcon(displayIconId);
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

    private static bool TryFindMacroSlotAtMouse(AddonMacro* addon, out MacroSlot target)
    {
        target = default;
        if (addon is null)
        {
            return false;
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

            if (!dragDrop->OwnerNode->AtkResNode.CheckCollisionAtCoords(mouseX, mouseY, true))
            {
                continue;
            }

            target = new MacroSlot(addon->SelectedPage, index);
            return true;
        }

        return false;
    }

    private static bool TryGetMacroDragSource(AddonMacro* addon, AtkEventData* eventData, out MacroSlot source)
    {
        source = default;
        if (addon is null || eventData is null)
        {
            return false;
        }

        var dragDropInterface = eventData->DragDropData.DragDropInterface;
        if (dragDropInterface is null)
        {
            return false;
        }

        var payload = DragDropPayload.FromDragDropInterface(dragDropInterface);
        if (payload.Type != DragDropType.Macro)
        {
            return false;
        }

        if ((payload.Int1 is 0 or 1) && payload.Int2 >= 0 && payload.Int2 < MacroSlotsPerPage)
        {
            source = new MacroSlot((uint)payload.Int1, (uint)payload.Int2);
            return true;
        }

        if (payload.Int2 >= 0 && payload.Int2 < MacroSlotsPerPage)
        {
            source = new MacroSlot(addon->SelectedPage, (uint)payload.Int2);
            return true;
        }

        if (payload.ReferenceIndex >= 0 && payload.ReferenceIndex < MacroSlotsPerPage)
        {
            source = new MacroSlot(addon->SelectedPage, (uint)payload.ReferenceIndex);
            return true;
        }

        return false;
    }

    private static void MarkDragDropAccepted(AtkEvent* atkEvent)
    {
        if (atkEvent is null)
        {
            return;
        }

        atkEvent->SetEventIsHandled();
        atkEvent->State.StateFlags |= AtkEventStateFlags.HasReturnFlags;
        atkEvent->State.ReturnFlags = 1;
    }

    private static void ClearDragDropState(AtkEventData* eventData)
    {
        var dragDropInterface = eventData is null ? null : eventData->DragDropData.DragDropInterface;
        if (dragDropInterface is not null)
        {
            dragDropInterface->GetPayloadContainer()->Clear();
        }
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

    private void ClearMacroUi()
    {
        ClearMacroDragRegistrations();
        ClearMacroLineNumbers();
        pendingRefreshFrames = 0;
        pendingSourceIcon = 0;
        pendingTargetIcon = 0;
    }

    private readonly record struct MacroSlot(uint Set, uint Index);

    private readonly struct MacroDragRegistration
    {
        public MacroDragRegistration(
            CustomEventListener listener,
            AtkResNode* node,
            AtkComponentDragDrop* component,
            DragDropType originalAcceptedType,
            uint param)
        {
            Listener = listener;
            Node = node;
            Component = component;
            OriginalAcceptedType = originalAcceptedType;
            Param = param;
        }

        public CustomEventListener Listener { get; }

        public AtkResNode* Node { get; }

        public AtkComponentDragDrop* Component { get; }

        public DragDropType OriginalAcceptedType { get; }

        public uint Param { get; }
    }

    private sealed class MacroSnapshot
    {
        private readonly uint iconId;
        private readonly byte[] name;
        private readonly byte[][] lines;

        private MacroSnapshot(uint iconId, byte[] name, byte[][] lines)
        {
            this.iconId = iconId;
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
            macro->SetIcon(iconId);
            macro->Name.SetString(name);
            for (var index = 0; index < lines.Length; index++)
            {
                macro->Lines[index].SetString(lines[index]);
            }
        }
    }
}
