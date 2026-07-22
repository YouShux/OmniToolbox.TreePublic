using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class AutoStorePanel
{
    public static bool Draw(
        AutoStoreConfig config,
        AutoStoreHotkey hotkey,
        bool isAvailable,
        Func<bool> openList)
    {
        var changed = DrawDefaultTarget(config);
        ImGui.SameLine(0f, OmniTheme.Scale(8f));
        using (ImRaii.Disabled(!isAvailable))
        {
            if (ImGui.Button(OmniLoc.Get("Feature.AutoStore.List.Open")))
            {
                openList();
            }
        }

        ImGui.Spacing();
        changed |= DrawHotkey(config, hotkey);
        ImGui.Spacing();
        changed |= DrawWindowSize(config);
        return changed;
    }

    private static bool DrawDefaultTarget(AutoStoreConfig config)
    {
        var changed = false;
        ImGui.SetNextItemWidth(MathF.Min(OmniTheme.Scale(220f), ImGui.GetContentRegionAvail().X));
        if (!ImGui.BeginCombo(
                $"{OmniLoc.Get("Feature.AutoStore.DefaultTarget")}##autoStoreDefaultTarget",
                AutoStoreListOptions.GetTargetLabel(config.DefaultStoreTarget)))
        {
            return false;
        }

        foreach (var target in AutoStoreListOptions.Targets)
        {
            var selected = target == config.DefaultStoreTarget;
            if (ImGui.Selectable(
                    $"{AutoStoreListOptions.GetTargetLabel(target)}##autoStoreDefaultTarget{target}",
                    selected))
            {
                config.DefaultStoreTarget = target;
                changed = true;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static bool DrawHotkey(AutoStoreConfig config, AutoStoreHotkey hotkey)
    {
        var changed = false;
        ImGui.SetNextItemWidth(OmniTheme.Scale(90f));
        if (ImGui.BeginCombo(
                "##autoStoreListHotkeyModifier",
                AutoStoreListOptions.GetModifierLabel(config.ListHotkeyModifier)))
        {
            foreach (var modifier in AutoStoreListOptions.Modifiers)
            {
                if (ImGui.Selectable(
                        $"{AutoStoreListOptions.GetModifierLabel(modifier)}##autoStoreListModifier{modifier}",
                        modifier == config.ListHotkeyModifier))
                {
                    config.ListHotkeyModifier = modifier;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.AutoStore.Hotkey.Modifier"));
        ImGui.SameLine();
        if (ImGui.Button(
                $"{(hotkey.IsCapturing ? OmniLoc.Get("Feature.AutoStore.Hotkey.Capturing") : AutoStoreHotkey.Format(config))}##autoStoreListHotkey",
                new(OmniTheme.Scale(128f), 0f)))
        {
            hotkey.StartCapture();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.AutoStore.Hotkey.Key"));
        ImGui.SameLine();
        if (ImGui.Button($"{OmniLoc.Get("Feature.AutoStore.Hotkey.Clear")}##autoStoreListHotkeyClear"))
        {
            changed |= config.ListHotkey != 0 || config.ListHotkeyModifier != AutoStoreHotkeyModifier.None;
            config.ListHotkey = 0;
            config.ListHotkeyModifier = AutoStoreHotkeyModifier.None;
            hotkey.Reset();
        }

        return hotkey.Capture() || changed;
    }

    private static bool DrawWindowSize(AutoStoreConfig config)
    {
        var changed = false;
        var width = (int)MathF.Round(config.ListWindowSize.X);
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        if (ImGui.InputInt(
                $"{OmniLoc.Get("Feature.AutoStore.List.Width")}##autoStoreListWidth",
                ref width,
                0,
                0))
        {
            config.ListWindowSize = new(Math.Clamp(width, 500, 1000), config.ListWindowSize.Y);
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine(0f, OmniTheme.Scale(18f));
        var height = (int)MathF.Round(config.ListWindowSize.Y);
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        if (ImGui.InputInt(
                $"{OmniLoc.Get("Feature.AutoStore.List.Height")}##autoStoreListHeight",
                ref height,
                0,
                0))
        {
            config.ListWindowSize = new(config.ListWindowSize.X, Math.Clamp(height, 360, 900));
        }

        return ImGui.IsItemDeactivatedAfterEdit() || changed;
    }
}
