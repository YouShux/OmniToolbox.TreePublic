using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;

namespace OmniToolbox.TreeHouse.General;

internal static class BackgroundChatReminderPanel
{
    public static bool Draw(BackgroundChatReminderConfig config)
    {
        var changed = false;
        var tell = config.EnableFlashOnTell;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.BackgroundChatReminder.Tell"), ref tell))
        {
            config.EnableFlashOnTell = tell;
            changed = true;
        }

        ImGui.SameLine();
        var party = config.EnableFlashOnParty;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.BackgroundChatReminder.Party"), ref party))
        {
            config.EnableFlashOnParty = party;
            changed = true;
        }

        OmniControls.HelpTooltip(OmniLoc.Get("Feature.BackgroundChatReminder.Help"));
        return changed;
    }
}
