using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class RaiseDispelEnhancementPanel
{
    public static bool Draw(RaiseDispelEnhancementConfig config)
    {
        var changed = false;
        var style = ImGui.GetStyle();
        using (var spacing = ImRaii.PushStyle(
                   ImGuiStyleVar.CellPadding,
                   new Vector2(
                       Math.Clamp(style.CellPadding.X * 0.9f, OmniTheme.Scale(5f), OmniTheme.Scale(11f)),
                       style.CellPadding.Y))
               .Push(
                   ImGuiStyleVar.ItemSpacing,
                   new Vector2(
                       Math.Clamp(style.ItemSpacing.X, OmniTheme.Scale(9f), OmniTheme.Scale(17f)),
                       style.ItemSpacing.Y)))
        using (var table = ImRaii.Table(
                   "##raiseDispelEnhancementOptions",
                   4,
                   ImGuiTableFlags.SizingStretchProp,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##raiseDispelEnhancementOptions0", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##raiseDispelEnhancementOptions1", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##raiseDispelEnhancementOptions2", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##raiseDispelEnhancementOptions3", ImGuiTableColumnFlags.WidthStretch, 1.25f);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    config.ShowRaise,
                    "Feature.RaiseDispelEnhancement.ShowRaise",
                    "showRaise",
                    value => config.ShowRaise = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    config.ShowDispel,
                    "Feature.RaiseDispelEnhancement.ShowDispel",
                    "showDispel",
                    value => config.ShowDispel = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    config.ShowWorldIcon,
                    "Feature.RaiseDispelEnhancement.ShowWorldIcon",
                    "showWorldIcon",
                    value => config.ShowWorldIcon = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    config.ShowWorldText,
                    "Feature.RaiseDispelEnhancement.ShowWorldText",
                    "showWorldText",
                    value => config.ShowWorldText = value);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    config.ShowCasterName,
                    "Feature.RaiseDispelEnhancement.ShowCasterName",
                    "showCasterName",
                    value => config.ShowCasterName = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    config.ShowCastProgress,
                    "Feature.RaiseDispelEnhancement.ShowCastProgress",
                    "showCastProgress",
                    value => config.ShowCastProgress = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    config.ShowRaiseOnList,
                    "Feature.RaiseDispelEnhancement.ShowRaiseOnList",
                    "showRaiseOnList",
                    value => config.ShowRaiseOnList = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    config.ShowDispelOnList,
                    "Feature.RaiseDispelEnhancement.ShowDispelOnList",
                    "showDispelOnList",
                    value => config.ShowDispelOnList = value);
            }
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        changed |= DrawColorEdit(
            config.RaiseListColor,
            "Feature.RaiseDispelEnhancement.RaiseListColor",
            "raiseListColor",
            value => config.RaiseListColor = value);
        ImGui.SameLine(0f, OmniTheme.Scale(24f));
        changed |= DrawColorEdit(
            config.DispelListColor,
            "Feature.RaiseDispelEnhancement.DispelListColor",
            "dispelListColor",
            value => config.DispelListColor = value);

        ImGui.SameLine(0f, OmniTheme.Scale(24f));
        changed |= DrawScaleSlider(
            config.IconScale,
            "Feature.RaiseDispelEnhancement.IconScale",
            "iconScale",
            value => config.IconScale = value);
        ImGui.SameLine(0f, OmniTheme.Scale(24f));
        changed |= DrawScaleSlider(
            config.WorldTextScale,
            "Feature.RaiseDispelEnhancement.WorldTextScale",
            "worldTextScale",
            value => config.WorldTextScale = value);
        return changed;
    }

    private static bool DrawCheckbox(
        bool current,
        string labelKey,
        string id,
        Action<bool> setter)
    {
        var value = current;
        if (!OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##raiseDispel{id}", ref value))
        {
            return false;
        }

        setter(value);
        return true;
    }

    private static bool DrawColorEdit(
        uint current,
        string labelKey,
        string id,
        Action<uint> setter)
    {
        var color = ImGui.ColorConvertU32ToFloat4(current);
        if (!OmniControls.ColorEdit($"##raiseDispel{id}", ref color))
        {
            ImGui.SameLine(0f, OmniTheme.Scale(8f));
            ImGui.TextUnformatted(OmniLoc.Get(labelKey));
            return false;
        }

        setter(ImGui.ColorConvertFloat4ToU32(color));
        ImGui.SameLine(0f, OmniTheme.Scale(8f));
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        return true;
    }

    private static bool DrawScaleSlider(
        float current,
        string labelKey,
        string id,
        Action<float> setter)
    {
        var value = Math.Clamp(current <= 0f ? 1f : current, 0.3f, 3f);
        var changed = OmniControls.SliderFloat(
            $"##raiseDispel{id}",
            ref value,
            0.3f,
            3f,
            "%.2f",
            OmniTheme.Scale(128f));
        ImGui.SameLine(0f, OmniTheme.Scale(8f));
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        if (changed)
        {
            setter(value);
        }

        return changed;
    }
}
