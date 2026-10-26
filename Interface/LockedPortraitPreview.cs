using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
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
    private AddonEventRegistry? addonEvents;

    protected override void OnStart(FeatureLifetime activation)
    {
        addonEvents = new(DalamudServices.AddonLifecycle);
        activation.OnStopping(addonEvents.Dispose);
        addonEvents.Register(AddonEvent.PostSetup, "BannerEditor", OnEditorEvent);
        addonEvents.Register(AddonEvent.PreFinalize, "BannerEditor", OnEditorEvent);
        UpdateEntries(false);

    }

    protected override void OnStop()
    {
        addonEvents = null;
        UpdateEntries(true);
        originalUnlockStates.Clear();

    }

    private void OnEditorEvent(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            originalUnlockStates.Clear();
            return;
        }

        UpdateEntries(false);
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

    private void UpdateDataset(ref AgentBannerEditorState.Dataset dataset, bool restore)
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
                originalUnlockStates[key] = entry->BannerConditionUnlockState;
                entry->BannerConditionUnlockState = 1;
            }
        }
    }

    private static bool IsPreviewAvailable(byte datasetIndex, ushort rowID)
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
