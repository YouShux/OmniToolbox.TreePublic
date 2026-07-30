using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;

namespace OmniToolbox.TreeHouse.General;

internal static class BackgroundChatReminderPanel
{
    public static bool Draw(BackgroundChatReminderConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##backgroundChatReminderOptions",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##backgroundChatReminderTell", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##backgroundChatReminderParty", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##backgroundChatReminderReserved1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##backgroundChatReminderReserved2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var tell = config.EnableFlashOnTell;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.BackgroundChatReminder.Tell"), ref tell))
        {
            config.EnableFlashOnTell = tell;
            changed = true;
        }

        ImGui.TableNextColumn();
        var party = config.EnableFlashOnParty;
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.BackgroundChatReminder.Party"), ref party))
        {
            config.EnableFlashOnParty = party;
            changed = true;
        }

        return changed;
    }
}
