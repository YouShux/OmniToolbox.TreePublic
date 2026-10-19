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

public sealed partial class SkillMonitor
{
    internal sealed class SkillMonitorPanel
    {
        private static readonly Vector2 DefaultOffset = new(17f, 0f);
        private readonly int[] CustomActionInputs = new int[4];
        private readonly string[] CustomActionErrors = new string[4];
        private static readonly SkillMonitorGroup[] GroupOrder =
        [
            SkillMonitorGroup.General,
            SkillMonitorGroup.Tank,
            SkillMonitorGroup.Healer,
            SkillMonitorGroup.Dps
        ];

        private uint DraggedScopeID = uint.MaxValue;
        private int DraggedOrderIndex = -1;

        public bool Draw(
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

        private bool DrawGroup(
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

        private bool DrawScope(
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
            var orderLabel = OmniLoc.Get("Feature.SkillMonitor.Column.Order");
            var enabledLabel = OmniLoc.Get("Feature.SkillMonitor.Column.Enabled");
            var actionLabel = OmniLoc.Get("Feature.SkillMonitor.Column.Action");
            var operationLabel = OmniLoc.Get("Feature.SkillMonitor.Column.Operation");
            var orderWidth = MathF.Max(OmniTheme.CheckboxSize(), ImGui.CalcTextSize(orderLabel).X);
            var enabledWidth = MathF.Max(OmniTheme.CheckboxSize(), ImGui.CalcTextSize(enabledLabel).X);
            var operationWidth = MathF.Max(deleteButtonSize.X, ImGui.CalcTextSize(operationLabel).X);
            var actionWidth = OmniTheme.TableItemIconSize() + ImGui.GetFontSize() * 5f;
            using var table = OmniControls.DataTable(
                $"##skillMonitorActions{scopeID}",
                [orderLabel, enabledLabel, actionLabel, operationLabel],
                [orderWidth, enabledWidth, actionWidth, operationWidth],
                [orderWidth, enabledWidth, actionWidth, operationWidth],
                out var detailLayout,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings, stretchColumn: 2);
            if (!table)
            {
                return false;
            }

            if (!detailLayout)
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                OmniControls.BeginTableHeaderRow();
                OmniControls.TableHeader(OmniLoc.Get("Feature.SkillMonitor.Column.Order"));
                OmniControls.TableHeader(OmniLoc.Get("Feature.SkillMonitor.Column.Enabled"));
                ImGui.TableNextColumn();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.SkillMonitor.Column.Action"));
                OmniControls.TableHeader(OmniLoc.Get("Feature.SkillMonitor.Column.Operation"));
            }
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
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
                ImGui.TableSetColumnIndex(detailLayout ? 0 : 2);
                var nameWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - iconSize.X - ImGui.GetStyle().ItemSpacing.X);
                var nameSize = ImGui.CalcTextSize(definition.Name, false, nameWidth);
                if (!detailLayout)
                    rowContentHeight = MathF.Max(rowContentHeight, nameSize.Y);
                ImGui.TableSetColumnIndex(0);
                OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
                if (DrawReorderHandle(scopeID, order, orderIndex, definition.Name))
                {
                    runtimeChanged = true;
                    changed = true;
                    break;
                }

                OmniControls.NextTableField(enabledLabel, detailLayout);
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

                OmniControls.NextTableField(actionLabel, detailLayout);
                var actionHeight = MathF.Max(rowContentHeight, nameSize.Y);
                FramedGameIcon.Draw(
                    definition.IconID,
                    ImGui.GetCursorScreenPos() + new Vector2(0f, (actionHeight - iconSize.Y) * 0.5f),
                    iconSize,
                    drawFrame: !definition.IsFood,
                    preserveAspectRatio: definition.IsFood);
                ImGui.Dummy(new Vector2(iconSize.X, actionHeight));
                ImGui.SameLine();
                ImGui.GetWindowDrawList().AddText(
                    ImGui.GetFont(), ImGui.GetFontSize(),
                    ImGui.GetCursorScreenPos() +
                    new Vector2(0f, MathF.Max(0f, (actionHeight - nameSize.Y) * 0.5f)),
                    ImGui.GetColorU32(ImGuiCol.Text),
                    definition.Name, nameWidth);
                ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, actionHeight));

                OmniControls.NextTableField(operationLabel, detailLayout);
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

        private bool DrawReorderHandle(uint scopeID, List<uint> order, int orderIndex, string label)
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
                    if (ImGui.SetDragDropPayload(REORDER_PAYLOAD, []))
                    {
                        DraggedScopeID = scopeID;
                        DraggedOrderIndex = orderIndex;
                    }

                    ImGui.TextUnformatted(label);
                }
            }

            using var target = ImRaii.DragDropTarget();
            if (!target)
            {
                return false;
            }

            var payload = ImGui.AcceptDragDropPayload(REORDER_PAYLOAD);
            if (payload.IsNull ||
                !payload.IsDelivery() ||
                DraggedScopeID != scopeID ||
                DraggedOrderIndex < 0 ||
                DraggedOrderIndex == orderIndex)
            {
                return false;
            }

            var configID = order[DraggedOrderIndex];
            order.RemoveAt(DraggedOrderIndex);
            order.Insert(orderIndex, configID);
            DraggedScopeID = uint.MaxValue;
            DraggedOrderIndex = -1;
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
                    definitions[index].MatchesGroup(group) &&
                    (scopeID == 0 || definitions[index].AppliesTo(scopeID)))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool ContainsAction(
            SkillMonitorDefinition[] definitions,
            IReadOnlyList<SkillMonitorConfig.CustomAction> customActions,
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
            using var table = OmniControls.SettingsTable(
                "##skillMonitorVisibilitySettings",
                [ImGui.CalcTextSize(OmniLoc.Get("Feature.SkillMonitor.ShowWhen")),
                    Vector2.Max(OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.SkillMonitor.Active")), OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.SkillMonitor.WeaponSheathed"))),
                    Vector2.Max(OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.SkillMonitor.OnCooldown")), OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.SkillMonitor.OutOfCombat"))),
                    OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.SkillMonitor.OffCooldown"))],
                ["##skillMonitorShowLabel", "##skillMonitorShowActive", "##skillMonitorShowCooldown", "##skillMonitorShowReady"],
                flags: ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.SizingStretchSame);
            if (!table)
            {
                return false;
            }

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
            var iconScaleLabel = OmniLoc.Get("Feature.SkillMonitor.IconScale");
            var iconSpacingLabel = OmniLoc.Get("Feature.SkillMonitor.IconSpacing");
            var offsetLabel = OmniLoc.Get("Feature.SkillMonitor.Offset");
            var iconsPerRowLabel = OmniLoc.Get("Feature.SkillMonitor.IconsPerRow");
            var fontLabel = OmniLoc.Get("Feature.LargeCooldownCounter.Font");
            var fontPreview = LargeCooldownCounter.GetFontName(config.Font);
            var generalPositionLabel = OmniLoc.Get("Feature.SkillMonitor.GeneralPosition");
            var generalPositionPreview = OmniLoc.Get(config.ShowGeneralSkillsFirst
                ? "Feature.SkillMonitor.GeneralPosition.First" : "Feature.SkillMonitor.GeneralPosition.Last");
            var alignmentLabel = OmniLoc.Get("Feature.SkillMonitor.Alignment");
            var alignmentPreview = OmniLoc.Get(config.Alignment == SkillMonitorAlignment.Mirror
                ? "Feature.SkillMonitor.Alignment.Mirror" : "Feature.SkillMonitor.Alignment.Right");
            var iconScale = config.IconScale / SkillMonitorConfig.DEFAULT_ICON_SCALE;
            var iconSpacing = config.IconSpacing;
            var offset = config.Offset - DefaultOffset;
            var scaleSize = OmniControls.MeasureFloatInput(iconScale, "%.2f", OmniTheme.Scale(64f));
            var spacingSize = OmniControls.MeasureFloatInput(iconSpacing, "%.1f", OmniTheme.Scale(64f));
            var offsetSize = OmniControls.MeasureGroup(
                [OmniControls.MeasureFloatInput(offset.X, "%.0f", OmniTheme.Scale(48f)),
                    OmniControls.MeasureFloatInput(offset.Y, "%.0f", OmniTheme.Scale(48f))], ImGui.GetStyle().ItemInnerSpacing.X);
            var countSize = OmniControls.MeasureInput(config.IconsPerRow.ToString(), OmniTheme.Scale(64f));
            using var table = OmniControls.SettingsTable(
                "##skillMonitorLayoutSettings",
                [OmniControls.MeasureGroup([ImGui.CalcTextSize(iconScaleLabel), scaleSize]),
                    OmniControls.MeasureGroup([ImGui.CalcTextSize(iconSpacingLabel), spacingSize]),
                    OmniControls.MeasureGroup([ImGui.CalcTextSize(offsetLabel), offsetSize]),
                    OmniControls.MeasureGroup([ImGui.CalcTextSize(iconsPerRowLabel), countSize]),
                    OmniControls.MeasureGroup([ImGui.CalcTextSize(fontLabel), OmniControls.MeasureCombo(fontPreview)]),
                    OmniControls.MeasureGroup([ImGui.CalcTextSize(generalPositionLabel), OmniControls.MeasureCombo(generalPositionPreview)]),
                    OmniControls.MeasureGroup([ImGui.CalcTextSize(alignmentLabel), OmniControls.MeasureCombo(alignmentPreview)])],
                ["##skillMonitorScale", "##skillMonitorSpacing", "##skillMonitorOffset", "##skillMonitorGeneralPosition",
                    "##skillMonitorFontColumn", "##skillMonitorPositionColumn", "##skillMonitorAlignmentColumn"],
                flags: ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.SizingStretchSame,
                columnsPerRow: 4);
            if (!table)
            {
                return false;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(iconScaleLabel);
            OmniControls.SameLineOrWrap(scaleSize.X);
            if (OmniControls.DragFloat(
                    "##skillMonitorIconScale",
                    ref iconScale,
                    0.05f,
                    0.5f,
                    2f,
                    "%.2f",
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X),
                    ImGuiSliderFlags.AlwaysClamp))
            {
                config.IconScale = iconScale * SkillMonitorConfig.DEFAULT_ICON_SCALE;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(iconSpacingLabel);
            OmniControls.SameLineOrWrap(spacingSize.X);
            if (OmniControls.DragFloat(
                    "##skillMonitorIconSpacing",
                    ref iconSpacing,
                    0.5f,
                    0f,
                    12f,
                    "%.1f",
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X),
                    ImGuiSliderFlags.AlwaysClamp))
            {
                config.IconSpacing = iconSpacing;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(offsetLabel);
            OmniControls.SameLineOrWrap(offsetSize.X);
            if (OmniControls.DragFloat2(
                    "##skillMonitorOffset",
                    ref offset,
                    1f,
                    -500f,
                    500f,
                    "%.0f",
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
            {
                config.Offset = offset + DefaultOffset;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(iconsPerRowLabel);
            OmniControls.SameLineOrWrap(countSize.X);
            var iconsPerRow = config.IconsPerRow;
            if (OmniControls.SliderInt("##skillMonitorIconsPerRow", ref iconsPerRow, 0, 20, "%d",
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
            {
                config.IconsPerRow = Math.Clamp(iconsPerRow, 0, 20);
            }
            changed |= ImGui.IsItemDeactivatedAfterEdit();

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(fontLabel);
            OmniControls.SameLineOrWrap(OmniControls.MeasureCombo(fontPreview).X);
            if (OmniControls.BeginCombo("##skillMonitorFont", fontPreview,
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
            {
                foreach (var font in LargeCooldownCounter.SupportedFonts)
                {
                    if (ImGui.Selectable(LargeCooldownCounter.GetFontName(font), config.Font == font))
                    {
                        config.Font = font;
                        changed = true;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(generalPositionLabel);
            OmniControls.SameLineOrWrap(OmniControls.MeasureCombo(generalPositionPreview).X);
            if (OmniControls.BeginCombo(
                    "##skillMonitorGeneralPosition",
                    generalPositionPreview,
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
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

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(alignmentLabel);
            OmniControls.SameLineOrWrap(OmniControls.MeasureCombo(alignmentPreview).X);
            if (OmniControls.BeginCombo(
                    "##skillMonitorAlignment",
                    alignmentPreview,
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
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


        #region 常量

        private const string REORDER_PAYLOAD = "SkillMonitorReorder";

        #endregion
    }

}