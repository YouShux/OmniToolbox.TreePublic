using OmenTools.ImGuiOm;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterCharacterPanel(BetterCharacterPanelConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BetterCharacterPanelTitle"),
        Description = OmniLoc.Get("BetterCharacterPanelDescription"),
        Category = ModuleCategory.Interface,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Interface/BetterCharacterPanel-1.png"
    };

    public override bool HasSettings => true;

    private BetterCharacterPanelNativeUI? nativeUI;

    protected override void OnEnable()
    {
        try
        {
            nativeUI = new(config);
        }
        catch
        {
            nativeUI = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            nativeUI?.Dispose();
        }
        finally
        {
            nativeUI = null;
        }
    }

    public override bool DrawSettings()
    {
        if (!BetterCharacterPanelPanel.Draw(config))
        {
            return false;
        }

        nativeUI?.RefreshSettings();
        return true;
    }
}
