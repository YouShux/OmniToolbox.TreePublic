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
        private readonly Dictionary<uint, bool>                    unlockedByGlassesID     = [];
        private DateTime                                          unlockFrame;
        private ulong                                             unlockContentID;

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

        public void InvalidateUnlocks() => unlockedByGlassesID.Clear();

        public void PrepareUnlocks()
        {
            var frame = DalamudServices.Framework.LastUpdateUTC;
            var contentID = DalamudServices.PlayerState.ContentId;
            if (frame != unlockFrame || contentID != unlockContentID)
            {
                unlockedByGlassesID.Clear();
                unlockFrame     = frame;
                unlockContentID = contentID;
            }
        }

        public bool IsGlassesUnlocked(uint glassesID)
        {
            if (!unlockedByGlassesID.TryGetValue(glassesID, out var unlocked))
            {
                unlocked = glassesByID.TryGetValue(glassesID, out var glasses) &&
                           DalamudServices.UnlockState.IsGlassesUnlocked(glasses);
                unlockedByGlassesID.Add(glassesID, unlocked);
            }

            return unlocked;
        }

        #region 常量

        private const uint GLASSES_ITEM_ACTION_TYPE = 37_312;

        #endregion
    }
}
