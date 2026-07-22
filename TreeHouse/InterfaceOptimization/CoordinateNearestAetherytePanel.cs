using OmniToolbox.Config;
using OmniToolbox.Teleport;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class CoordinateNearestAetherytePanel
{
    private const int MaxVisibleRows = 14;

    public static bool Draw(
        CoordinateNearestAetheryteConfig config,
        IReadOnlyList<AetheryteDestination> aetherytes)
    {
        var changed = false;
        var ignoredCount = 0;
        foreach (var aetheryte in aetherytes)
        {
            if (config.IgnoredAetheryteIds.Contains(aetheryte.Id))
            {
                ignoredCount++;
            }
        }

        ImGui.TextDisabled(string.Format(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.IgnoredCount"),
            ignoredCount));
        ImGui.SameLine(0f, OmniTheme.Scale(20f));
        if (ImGui.Button(OmniLoc.Get("Feature.CoordinateNearestAetheryte.ClearIgnored")))
        {
            changed |= config.IgnoredAetheryteIds.Count > 0;
            config.IgnoredAetheryteIds.Clear();
        }

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

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Id"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(52f));
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Name"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Map"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Action"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(86f));
        ImGui.TableHeadersRow();

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
                if (!ignored)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                }

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(aetheryte.Id.ToString());
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(aetheryte.Name);
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(aetheryte.MapName);

                if (!ignored)
                {
                    ImGui.PopStyleColor();
                }

                ImGui.TableNextColumn();
                if (ImGui.Button(
                        $"{OmniLoc.Get(ignored
                            ? "Feature.CoordinateNearestAetheryte.Restore"
                            : "Feature.CoordinateNearestAetheryte.Exclude")}##coordinateNearestAetheryte{aetheryte.Id}"))
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
