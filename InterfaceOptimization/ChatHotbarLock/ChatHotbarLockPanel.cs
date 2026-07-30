using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class ChatHotbarLockPanel
{
    public static bool Draw(ChatHotbarLockConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##chatHotbarLockOptions",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##chatHotbarLockShowChatLock", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##chatHotbarLockHideLocks", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##chatHotbarLockModifierLabel", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##chatHotbarLockModifier", ImGuiTableColumnFlags.WidthStretch, 1f);
        using var rowStyle = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(
                ImGui.GetStyle().FramePadding.X,
                MathF.Max(0f, (OmniTheme.CheckboxSize() - ImGui.GetTextLineHeight()) * 0.5f)));
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var showChatLock = config.ShowChatLock;
        if (OmniControls.Checkbox(
                $"{OmniLoc.Get("Feature.ChatHotbarLock.ShowChatLock")}##chatHotbarLockShowChatLock",
                ref showChatLock))
        {
            config.ShowChatLock = showChatLock;
            if (!showChatLock)
            {
                config.ChatLocked = false;
            }

            changed = true;
        }

        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.ChatHotbarLock.ShowChatLock.Help"));

        ImGui.TableNextColumn();
        var hideLocks = config.HideLocks;
        if (OmniControls.Checkbox(
                $"{OmniLoc.Get("Feature.ChatHotbarLock.HideLocks")}##chatHotbarLockHideLocks",
                ref hideLocks))
        {
            config.HideLocks = hideLocks;
            changed = true;
        }

        var modifierKey = config.ModifierKey is 0 or 1 or 2 ? config.ModifierKey : 0;
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ChatHotbarLock.Modifier"));

        ImGui.TableNextColumn();
        if (OmniControls.BeginCombo(
                "##chatHotbarLockModifier",
                modifierKey switch
                {
                    0 => "Shift",
                    1 => "Alt",
                    _ => "Ctrl"
                },
                MathF.Max(
                    1f,
                    ImGui.GetContentRegionAvail().X -
                    OmniControls.HelpIconSize().X -
                    ImGui.GetStyle().ItemSpacing.X)))
        {
            if (ImGui.Selectable("Shift", modifierKey == 0))
            {
                modifierKey = 0;
            }

            if (ImGui.Selectable("Alt", modifierKey == 1))
            {
                modifierKey = 1;
            }

            if (ImGui.Selectable("Ctrl", modifierKey == 2))
            {
                modifierKey = 2;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.ChatHotbarLock.Modifier.Help"));
        if (modifierKey != config.ModifierKey)
        {
            config.ModifierKey = modifierKey;
            changed = true;
        }

        return changed;
    }
}
