using Dalamud.Utility;
using Lumina.Excel.Sheets;
using OmniToolbox.Host;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

internal sealed class RealItemIconReplacementResolver
{
    private const uint GlassesItemActionType = 37_312;

    private readonly Dictionary<uint, RealItemIconReplacement> replacementsByItemID = [];
    private readonly Dictionary<uint, RealItemIconReplacement> replacementsByGlassesID = [];
    private readonly Dictionary<uint, Glasses> glassesByID = [];

    public RealItemIconReplacementResolver()
    {
        foreach (var glasses in LuminaGetter.Get<Glasses>())
        {
            if (glasses.RowId != 0 && glasses.Icon != 0)
            {
                glassesByID[glasses.RowId] = glasses;
            }
        }

        foreach (var item in LuminaGetter.Get<Item>())
        {
            if (item.RowId == 0 ||
                !item.ItemAction.IsValid ||
                item.ItemAction.Value.Action.RowId != GlassesItemActionType ||
                !glassesByID.TryGetValue(item.AdditionalData.RowId, out var glasses))
            {
                continue;
            }

            var replacement = new RealItemIconReplacement(glasses.RowId, (uint)glasses.Icon);
            replacementsByItemID[item.RowId] = replacement;
            replacementsByGlassesID[glasses.RowId] = replacement;
        }
    }

    public bool TryGetByItemID(uint itemID, out RealItemIconReplacement replacement) =>
        replacementsByItemID.TryGetValue(ItemUtil.GetBaseId(itemID).ItemId, out replacement);

    public bool TryGetByGlassesID(uint glassesID, out RealItemIconReplacement replacement) =>
        replacementsByGlassesID.TryGetValue(glassesID, out replacement);

    public bool IsGlassesUnlocked(uint glassesID) =>
        glassesByID.TryGetValue(glassesID, out var glasses) &&
        DalamudServices.UnlockState.IsGlassesUnlocked(glasses);
}

internal readonly record struct RealItemIconReplacement(uint GlassesID, uint IconID);
