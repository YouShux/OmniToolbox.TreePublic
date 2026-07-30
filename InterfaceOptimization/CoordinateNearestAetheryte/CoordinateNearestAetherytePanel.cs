using OmniToolbox.Config;
using OmniToolbox.Teleport;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class CoordinateNearestAetherytePanel
{
    private const int MaxVisibleRows = 8;

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
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Name"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Map"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Id"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(92f));
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Action"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(86f));
        ImGui.TableSetupScrollFreeze(0, 1);
        OmniControls.BeginTableHeaderRow();
        OmniControls.TableHeader(OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Name"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Map"));
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        var idHeader = OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Id");
        var idHeaderSpacing = OmniTheme.Scale(4f);
        OmniControls.CenterTableItem(
            new Vector2(
                OmniTheme.CheckboxSize() + idHeaderSpacing + ImGui.CalcTextSize(idHeader).X,
                OmniTheme.CheckboxSize()),
            OmniTheme.SmallButtonSize().Y);
        var hasIgnoredAetherytes = config.IgnoredAetheryteIds.Count > 0;
        using (ImRaii.Disabled(!hasIgnoredAetherytes))
        {
            if (OmniControls.Checkbox("##coordinateNearestAetheryteClearIgnored", ref hasIgnoredAetherytes) &&
                !hasIgnoredAetherytes)
            {
                config.IgnoredAetheryteIds.Clear();
                changed = true;
            }
        }

        ImGui.SameLine(0f, idHeaderSpacing);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(idHeader);
        OmniControls.TableHeader(OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Action"));

        for (var pass = 0; pass < 2; pass++)
        {
            var drawIgnored = pass == 0;
            foreach (var aetheryte in aetherytes)
            {
                var ignored = config.IgnoredAetheryteIds.Contains(aetheryte.Id);
                if (ignored != drawIgnored)
                {
                    continue;
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var textColor = ignored
                    ? (Vector4?)null
                    : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
                OmniControls.TableTextCentered(aetheryte.Name, OmniTheme.SmallButtonSize().Y, textColor);
                ImGui.TableNextColumn();
                OmniControls.TableTextCentered(aetheryte.MapName, OmniTheme.SmallButtonSize().Y, textColor);
                ImGui.TableNextColumn();
                OmniControls.TableTextCentered(aetheryte.Id.ToString(), OmniTheme.SmallButtonSize().Y, textColor);

                ImGui.TableNextColumn();
                var actionLabel = OmniLoc.Get(ignored
                    ? "Feature.CoordinateNearestAetheryte.Restore"
                    : "Feature.CoordinateNearestAetheryte.Exclude");
                var actionButtonSize = OmniControls.CompactButtonSize(actionLabel);
                OmniControls.CenterTableItem(actionButtonSize, OmniTheme.SmallButtonSize().Y);
                if (OmniControls.SmallButton(
                        $"{actionLabel}##coordinateNearestAetheryte{aetheryte.Id}",
                        false,
                        actionButtonSize))
                {
                    if (ignored)
                    {
                        config.IgnoredAetheryteIds.Remove(aetheryte.Id);
                    }
                    else
                    {
                        config.IgnoredAetheryteIds.Add(aetheryte.Id);
                    }

                    changed = true;
                }
            }
        }

        return changed;
    }
}
