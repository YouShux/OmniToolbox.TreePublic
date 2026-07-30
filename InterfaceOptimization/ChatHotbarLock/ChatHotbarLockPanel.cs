using OmniToolbox.Config;
using OmniToolbox.UI;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class ChatHotbarLockPanel
{
    public static bool Draw(ChatHotbarLockConfig config)
    {
        var changed = false;
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

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.ChatHotbarLock.ShowChatLock.Help"));

        var hideLocks = config.HideLocks;
        if (OmniControls.Checkbox(
                $"{OmniLoc.Get("Feature.ChatHotbarLock.HideLocks")}##chatHotbarLockHideLocks",
                ref hideLocks))
        {
            config.HideLocks = hideLocks;
            changed = true;
        }

        var modifierKey = config.ModifierKey is 0 or 1 or 2 ? config.ModifierKey : 2;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ChatHotbarLock.Modifier"));
        ImGui.SameLine();
        if (ImGui.RadioButton("Ctrl##chatHotbarLockModifierCtrl", modifierKey == 2))
        {
            modifierKey = 2;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Shift##chatHotbarLockModifierShift", modifierKey == 0))
        {
            modifierKey = 0;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Alt##chatHotbarLockModifierAlt", modifierKey == 1))
        {
            modifierKey = 1;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.ChatHotbarLock.Modifier.Help"));
        if (modifierKey != config.ModifierKey)
        {
            config.ModifierKey = modifierKey;
            changed = true;
        }

        return changed;
    }
}
