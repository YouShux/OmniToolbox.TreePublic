using OmniToolbox.Config;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class BetterCharacterPanelFeature(BetterCharacterPanelConfig config) : TreeHouseFeatureBase(new(
    "better-character-panel",
    "Feature.BetterCharacterPanel.Title",
    "Feature.BetterCharacterPanel.Description",
    TreeHouseCategory.InterfaceOptimization))
{
    private BetterCharacterPanelNativeUi? nativeUi;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (!BetterCharacterPanelPanel.Draw(config))
        {
            return false;
        }

        nativeUi?.RefreshSettings();
        return true;
    }

    protected override void OnEnable()
    {
        try
        {
            nativeUi = new(config);
        }
        catch
        {
            nativeUi = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            nativeUi?.Dispose();
        }
        finally
        {
            nativeUi = null;
        }
    }
}
