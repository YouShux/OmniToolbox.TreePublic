using OmniToolbox.Config;
using OmniToolbox.UI;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class BetterUserMacroPanel
{
    public static bool Draw(BetterUserMacroConfig config)
    {
        var changed = false;
        var style = ImGui.GetStyle();
        using var cellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y));
        using var itemSpacing = ImRaii.PushStyle(
            ImGuiStyleVar.ItemSpacing,
            new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
        using var table = ImRaii.Table(
            "##betterUserMacroSettings",
            4,
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##c0", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##c1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##c2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##c3", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var tooltips = config.Tooltips;
        if (DrawCheckbox("Feature.BetterUserMacro.Tooltips", "tooltips", ref tooltips))
        {
            config.Tooltips = tooltips;
            changed = true;
        }

        ImGui.TableNextColumn();
        var lineNumbers = config.LineNumbers;
        if (DrawCheckbox("Feature.BetterUserMacro.LineNumbers", "lineNumbers", ref lineNumbers))
        {
            config.LineNumbers = lineNumbers;
            changed = true;
        }

        ImGui.TableNextColumn();
        var dragSwap = config.DragSwap;
        if (DrawCheckbox("Feature.BetterUserMacro.DragSwap", "dragSwap", ref dragSwap))
        {
            config.DragSwap = dragSwap;
            changed = true;
        }

        ImGui.TableNextColumn();
        var customIcons = config.CustomIcons;
        if (DrawCheckbox("Feature.BetterUserMacro.CustomIcons", "customIcons", ref customIcons))
        {
            config.CustomIcons = customIcons;
            changed = true;
        }

        return changed;
    }

    private static bool DrawCheckbox(string labelKey, string id, ref bool value)
    {
        var changed = OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##betterUserMacro{id}", ref value);
        ImGuiOm.HelpMarker(OmniLoc.Get($"{labelKey}.Help"));
        return changed;
    }
}
