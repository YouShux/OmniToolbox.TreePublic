using OmniToolbox.Config;
using OmniToolbox.Tooltips;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class MateriaTotalFeature(MateriaTotalConfig config, ItemTooltipInventoryContext inventoryContext) : TreeHouseFeatureBase(new(
    "materia-total",
    "Feature.MateriaTotal.Title",
    "Feature.MateriaTotal.Description",
    TreeHouseCategory.InterfaceOptimization))
{
    private MateriaTotalNativeUi? nativeUi;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (!MateriaTotalPanel.Draw(config))
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
            nativeUi = new(config, inventoryContext);
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
