using Dalamud.Utility;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed partial class RealItemIcon
{
    private readonly record struct RealItemIconReplacement
    (
        uint GlassesID,
        uint IconID
    );

    private sealed class RealItemIconReplacementResolver
    {
        private readonly Dictionary<uint, RealItemIconReplacement> replacementsByItemID    = [];
        private readonly Dictionary<uint, RealItemIconReplacement> replacementsByGlassesID = [];
        private readonly Dictionary<uint, Glasses>                 glassesByID             = [];

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
                    item.ItemAction.Value.Action.RowId != GLASSES_ITEM_ACTION_TYPE ||
                    !glassesByID.TryGetValue(item.AdditionalData.RowId, out var glasses))
                {
                    continue;
                }

                var replacement = new RealItemIconReplacement(glasses.RowId, (uint)glasses.Icon);
                replacementsByItemID[item.RowId]       = replacement;
                replacementsByGlassesID[glasses.RowId] = replacement;
            }
        }

        public bool TryGetByItemID
        (
            uint                        itemID,
            out RealItemIconReplacement replacement
        ) =>
            replacementsByItemID.TryGetValue(ItemUtil.GetBaseId(itemID).ItemId, out replacement);

        public bool TryGetByGlassesID
        (
            uint                        glassesID,
            out RealItemIconReplacement replacement
        ) =>
            replacementsByGlassesID.TryGetValue(glassesID, out replacement);

        public bool IsGlassesUnlocked(uint glassesID) =>
            glassesByID.TryGetValue(glassesID, out var glasses) &&
            DalamudServices.UnlockState.IsGlassesUnlocked(glasses);

        #region 常量

        private const uint GLASSES_ITEM_ACTION_TYPE = 37_312;

        #endregion
    }
}
