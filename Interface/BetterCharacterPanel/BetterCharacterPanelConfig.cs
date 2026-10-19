using OmenTools.ImGuiOm;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed class BetterCharacterPanelConfig
{
    public bool ShowUsefulStats { get; set; } = true;

    public bool AdjustEquipmentPositions { get; set; } = true;

    public bool SoulstoneAboveOffhand { get; set; } = true;

    public bool ReverseCharacterPanel { get; set; } = true;

    public bool ShowGearSetReorderButtons { get; set; } = true;
}