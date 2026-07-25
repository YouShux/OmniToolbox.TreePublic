using OmenTools.ImGuiOm;
using OmniToolbox.Config;
using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.Actions;

internal static class InfiniteSprintPanel
{
    public static bool Draw(InfiniteSprintConfig config)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.InfiniteSprint.Interval"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Max(
            ImGui.GetFrameHeight(),
            ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeightWithSpacing()));

        var intervalSeconds = Math.Clamp(config.IntervalMs, 0, InfiniteSprintFeature.MaxIntervalMs) / 1000f;
        if (OmniControls.SliderFloat(
                "##infiniteSprintInterval",
                ref intervalSeconds,
                0f,
                InfiniteSprintFeature.MaxIntervalMs / 1000f,
                intervalSeconds == 0f ? OmniLoc.Get("Feature.InfiniteSprint.NoInterval") : "%.1fs",
                ImGui.GetContentRegionAvail().X))
        {
            config.IntervalMs = Math.Clamp(
                (int)(intervalSeconds * 1000f),
                0,
                InfiniteSprintFeature.MaxIntervalMs);
        }

        var changed = ImGui.IsItemDeactivatedAfterEdit();
        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.InfiniteSprint.Help"));
        return changed;
    }
}
