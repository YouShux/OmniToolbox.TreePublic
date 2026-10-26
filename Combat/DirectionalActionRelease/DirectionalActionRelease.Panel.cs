using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using OmenTools;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace OmniToolbox.TreePublic;

public sealed partial class DirectionalActionRelease
{
    private static class Panel
    {
        public static bool Draw(Settings config, ReadOnlySpan<uint> actionIDs)
        {
            var changed = false;

            using var cellPadding = ImRaii.PushStyle(
                ImGuiStyleVar.CellPadding,
                new Vector2(ImGui.GetStyle().CellPadding.X, OmniTheme.Scale(3f)));
            using var framePadding = ImRaii.PushStyle(
                ImGuiStyleVar.FramePadding,
                new Vector2(ImGui.GetStyle().FramePadding.X, OmniTheme.Scale(3f)));
            var iconSize = new Vector2(MathF.Max(
                OmniTheme.Scale(24f),
                ImGui.GetTextLineHeightWithSpacing() + OmniTheme.Scale(6f)));
            var checkboxSize = OmniControls.MeasureCheckbox(string.Empty);
            var rowHeight = MathF.Max(iconSize.Y, checkboxSize.Y);
            var toggleWidth = MathF.Max(checkboxSize.X, ImGui.GetFontSize() * 3f);
            using var table = OmniControls.DataTable(
                "##directionalActions",
                [OmniLoc.Get("Feature.DirectionalActionRelease.Camera"), OmniLoc.Get("Feature.DirectionalActionRelease.Mouse"),
                OmniLoc.Get("Feature.DirectionalActionRelease.Reverse"), OmniLoc.Get("Feature.DirectionalActionRelease.Action")],
                [toggleWidth, toggleWidth, toggleWidth, ImGui.GetFontSize() * 8f],
                [toggleWidth, toggleWidth, toggleWidth, ImGui.GetFontSize() * 12f], out var detailLayout,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(ImGui.GetContentRegionAvail().X,
                    rowHeight * 6f + ImGui.GetStyle().ItemSpacing.Y), stretchColumn: 3);
            if (!table)
            {
                return false;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            OmniControls.BeginTableHeaderRow(rowHeight);
            OmniControls.TableHeader(OmniLoc.Get("Feature.DirectionalActionRelease.Camera"), rowHeight);
            if (!detailLayout)
            {
                OmniControls.TableHeader(OmniLoc.Get("Feature.DirectionalActionRelease.Mouse"), rowHeight);
                OmniControls.TableHeader(OmniLoc.Get("Feature.DirectionalActionRelease.Reverse"), rowHeight);
                OmniControls.TableHeader(OmniLoc.Get("Feature.DirectionalActionRelease.Action"), rowHeight);
            }

            var actionNameWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - iconSize.X - ImGui.GetStyle().ItemSpacing.X);

            foreach (var actionID in actionIDs)
            {
                if (!LuminaGetter.TryGetRow<LuminaAction>(actionID, out var action))
                {
                    continue;
                }

                var rowContentHeight = MathF.Max(rowHeight, ImGui.CalcTextSize(action.Name.ToString(), false, actionNameWidth).Y);
                ImGui.TableNextRow(ImGuiTableRowFlags.None, detailLayout ? 0f : rowContentHeight);
                OmniControls.NextTableField(OmniLoc.Get("Feature.DirectionalActionRelease.Camera"), detailLayout);
                OmniControls.CenterTableItem(checkboxSize, rowContentHeight);
                var camera = config.Actions[actionID] == DirectionalActionMode.Camera;
                if (OmniControls.Checkbox($"##directionalCamera{actionID}", ref camera))
                {
                    config.Actions[actionID] = camera ? DirectionalActionMode.Camera : DirectionalActionMode.Disabled;
                    changed = true;
                }

                OmniControls.NextTableField(OmniLoc.Get("Feature.DirectionalActionRelease.Mouse"), detailLayout);
                OmniControls.CenterTableItem(checkboxSize, rowContentHeight);
                var mouse = config.Actions[actionID] == DirectionalActionMode.Mouse;
                if (OmniControls.Checkbox($"##directionalMouse{actionID}", ref mouse))
                {
                    config.Actions[actionID] = mouse ? DirectionalActionMode.Mouse : DirectionalActionMode.Disabled;
                    changed = true;
                }

                OmniControls.NextTableField(OmniLoc.Get("Feature.DirectionalActionRelease.Reverse"), detailLayout);
                OmniControls.CenterTableItem(checkboxSize, rowContentHeight);
                var reversed = config.ReversedActions.Contains(actionID);
                if (OmniControls.Checkbox($"##directionalReverse{actionID}", ref reversed))
                {
                    if (reversed)
                    {
                        config.ReversedActions.Add(actionID);
                    }
                    else
                    {
                        config.ReversedActions.Remove(actionID);
                    }

                    changed = true;
                }

                OmniControls.NextTableField(OmniLoc.Get("Feature.DirectionalActionRelease.Action"), detailLayout);
                DrawAction(action, iconSize, rowContentHeight);
            }

            return changed;
        }

        private static void DrawAction(LuminaAction action, Vector2 iconSize, float rowContentHeight)
        {
            var rowTop = ImGui.GetCursorPosY();
            OmniControls.CenterTableItem(new Vector2(ImGui.GetContentRegionAvail().X, iconSize.Y), rowContentHeight);
            FramedGameIcon.DrawAction(action, iconSize);
            ImGui.SameLine();
            ImGui.SetCursorPosY(rowTop);
            OmniControls.TableTextCentered(action.Name.ToString(), rowContentHeight, null, false);
        }
    }
}