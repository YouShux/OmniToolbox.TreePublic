using OmniToolbox.Config;
using OmniToolbox.UI;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class BetterUserMacroPanel
{
    public static bool Draw(BetterUserMacroConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##betterUserMacroSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

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
        var changed = ImGui.Checkbox($"{OmniLoc.Get(labelKey)}##betterUserMacro{id}", ref value);
        ImGuiOm.HelpMarker(OmniLoc.Get($"{labelKey}.Help"));
        return changed;
    }
}
