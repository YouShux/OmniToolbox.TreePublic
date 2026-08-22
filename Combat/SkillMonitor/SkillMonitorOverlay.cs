using System.Drawing;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Host;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class SkillMonitorOverlay(
    SkillMonitorConfig config,
    SkillMonitorDefinition[] definitions,
    SkillMonitorTracker tracker,
    int[][] definitionIndexesByJob)
{
    public void Draw()
    {
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            return;
        }

        var localPlayer = DService.Instance().ObjectTable.LocalPlayer;
        if ((config.HideOutOfCombat && !DService.Instance().Condition[ConditionFlag.InCombat]) ||
            (config.HideWeaponSheathed && localPlayer is not null &&
             !localPlayer.StatusFlags.HasFlag(StatusFlags.WeaponOut)))
        {
            return;
        }

        var partyList = AddonHelper.GetByName<AddonPartyList>("_PartyList");
        if (partyList == null || !IsActuallyVisible(&partyList->AtkUnitBase))
        {
            return;
        }

        var drawList = ImGui.GetBackgroundDrawList();
        for (var memberIndex = 0; memberIndex < 8; memberIndex++)
        {
            var member = tracker.GetMember(memberIndex);
            if (!member.Visible)
            {
                continue;
            }

            DrawMember(memberIndex, member.ClassJobID, partyList, drawList);
        }
    }

    private void DrawMember(int memberIndex, uint classJobID, AddonPartyList* partyList, ImDrawListPtr drawList)
    {
        if (classJobID >= definitionIndexesByJob.Length)
        {
            return;
        }

        var definitionIndexes = definitionIndexesByJob[classJobID];
        var partyMember = partyList->PartyMembers[memberIndex];
        if (partyMember.PartyMemberComponent == null ||
            partyMember.PartyMemberComponent->OwnerNode == null ||
            partyMember.ClassJobIcon == null ||
            partyList->PartyListAtkResNode == null)
        {
            return;
        }

        var visibleCount = 0;
        for (var index = 0; index < definitionIndexes.Length; index++)
        {
            if (ShouldShow(tracker.GetState(memberIndex, definitionIndexes[index]).DisplayState))
            {
                visibleCount++;
            }
        }

        if (visibleCount == 0)
        {
            return;
        }

        var memberNode = partyMember.PartyMemberComponent->OwnerNode;
        var iconNode = partyMember.ClassJobIcon;
        var classJobIconScale = iconNode->AtkResNode.GetScale();
        var classJobIconSize = iconNode->Height * classJobIconScale.Y;
        var iconSize = new Vector2(classJobIconSize * Math.Clamp(
            config.IconScale,
            SkillMonitorConfig.DefaultIconScale * 0.5f,
            SkillMonitorConfig.DefaultIconScale * 2f));
        var spacing = Math.Clamp(config.IconSpacing, 0f, 12f) * partyList->Scale;
        var groupWidth = (visibleCount - 1) * spacing;
        for (var index = 0; index < definitionIndexes.Length; index++)
        {
            var definitionIndex = definitionIndexes[index];
            var definition = definitions[definitionIndex];
            if (ShouldShow(tracker.GetState(memberIndex, definitionIndex).DisplayState))
            {
                groupWidth += GetIconSize(definition, iconSize).X;
            }
        }

        var anchorX = memberNode->AtkResNode.ScreenX + config.Offset.X * partyList->Scale;
        var mirrored = config.Alignment == SkillMonitorAlignment.Mirror;
        var position = new Vector2(
            mirrored ? anchorX + spacing : anchorX - spacing - groupWidth,
            iconNode->ScreenY + config.Offset.Y * partyList->Scale);

        if (mirrored)
        {
            for (var index = 0; index < definitionIndexes.Length; index++)
            {
                DrawMemberIcon(memberIndex, definitionIndexes[index], classJobIconSize, iconSize, spacing, drawList, ref position);
            }

            return;
        }

        for (var index = definitionIndexes.Length - 1; index >= 0; index--)
        {
            DrawMemberIcon(memberIndex, definitionIndexes[index], classJobIconSize, iconSize, spacing, drawList, ref position);
        }
    }

    private void DrawMemberIcon(
        int memberIndex,
        int definitionIndex,
        float classJobIconSize,
        Vector2 iconSize,
        float spacing,
        ImDrawListPtr drawList,
        ref Vector2 position)
    {
        var definition = definitions[definitionIndex];
        var state = tracker.GetState(memberIndex, definitionIndex);
        if (!ShouldShow(state.DisplayState))
        {
            return;
        }

        var currentSize = GetIconSize(definition, iconSize);
        DrawIcon(
            drawList,
            definition,
            state,
            position + new Vector2(0f, (classJobIconSize - currentSize.Y) * 0.5f),
            currentSize);
        position.X += currentSize.X + spacing;
    }

    private static void DrawIcon(
        ImDrawListPtr drawList,
        SkillMonitorDefinition definition,
        SkillMonitorRuntimeState state,
        Vector2 position,
        Vector2 size)
    {
        FramedGameIcon.Draw(
            drawList,
            definition.IconID,
            position,
            size,
            dimmed: state.DisplayState == SkillMonitorDisplayState.Inactive,
            active: state.DisplayState == SkillMonitorDisplayState.Active,
            drawFrame: !definition.IsFood,
            preserveAspectRatio: definition.IsFood);

        if (definition.IsFood)
        {
            if (state.StatusActive && !string.IsNullOrEmpty(state.StatusText))
            {
                DrawFoodDuration(drawList, state.StatusText, position, size);
            }

            return;
        }

        if (state.DisplayState == SkillMonitorDisplayState.Cooldown)
        {
            DrawCooldownMask(drawList, position, size, state.CooldownProgress);
            FramedGameIcon.DrawFrame(drawList, position, size);
            DrawCenteredText(drawList, state.CooldownText ?? string.Empty, position, size);
        }
    }

    private static void DrawCenteredText(ImDrawListPtr drawList, string text, Vector2 position, Vector2 size)
    {
        using var font = (size.Y >= 39f
            ? FontManager.Instance().TrumpGothicFont340
            : size.Y >= 29f
                ? FontManager.Instance().TrumpGothicFont230
                : FontManager.Instance().TrumpGothicFont184).Push();
        var textSize = ImGui.CalcTextSize(text);
        var textPosition = position + (size - textSize) * 0.5f;
        DrawOutlinedText(drawList, text, textPosition, OmniTheme.Color(Vector4.One));
    }

    private static void DrawFoodDuration(ImDrawListPtr drawList, string text, Vector2 position, Vector2 size)
    {
        var value = text[..^1];
        var unit = text[^1..];
        Vector2 valueSize;
        Vector2 unitSize;
        using (FontManager.Instance().UIFont90.Push())
        {
            valueSize = ImGui.CalcTextSize(value);
        }

        using (FontManager.Instance().UIFont80.Push())
        {
            unitSize = ImGui.CalcTextSize(unit);
        }

        var textPosition = new Vector2(
            position.X + (size.X - valueSize.X - unitSize.X) * 0.5f,
            position.Y + size.Y - valueSize.Y * 0.55f);
        var color = OmniTheme.Color(KnownColor.PaleTurquoise.ToVector4());
        using (FontManager.Instance().UIFont90.Push())
        {
            DrawOutlinedText(drawList, value, textPosition, color);
        }

        using (FontManager.Instance().UIFont80.Push())
        {
            DrawOutlinedText(
                drawList,
                unit,
                textPosition + new Vector2(valueSize.X, valueSize.Y - unitSize.Y),
                color);
        }
    }

    private static void DrawOutlinedText(ImDrawListPtr drawList, string text, Vector2 position, uint color)
    {
        var outline = OmniTheme.Color(KnownColor.Black.ToVector4() with { W = 0.95f });
        drawList.AddText(position + new Vector2(-1f, 0f), outline, text);
        drawList.AddText(position + new Vector2(1f, 0f), outline, text);
        drawList.AddText(position + new Vector2(0f, -1f), outline, text);
        drawList.AddText(position + new Vector2(0f, 1f), outline, text);
        drawList.AddText(position, color, text);
    }

    private static void DrawCooldownMask(ImDrawListPtr drawList, Vector2 position, Vector2 size, float progress)
    {
        if (progress <= 0f)
        {
            return;
        }

        var rectMaximum = position + size;
        var center = position + size * 0.5f;
        var start = -MathF.PI * 0.5f;
        var end = start - Math.Clamp(progress, 0f, 1f) * 2f * MathF.PI;
        var (startPoint, startEdge) = RayHitRectEdge(center, start, position, rectMaximum);
        var (endPoint, endEdge) = RayHitRectEdge(center, end, position, rectMaximum);

        drawList.PathClear();
        drawList.PathLineTo(center);
        drawList.PathLineTo(endPoint);
        var edge = endEdge;
        if (startEdge == endEdge && IsEndAheadClockwise(startEdge, startPoint, endPoint))
        {
            for (var index = 0; index < 4; index++)
            {
                drawList.PathLineTo(CornerOf(position, rectMaximum, edge));
                edge = NextClockwise(edge);
            }
        }
        else
        {
            while (edge != startEdge)
            {
                drawList.PathLineTo(CornerOf(position, rectMaximum, edge));
                edge = NextClockwise(edge);
            }
        }

        drawList.PathLineTo(startPoint);
        drawList.PathLineTo(center);
        drawList.PathFillConvex(OmniTheme.Color(KnownColor.Black.ToVector4() with { W = 0.68f }));
    }

    private static (Vector2 Point, int Edge) RayHitRectEdge(
        Vector2 center,
        float angle,
        Vector2 rectMinimum,
        Vector2 rectMaximum)
    {
        var deltaX = MathF.Cos(angle);
        var deltaY = MathF.Sin(angle);
        var best = float.PositiveInfinity;
        var hit = center;
        var hitEdge = -1;

        if (MathF.Abs(deltaY) > 0.000001f)
        {
            UpdateHit((rectMinimum.Y - center.Y) / deltaY, 0, true);
            UpdateHit((rectMaximum.Y - center.Y) / deltaY, 2, true);
        }

        if (MathF.Abs(deltaX) > 0.000001f)
        {
            UpdateHit((rectMaximum.X - center.X) / deltaX, 1, false);
            UpdateHit((rectMinimum.X - center.X) / deltaX, 3, false);
        }

        return (hit, hitEdge);

        void UpdateHit(float distance, int edge, bool horizontal)
        {
            if (distance < 0f || distance >= best)
            {
                return;
            }

            var point = horizontal
                ? new Vector2(center.X + deltaX * distance, edge == 0 ? rectMinimum.Y : rectMaximum.Y)
                : new Vector2(edge == 1 ? rectMaximum.X : rectMinimum.X, center.Y + deltaY * distance);
            if (point.X < rectMinimum.X - 0.001f ||
                point.X > rectMaximum.X + 0.001f ||
                point.Y < rectMinimum.Y - 0.001f ||
                point.Y > rectMaximum.Y + 0.001f)
            {
                return;
            }

            best = distance;
            hit = point;
            hitEdge = edge;
        }
    }

    private static Vector2 CornerOf(Vector2 rectMinimum, Vector2 rectMaximum, int edge) => edge switch
    {
        0 => new(rectMaximum.X, rectMinimum.Y),
        1 => rectMaximum,
        2 => new(rectMinimum.X, rectMaximum.Y),
        _ => rectMinimum
    };

    private static int NextClockwise(int edge) => (edge + 1) & 3;

    private static bool IsEndAheadClockwise(int edge, Vector2 start, Vector2 end) => edge switch
    {
        0 => end.X > start.X,
        1 => end.Y > start.Y,
        2 => end.X < start.X,
        3 => end.Y < start.Y,
        _ => false
    };

    private bool ShouldShow(SkillMonitorDisplayState state) => state switch
    {
        SkillMonitorDisplayState.Active => config.ShowActive,
        SkillMonitorDisplayState.Cooldown => config.ShowOnCooldown,
        SkillMonitorDisplayState.Ready => config.ShowOffCooldown,
        SkillMonitorDisplayState.Unknown => config.ShowOffCooldown,
        _ => false
    };

    private static Vector2 GetIconSize(SkillMonitorDefinition definition, Vector2 size) =>
        definition.IsFood ? OmniTheme.StatusIconSize(size.Y * 1.1f) : size;

    private static bool IsActuallyVisible(AtkUnitBase* addon) =>
        addon != null &&
        addon->IsVisible &&
        addon->RootNode != null &&
        addon->RootNode->IsVisible() &&
        (addon->VisibilityFlags & 5) == 0;
}
