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

public sealed class ArmoireRecordConfig
{
    public bool CheckOnCabinetOpen { get; set; } = true;

    public bool ExcludeDyedItems { get; set; } = true;

    public bool ScanBackpack { get; set; } = true;

    public bool ScanArmory { get; set; } = true;

    public bool ScanSaddlebag { get; set; } = true;

    public bool ScanRetainers { get; set; } = true;

    public bool ScanFreeCompanyChest { get; set; } = true;

    public bool ScanGlamourDresser { get; set; } = true;

    public bool ScanGearsets { get; set; }

    public bool ScanDyedItems { get; set; } = true;

    public bool ExcludeGearsetItems { get; set; } = true;

    public ItemInventoryLocation ExcludedLocations { get; set; } = ItemInventoryLocation.Armory;
}
