using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.Exd;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class LockedPortraitPreview : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "LockedPortraitPreviewTitle",
            ["Description"] = "LockedPortraitPreviewDescription"
        });

    private readonly Dictionary<(byte DatasetIndex, ushort RowID), byte> originalUnlockStates = [];
    private readonly Dictionary<nint, bool> platePreviewConditions = [];
    private AddonEventRegistry? addonEvents;
    private nint plateDesignAgent;
    private int platePreviewThreadID;

    private Hook<ExdModule.Delegates.GetBannerConditionUnlockState> GetBannerConditionUnlockStateHook = null!;
    private Hook<AgentInterface.Delegates.Show> PlateDesignShowHook = null!;
    private Hook<AtkModuleInterface.AtkEventInterface.Delegates.ReceiveEvent> PlateDesignReceiveEventHook = null!;
    private Hook<AgentInterface.Delegates.Update> PlateDesignUpdateHook = null!;

    protected override void OnStart(FeatureLifetime activation)
    {
        var agents = AgentModule.Instance();
        var agent = agents == null ? null : agents->GetAgentByInternalId(AgentId.CharaCardDesignSetting);
        if (agent == null)
            throw new InvalidOperationException("Plate design agent is unavailable.");

        plateDesignAgent = (nint)agent;
        addonEvents = new(DalamudServices.AddonLifecycle);
        activation.OnStopping(addonEvents.Dispose);
        addonEvents.Register(AddonEvent.PostSetup, "BannerEditor", OnEditorEvent);
        addonEvents.Register(AddonEvent.PreFinalize, "BannerEditor", OnEditorEvent);

        GetBannerConditionUnlockStateHook = DService.Instance().Hook.HookFromAddress<ExdModule.Delegates.GetBannerConditionUnlockState>(
            ExdModule.MemberFunctionPointers.GetBannerConditionUnlockState,
            OnGetBannerConditionUnlockState);
        activation.Add(GetBannerConditionUnlockStateHook);
        activation.OnStopping(GetBannerConditionUnlockStateHook.Disable);
        GetBannerConditionUnlockStateHook.Enable();

        PlateDesignShowHook = DService.Instance().Hook.HookFromAddress<AgentInterface.Delegates.Show>(
            agent->VirtualTable->Show, OnPlateDesignShow);
        activation.Add(PlateDesignShowHook);
        activation.OnStopping(PlateDesignShowHook.Disable);
        PlateDesignShowHook.Enable();

        PlateDesignReceiveEventHook = DService.Instance().Hook.HookFromAddress<AtkModuleInterface.AtkEventInterface.Delegates.ReceiveEvent>(
            agent->VirtualTable->ReceiveEvent, OnPlateDesignReceiveEvent);
        activation.Add(PlateDesignReceiveEventHook);
        activation.OnStopping(PlateDesignReceiveEventHook.Disable);
        PlateDesignReceiveEventHook.Enable();

        PlateDesignUpdateHook = DService.Instance().Hook.HookFromAddress<AgentInterface.Delegates.Update>(
            agent->VirtualTable->Update, OnPlateDesignUpdate);
        activation.Add(PlateDesignUpdateHook);
        activation.OnStopping(PlateDesignUpdateHook.Disable);
        PlateDesignUpdateHook.Enable();
        UpdateEntries(false);
    }

    protected override void OnStop()
    {
        addonEvents = null;
        plateDesignAgent = 0;
        platePreviewThreadID = 0;
        platePreviewConditions.Clear();
        UpdateEntries(true);
        originalUnlockStates.Clear();
    }

    private void OnEditorEvent
    (
        AddonEvent eventType,
        AddonArgs  args
    )
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            originalUnlockStates.Clear();
            return;
        }

        UpdateEntries(false);
    }

    private void OnPlateDesignShow(AgentInterface* agent)
    {
        var previousThreadID = platePreviewThreadID;
        if ((nint)agent == plateDesignAgent)
        {
            platePreviewConditions.Clear();
            platePreviewThreadID = Environment.CurrentManagedThreadId;
        }

        try
        {
            PlateDesignShowHook.Original(agent);
        }
        finally
        {
            platePreviewThreadID = previousThreadID;
        }
    }

    private AtkValue* OnPlateDesignReceiveEvent
    (
        AtkModuleInterface.AtkEventInterface* agent,
        AtkValue* returnValue,
        AtkValue* values,
        uint valueCount,
        ulong eventKind
    )
    {
        var previousThreadID = platePreviewThreadID;
        if ((nint)agent == plateDesignAgent)
            platePreviewThreadID = Environment.CurrentManagedThreadId;

        try
        {
            return PlateDesignReceiveEventHook.Original(agent, returnValue, values, valueCount, eventKind);
        }
        finally
        {
            platePreviewThreadID = previousThreadID;
        }
    }

    private void OnPlateDesignUpdate(AgentInterface* agent, uint frameCount)
    {
        var previousThreadID = platePreviewThreadID;
        if ((nint)agent == plateDesignAgent)
            platePreviewThreadID = Environment.CurrentManagedThreadId;

        try
        {
            PlateDesignUpdateHook.Original(agent, frameCount);
        }
        finally
        {
            platePreviewThreadID = previousThreadID;
        }
    }

    private int OnGetBannerConditionUnlockState(void* row)
    {
        var unlockState = GetBannerConditionUnlockStateHook.Original(row);
        if (platePreviewThreadID != Environment.CurrentManagedThreadId || row == null)
            return unlockState;

        return unlockState == 2 || unlockState == 3 && IsPlatePreviewAvailable(row) ? 1 : unlockState;
    }

    private bool IsPlatePreviewAvailable(void* row)
    {
        if (platePreviewConditions.TryGetValue((nint)row, out var available))
            return available;

        var index = 0u;
        foreach (var condition in LuminaGetter.Get<BannerCondition>())
        {
            var nativeRow = ExdModule.GetBannerConditionByIndex(index++);
            if (nativeRow != null)
                platePreviewConditions[(nint)nativeRow] = condition.UnlockType2 > 1;
        }

        return platePreviewConditions.GetValueOrDefault((nint)row);
    }

    private void UpdateEntries(bool restore)
    {
        var agent = AgentBannerEditor.Instance();
        var state = agent == null ? null : agent->EditorState;
        if (state == null)
            return;

        UpdateDataset(ref state->Backgrounds, restore);
        UpdateDataset(ref state->Frames, restore);
        UpdateDataset(ref state->Accents, restore);
        UpdateDataset(ref state->Poses, restore);
        UpdateDataset(ref state->Expressions, restore);
    }

    private void UpdateDataset
    (
        ref AgentBannerEditorState.Dataset dataset,
        bool                               restore
    )
    {
        if (dataset.SortedEntries == null)
            return;

        for (var index = 0u; index < dataset.SortedEntriesCount; index++)
        {
            var entry = dataset.SortedEntries[index];
            if (entry == null)
                continue;

            var key = (dataset.DatasetIndex, entry->RowId);
            if (restore)
            {
                if (entry->BannerConditionUnlockState == 1 && originalUnlockStates.TryGetValue(key, out var originalState))
                    entry->BannerConditionUnlockState = originalState;
            }
            else if (entry->BannerConditionUnlockState == 2 ||
                     entry->BannerConditionUnlockState == 3 && IsPreviewAvailable(dataset.DatasetIndex, entry->RowId))
            {
                originalUnlockStates[key]         = entry->BannerConditionUnlockState;
                entry->BannerConditionUnlockState = 1;
            }
        }
    }

    private static bool IsPreviewAvailable
    (
        byte   datasetIndex,
        ushort rowID
    )
    {
        uint? conditionID = datasetIndex switch
        {
            1 when LuminaGetter.TryGetRow<BannerBg>(rowID, out var background) => background.UnlockCondition.RowId,
            2 when LuminaGetter.TryGetRow<BannerFrame>(rowID, out var frame) => frame.UnlockCondition.RowId,
            3 when LuminaGetter.TryGetRow<BannerDecoration>(rowID, out var accent) => accent.UnlockCondition.RowId,
            4 when LuminaGetter.TryGetRow<BannerTimeline>(rowID, out var pose) => pose.UnlockCondition.RowId,
            5 when LuminaGetter.TryGetRow<BannerFacial>(rowID, out var expression) => expression.UnlockCondition.RowId,
            _ => null
        };

        return conditionID is { } id &&
               LuminaGetter.TryGetRow<BannerCondition>(id, out var condition) &&
               condition.UnlockType2 > 1;
    }
}
