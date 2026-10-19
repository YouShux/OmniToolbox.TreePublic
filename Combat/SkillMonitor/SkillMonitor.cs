using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class SkillMonitor : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("SkillMonitorTitle"),
        Description = OmniLoc.Get("SkillMonitorDescription"),
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Combat/SkillMonitor-1.png"
    };

    public override bool HasSettings => true;

    private readonly SkillMonitorConfig config;
    private readonly SkillMonitorPanel panel = new();
    private SkillMonitorDefinition[] definitions;
    private SkillMonitorTracker tracker;
    private SkillMonitorOverlay overlay;
    private FeatureLifetime? runtimeLifetime;

    public SkillMonitor(SkillMonitorConfig config)
    {
        this.config = config;
        NormalizeConfig();
        definitions = SkillMonitorDefinitions.Create(config.CustomActions);
        NormalizeJobActions();
        tracker = new(definitions);
        overlay = new(config, definitions, tracker, CreateDefinitionIndexesByJob());
    }

    protected override void OnEnable()
    {
        NormalizeConfig();
        var lifetime = new FeatureLifetime();
        try
        {
            tracker.Register(lifetime);
            lifetime.Add(overlay.Dispose);
            var windowManager = WindowManager.Instance();
            _ = windowManager.WindowSystem;
            windowManager.PostDraw += overlay.Draw;
            lifetime.Add(() => windowManager.PostDraw -= overlay.Draw);
            runtimeLifetime = lifetime;
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                runtimeLifetime = null;
                tracker.Clear();
            }

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
            tracker.Clear();
        }
    }

    public override bool DrawSettings()
    {
        var changed = panel.Draw(config, definitions, out var definitionsChanged);
        if (definitionsChanged)
        {
            RebuildRuntime();
        }

        return changed;
    }

    public override bool ResetSettings()
    {
        var defaults = new SkillMonitorConfig();
        config.Offset = defaults.Offset;
        config.IconScale = defaults.IconScale;
        config.IconSpacing = defaults.IconSpacing;
        config.IconsPerRow = defaults.IconsPerRow;
        config.Font = defaults.Font;
        config.Alignment = defaults.Alignment;
        config.ShowActive = defaults.ShowActive;
        config.ShowOnCooldown = defaults.ShowOnCooldown;
        config.ShowOffCooldown = defaults.ShowOffCooldown;
        config.HideOutOfCombat = defaults.HideOutOfCombat;
        config.HideWeaponSheathed = defaults.HideWeaponSheathed;
        config.ShowGeneralSkillsFirst = defaults.ShowGeneralSkillsFirst;
        config.CustomActions = [];
        config.JobActionOrder = [];
        config.JobDisabledActions = defaults.JobDisabledActions;
        NormalizeConfig();
        RebuildRuntime();
        return true;
    }

    private void NormalizeConfig()
    {
        config.IconScale = Math.Clamp(
            config.IconScale <= 0f ? SkillMonitorConfig.DEFAULT_ICON_SCALE : config.IconScale,
            SkillMonitorConfig.DEFAULT_ICON_SCALE * 0.5f,
            SkillMonitorConfig.DEFAULT_ICON_SCALE * 2f);
        config.IconSpacing = Math.Clamp(config.IconSpacing, 0f, 12f);
        config.IconsPerRow = Math.Clamp(config.IconsPerRow, 0, 20);
        if (!Enum.IsDefined(config.Alignment))
        {
            config.Alignment = SkillMonitorAlignment.Right;
        }
        config.CustomActions ??= [];
        var seenActionIDs = new HashSet<uint>();
        for (var index = config.CustomActions.Count - 1; index >= 0; index--)
        {
            if (!seenActionIDs.Add(config.CustomActions[index].ActionID) ||
                !SkillMonitorDefinitions.TryCreateCustom(config.CustomActions[index], out _))
            {
                config.CustomActions.RemoveAt(index);
            }
        }
    }

    private void NormalizeJobActions()
    {
        config.JobActionOrder ??= [];
        config.JobDisabledActions ??= [];
        NormalizeScope(0, SkillMonitorGroup.General);
        for (var groupIndex = 0; groupIndex < SkillMonitorDefinitions.JobGroups.Length; groupIndex++)
        {
            var group = SkillMonitorDefinitions.JobGroups[groupIndex];
            for (var jobIndex = 0; jobIndex < group.JobIDs.Length; jobIndex++)
            {
                NormalizeScope(group.JobIDs[jobIndex], group.Group);
            }
        }

    }

    private void NormalizeScope(uint scopeID, SkillMonitorGroup group)
    {
        if (!config.JobActionOrder.TryGetValue(scopeID, out var order))
        {
            order = [];
            config.JobActionOrder[scopeID] = order;
        }

        if (!config.JobDisabledActions.TryGetValue(scopeID, out var disabled))
        {
            disabled = [];
            config.JobDisabledActions[scopeID] = disabled;
        }

        var seen = new HashSet<uint>();
        for (var index = order.Count - 1; index >= 0; index--)
        {
            var valid = false;
            for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
            {
                if (definitions[definitionIndex].ConfigID == order[index] &&
                    definitions[definitionIndex].MatchesGroup(group) &&
                    (scopeID == 0 || definitions[definitionIndex].AppliesTo(scopeID)))
                {
                    valid = true;
                    break;
                }
            }

            if (!valid || !seen.Add(order[index]))
            {
                order.RemoveAt(index);
            }
        }

        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            if (!definition.MatchesGroup(group) ||
                (scopeID != 0 && !definition.AppliesTo(scopeID)) ||
                seen.Contains(definition.ConfigID))
            {
                continue;
            }

            order.Add(definition.ConfigID);
            if (definition.ActionID == 7561 && group == SkillMonitorGroup.Dps && !disabled.Contains(definition.ConfigID))
            {
                disabled.Add(definition.ConfigID);
            }
            seen.Add(definition.ConfigID);
        }

        for (var index = disabled.Count - 1; index >= 0; index--)
        {
            if (!seen.Contains(disabled[index]))
            {
                disabled.RemoveAt(index);
            }
        }

    }

    private int[][] CreateDefinitionIndexesByJob()
    {
        var indexesByJob = new int[64][];
        for (var groupIndex = 0; groupIndex < SkillMonitorDefinitions.JobGroups.Length; groupIndex++)
        {
            var group = SkillMonitorDefinitions.JobGroups[groupIndex];
            for (var jobIndex = 0; jobIndex < group.JobIDs.Length; jobIndex++)
            {
                var jobID = group.JobIDs[jobIndex];
                var indexes = new List<int>();
                if (config.ShowGeneralSkillsFirst)
                {
                    AddScopeIndexes(indexes, 0, jobID);
                    AddScopeIndexes(indexes, jobID, jobID);
                }
                else
                {
                    AddScopeIndexes(indexes, jobID, jobID);
                    AddScopeIndexes(indexes, 0, jobID);
                }

                indexesByJob[jobID] = indexes.ToArray();
            }
        }

        for (var index = 0; index < indexesByJob.Length; index++)
        {
            indexesByJob[index] ??= [];
        }

        return indexesByJob;
    }

    private void AddScopeIndexes(List<int> indexes, uint scopeID, uint jobID)
    {
        if (!config.JobActionOrder.TryGetValue(scopeID, out var order) ||
            !config.JobDisabledActions.TryGetValue(scopeID, out var disabled))
        {
            return;
        }

        for (var orderIndex = 0; orderIndex < order.Count; orderIndex++)
        {
            if (disabled.Contains(order[orderIndex]))
            {
                continue;
            }

            for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
            {
                if (definitions[definitionIndex].ConfigID == order[orderIndex] &&
                    definitions[definitionIndex].AppliesTo(jobID))
                {
                    indexes.Add(definitionIndex);
                    break;
                }
            }
        }
    }

    private void RebuildRuntime()
    {
        var wasEnabled = IsEnabled;
        if (wasEnabled)
        {
            OnDisable();
        }

        definitions = SkillMonitorDefinitions.Create(config.CustomActions);
        NormalizeJobActions();
        tracker = new(definitions);
        overlay = new(config, definitions, tracker, CreateDefinitionIndexesByJob());
        if (wasEnabled)
        {
            OnEnable();
        }
    }
}