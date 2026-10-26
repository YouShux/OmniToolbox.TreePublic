using System.Collections.Frozen;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Dalamud.Services.Game;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Notifications;
using OmniToolbox.UI.Theme;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace OmniToolbox.TreePublic;

public sealed partial class DuplicateStatusPrevention
{
    private static class Panel
    {
        public static bool Draw(Settings config)
        {
            var changed = false;
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.DuplicateStatusPrevention.OverlapThreshold"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(OmniTheme.Scale(100f));
            var overlapThreshold = config.OverlapThreshold;
            if (OmniControls.InputFloat("##duplicateStatusOverlapThreshold", ref overlapThreshold, 0f, 0f, "%.1f"))
            {
                config.OverlapThreshold = overlapThreshold;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
            OmniControls.HelpIcon(OmniLoc.Get("Feature.DuplicateStatusPrevention.OverlapThreshold.Help"));
            ImGui.SameLine();
            var chatNotify = config.ChatNotify;
            if (OmniControls.Checkbox(
                    OmniLoc.Get("Feature.DuplicateStatusPrevention.ChatNotify"),
                    ref chatNotify))
            {
                config.ChatNotify = chatNotify;
                changed = true;
            }

            ImGui.SameLine();
            var popupNotify = config.PopupNotify;
            if (OmniControls.Checkbox(
                    OmniLoc.Get("Feature.DuplicateStatusPrevention.PopupNotify"),
                    ref popupNotify))
            {
                config.PopupNotify = popupNotify;
                changed = true;
            }

            ImGui.Spacing();

            var enabledCount = 0;
            foreach (var enabled in config.EnabledActions.Values)
            {
                if (enabled)
                {
                    enabledCount++;
                }
            }

            ImGui.TextUnformatted(string.Format(
                OmniLoc.Get("Feature.DuplicateStatusPrevention.ActionCount"),
                enabledCount,
                config.EnabledActions.Count));

            var rowHeight = MathF.Max(ImGui.GetFrameHeightWithSpacing(), OmniTheme.Scale(34f));
            var visibleRows = Math.Clamp(config.EnabledActions.Count, 1, 5);
            using var cellPadding = ImRaii.PushStyle(
                ImGuiStyleVar.CellPadding,
                new Vector2(ImGui.GetStyle().CellPadding.X, OmniTheme.Scale(3f)));
            using var framePadding = ImRaii.PushStyle(
                ImGuiStyleVar.FramePadding,
                new Vector2(ImGui.GetStyle().FramePadding.X, OmniTheme.Scale(3f)));
            var checkboxSize = OmniControls.MeasureCheckbox(string.Empty);
            using var table = OmniControls.DataTable(
                "##duplicateStatusActions",
                [OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Action"), OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Status")],
                [MathF.Max(ImGui.GetFontSize() * 10f, checkboxSize.X * 2f +
                ImGui.CalcTextSize(OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Action")).X), ImGui.GetFontSize() * 6f],
                [ImGui.GetFontSize() * 14f, ImGui.GetFontSize() * 10f], out var detailLayout,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchSame,
                new Vector2(
                    ImGui.GetContentRegionAvail().X,
                    rowHeight * (visibleRows + 1) + ImGui.GetStyle().ItemSpacing.Y), stretchColumn: 0);
            if (!table)
            {
                return changed;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            var headerHeight = MathF.Max(checkboxSize.Y, ImGui.GetFrameHeight());
            OmniControls.BeginTableHeaderRow(headerHeight);
            OmniControls.TableHeader(OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Action"), headerHeight);
            ImGui.SetCursorScreenPos(ImGui.GetItemRectMin());
            OmniControls.CenterTableItem(new Vector2(ImGui.GetContentRegionAvail().X, checkboxSize.Y), headerHeight);
            var allEnabled = config.EnabledActions.Count > 0 && enabledCount == config.EnabledActions.Count;
            if (OmniControls.Checkbox("##duplicateStatusAllActions", ref allEnabled))
            {
                foreach (var action in Rules.OrderedActions)
                {
                    config.EnabledActions[action.Key] = allEnabled;
                }

                changed = true;
            }

            if (!detailLayout)
            {
                OmniControls.TableHeader(OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Status"), headerHeight);
            }

            foreach (var action in Rules.OrderedActions)
            {
                if (!LuminaGetter.TryGetRow<LuminaAction>(action.Key, out var actionRow))
                {
                    continue;
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var enabled = config.EnabledActions[action.Key];
                if (OmniControls.Checkbox($"##duplicateStatusAction{action.Key}", ref enabled))
                {
                    config.EnabledActions[action.Key] = enabled;
                    changed = true;
                }

                ImGui.SameLine();
                DrawAction(actionRow);

                OmniControls.NextTableField(OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Status"), detailLayout);
                var first = true;
                foreach (var status in action.Value.Statuses)
                {
                    DrawStatus(status, ref first);
                }

                if (action.Value.SecondStatuses is not null)
                {
                    foreach (var status in action.Value.SecondStatuses)
                    {
                        DrawStatus(status, ref first);
                    }
                }
            }

            return changed;
        }

        private static void DrawAction(LuminaAction action)
        {
            var iconSize = new Vector2(MathF.Max(
                OmniTheme.Scale(24f),
                ImGui.GetTextLineHeightWithSpacing() + OmniTheme.Scale(6f)));
            FramedGameIcon.DrawAction(action, iconSize);
            ImGui.SameLine();

            ImGui.AlignTextToFramePadding();
            ImGui.TextWrapped(action.Name.ToString());
        }

        private static void DrawStatus(StatusRule status, ref bool first)
        {
            if (!LuminaGetter.TryGetRow<LuminaStatus>(status.StatusID, out var statusRow))
            {
                return;
            }

            var iconSize = OmniTheme.StatusIconSize(MathF.Max(
                OmniTheme.Scale(24f),
                ImGui.GetTextLineHeightWithSpacing() + OmniTheme.Scale(10f)));
            if (!first && ImGui.GetContentRegionAvail().X >= iconSize.X + ImGui.GetStyle().ItemSpacing.X)
            {
                ImGui.SameLine();
            }

            if (ImageHelper.GetGameIcon(statusRow.Icon) is { } icon)
            {
                ImGui.Image(icon.Handle, iconSize);
            }
            else
            {
                ImGui.TextUnformatted(statusRow.Name.ToString());
            }

            if (ImGui.IsItemHovered())
            {
                OmniControls.HelpTooltip(string.Format(
                    OmniLoc.Get("Feature.DuplicateStatusPrevention.StatusTooltip"),
                    statusRow.Name,
                    OmniLoc.Get(status.DetectionTarget switch
                    {
                        StatusDetectionTarget.Self => "Feature.DuplicateStatusPrevention.Target.Self",
                        StatusDetectionTarget.Member => "Feature.DuplicateStatusPrevention.Target.Member",
                        _ => "Feature.DuplicateStatusPrevention.Target.Target"
                    })));
            }

            first = false;
        }
    }
}