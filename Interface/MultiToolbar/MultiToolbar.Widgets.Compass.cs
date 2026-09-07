using OmniToolbox.UI.Theme;
using OmenTools.OmenService;
using OmenTools.Interop.Game.Helpers;
using Camera = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Camera;
using GameVector2 = FFXIVClientStructs.FFXIV.Common.Math.Vector2;
using GameVector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private static unsafe bool TryProjectWorldMarker(Vector3 worldPosition, Vector2 viewportPosition, out Vector2 screenPosition, out bool inFront)
    {
        inFront = GameViewHelper.WorldToScreen(worldPosition, out screenPosition, out _);
        if (inFront)
        {
            return float.IsFinite(screenPosition.X) && float.IsFinite(screenPosition.Y);
        }
        // 背后的目标仍需投影坐标来确定屏外箭头方向。
        var world = new GameVector3(worldPosition.X, worldPosition.Y, worldPosition.Z);
        var screen = new GameVector2();
        Camera.WorldToScreenPoint(&screen, &world);
        screenPosition = new Vector2(screen.X, screen.Y) + viewportPosition;
        return float.IsFinite(screenPosition.X) && float.IsFinite(screenPosition.Y);
    }

    private static void DrawWorldMarkerDirection(ImDrawListPtr drawList, uint iconID, Vector2 origin,
        Vector2 direction, Vector2 workPosition, Vector2 workSize, float opacity)
    {
        var length = direction.Length();
        var iconSize = OmniTheme.Scale(24f);
        var inset = new Vector2(iconSize * 2.5f);
        if (!float.IsFinite(length) || length < 0.001f || workSize.X <= inset.X * 2f || workSize.Y <= inset.Y * 2f)
        {
            return;
        }
        direction /= length;
        var icon = ImageHelper.GetGameIcon(iconID);
        if (icon == null)
        {
            return;
        }
        var minimum = workPosition + inset;
        var maximum = workPosition + workSize - inset;
        origin = Vector2.Clamp(origin, minimum, maximum);
        var horizontal = direction.X == 0f ? float.PositiveInfinity :
            ((direction.X > 0f ? maximum.X : minimum.X) - origin.X) / direction.X;
        var vertical = direction.Y == 0f ? float.PositiveInfinity :
            ((direction.Y > 0f ? maximum.Y : minimum.Y) - origin.Y) / direction.Y;
        var center = origin + direction * MathF.Min(horizontal, vertical);
        var halfIcon = new Vector2(iconSize * 0.5f);
        var color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, opacity));
        drawList.AddImage(icon.Handle, center - halfIcon, center + halfIcon, Vector2.Zero, Vector2.One, color);

        if (ImageHelper.GetGameIcon(60541) is not { } arrow)
        {
            return;
        }
        // 纹理箭头朝上，将其上边旋转到目标方向。
        var arrowCenter = center + direction * iconSize;
        var forward = direction * OmniTheme.Scale(16f);
        var side = new Vector2(-forward.Y, forward.X);
        drawList.AddImageQuad(arrow.Handle,
            arrowCenter - side + forward, arrowCenter + side + forward,
            arrowCenter + side - forward, arrowCenter - side - forward,
            Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, color);
    }
}
