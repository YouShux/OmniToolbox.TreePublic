using System.Drawing;
using System.Globalization;
using Lumina.Excel.Sheets;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Extensions;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace OmniToolbox.TreePublic;

internal sealed class FloatingInfoOverlayNativeUI(
    FloatingInfoOverlayConfig config,
    FloatingInfoOverlayState state)
{
    private readonly Dictionary<uint, int> currentPages = [];
    private readonly Dictionary<uint, string> actionNameCache = [];
    private readonly List<uint> removeBuffer = [];
    private readonly List<FloatingInfoLine> lineBuffer = [];
    private readonly List<FloatingInfoLineRect> lineRectBuffer = [];

    public void Draw()
    {
        if (!state.IsActive)
        {
            return;
        }

        if (state.Groups.Count == 0)
        {
            return;
        }

        try
        {
            CleanupPages();
            var drawList = ImGui.GetBackgroundDrawList();
            var clickConsumed = false;
            foreach (var pair in state.Groups)
            {
                var group = pair.Value;
                if (group.Objects.Count == 0)
                {
                    continue;
                }

                var currentPage = ResolveCurrentPage(pair.Key, group.Objects);
                var bounds = DrawObjectInfoAt(
                    drawList,
                    group.Objects[currentPage],
                    group.AnchorPosition,
                    group.Objects.Count,
                    currentPage);
                if (!clickConsumed && bounds.LineRects.Count > 0)
                {
                    clickConsumed = DrawClickWindow(
                        bounds.Minimum,
                        bounds.Maximum,
                        bounds.LineRects,
                        group.Objects.Count,
                        currentPage,
                        pair.Key);
                }
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Floating info overlay draw failed.");
        }
    }

    private int ResolveCurrentPage(uint groupID, List<FloatingInfoObject> objects)
    {
        currentPages.TryAdd(groupID, 0);
        var castingIndex = -1;
        for (var index = 0; index < objects.Count; index++)
        {
            if (objects[index].IsCasting)
            {
                castingIndex = index;
                break;
            }
        }
        var currentPage = castingIndex >= 0 ? castingIndex : currentPages[groupID];
        if (currentPage >= objects.Count)
        {
            currentPage = 0;
            currentPages[groupID] = 0;
        }

        if (castingIndex < 0)
        {
            currentPages[groupID] = currentPage;
        }

        return currentPage;
    }

    private bool DrawClickWindow(
        Vector2 minimum,
        Vector2 maximum,
        List<FloatingInfoLineRect> lineRects,
        int objectCount,
        int currentPage,
        uint groupID)
    {
        ImGui.SetNextWindowPos(minimum, ImGuiCond.Always);
        ImGui.SetNextWindowSize(maximum - minimum, ImGuiCond.Always);
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse;
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var colors = ImRaii.PushColor(ImGuiCol.WindowBg, Vector4.Zero)
            .Push(ImGuiCol.Border, Vector4.Zero);
        var windowOpen = ImGui.Begin($"##OmniFloatingInfo_{groupID:X8}", flags);
        try
        {
            if (!windowOpen || !ImGui.IsWindowHovered() || !ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                return false;
            }

            var mousePosition = ImGui.GetMousePos();
            if (!HitTest(mousePosition, minimum, maximum))
            {
                return false;
            }

            for (var index = 0; index < lineRects.Count; index++)
            {
                var line = lineRects[index];
                if (line.CopyValue.Length == 0 || !HitTest(mousePosition, line.Minimum, line.Maximum))
                {
                    continue;
                }

                ImGui.SetClipboardText(line.CopyValue);
                OmniNotifier.Chat(Format("Feature.FloatingInfoOverlay.Copied", line.CopyValue));
                return true;
            }

            if (objectCount > 1)
            {
                currentPages[groupID] = (currentPage + 1) % objectCount;
            }

            return true;
        }
        finally
        {
            ImGui.End();
        }
    }

    private FloatingInfoBounds DrawObjectInfoAt(
        ImDrawListPtr drawList,
        FloatingInfoObject item,
        Vector2 position,
        int totalCount,
        int currentIndex)
    {
        var lines = BuildOverlayLines(item, totalCount, currentIndex);
        lineRectBuffer.Clear();
        if (lines.Count == 0)
        {
            return new(Vector2.Zero, Vector2.Zero, lineRectBuffer);
        }

        var overlayScale = Math.Clamp(config.Scale, 0.3f, 3f) * 1.1f;
        var font = ImGui.GetFont();
        var fontSize = font.FontSize * overlayScale;
        var lineHeight = fontSize + OmniTheme.Scale(4f) * overlayScale;
        var padding = OmniTheme.Scale(new Vector2(8f, 6f)) * overlayScale;
        var contentWidth = OmniTheme.Scale(lines.Count > 8 ? 340f : 260f) * overlayScale;
        var showCastProgress = config.ShowCastInfo && item.IsCasting && item.TotalCastTime > 0f;
        var castProgressHeight = showCastProgress ? OmniTheme.Scale(6f) * overlayScale : 0f;
        var castProgressGap = showCastProgress ? OmniTheme.Scale(5f) * overlayScale : 0f;
        var totalHeight = lines.Count * lineHeight + padding.Y * 2f + castProgressGap + castProgressHeight;
        var minimum = position - padding;
        var maximum = minimum + new Vector2(contentWidth + padding.X * 2f, totalHeight);
        ClampToViewport(ref minimum, ref maximum, OmniTheme.Scale(4f) * overlayScale);

        var opacity = Math.Clamp(config.Opacity, 0.1f, 1f);
        drawList.AddRectFilled(
            minimum,
            maximum,
            OmniTheme.Color(KnownColor.Black.ToVector4() with { W = opacity }));
        drawList.AddRect(
            minimum - Vector2.One * OmniTheme.Scale(1f) * overlayScale,
            maximum + Vector2.One * OmniTheme.Scale(1f) * overlayScale,
            OmniTheme.Color(KnownColor.DimGray.ToVector4() with { W = opacity * 0.26f }),
            OmniTheme.Scale(4f) * overlayScale,
            ImDrawFlags.None,
            OmniTheme.Scale(2f) * overlayScale);
        drawList.AddRect(
            minimum,
            maximum,
            OmniTheme.Color(KnownColor.LightGray.ToVector4() with { W = opacity * 0.44f }),
            OmniTheme.Scale(4f) * overlayScale,
            ImDrawFlags.None,
            OmniTheme.Scale(1.5f) * overlayScale);

        var currentPosition = minimum + padding;
        var mousePosition = ImGui.GetMousePos();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineMinimum = new Vector2(minimum.X, currentPosition.Y);
            var lineMaximum = new Vector2(maximum.X, currentPosition.Y + lineHeight);
            lineRectBuffer.Add(new(lineMinimum, lineMaximum, line.CopyValue));
            if (line.CopyValue.Length > 0 && HitTest(mousePosition, lineMinimum, lineMaximum))
            {
                drawList.AddRectFilled(
                    lineMinimum,
                    lineMaximum,
                    OmniTheme.Color(OmniTheme.Orange with { W = opacity * 0.16f }),
                    OmniTheme.Scale(2f) * overlayScale);
            }

            drawList.AddText(
                font,
                fontSize,
                currentPosition,
                Color(line.Color, opacity),
                line.Text);
            currentPosition.Y += lineHeight;
        }

        if (showCastProgress)
        {
            DrawCastProgressBar(
                drawList,
                item,
                minimum,
                maximum,
                padding,
                castProgressHeight,
                opacity);
        }

        return new(minimum, maximum, lineRectBuffer);
    }

    private List<FloatingInfoLine> BuildOverlayLines(
        FloatingInfoObject item,
        int totalCount,
        int currentIndex)
    {
        lineBuffer.Clear();
        var standardText = KnownColor.White.ToVector4();
        if (totalCount > 1)
        {
            lineBuffer.Add(new(
                Format("Feature.FloatingInfoOverlay.Line.Page", currentIndex + 1, totalCount),
                KnownColor.Cyan.ToVector4(),
                string.Empty));
        }

        lineBuffer.Add(new(item.Name, KnownColor.Gold.ToVector4(), item.Name));
        lineBuffer.Add(new(
            Format("Feature.FloatingInfoOverlay.Line.Type", GetObjectKindName(item)),
            standardText,
            GetObjectKindName(item)));

        if (config.ShowMarker && item.Marker != FloatingInfoMarker.None)
        {
            lineBuffer.Add(new(
                Format("Feature.FloatingInfoOverlay.Line.Marker", GetMarkerName(item.Marker)),
                standardText,
                GetMarkerName(item.Marker)));
        }

        if (config.ShowEntityID)
        {
            if (config.ShowDecimalID)
            {
                var entityID = item.EntityID.ToString(CultureInfo.InvariantCulture);
                lineBuffer.Add(new(
                    Format("Feature.FloatingInfoOverlay.Line.EntityId", entityID),
                    standardText,
                    entityID));
            }

            if (config.ShowHexID)
            {
                var entityID = $"0x{item.EntityID:X8}";
                lineBuffer.Add(new(
                    Format("Feature.FloatingInfoOverlay.Line.EntityIdHex", entityID),
                    standardText,
                    entityID));
            }
        }

        if (config.ShowDataID)
        {
            if (config.ShowDecimalID)
            {
                var dataID = item.DataID.ToString(CultureInfo.InvariantCulture);
                lineBuffer.Add(new(
                    Format("Feature.FloatingInfoOverlay.Line.DataId", dataID),
                    standardText,
                    dataID));
            }

            if (config.ShowHexID)
            {
                var dataID = $"0x{item.DataID:X8}";
                lineBuffer.Add(new(
                    Format("Feature.FloatingInfoOverlay.Line.DataIdHex", dataID),
                    standardText,
                    dataID));
            }
        }

        if (config.ShowPosition)
        {
            var position = FormattableString.Invariant(
                $"{item.Position.X:F1}, {item.Position.Y:F1}, {item.Position.Z:F1}");
            lineBuffer.Add(new(
                Format("Feature.FloatingInfoOverlay.Line.Position", position),
                standardText,
                position));
        }

        if (config.ShowRotation && !item.IsNonEntity)
        {
            var degrees = item.Rotation * 180f / MathF.PI % 360f;
            if (degrees < 0f)
            {
                degrees += 360f;
            }

            lineBuffer.Add(new(
                Format("Feature.FloatingInfoOverlay.Line.Rotation", item.Rotation, degrees),
                standardText,
                item.Rotation.ToString("F3", CultureInfo.InvariantCulture)));
        }

        if (config.ShowDistance && item.Distance >= 0f)
        {
            lineBuffer.Add(new(
                Format("Feature.FloatingInfoOverlay.Line.Distance", item.Distance),
                standardText,
                item.Distance.ToString("F1", CultureInfo.InvariantCulture)));
        }

        if (config.ShowHealth && item.MaxHp > 0)
        {
            var percentage = (float)item.CurrentHp / item.MaxHp;
            lineBuffer.Add(new(
                Format(
                    "Feature.FloatingInfoOverlay.Line.Health",
                    OmniNumberFormatter.Format(item.CurrentHp),
                    OmniNumberFormatter.Format(item.MaxHp),
                    percentage),
                percentage switch
                {
                    > 0.7f => KnownColor.LimeGreen.ToVector4(),
                    > 0.3f => KnownColor.Gold.ToVector4(),
                    _ => KnownColor.OrangeRed.ToVector4()
                },
                $"{OmniNumberFormatter.Format(item.CurrentHp)}/{OmniNumberFormatter.Format(item.MaxHp)}"));
        }

        if (config.ShowMana && item.MaxMp > 0)
        {
            var percentage = (float)item.CurrentMp / item.MaxMp;
            lineBuffer.Add(new(
                Format(
                    "Feature.FloatingInfoOverlay.Line.Mana",
                    OmniNumberFormatter.Format(item.CurrentMp),
                    OmniNumberFormatter.Format(item.MaxMp),
                    percentage),
                percentage switch
                {
                    > 0.7f => KnownColor.DodgerBlue.ToVector4(),
                    > 0.3f => KnownColor.MediumPurple.ToVector4(),
                    _ => KnownColor.DarkOrchid.ToVector4()
                },
                $"{OmniNumberFormatter.Format(item.CurrentMp)}/{OmniNumberFormatter.Format(item.MaxMp)}"));
        }

        if (config.ShowCastInfo && item.IsCasting)
        {
            AppendCastLines(item);
        }

        if (config.ShowStatusList && item.Statuses.Count > 0)
        {
            lineBuffer.Add(new(
                Format("Feature.FloatingInfoOverlay.Line.StatusCount", item.Statuses.Count),
                KnownColor.Cyan.ToVector4(),
                string.Empty));
            for (var index = 0; index < item.Statuses.Count; index++)
            {
                var status = item.Statuses[index];
                var statusText = status.Name.Length == 0
                    ? Format(
                        "Feature.FloatingInfoOverlay.Line.StatusWithoutName",
                        status.StatusID,
                        status.RemainingTime)
                    : Format(
                        "Feature.FloatingInfoOverlay.Line.Status",
                        status.StatusID,
                        status.Name,
                        status.RemainingTime);
                if (status.Param > 0)
                {
                    statusText += Format(
                        "Feature.FloatingInfoOverlay.Line.StatusParam",
                        status.Param);
                }

                lineBuffer.Add(new(
                    statusText,
                    status.RemainingTime < 5f ? KnownColor.OrangeRed.ToVector4() : standardText,
                    status.StatusID.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return lineBuffer;
    }

    private void AppendCastLines(FloatingInfoObject item)
    {
        var color = OmniTheme.Orange;
        lineBuffer.Add(new(
            OmniLoc.Get("Feature.FloatingInfoOverlay.Line.Casting"),
            color,
            string.Empty));
        var actionName = GetActionName(item.CastActionID);
        lineBuffer.Add(new(
            Format(
                actionName.Length == 0
                    ? "Feature.FloatingInfoOverlay.Line.CastActionWithoutName"
                    : "Feature.FloatingInfoOverlay.Line.CastAction",
                item.CastActionID,
                actionName),
            color,
            item.CastActionID.ToString(CultureInfo.InvariantCulture)));

        if (LuminaGetter.TryGetRow<LuminaAction>(item.CastActionID, out var action))
        {
            var range = (float)action.EffectRange;
            if (action.CastType is > 2 and < 6)
            {
                range += item.HitboxRadius;
            }

            lineBuffer.Add(new(
                Format("Feature.FloatingInfoOverlay.Line.CastRange", range, action.XAxisModifier),
                color,
                FormattableString.Invariant($"{range:F2}, {action.XAxisModifier:F2}")));
            lineBuffer.Add(new(
                Format("Feature.FloatingInfoOverlay.Line.CastShape", GetCastTypeName(action.CastType)),
                color,
                GetCastTypeName(action.CastType)));
        }

        if (item.CastRotation.HasValue)
        {
            var degrees = item.CastRotation.Value * 180f / MathF.PI % 360f;
            if (degrees < 0f)
            {
                degrees += 360f;
            }

            lineBuffer.Add(new(
                Format("Feature.FloatingInfoOverlay.Line.CastRotation", item.CastRotation.Value, degrees),
                color,
                item.CastRotation.Value.ToString("F3", CultureInfo.InvariantCulture)));
        }

        if (item.TotalCastTime > 0f)
        {
            lineBuffer.Add(new(
                Format(
                    "Feature.FloatingInfoOverlay.Line.CastTime",
                    item.CurrentCastTime,
                    item.TotalCastTime,
                    Math.Clamp(item.CurrentCastTime / item.TotalCastTime, 0f, 1f)),
                color,
                FormattableString.Invariant($"{item.CurrentCastTime:F1}/{item.TotalCastTime:F1}")));
        }
    }

    private string GetActionName(uint actionID)
    {
        if (actionNameCache.TryGetValue(actionID, out var name))
        {
            return name;
        }

        name = LuminaGetter.TryGetRow<LuminaAction>(actionID, out var action)
            ? action.Name.ToString()
            : string.Empty;
        actionNameCache[actionID] = name;
        return name;
    }

    private static string GetObjectKindName(FloatingInfoObject item) =>
        OmniLoc.Get(item.IsNonEntity
            ? "Feature.FloatingInfoOverlay.ObjectKind.NonEntity"
            : item.ObjectKind switch
            {
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc =>
                    "Feature.FloatingInfoOverlay.ObjectKind.Player",
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc =>
                    "Feature.FloatingInfoOverlay.ObjectKind.BattleNpc",
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc =>
                    "Feature.FloatingInfoOverlay.ObjectKind.EventNpc",
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj =>
                    "Feature.FloatingInfoOverlay.ObjectKind.EventObject",
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion =>
                    "Feature.FloatingInfoOverlay.ObjectKind.Companion",
                _ => "Feature.FloatingInfoOverlay.ObjectKind.Other"
            });

    private static string GetMarkerName(FloatingInfoMarker marker) =>
        OmniLoc.Get($"Feature.FloatingInfoOverlay.Marker.{marker}");

    private static string GetCastTypeName(uint castType) =>
        castType > 5
            ? castType.ToString(CultureInfo.InvariantCulture)
            : OmniLoc.Get(castType switch
            {
                1 => "Feature.FloatingInfoOverlay.CastType.Circle",
                2 => "Feature.FloatingInfoOverlay.CastType.Cone",
                3 => "Feature.FloatingInfoOverlay.CastType.Line",
                4 => "Feature.FloatingInfoOverlay.CastType.Rectangle",
                5 => "Feature.FloatingInfoOverlay.CastType.Charge",
                _ => "Feature.FloatingInfoOverlay.CastType.None"
            });

    private static void DrawCastProgressBar(
        ImDrawListPtr drawList,
        FloatingInfoObject item,
        Vector2 minimum,
        Vector2 maximum,
        Vector2 padding,
        float height,
        float opacity)
    {
        var progress = Math.Clamp(item.CurrentCastTime / item.TotalCastTime, 0f, 1f);
        var barMinimum = new Vector2(minimum.X + padding.X, maximum.Y - padding.Y - height);
        var barMaximum = new Vector2(maximum.X - padding.X, barMinimum.Y + height);
        var fillMaximum = new Vector2(
            barMinimum.X + (barMaximum.X - barMinimum.X) * progress,
            barMaximum.Y);
        drawList.AddRectFilled(
            barMinimum,
            barMaximum,
            OmniTheme.Color(KnownColor.Black.ToVector4() with { W = opacity * 0.86f }),
            OmniTheme.Scale(3f));
        if (fillMaximum.X > barMinimum.X)
        {
            drawList.AddRectFilled(
                barMinimum,
                fillMaximum,
                OmniTheme.Color(OmniTheme.Orange with { W = opacity }),
                OmniTheme.Scale(3f));
            drawList.AddRectFilled(
                barMinimum,
                new Vector2(fillMaximum.X, barMinimum.Y + height * 0.36f),
                OmniTheme.Color(KnownColor.Gold.ToVector4() with { W = opacity * 0.44f }),
                OmniTheme.Scale(3f));
            DrawCastProgressShimmer(drawList, barMinimum, fillMaximum, height, opacity);
        }

        drawList.AddRect(
            barMinimum,
            barMaximum,
            OmniTheme.Color(KnownColor.Goldenrod.ToVector4() with { W = opacity * 0.62f }),
            OmniTheme.Scale(3f));
    }

    private static void DrawCastProgressShimmer(
        ImDrawListPtr drawList,
        Vector2 barMinimum,
        Vector2 fillMaximum,
        float height,
        float opacity)
    {
        var fillWidth = fillMaximum.X - barMinimum.X;
        if (fillWidth <= height * 1.5f)
        {
            return;
        }

        var shimmerWidth = Math.Min(
            Math.Max(height * 7.5f, OmniTheme.Scale(28f)),
            fillWidth * 0.58f);
        var centerX = barMinimum.X - shimmerWidth +
                      (fillWidth + shimmerWidth * 2f) * (float)(ImGui.GetTime() * 0.95 % 1.0);
        var left = centerX - shimmerWidth * 0.5f;
        var right = centerX + shimmerWidth * 0.5f;
        if (right <= barMinimum.X || left >= fillMaximum.X)
        {
            return;
        }

        var transparent = OmniTheme.Color(KnownColor.Gold.ToVector4() with { W = 0f });
        var highlight = OmniTheme.Color(
            KnownColor.LightGoldenrodYellow.ToVector4() with { W = opacity * 0.68f });
        var shimmerHeight = Math.Max(OmniTheme.Scale(1f), height * 0.5f);
        var shimmerMinimumY = barMinimum.Y + (height - shimmerHeight) * 0.5f;
        var shimmerMaximumY = shimmerMinimumY + shimmerHeight;
        DrawShimmerHalf(
            drawList,
            barMinimum.X,
            fillMaximum.X,
            left,
            centerX,
            shimmerMinimumY,
            shimmerMaximumY,
            transparent,
            highlight);
        DrawShimmerHalf(
            drawList,
            barMinimum.X,
            fillMaximum.X,
            centerX,
            right,
            shimmerMinimumY,
            shimmerMaximumY,
            highlight,
            transparent);
    }

    private static void DrawShimmerHalf(
        ImDrawListPtr drawList,
        float clipMinimumX,
        float clipMaximumX,
        float left,
        float right,
        float minimumY,
        float maximumY,
        uint leftColor,
        uint rightColor)
    {
        left = Math.Clamp(left, clipMinimumX, clipMaximumX);
        right = Math.Clamp(right, clipMinimumX, clipMaximumX);
        if (right <= left)
        {
            return;
        }

        drawList.AddRectFilledMultiColor(
            new Vector2(left, minimumY),
            new Vector2(right, maximumY),
            leftColor,
            rightColor,
            rightColor,
            leftColor);
    }

    private void CleanupPages()
    {
        removeBuffer.Clear();
        foreach (var groupID in currentPages.Keys)
        {
            if (!state.Groups.ContainsKey(groupID))
            {
                removeBuffer.Add(groupID);
            }
        }

        for (var index = 0; index < removeBuffer.Count; index++)
        {
            currentPages.Remove(removeBuffer[index]);
        }

        removeBuffer.Clear();
    }

    private static void ClampToViewport(ref Vector2 minimum, ref Vector2 maximum, float margin)
    {
        var displaySize = ImGui.GetIO().DisplaySize;
        if (displaySize.X <= 0f || displaySize.Y <= 0f)
        {
            return;
        }

        var size = maximum - minimum;
        minimum = new(
            Math.Clamp(minimum.X, margin, Math.Max(margin, displaySize.X - size.X - margin)),
            Math.Clamp(minimum.Y, margin, Math.Max(margin, displaySize.Y - size.Y - margin)));
        maximum = minimum + size;
    }

    private static uint Color(Vector4 color, float opacity)
    {
        color.W *= opacity;
        return ImGui.ColorConvertFloat4ToU32(color);
    }

    private static string Format<T>(string key, T value) =>
        string.Format(CultureInfo.CurrentCulture, OmniLoc.Get(key), value);

    private static string Format<T1, T2>(string key, T1 value1, T2 value2) =>
        string.Format(CultureInfo.CurrentCulture, OmniLoc.Get(key), value1, value2);

    private static string Format<T1, T2, T3>(string key, T1 value1, T2 value2, T3 value3) =>
        string.Format(CultureInfo.CurrentCulture, OmniLoc.Get(key), value1, value2, value3);

    private static bool HitTest(in Vector2 point, in Vector2 minimum, in Vector2 maximum) =>
        point.X >= minimum.X && point.X <= maximum.X &&
        point.Y >= minimum.Y && point.Y <= maximum.Y;

    private readonly record struct FloatingInfoBounds(
        Vector2 Minimum,
        Vector2 Maximum,
        List<FloatingInfoLineRect> LineRects);

    private readonly record struct FloatingInfoLine(string Text, Vector4 Color, string CopyValue);

    private readonly record struct FloatingInfoLineRect(
        Vector2 Minimum,
        Vector2 Maximum,
        string CopyValue);
}
