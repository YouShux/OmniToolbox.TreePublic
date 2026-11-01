using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Notifications;
using OmniToolbox.TreeHouse.InterfaceOperation;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class ArmoireRecord
{
    internal sealed record ArmoireRecordItem
    (
        uint   ItemID,
        uint   CabinetRowID,
        string Name,
        uint   IconID,
        int    Quantity,
        string LocationsText,
        byte   Stain0,
        byte   Stain1,
        bool   CanDirectStore,
        bool   CanRetrieve
    )
    {
        public bool IsDyed => Stain0 != 0 || Stain1 != 0;

        public static int Compare
        (
            ArmoireRecordItem left,
            ArmoireRecordItem right
        )
        {
            var directCompare = right.CanDirectStore.CompareTo(left.CanDirectStore);
            if (directCompare != 0)
            {
                return directCompare;
            }

            var itemIDCompare = right.ItemID.CompareTo(left.ItemID);
            return itemIDCompare != 0
                ? itemIDCompare
                : string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
        }
    }

}
