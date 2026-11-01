using Dalamud.Utility;
using OmenTools.ImGuiOm;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Tooltips;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed class DesynthesisEnhancementConfig
{
    public bool SkipHQ { get; set; } = true;

    public bool ConfirmWhenManual { get; set; }

    public bool GuaranteeNQ { get; set; }

    public bool ShowTooltipText { get; set; } = true;

    public bool LockGearsetItems { get; set; } = true;

    public bool LockCustomItems { get; set; } = true;

    public List<uint> CustomLockItemIds { get; set; } = [];
}
