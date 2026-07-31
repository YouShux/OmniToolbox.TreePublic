using OmniToolbox.Config;
using OmniToolbox.Teleport;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class CoordinateNearestAetherytePanel
{
    private const int MaxVisibleRows = 5;

    public static bool Draw(
        CoordinateNearestAetheryteConfig config,
        IReadOnlyList<AetheryteDestination> aetherytes)
    {
        var changed = false;
        var rowHeight = MathF.Max(ImGui.GetFrameHeightWithSpacing(), OmniTheme.Scale(30f));
        using var table = ImRaii.Table(
            "##coordinateNearestAetheryteTable",
            4,
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                rowHeight * (Math.Min(aetherytes.Count, MaxVisibleRows) + 1) + ImGui.GetStyle().ItemSpacing.Y));
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupColumn(
            "##coordinateNearestAetheryteVisible",
            ImGuiTableColumnFlags.WidthFixed,
            ImGui.GetFrameHeight());
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Name"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Map"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Id"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(92f));
        ImGui.TableSetupScrollFreeze(0, 1);
        OmniControls.BeginTableHeaderRow();
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), OmniTheme.SmallButtonSize().Y);
        var selectAll = config.IgnoredAetheryteIds.Count == 0;
        if (OmniControls.Checkbox("##coordinateNearestAetheryteSelectAll", ref selectAll))
        {
            if (selectAll)
            {
                config.IgnoredAetheryteIds.Clear();
            }
            else
            {
                foreach (var aetheryte in aetherytes)
                {
                    config.IgnoredAetheryteIds.Add(aetheryte.Id);
                }
            }

            changed = true;
        }

        OmniControls.TableHeader(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Name"),
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Name.Help"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Map"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Id"));

        foreach (var aetheryte in aetherytes)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), OmniTheme.SmallButtonSize().Y);
            var visible = !config.IgnoredAetheryteIds.Contains(aetheryte.Id);
            if (OmniControls.Checkbox($"##coordinateNearestAetheryte{aetheryte.Id}", ref visible))
            {
                if (visible)
                {
                    config.IgnoredAetheryteIds.Remove(aetheryte.Id);
                }
                else
                {
                    config.IgnoredAetheryteIds.Add(aetheryte.Id);
                }

                changed = true;
            }

            var textColor = visible
                ? (Vector4?)null
                : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(aetheryte.Name, OmniTheme.SmallButtonSize().Y, textColor);
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(aetheryte.MapName, OmniTheme.SmallButtonSize().Y, textColor);
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(aetheryte.Id.ToString(), OmniTheme.SmallButtonSize().Y, textColor);
        }

        return changed;
    }
}
