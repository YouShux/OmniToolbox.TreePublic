using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class RaiseDispelEnhancementPanel
{
    public static bool Draw(RaiseDispelEnhancementConfig config)
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##raiseDispelEnhancementOptions",
                   4,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
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
        if (!ImGui.Checkbox($"{OmniLoc.Get(labelKey)}##raiseDispel{id}", ref value))
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
        ImGui.SetNextItemWidth(OmniTheme.Scale(26f));
        if (!ImGui.ColorEdit4(
                $"##raiseDispel{id}",
                ref color,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
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
        ImGui.SetNextItemWidth(OmniTheme.Scale(128f));
        var changed = ImGui.SliderFloat(
            $"##raiseDispel{id}",
            ref value,
            0.3f,
            3f,
            "%.2f");
        ImGui.SameLine(0f, OmniTheme.Scale(8f));
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        if (changed)
        {
            setter(value);
        }

        return changed;
    }
}
