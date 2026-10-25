using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using TreeTools.Lifecycle;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class LockedDutyPreview
{
    internal static class LockedDutyPreviewPanel
    {
        public static bool Draw(Settings config)
        {
            var changed = false;
            if (OmniControls.SmallButton(OmniLoc.Get("Feature.LockedDutyPreview.ClearExcluded"), false))
            {
                changed = config.ExcludedContentFinderConditionIds.Count > 0;
                config.ExcludedContentFinderConditionIds.Clear();
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(string.Format(
                OmniLoc.Get("Feature.LockedDutyPreview.ExcludedCount"),
                config.ExcludedContentFinderConditionIds.Count));

            return changed;
        }
    }

}