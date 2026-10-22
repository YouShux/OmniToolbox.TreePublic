using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class HotbarAdjustments
{
    public override bool DrawSettings()
    {
        var changed = false;
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var modifierSize = OmniControls.MeasureCombo(GetModifierName(config.ModifierKey), OmniTheme.Scale(140f));
        var opacityLabel = OmniLoc.Get("Feature.HotbarAdjustments.BackgroundOpacity");
        using var table = OmniControls.SettingsTable(
            "##hotbarAdjustmentsOptions",
            [
                OmniControls.MeasureGroup(
                    [OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.HotbarAdjustments.HideBackground")),
                     OmniControls.HelpIconSize()]),
                OmniControls.MeasureGroup(
                    [ImGui.CalcTextSize(opacityLabel), OmniTheme.Scale(new Vector2(100f, 0f))], spacing),
                OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.HotbarAdjustments.HideLocks")),
                OmniControls.MeasureGroup(
                    [ImGui.CalcTextSize(OmniLoc.Get("Feature.HotbarAdjustments.Modifier")),
                     modifierSize, OmniControls.HelpIconSize()], spacing)
            ],
            ["##hotbarBackground", "##hotbarBackgroundOpacity", "##hotbarLocks", "##hotbarLockModifier"],
            weights: [1f, 1.5f, 1f, 2f],
            flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            columnsPerRow: 4);
        if (!table)
            return false;

        using var rowStyle = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(
                ImGui.GetStyle().FramePadding.X,
                MathF.Max(0f, (OmniTheme.CheckboxSize() - ImGui.GetTextLineHeight()) * 0.5f)));
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        changed |= OmniControls.Checkbox(
            $"{OmniLoc.Get("Feature.HotbarAdjustments.HideBackground")}##hotbarHideBackground",
            ref config.HideBackground);
        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.HotbarAdjustments.HideBackground.Help"));

        ImGui.TableNextColumn();
        using (ImRaii.Disabled(!config.HideBackground))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(opacityLabel);
            OmniControls.SameLineOrWrap(OmniTheme.Scale(100f), spacing);
            OmniControls.SliderFloat("##hotbarBackgroundOpacity", ref config.BackgroundOpacity,
                0f, 1f, "%.2f", MathF.Max(1f, ImGui.GetContentRegionAvail().X));
            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }

        ImGui.TableNextColumn();
        changed |= OmniControls.Checkbox(
            $"{OmniLoc.Get("Feature.HotbarAdjustments.HideLocks")}##hotbarHideLocks",
            ref config.HideLocks);

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.HotbarAdjustments.Modifier"));
        OmniControls.SameLineOrWrap(modifierSize.X + OmniControls.HelpIconSize().X + spacing, spacing);
        using (var combo = OmniControls.Combo(
                   "##hotbarLockModifier",
                   GetModifierName(config.ModifierKey),
                   MathF.Min(
                       modifierSize.X,
                       MathF.Max(1f, ImGui.GetContentRegionAvail().X - OmniControls.HelpIconSize().X - spacing))))
        {
            if (combo)
            {
                for (var modifierKey = 0; modifierKey < 3; modifierKey++)
                {
                    if (!ImGui.Selectable(GetModifierName(modifierKey), config.ModifierKey == modifierKey))
                        continue;

                    config.ModifierKey = modifierKey;
                    changed = true;
                }
            }
        }

        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X, spacing);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.HotbarAdjustments.Modifier.Help"));

        if (changed && IsEnabled)
            UpdateAllHotbars(false);

        return changed;
    }

    private static string GetModifierName(int modifierKey) => OmniLoc.Get(modifierKey switch
    {
        1 => "Feature.HotbarAdjustments.Modifier.Alt",
        2 => "Feature.HotbarAdjustments.Modifier.Control",
        _ => "Feature.HotbarAdjustments.Modifier.Shift"
    });
}
