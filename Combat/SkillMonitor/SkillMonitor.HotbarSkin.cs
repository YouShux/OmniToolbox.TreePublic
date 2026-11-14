using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Interop.Game.Helpers;
using OmniToolbox.UI.Theme;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

public sealed partial class SkillMonitor
{
    private readonly record struct HotbarSkin(NativeTextureRegion Frame, NativeTextureRegion Cooldown)
    {
        public static unsafe HotbarSkin Read()
        {
            var skin = default(HotbarSkin);
            foreach (var name in HotbarAddons)
            {
                if (!AddonHelper.TryGetByName(name, out AddonActionBarBase* addon) || !addon->IsFullyLoaded())
                    continue;

                var slots = addon->ActionBarSlotVector.AsSpan();
                var slotCount = Math.Min(slots.Length, addon->SlotCount);
                for (var index = 0; index < slotCount; index++)
                {
                    var dragDrop = slots[index].ComponentDragDrop;
                    if (dragDrop is null || dragDrop->AtkComponentIcon is null)
                        continue;

                    var icon = dragDrop->AtkComponentIcon;
                    if (skin.Frame.Handle == nint.Zero &&
                        TryReadFirstPart(icon->GetImageNodeById(FRAME_NODE_ID), FRAME_PARTS_ID, 1,
                            new(426f, 144f), new(SLOT_SIZE), out var frame))
                        skin = skin with { Frame = frame };

                    // 保留普通冷却图集的首帧作为锚点，实际分帧按被监控技能的剩余时间选择。
                    if (skin.Cooldown.Handle == nint.Zero &&
                        TryReadFirstPart(icon->GetImageNodeById(COOLDOWN_NODE_ID), COOLDOWN_PARTS_ID, COOLDOWN_FRAME_COUNT,
                            new(396f, 432f), new(44f, 46f), out var cooldown))
                        skin = skin with { Cooldown = cooldown };

                    if (skin.Frame.Handle != nint.Zero && skin.Cooldown.Handle != nint.Zero)
                        return skin;
                }
            }
            return skin;
        }

        public void DrawCooldown(ImDrawListPtr drawList, Vector2 position, Vector2 size, float remaining)
        {
            if (Cooldown.Handle == nint.Zero || !float.IsFinite(remaining) || remaining <= 0f)
                return;

            // 最后一格是闪光效果，普通冷却只使用前 80 格。
            var frame = Math.Min((int)((1f - Math.Clamp(remaining, 0f, 1f)) * COOLDOWN_FRAME_COUNT), COOLDOWN_FRAME_COUNT - 1);
            var offset = new Vector2(frame % COOLDOWN_COLUMNS / (float)COOLDOWN_COLUMNS,
                frame / COOLDOWN_COLUMNS / (float)COOLDOWN_COLUMNS);
            drawList.AddImage(Cooldown.TextureID, position + size * (2f / SLOT_SIZE),
                position + size * new Vector2(46f / SLOT_SIZE, 1f),
                Cooldown.UVMinimum + offset, Cooldown.UVMaximum + offset,
                OmniTheme.Color(new Vector4(1f, 1f, 1f, 115f / 255f)));
        }

        private static unsafe bool TryReadFirstPart(AtkImageNode* image, uint listID, uint minimumParts,
            Vector2 atlasSize, Vector2 partSize, out NativeTextureRegion region)
        {
            region = default;
            if (image is null || image->PartsList is null || image->PartsList->Id != listID ||
                image->PartsList->Parts is null || image->PartsList->PartCount < minimumParts)
                return false;

            var part = image->PartsList->Parts;
            return part->U == 0 && part->V == 0 && part->Width == partSize.X && part->Height == partSize.Y &&
                NativeTextureRegion.TryRead(part, atlasSize, out region);
        }

        private static readonly string[] HotbarAddons =
        [
            "_ActionBar", "_ActionCross", "_ActionDoubleCrossL", "_ActionDoubleCrossR",
            "_ActionBar01", "_ActionBar02", "_ActionBar03", "_ActionBar04", "_ActionBar05",
            "_ActionBar06", "_ActionBar07", "_ActionBar08", "_ActionBar09"
        ];

        #region 常量

        private const uint  FRAME_NODE_ID         = 18;
        private const uint  FRAME_PARTS_ID        = 3;
        private const uint  COOLDOWN_NODE_ID      = 17;
        private const uint  COOLDOWN_PARTS_ID     = 4;
        private const int   COOLDOWN_COLUMNS      = 9;
        private const int   COOLDOWN_FRAME_COUNT  = 80;
        private const float SLOT_SIZE            = 48f;

        #endregion
    }
}
