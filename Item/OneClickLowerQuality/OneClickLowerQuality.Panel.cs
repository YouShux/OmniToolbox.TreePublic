using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Items;
using OmniToolbox.UI.Theme;
using TreeTools.Threading;

namespace OmniToolbox.TreePublic;

public sealed partial class OneClickLowerQuality
{
    internal sealed class Panel
    {
        private int ItemIDInput;

        public bool Draw(Settings config)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.OneClickLowerQuality.ItemId"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(OmniTheme.Scale(110f));
            if (OmniControls.InputInt("##oneClickLowerQualityItemId", ref ItemIDInput) && ItemIDInput < 0)
            {
                ItemIDInput = 0;
            }

            var itemID = ItemUtil.GetBaseId((uint)Math.Max(0, ItemIDInput)).ItemId;
            ImGui.SameLine();
            var changed = OmniControls.SmallButton(
                              $"{OmniLoc.Get("Feature.OneClickLowerQuality.Add")}##oneClickLowerQualityAdd",
                              false) &&
                          itemID > 0 &&
                          config.ItemIDs.Add(itemID);

            ImGui.Dummy(new(0f, OmniTheme.Scale(6f)));
            var itemIDs = new uint[config.ItemIDs.Count];
            config.ItemIDs.CopyTo(itemIDs);
            Array.Sort(itemIDs);
            var rows = new ItemSelectionTableRow[itemIDs.Length];
            for (var index = 0; index < itemIDs.Length; index++)
            {
                rows[index] = new(itemIDs[index], true, true);
            }

            var change = ItemSelectionTable.Draw("oneClickLowerQualityItems", rows, showEnabledColumn: false);
            if (change.Action == ItemSelectionTableAction.Delete)
            {
                changed |= config.ItemIDs.Remove(change.ItemID);
            }

            return changed;
        }
    }

}
