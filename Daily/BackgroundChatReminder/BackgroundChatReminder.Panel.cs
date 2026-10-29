using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

public sealed partial class BackgroundChatReminder
{
    private static class BackgroundChatReminderPanel
    {
        public static bool Draw(Settings config)
        {
            var changed = false;
            using var table = OmniControls.SettingsTable(
                "##backgroundChatReminderOptions",
                [OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BackgroundChatReminder.Tell")),
                    OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BackgroundChatReminder.Party"))],
                ["##backgroundChatReminderTell", "##backgroundChatReminderParty"],
                flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 4);
            if (!table)
            {
                return false;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var tell = config.EnableFlashOnTell;
            if (OmniControls.Checkbox(OmniLoc.Get("Feature.BackgroundChatReminder.Tell"), ref tell))
            {
                config.EnableFlashOnTell = tell;
                changed                  = true;
            }

            ImGui.TableNextColumn();
            var party = config.EnableFlashOnParty;
            if (OmniControls.Checkbox(OmniLoc.Get("Feature.BackgroundChatReminder.Party"), ref party))
            {
                config.EnableFlashOnParty = party;
                changed                   = true;
            }

            return changed;
        }
    }
}
