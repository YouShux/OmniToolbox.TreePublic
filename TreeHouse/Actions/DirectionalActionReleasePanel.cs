using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace OmniToolbox.TreeHouse.Actions;

internal static class DirectionalActionReleasePanel
{
    public static bool Draw(DirectionalActionReleaseConfig config, ReadOnlySpan<uint> actionIds)
    {
        var changed = false;
        var rowHeight = MathF.Max(ImGui.GetFrameHeightWithSpacing(), OmniTheme.Scale(30f));
        using var table = ImRaii.Table(
            "##directionalActions",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(ImGui.GetContentRegionAvail().X, rowHeight * 9f + ImGui.GetStyle().ItemSpacing.Y));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DirectionalActionRelease.Camera"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(54f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DirectionalActionRelease.Mouse"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(54f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DirectionalActionRelease.Action"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DirectionalActionRelease.Reverse"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(54f));
        ImGui.TableHeadersRow();

        foreach (var actionId in actionIds)
        {
            if (!LuminaGetter.TryGetRow<LuminaAction>(actionId, out var action))
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var camera = config.Actions[actionId] == DirectionalActionMode.Camera;
            if (ImGui.Checkbox($"##directionalCamera{actionId}", ref camera))
            {
                config.Actions[actionId] = camera ? DirectionalActionMode.Camera : DirectionalActionMode.Disabled;
                changed = true;
            }

            ImGui.TableNextColumn();
            var mouse = config.Actions[actionId] == DirectionalActionMode.Mouse;
            if (ImGui.Checkbox($"##directionalMouse{actionId}", ref mouse))
            {
                config.Actions[actionId] = mouse ? DirectionalActionMode.Mouse : DirectionalActionMode.Disabled;
                changed = true;
            }

            ImGui.TableNextColumn();
            DrawAction(action);

            ImGui.TableNextColumn();
            var reversed = config.ReversedActions.Contains(actionId);
            if (ImGui.Checkbox($"##directionalReverse{actionId}", ref reversed))
            {
                if (reversed)
                {
                    config.ReversedActions.Add(actionId);
                }
                else
                {
                    config.ReversedActions.Remove(actionId);
                }

                changed = true;
            }
        }

        return changed;
    }

    private static void DrawAction(LuminaAction action)
    {
        if (ImageHelper.GetGameIcon(action.Icon) is { } icon)
        {
            ImGui.Image(icon.Handle, new Vector2(OmniTheme.Scale(24f)));
            ImGui.SameLine();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(action.Name.ToString());
    }
}
