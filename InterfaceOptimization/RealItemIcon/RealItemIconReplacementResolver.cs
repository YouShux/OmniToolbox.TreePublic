using Lumina.Excel.Sheets;
using OmniToolbox.Host;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed class RealItemIconReplacementResolver
{
    private const uint GlassesItemActionType = 37_312;

    private readonly Dictionary<uint, RealItemIconReplacement> replacementsByItemId = [];
    private readonly Dictionary<uint, RealItemIconReplacement> replacementsByGlassesId = [];
    private readonly Dictionary<uint, Glasses> glassesById = [];

    public RealItemIconReplacementResolver()
    {
        foreach (var glasses in LuminaGetter.Get<Glasses>())
        {
            if (glasses.RowId != 0 && glasses.Icon != 0)
            {
                glassesById[glasses.RowId] = glasses;
            }
        }

        foreach (var item in LuminaGetter.Get<Item>())
        {
            if (item.RowId == 0 ||
                !item.ItemAction.IsValid ||
                item.ItemAction.Value.Action.RowId != GlassesItemActionType ||
                !glassesById.TryGetValue(item.AdditionalData.RowId, out var glasses))
            {
                continue;
            }

            var replacement = new RealItemIconReplacement(glasses.RowId, (uint)glasses.Icon);
            replacementsByItemId[item.RowId] = replacement;
            replacementsByGlassesId[glasses.RowId] = replacement;
        }
    }

    public bool TryGetByItemId(uint itemId, out RealItemIconReplacement replacement) =>
        replacementsByItemId.TryGetValue(NormalizeItemId(itemId), out replacement);

    public bool TryGetByGlassesId(uint glassesId, out RealItemIconReplacement replacement) =>
        replacementsByGlassesId.TryGetValue(glassesId, out replacement);

    public bool IsGlassesUnlocked(uint glassesId) =>
        glassesById.TryGetValue(glassesId, out var glasses) &&
        DalamudServices.UnlockState.IsGlassesUnlocked(glasses);

    private static uint NormalizeItemId(uint itemId) =>
        itemId < 2_000_000 ? itemId % 500_000 : itemId;
}

internal readonly record struct RealItemIconReplacement(uint GlassesId, uint IconId);
