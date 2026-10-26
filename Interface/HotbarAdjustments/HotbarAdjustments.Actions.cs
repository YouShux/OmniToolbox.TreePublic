using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Models;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class HotbarAdjustments
{
    private static readonly CompSig ActionMenuRefreshSig = new(
        "48 8B C4 53 41 55 48 81 EC 88 00 00 00 4D 8B E8");
    private delegate byte ActionMenuRefreshDelegate(AtkUnitBase* addon, uint valueCount, AtkValue* values);
    private Hook<ActionMenuRefreshDelegate> ActionMenuRefreshHook = null!;

    private static readonly CompSig PVPActionRenderSig = new(
        "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 41 54 41 55 41 56 41 57 48 83 EC 30 49 8B F9");
    private delegate void PVPActionRenderDelegate(AtkUnitBase* addon, int index, nint nodeData, AtkComponentBase* listItem);
    private Hook<PVPActionRenderDelegate> PVPActionRenderHook = null!;

    private static readonly CompSig PVPActionReceiveEventSig = new(
        "40 55 53 56 57 41 54 41 56 41 57 48 8D 6C 24 E1 48 81 EC C0 00 00 00 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 0F 4C 8B 65 7F 4D 8B F9");
    private delegate void PVPActionReceiveEventDelegate(AtkUnitBase* addon, AtkEventType eventType, int eventParam,
        AtkEvent* atkEvent, AtkEventData* eventData);
    private Hook<PVPActionReceiveEventDelegate> PVPActionReceiveEventHook = null!;

    private void InitializeActionDragDrop(FeatureLifetime lifetime)
    {
        lifetime.Add(UpdateActionDragDropRestrictions);
        ActionMenuRefreshHook = ActionMenuRefreshSig.GetHook<ActionMenuRefreshDelegate>(ActionMenuRefreshDetour);
        lifetime.Add(ActionMenuRefreshHook.Dispose);
        ActionMenuRefreshHook.Enable();
        PVPActionRenderHook = PVPActionRenderSig.GetHook<PVPActionRenderDelegate>(PVPActionRenderDetour);
        lifetime.Add(PVPActionRenderHook.Dispose);
        PVPActionRenderHook.Enable();
        PVPActionReceiveEventHook = PVPActionReceiveEventSig.GetHook<PVPActionReceiveEventDelegate>(PVPActionReceiveEventDetour);
        lifetime.Add(PVPActionReceiveEventHook.Dispose);
        PVPActionReceiveEventHook.Enable();
    }

    private void UpdateActionDragDropRestrictions()
    {
        if (AddonHelper.TryGetByName("ActionMenu", out var addon) &&
            addon->IsAddonAndNodesReady() && addon->AtkValues != null)
        {
            addon->OnRefresh(addon->AtkValuesCount, addon->AtkValues);
        }

        var stage = AtkStage.Instance();
        if (stage != null && AddonHelper.TryGetByName("PvpProfileAction", out var pvpAddon) &&
            pvpAddon->IsAddonAndNodesReady())
        {
            pvpAddon->OnRequestedUpdate(stage->GetNumberArrayData(), stage->GetStringArrayData());
        }
    }

    private void PVPActionRenderDetour(AtkUnitBase* addon, int index, nint nodeData, AtkComponentBase* listItem)
    {
        var stage = config.UnlockActions ? AtkStage.Instance() : null;
        var data = stage == null ? null : stage->GetNumberArrayData(NumberArrayType.PvPProfile);
        if (data == null || data->IntArray == null || data->Size <= PVP_ACTION_COUNT_INDEX ||
            index < 0 || index >= data->IntArray[PVP_ACTION_COUNT_INDEX] ||
            PVP_FIRST_ACTION_FLAGS_INDEX + (long)index * PVP_ACTION_VALUE_STRIDE >= data->Size)
        {
            PVPActionRenderHook.Original(addon, index, nodeData, listItem);
            return;
        }

        ref var flags = ref data->IntArray[PVP_FIRST_ACTION_FLAGS_INDEX + index * PVP_ACTION_VALUE_STRIDE];
        var originalFlags = flags;
        flags |= PVP_ACTION_SLOTABLE_FLAG;
        try
        {
            PVPActionRenderHook.Original(addon, index, nodeData, listItem);
        }
        finally
        {
            flags = originalFlags;
        }
    }

    private byte ActionMenuRefreshDetour(AtkUnitBase* addon, uint valueCount, AtkValue* values)
    {
        if (!config.UnlockActions || values == null || valueCount <= ACTION_COUNT_INDEX ||
            values[ACTION_COUNT_INDEX].Type != AtkValueType.UInt)
        {
            return ActionMenuRefreshHook.Original(addon, valueCount, values);
        }

        var actionCount = (int)Math.Min(values[ACTION_COUNT_INDEX].UInt, MAX_ACTION_COUNT);
        if (actionCount == 0 || FIRST_ACTION_FLAGS_INDEX + (actionCount - 1) * ACTION_VALUE_STRIDE >= valueCount)
            return ActionMenuRefreshHook.Original(addon, valueCount, values);

        Span<uint> originalFlags = stackalloc uint[actionCount];
        for (var index = 0; index < actionCount; index++)
        {
            ref var flags = ref values[FIRST_ACTION_FLAGS_INDEX + index * ACTION_VALUE_STRIDE];
            originalFlags[index] = flags.UInt;
            if (flags.Type == AtkValueType.UInt)
                flags.UInt |= ACTION_SLOTABLE_FLAG;
        }

        try
        {
            return ActionMenuRefreshHook.Original(addon, valueCount, values);
        }
        finally
        {
            for (var index = 0; index < actionCount; index++)
                values[FIRST_ACTION_FLAGS_INDEX + index * ACTION_VALUE_STRIDE].UInt = originalFlags[index];
        }
    }

    private void PVPActionReceiveEventDetour(AtkUnitBase* addon, AtkEventType eventType, int eventParam,
        AtkEvent* atkEvent, AtkEventData* eventData)
    {
        if (!config.UnlockActions || eventType is not (AtkEventType.DragDropBegin or AtkEventType.ListItemRollOver))
        {
            PVPActionReceiveEventHook.Original(addon, eventType, eventParam, atkEvent, eventData);
            return;
        }

        var stage = AtkStage.Instance();
        var data = stage == null ? null : stage->GetNumberArrayData(NumberArrayType.PvPProfile);
        if (data == null || data->IntArray == null || data->Size <= PVP_FIRST_ACTION_FLAGS_INDEX)
        {
            PVPActionReceiveEventHook.Original(addon, eventType, eventParam, atkEvent, eventData);
            return;
        }

        var actionCount = Math.Min(data->IntArray[PVP_ACTION_COUNT_INDEX], MAX_PVP_ACTION_COUNT);
        if (actionCount <= 0 || PVP_FIRST_ACTION_FLAGS_INDEX + (actionCount - 1) * PVP_ACTION_VALUE_STRIDE >= data->Size)
        {
            PVPActionReceiveEventHook.Original(addon, eventType, eventParam, atkEvent, eventData);
            return;
        }

        Span<int> originalFlags = stackalloc int[actionCount];
        for (var index = 0; index < actionCount; index++)
        {
            ref var flags = ref data->IntArray[PVP_FIRST_ACTION_FLAGS_INDEX + index * PVP_ACTION_VALUE_STRIDE];
            originalFlags[index] = flags;
            flags |= PVP_ACTION_SLOTABLE_FLAG;
        }

        try
        {
            PVPActionReceiveEventHook.Original(addon, eventType, eventParam, atkEvent, eventData);
        }
        finally
        {
            for (var index = 0; index < actionCount; index++)
                data->IntArray[PVP_FIRST_ACTION_FLAGS_INDEX + index * PVP_ACTION_VALUE_STRIDE] = originalFlags[index];
        }
    }

    #region 常量

    private const int ACTION_COUNT_INDEX = 15;
    private const int FIRST_ACTION_FLAGS_INDEX = 20;
    private const int ACTION_VALUE_STRIDE = 8;
    private const uint MAX_ACTION_COUNT = 80;
    private const uint ACTION_SLOTABLE_FLAG = 2;
    private const int PVP_ACTION_COUNT_INDEX = 99;
    private const int PVP_FIRST_ACTION_FLAGS_INDEX = 101;
    private const int PVP_ACTION_VALUE_STRIDE = 2;
    private const int PVP_ACTION_SLOTABLE_FLAG = 0x01000000;
    private const int MAX_PVP_ACTION_COUNT = 64;

    #endregion
}
