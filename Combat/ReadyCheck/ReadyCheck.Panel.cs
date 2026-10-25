using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using TreeTools.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using ContentFinderCondition = Lumina.Excel.Sheets.ContentFinderCondition;

namespace OmniToolbox.TreePublic;

public sealed partial class ReadyCheck
{
    private static class Panel
    {
        public static bool Draw(ReadyCheckConfig config)
        {
            var changed = DrawClearSettings(config);
            ImGui.Spacing();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.ReadyCheck.IconSettings"));
            var listWidth = MathF.Max(ImGui.CalcTextSize(OmniLoc.Get("Feature.ReadyCheck.PartyList")).X,
                MathF.Max(ImGui.CalcTextSize(OmniLoc.Get("Feature.ReadyCheck.AllianceList")).X,
                    ImGui.CalcTextSize(OmniLoc.Get("Feature.ReadyCheck.CrossWorldAllianceList")).X));
            var offsetWidth = OmniControls.MeasureInput("-100").X * 2f + ImGui.GetStyle().ItemInnerSpacing.X;
            var scaleWidth = OmniControls.MeasureFloatInput(5f, "%.2f").X;
            using var table = OmniControls.DataTable(
                "##readyCheckIconSettings",
                [OmniLoc.Get("Feature.ReadyCheck.List"), OmniLoc.Get("Feature.ReadyCheck.Offset"), OmniLoc.Get("Feature.ReadyCheck.Scale")],
                [listWidth, offsetWidth, scaleWidth], [listWidth, offsetWidth, scaleWidth], out var detailLayout,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                secondStretchColumn: 2, size: new Vector2(ImGui.GetContentRegionAvail().X, 0f), stretchColumn: 1);
            if (!table)
            {
                return changed;
            }

            if (!detailLayout)
            {
                OmniControls.BeginTableHeaderRow();
                OmniControls.TableHeader(OmniLoc.Get("Feature.ReadyCheck.List"));
                OmniControls.TableHeader(OmniLoc.Get("Feature.ReadyCheck.Offset"));
                OmniControls.TableHeader(OmniLoc.Get("Feature.ReadyCheck.Scale"));
            }
            var partyOffset = config.PartyIconOffset;
            var partyScale = config.PartyIconScale;
            changed |= DrawIconSettings(
                "party",
                "Feature.ReadyCheck.PartyList",
                ref partyOffset,
                ref partyScale);
            config.PartyIconOffset = partyOffset;
            config.PartyIconScale = partyScale;

            var allianceOffset = config.AllianceIconOffset;
            var allianceScale = config.AllianceIconScale;
            changed |= DrawIconSettings(
                "alliance",
                "Feature.ReadyCheck.AllianceList",
                ref allianceOffset,
                ref allianceScale);
            config.AllianceIconOffset = allianceOffset;
            config.AllianceIconScale = allianceScale;

            var crossWorldAllianceOffset = config.CrossWorldAllianceIconOffset;
            var crossWorldAllianceScale = config.CrossWorldAllianceIconScale;
            changed |= DrawIconSettings(
                "crossWorldAlliance",
                "Feature.ReadyCheck.CrossWorldAllianceList",
                ref crossWorldAllianceOffset,
                ref crossWorldAllianceScale);
            config.CrossWorldAllianceIconOffset = crossWorldAllianceOffset;
            config.CrossWorldAllianceIconScale = crossWorldAllianceScale;
            return changed;
        }

        private static bool DrawClearSettings(ReadyCheckConfig config)
        {
            var changed = false;
            var secondsInputWidth = ImGui.CalcTextSize("000").X + ImGui.GetStyle().FramePadding.X * 4f;
            using var table = OmniControls.SettingsTable(
                "##readyCheckClearSettings",
                [OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.ReadyCheck.ClearOnTerritoryChanged")),
                OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.ReadyCheck.ClearOnCombat")),
                OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.ReadyCheck.ClearOnInstancedCombat")),
                OmniControls.MeasureGroup([OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.ReadyCheck.ClearAfterTime")),
                    OmniControls.MeasureInput("900", secondsInputWidth),
                    ImGui.CalcTextSize(OmniLoc.Get("Feature.ReadyCheck.Seconds"))],
                    MathF.Max(ImGui.GetStyle().ItemSpacing.X, OmniTheme.Scale(10f)))],
                ["##readyCheckClearTerritory", "##readyCheckClearCombat", "##readyCheckClearDutyCombat", "##readyCheckClearTime"],
                weights: [1f, 1f, 1f, 1.25f], flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 4);
            if (!table)
            {
                return false;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (DrawCheckbox(
                    "Feature.ReadyCheck.ClearOnTerritoryChanged",
                    "territory",
                    config.ClearOnTerritoryChanged,
                    out var clearOnTerritoryChanged))
            {
                config.ClearOnTerritoryChanged = clearOnTerritoryChanged;
                changed = true;
            }

            ImGui.TableNextColumn();
            if (DrawCheckbox(
                    "Feature.ReadyCheck.ClearOnCombat",
                    "combat",
                    config.ClearOnCombat,
                    out var clearOnCombat))
            {
                config.ClearOnCombat = clearOnCombat;
                changed = true;
            }

            ImGui.TableNextColumn();
            if (DrawCheckbox(
                    "Feature.ReadyCheck.ClearOnInstancedCombat",
                    "instancedCombat",
                    config.ClearOnInstancedCombat,
                    out var clearOnInstancedCombat))
            {
                config.ClearOnInstancedCombat = clearOnInstancedCombat;
                changed = true;
            }

            ImGui.TableNextColumn();
            if (DrawCheckbox(
                    "Feature.ReadyCheck.ClearAfterTime",
                    "time",
                    config.ClearAfterTime,
                    out var clearAfterTime))
            {
                config.ClearAfterTime = clearAfterTime;
                changed = true;
            }

            OmniControls.SameLineOrWrap(OmniControls.MeasureInput("900", secondsInputWidth).X +
                ImGui.CalcTextSize(OmniLoc.Get("Feature.ReadyCheck.Seconds")).X + ImGui.GetStyle().ItemSpacing.X, OmniTheme.Scale(10f));
            using (ImRaii.Disabled(!config.ClearAfterTime))
            {
                ImGui.SetNextItemWidth(secondsInputWidth);
                var seconds = Math.Clamp(config.ClearAfterSeconds, 30, 900);
                if (OmniControls.InputInt("##readyCheckClearAfterSeconds", ref seconds, 0, 0))
                {
                    config.ClearAfterSeconds = Math.Clamp(seconds, 30, 900);
                }

                changed |= ImGui.IsItemDeactivatedAfterEdit();
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.ReadyCheck.Seconds"));
            }

            return changed;
        }

        private static bool DrawCheckbox(
            string labelKey,
            string id,
            bool current,
            out bool value)
        {
            value = current;
            return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##readyCheckClear{id}", ref value);
        }

        private static bool DrawIconSettings(
            string id,
            string labelKey,
            ref Vector2 offset,
            ref float scale)
        {
            var changed = false;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var detailLayout = ImGui.TableGetColumnCount() == 1;
            var label = OmniLoc.Get(labelKey);
            var inputHeight = OmniControls.MeasureInput(string.Empty).Y;
            var rowContentHeight = MathF.Max(inputHeight,
                ImGui.CalcTextSize(label, false, MathF.Max(1f, ImGui.GetContentRegionAvail().X)).Y);
            OmniControls.TableTextCentered(label, rowContentHeight);
            OmniControls.NextTableField(OmniLoc.Get("Feature.ReadyCheck.Offset"), detailLayout);
            OmniControls.CenterTableItem(new Vector2(ImGui.GetContentRegionAvail().X, inputHeight), rowContentHeight);
            OmniControls.DragFloat2(
                $"##readyCheck{id}Offset",
                ref offset,
                1f,
                -100f,
                100f,
                "%.0f",
                ImGui.GetContentRegionAvail().X);

            changed |= ImGui.IsItemDeactivatedAfterEdit();
            OmniControls.NextTableField(OmniLoc.Get("Feature.ReadyCheck.Scale"), detailLayout);
            OmniControls.CenterTableItem(new Vector2(ImGui.GetContentRegionAvail().X, inputHeight), rowContentHeight);
            OmniControls.DragFloat(
                $"##readyCheck{id}Scale",
                ref scale,
                0.05f,
                0.3f,
                5f,
                "%.2f",
                ImGui.GetContentRegionAvail().X,
                ImGuiSliderFlags.AlwaysClamp);

            changed |= ImGui.IsItemDeactivatedAfterEdit();
            return changed;
        }
    }
}