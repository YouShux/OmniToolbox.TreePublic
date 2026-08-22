using Dalamud.Interface;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class SkillMonitor : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("SkillMonitorTitle"),
        Description = OmniLoc.Get("SkillMonitorDescription"),
        Category = ModuleCategory.Combat
    };

    private readonly SkillMonitorConfig config;
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

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = SkillMonitorPanel.Draw(config, definitions, out var definitionsChanged);
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
        config.Alignment = defaults.Alignment;
        config.ShowActive = defaults.ShowActive;
        config.ShowOnCooldown = defaults.ShowOnCooldown;
        config.ShowOffCooldown = defaults.ShowOffCooldown;
        config.HideOutOfCombat = defaults.HideOutOfCombat;
        config.HideWeaponSheathed = defaults.HideWeaponSheathed;
        config.ShowGeneralSkillsFirst = defaults.ShowGeneralSkillsFirst;
        config.CustomActions = [];
        config.EnabledActions = [];
        config.JobActionOrder = [];
        config.JobDisabledActions = defaults.JobDisabledActions;
        NormalizeConfig();
        RebuildRuntime();
        return true;
    }

    protected override void OnEnable()
    {
        NormalizeConfig();
        var lifetime = new FeatureLifetime();
        try
        {
            tracker.Register(lifetime);
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

    private void NormalizeConfig()
    {
        config.IconScale = Math.Clamp(
            config.IconScale <= 0f ? SkillMonitorConfig.DefaultIconScale : config.IconScale,
            SkillMonitorConfig.DefaultIconScale * 0.5f,
            SkillMonitorConfig.DefaultIconScale * 2f);
        config.IconSpacing = Math.Clamp(config.IconSpacing, 0f, 12f);
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
        config.EnabledActions ??= [];
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

        config.EnabledActions.Clear();
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
                    definitions[definitionIndex].Group == group &&
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
            if (definition.Group != group ||
                (scopeID != 0 && !definition.AppliesTo(scopeID)) ||
                seen.Contains(definition.ConfigID))
            {
                continue;
            }

            order.Add(definition.ConfigID);
            seen.Add(definition.ConfigID);
        }

        for (var index = disabled.Count - 1; index >= 0; index--)
        {
            if (!seen.Contains(disabled[index]))
            {
                disabled.RemoveAt(index);
            }
        }

        foreach (var enabledAction in config.EnabledActions)
        {
            if (!enabledAction.Value && seen.Contains(enabledAction.Key) && !disabled.Contains(enabledAction.Key))
            {
                disabled.Add(enabledAction.Key);
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

internal static class SkillMonitorPanel
{
    private const string ReorderPayload = "SkillMonitorReorder";
    private static readonly Vector2 DefaultOffset = new(17f, 0f);
    private static readonly int[] CustomActionInputs = new int[4];
    private static readonly string[] CustomActionErrors = new string[4];
    private static readonly SkillMonitorGroup[] GroupOrder =
    [
        SkillMonitorGroup.General,
        SkillMonitorGroup.Tank,
        SkillMonitorGroup.Healer,
        SkillMonitorGroup.Dps
    ];

    public static bool Draw(
        SkillMonitorConfig config,
        SkillMonitorDefinition[] definitions,
        out bool definitionsChanged)
    {
        var changed = false;
        definitionsChanged = false;
        changed |= DrawVisibilitySettings(config);
        changed |= DrawLayoutSettings(config, ref definitionsChanged);

        if (!ImGui.BeginTabBar("##skillMonitorGroups"))
        {
            return changed;
        }

        try
        {
            foreach (var group in GroupOrder)
            {
                if (!ImGui.BeginTabItem(OmniLoc.Get(group switch
                {
                    SkillMonitorGroup.Tank => "Feature.SkillMonitor.Group.Tank",
                    SkillMonitorGroup.Healer => "Feature.SkillMonitor.Group.Healer",
                    SkillMonitorGroup.Dps => "Feature.SkillMonitor.Group.Dps",
                    _ => "Feature.SkillMonitor.Group.General"
                })))
                {
                    continue;
                }

                try
                {
                    changed |= DrawGroup(config, definitions, group, ref definitionsChanged);
                }
                finally
                {
                    ImGui.EndTabItem();
                }
            }
        }
        finally
        {
            ImGui.EndTabBar();
        }

        return changed;
    }

    private static bool DrawGroup(
        SkillMonitorConfig config,
        SkillMonitorDefinition[] definitions,
        SkillMonitorGroup group,
        ref bool runtimeChanged)
    {
        var changed = false;
        var groupIndex = (int)group;
        var inputWidth = MathF.Max(
            OmniTheme.Scale(110f),
            ImGui.CalcTextSize("000000").X + ImGui.GetStyle().FramePadding.X * 4f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.SkillMonitor.Custom.ActionID"));
        ImGui.SameLine();
        OmniControls.InputInt($"##skillMonitorCustomAction{group}", ref CustomActionInputs[groupIndex], inputWidth);
        ImGui.SameLine();
        if (OmniControls.SmallButton(
                $"{OmniLoc.Get("Feature.SkillMonitor.Custom.Add")}##skillMonitorAddAction{group}",
                false))
        {
            var actionID = CustomActionInputs[groupIndex] > 0 ? (uint)CustomActionInputs[groupIndex] : 0;
            if (ContainsAction(definitions, config.CustomActions, actionID))
            {
                CustomActionErrors[groupIndex] = OmniLoc.Get("Feature.SkillMonitor.Custom.Duplicate");
            }
            else if (!SkillMonitorDefinitions.TryCreateCustom(new(actionID), out _))
            {
                CustomActionErrors[groupIndex] = OmniLoc.Get("Feature.SkillMonitor.Custom.Invalid");
            }
            else
            {
                config.CustomActions.Add(new(actionID));
                CustomActionInputs[groupIndex] = 0;
                CustomActionErrors[groupIndex] = string.Empty;
                runtimeChanged = true;
                changed = true;
            }
        }

        if (!string.IsNullOrEmpty(CustomActionErrors[groupIndex]))
        {
            ImGui.SameLine();
            using var errorColor = ImRaii.PushColor(ImGuiCol.Text, OmniTheme.Tokens.Error);
            ImGui.TextUnformatted(CustomActionErrors[groupIndex]);
        }

        if (group == SkillMonitorGroup.General)
        {
            if (OmniControls.CollapsingHeader(
                    $"{OmniLoc.Get("Feature.SkillMonitor.Group.General")}##skillMonitorGeneral",
                    ImGuiTreeNodeFlags.DefaultOpen))
            {
                changed |= DrawScope(config, definitions, 0, group, ref runtimeChanged);
            }

            return changed;
        }

        for (var groupJobIndex = 0; groupJobIndex < SkillMonitorDefinitions.JobGroups.Length; groupJobIndex++)
        {
            var jobGroup = SkillMonitorDefinitions.JobGroups[groupJobIndex];
            if (jobGroup.Group != group)
            {
                continue;
            }

            for (var jobIndex = 0; jobIndex < jobGroup.JobIDs.Length; jobIndex++)
            {
                var jobID = jobGroup.JobIDs[jobIndex];
                if (LuminaGetter.TryGetRow<ClassJob>(jobID, out var job) &&
                    OmniControls.CollapsingHeader($"{job.Name.ExtractText()}##skillMonitorJob{jobID}"))
                {
                    changed |= DrawScope(config, definitions, jobID, group, ref runtimeChanged);
                }
            }
        }

        return changed;
    }

    private static bool DrawScope(
        SkillMonitorConfig config,
        SkillMonitorDefinition[] definitions,
        uint scopeID,
        SkillMonitorGroup group,
        ref bool runtimeChanged)
    {
        if (!config.JobActionOrder.TryGetValue(scopeID, out var order) ||
            !config.JobDisabledActions.TryGetValue(scopeID, out var disabled))
        {
            return false;
        }

        var changed = false;
        var deleteLabel = OmniLoc.Get("Feature.SkillMonitor.Custom.Delete");
        var deleteButtonSize = OmniControls.CompactButtonSize(deleteLabel);
        using var table = ImRaii.Table(
            $"##skillMonitorActions{scopeID}",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings);
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.SkillMonitor.Column.Order"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.CheckboxSize() + ImGui.GetStyle().CellPadding.X * 2f);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.SkillMonitor.Column.Enabled"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.CheckboxSize() + ImGui.GetStyle().CellPadding.X * 2f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.SkillMonitor.Column.Action"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.SkillMonitor.Column.Operation"),
            ImGuiTableColumnFlags.WidthFixed,
            MathF.Max(
                ImGui.CalcTextSize(OmniLoc.Get("Feature.SkillMonitor.Column.Operation")).X,
                deleteButtonSize.X) + ImGui.GetStyle().CellPadding.X * 2f);
        ImGui.TableSetupScrollFreeze(0, 1);
        OmniControls.BeginTableHeaderRow();
        OmniControls.TableHeader(OmniLoc.Get("Feature.SkillMonitor.Column.Order"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.SkillMonitor.Column.Enabled"));
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.SkillMonitor.Column.Action"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.SkillMonitor.Column.Operation"));
        for (var orderIndex = 0; orderIndex < order.Count; orderIndex++)
        {
            var definitionIndex = FindDefinition(definitions, order[orderIndex], scopeID, group);
            if (definitionIndex < 0)
            {
                continue;
            }

            var definition = definitions[definitionIndex];
            var iconSize = new Vector2(OmniTheme.TableItemIconSize());
            var rowContentHeight = MathF.Max(iconSize.Y, MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight()));
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight + ImGui.GetStyle().CellPadding.Y * 2f);
            ImGui.TableNextColumn();
            OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
            if (DrawReorderHandle(scopeID, order, orderIndex, definition.Name))
            {
                runtimeChanged = true;
                changed = true;
                break;
            }

            ImGui.TableNextColumn();
            var enabled = !disabled.Contains(definition.ConfigID);
            OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
            if (OmniControls.Checkbox($"##skillMonitorAction{scopeID}_{definition.ConfigID}", ref enabled))
            {
                if (enabled)
                {
                    disabled.Remove(definition.ConfigID);
                }
                else
                {
                    disabled.Add(definition.ConfigID);
                }

                runtimeChanged = true;
                changed = true;
            }

            ImGui.TableNextColumn();
            FramedGameIcon.Draw(
                definition.IconID,
                ImGui.GetCursorScreenPos() + new Vector2(0f, (rowContentHeight - iconSize.Y) * 0.5f),
                iconSize,
                drawFrame: !definition.IsFood,
                preserveAspectRatio: definition.IsFood);
            ImGui.Dummy(new Vector2(iconSize.X, rowContentHeight));
            ImGui.SameLine();
            ImGui.GetWindowDrawList().AddText(
                ImGui.GetCursorScreenPos() +
                new Vector2(0f, MathF.Max(0f, (rowContentHeight - ImGui.CalcTextSize(definition.Name).Y) * 0.5f)),
                ImGui.GetColorU32(ImGuiCol.Text),
                definition.Name);
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, rowContentHeight));

            ImGui.TableNextColumn();
            if (!definition.IsCustom)
            {
                continue;
            }

            OmniControls.CenterTableItem(deleteButtonSize, rowContentHeight);
            if (OmniControls.SmallButton(
                    $"{deleteLabel}##skillMonitorDelete{scopeID}_{definition.ActionID}",
                    false,
                    deleteButtonSize))
            {
                config.CustomActions.RemoveAll(action => action.ActionID == definition.ActionID);
                runtimeChanged = true;
                changed = true;
                break;
            }
        }

        return changed;
    }

    private static bool DrawReorderHandle(uint scopeID, List<uint> order, int orderIndex, string label)
    {
        OmniControls.IconButton(
            $"##skillMonitorReorder{scopeID}_{order[orderIndex]}",
            FontAwesomeIcon.Bars,
            false,
            OmniLoc.Get("Feature.SkillMonitor.Reorder"));
        using (var source = ImRaii.DragDropSource())
        {
            if (source)
            {
                if (ImGui.SetDragDropPayload(ReorderPayload, []))
                {
                    draggedScopeID = scopeID;
                    draggedOrderIndex = orderIndex;
                }

                ImGui.TextUnformatted(label);
            }
        }

        using var target = ImRaii.DragDropTarget();
        if (!target)
        {
            return false;
        }

        var payload = ImGui.AcceptDragDropPayload(ReorderPayload);
        if (payload.IsNull ||
            !payload.IsDelivery() ||
            draggedScopeID != scopeID ||
            draggedOrderIndex < 0 ||
            draggedOrderIndex == orderIndex)
        {
            return false;
        }

        var configID = order[draggedOrderIndex];
        order.RemoveAt(draggedOrderIndex);
        order.Insert(orderIndex, configID);
        draggedScopeID = uint.MaxValue;
        draggedOrderIndex = -1;
        return true;
    }

    private static int FindDefinition(
        SkillMonitorDefinition[] definitions,
        uint configID,
        uint scopeID,
        SkillMonitorGroup group)
    {
        for (var index = 0; index < definitions.Length; index++)
        {
            if (definitions[index].ConfigID == configID &&
                definitions[index].Group == group &&
                (scopeID == 0 || definitions[index].AppliesTo(scopeID)))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool ContainsAction(
        SkillMonitorDefinition[] definitions,
        IReadOnlyList<SkillMonitorCustomActionConfig> customActions,
        uint actionID)
    {
        for (var index = 0; index < definitions.Length; index++)
        {
            if (definitions[index].ActionID == actionID)
            {
                return true;
            }
        }

        for (var index = 0; index < customActions.Count; index++)
        {
            if (customActions[index].ActionID == actionID)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DrawVisibilitySettings(SkillMonitorConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##skillMonitorVisibilitySettings",
            4,
            ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.SizingStretchSame,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##skillMonitorShowLabel", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##skillMonitorShowActive", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##skillMonitorShowCooldown", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##skillMonitorShowReady", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.SkillMonitor.ShowWhen"));
        ImGui.TableNextColumn();
        var showActive = config.ShowActive;
        if (DrawCheckbox("Active", ref showActive))
        {
            config.ShowActive = showActive;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showOnCooldown = config.ShowOnCooldown;
        if (DrawCheckbox("OnCooldown", ref showOnCooldown))
        {
            config.ShowOnCooldown = showOnCooldown;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showOffCooldown = config.ShowOffCooldown;
        if (DrawCheckbox("OffCooldown", ref showOffCooldown))
        {
            config.ShowOffCooldown = showOffCooldown;
            changed = true;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.SkillMonitor.HideWhen"));
        ImGui.TableNextColumn();
        var hideWeaponSheathed = config.HideWeaponSheathed;
        if (DrawCheckbox("WeaponSheathed", ref hideWeaponSheathed))
        {
            config.HideWeaponSheathed = hideWeaponSheathed;
            changed = true;
        }

        ImGui.TableNextColumn();
        var hideOutOfCombat = config.HideOutOfCombat;
        if (DrawCheckbox("OutOfCombat", ref hideOutOfCombat))
        {
            config.HideOutOfCombat = hideOutOfCombat;
            changed = true;
        }

        return changed;
    }

    private static bool DrawLayoutSettings(SkillMonitorConfig config, ref bool definitionsChanged)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##skillMonitorLayoutSettings",
            4,
            ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.SizingStretchSame,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##skillMonitorScale", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##skillMonitorSpacing", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##skillMonitorOffset", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##skillMonitorGeneralPosition", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var iconScaleLabel = OmniLoc.Get("Feature.SkillMonitor.IconScale");
        var iconScaleWidth = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(iconScaleLabel).X -
                             ImGui.GetStyle().ItemSpacing.X;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(iconScaleLabel);
        ImGui.SameLine();
        var iconScale = config.IconScale / SkillMonitorConfig.DefaultIconScale;
        if (OmniControls.DragFloat(
                "##skillMonitorIconScale",
                ref iconScale,
                0.05f,
                0.5f,
                2f,
                "%.2f",
                iconScaleWidth,
                ImGuiSliderFlags.AlwaysClamp))
        {
            config.IconScale = iconScale * SkillMonitorConfig.DefaultIconScale;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.TableNextColumn();
        var iconSpacingLabel = OmniLoc.Get("Feature.SkillMonitor.IconSpacing");
        var iconSpacingWidth = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(iconSpacingLabel).X -
                               ImGui.GetStyle().ItemSpacing.X;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(iconSpacingLabel);
        ImGui.SameLine();
        var iconSpacing = config.IconSpacing;
        if (OmniControls.DragFloat(
                "##skillMonitorIconSpacing",
                ref iconSpacing,
                0.5f,
                0f,
                12f,
                "%.1f",
                iconSpacingWidth,
                ImGuiSliderFlags.AlwaysClamp))
        {
            config.IconSpacing = iconSpacing;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.TableNextColumn();
        var offsetLabel = OmniLoc.Get("Feature.SkillMonitor.Offset");
        var offsetWidth = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(offsetLabel).X -
                          ImGui.GetStyle().ItemSpacing.X;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(offsetLabel);
        ImGui.SameLine();
        var offset = config.Offset - DefaultOffset;
        if (OmniControls.DragFloat2(
                "##skillMonitorOffset",
                ref offset,
                1f,
                -500f,
                500f,
                "%.0f",
                offsetWidth))
        {
            config.Offset = offset + DefaultOffset;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.TableNextColumn();
        var generalPositionLabel = OmniLoc.Get("Feature.SkillMonitor.GeneralPosition");
        var generalPositionWidth = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(generalPositionLabel).X -
                                   ImGui.GetStyle().ItemSpacing.X;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(generalPositionLabel);
        ImGui.SameLine();
        if (OmniControls.BeginCombo(
                "##skillMonitorGeneralPosition",
                OmniLoc.Get(config.ShowGeneralSkillsFirst
                    ? "Feature.SkillMonitor.GeneralPosition.First"
                    : "Feature.SkillMonitor.GeneralPosition.Last"),
                generalPositionWidth))
        {
            if (ImGui.Selectable(
                    OmniLoc.Get("Feature.SkillMonitor.GeneralPosition.First"),
                    config.ShowGeneralSkillsFirst))
            {
                config.ShowGeneralSkillsFirst = true;
                definitionsChanged = true;
                changed = true;
            }

            if (ImGui.Selectable(
                    OmniLoc.Get("Feature.SkillMonitor.GeneralPosition.Last"),
                    !config.ShowGeneralSkillsFirst))
            {
                config.ShowGeneralSkillsFirst = false;
                definitionsChanged = true;
                changed = true;
            }

            ImGui.EndCombo();
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var alignmentLabel = OmniLoc.Get("Feature.SkillMonitor.Alignment");
        var alignmentWidth = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(alignmentLabel).X -
                             ImGui.GetStyle().ItemSpacing.X;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(alignmentLabel);
        ImGui.SameLine();
        if (OmniControls.BeginCombo(
                "##skillMonitorAlignment",
                OmniLoc.Get(config.Alignment == SkillMonitorAlignment.Mirror
                    ? "Feature.SkillMonitor.Alignment.Mirror"
                    : "Feature.SkillMonitor.Alignment.Right"),
                alignmentWidth))
        {
            if (ImGui.Selectable(
                    OmniLoc.Get("Feature.SkillMonitor.Alignment.Right"),
                    config.Alignment == SkillMonitorAlignment.Right))
            {
                config.Alignment = SkillMonitorAlignment.Right;
                changed = true;
            }

            if (ImGui.Selectable(
                    OmniLoc.Get("Feature.SkillMonitor.Alignment.Mirror"),
                    config.Alignment == SkillMonitorAlignment.Mirror))
            {
                config.Alignment = SkillMonitorAlignment.Mirror;
                changed = true;
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private static bool DrawCheckbox(string name, ref bool value) =>
        OmniControls.Checkbox($"{OmniLoc.Get($"Feature.SkillMonitor.{name}")}##skillMonitor{name}", ref value);

    private static uint draggedScopeID = uint.MaxValue;
    private static int draggedOrderIndex = -1;
}

[Serializable]
public sealed class SkillMonitorConfig
{
    internal const float DefaultIconScale = 0.9f;

    public Vector2 Offset { get; set; } = new(17f, 0f);
    public float IconScale { get; set; } = DefaultIconScale;
    public float IconSpacing { get; set; } = 3f;
    public SkillMonitorAlignment Alignment { get; set; }
    public bool ShowActive { get; set; } = true;
    public bool ShowOnCooldown { get; set; } = true;
    public bool ShowOffCooldown { get; set; } = true;
    public bool HideOutOfCombat { get; set; }
    public bool HideWeaponSheathed { get; set; } = true;
    public bool ShowGeneralSkillsFirst { get; set; } = true;
    public List<SkillMonitorCustomActionConfig> CustomActions { get; set; } = [];
    public Dictionary<uint, bool> EnabledActions { get; set; } = [];
    public Dictionary<uint, List<uint>> JobActionOrder { get; set; } = [];
    public Dictionary<uint, List<uint>> JobDisabledActions { get; set; } = new()
    {
        [0] = [3],
        [19] = [7531, 7535, 7548],
        [21] = [7531, 7535, 7548],
        [32] = [7531, 7535, 7548],
        [37] = [7531, 7535, 7548]
    };
}

public enum SkillMonitorAlignment
{
    Right,
    Mirror
}

[Serializable]
public sealed record SkillMonitorCustomActionConfig(uint ActionID);
