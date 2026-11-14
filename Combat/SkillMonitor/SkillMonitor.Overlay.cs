using OmniToolbox.UI.Widgets;
using System.Drawing;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.UI.Theme;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

public sealed partial class SkillMonitor
{
    private sealed unsafe class SkillMonitorOverlay
    (
        SkillMonitorConfig       config,
        SkillMonitorDefinition[] definitions,
        SkillMonitorTracker      tracker,
        int[][]                  definitionIndexesByJob
    ) : IDisposable
    {
        private uint               activeCountdownColor = 0xFFFFFFFF;
        private readonly List<int> visibleDefinitions   = [];
        private readonly List<IconLayout> iconLayout = [];
        private IFontHandle?       countdownFont;
        private FontType           loadedFont;
        private Vector2            placeholderSize;

        public void Dispose()
        {
            countdownFont?.Dispose();
            countdownFont = null;
            iconLayout.Clear();
        }

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

            UpdateActiveCountdownColor(partyList);
            if (countdownFont == null || loadedFont != config.Font)
            {
                Dispose();
                loadedFont    = config.Font;
                countdownFont = Fonts.CreateGameFont(config.Font switch
                {
                    FontType.Axis => GameFontFamily.Axis,
                    FontType.MiedingerMed => GameFontFamily.MiedingerMid,
                    FontType.Miedinger => GameFontFamily.Meidinger,
                    _ => GameFontFamily.TrumpGothic
                }, 32f);
            }
            if (!countdownFont.Available)
            {
                return;
            }
            using var font = countdownFont.Push();
            placeholderSize = OmniTextMeasurements.Measure("?");
            var drawList = ImGui.GetBackgroundDrawList();
            iconLayout.Clear();
            for (var memberIndex = 0; memberIndex < 8; memberIndex++)
            {
                var member = tracker.GetMember(memberIndex);
                if (member.Visible)
                    BuildMemberLayout(memberIndex, member.ClassJobID, member.Level, partyList);
            }
            if (iconLayout.Count == 0)
                return;

            var skin = HotbarSkin.Read();
            foreach (var clip in GameWindowClip.GetVisibleRegions())
            {
                drawList.PushClipRect(clip.Min, clip.Max, true);
                foreach (var icon in iconLayout)
                {
                    if (icon.Maximum.X < clip.Min.X || icon.Maximum.Y < clip.Min.Y ||
                        icon.Minimum.X > clip.Max.X || icon.Minimum.Y > clip.Max.Y)
                    {
                        continue;
                    }
                    DrawIcon(drawList, icon, skin);
                }
                drawList.PopClipRect();
            }
        }

        private void UpdateActiveCountdownColor(AddonPartyList* partyList)
        {
            for (var memberIndex = 0; memberIndex < 8; memberIndex++)
            {
                if (!tracker.GetMember(memberIndex).Visible)
                {
                    continue;
                }

                var statusIcons = partyList->PartyMembers[memberIndex].StatusIcons;
                for (var index = 0; index < statusIcons.Length; index++)
                {
                    var icon = statusIcons[index].Value;
                    if (icon == null || icon->OwnerNode == null || !icon->OwnerNode->IsVisible())
                    {
                        continue;
                    }

                    var text = icon->GetTextNodeById(2);
                    if (text == null || !text->IsVisible())
                    {
                        continue;
                    }

                    var color = text->TextColor;
                    if (color.G > color.R && color.G > color.B)
                    {
                        activeCountdownColor = OmniTheme.Color(new Vector4(
                            color.R / 255f, color.G / 255f, color.B / 255f, 1f));
                        return;
                    }
                }
            }
        }

        private void BuildMemberLayout
        (
            int             memberIndex,
            uint            classJobID,
            byte            level,
            AddonPartyList* partyList
        )
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

            visibleDefinitions.Clear();
            var mirrored = config.Alignment == SkillMonitorAlignment.Mirror;
            for (var index = 0; index < definitionIndexes.Length; index++)
            {
                var definitionIndex = definitionIndexes[mirrored ? index : definitionIndexes.Length - 1 - index];
                var definition = definitions[definitionIndex];
                if (definition.UnlockLevel <= level &&
                    ShouldShow(tracker.GetState(memberIndex, definitionIndex).DisplayState))
                {
                    visibleDefinitions.Add(definitionIndex);
                }
            }

            if (visibleDefinitions.Count == 0)
            {
                return;
            }

            var memberNode = partyMember.PartyMemberComponent->OwnerNode;
            var iconNode = partyMember.ClassJobIcon;
            var classJobIconScale = iconNode->AtkResNode.GetScale();
            var classJobIconSize = iconNode->Height * classJobIconScale.Y;
            var iconSize = new Vector2(classJobIconSize * Math.Clamp(
                config.IconScale,
                SkillMonitorConfig.DEFAULT_ICON_SCALE * 0.5f,
                SkillMonitorConfig.DEFAULT_ICON_SCALE * 2f));
            var scale = config.IconScale / SkillMonitorConfig.DEFAULT_ICON_SCALE;
            var addonScale = partyList->AtkUnitBase.GetScale();
            var spacing = Math.Clamp(config.IconSpacing, 0f, 12f) * addonScale * scale;
            var anchorX = memberNode->AtkResNode.ScreenX + config.Offset.X * addonScale;
            var perRow = config.IconsPerRow == 0 ? visibleDefinitions.Count : Math.Clamp(config.IconsPerRow, 1, 20);
            for (var start = 0; start < visibleDefinitions.Count; start += perRow)
            {
                var end = Math.Min(start + perRow, visibleDefinitions.Count);
                var rowWidth = (end - start - 1) * spacing;
                for (var index = start; index < end; index++)
                {
                    rowWidth += GetIconSize(definitions[visibleDefinitions[index]], iconSize).X;
                }
                var position = new Vector2(mirrored ? anchorX + spacing : anchorX - spacing - rowWidth,
                    iconNode->ScreenY + config.Offset.Y * addonScale +
                    start / perRow * (iconSize.Y * 1.1f + spacing));
                for (var index = start; index < end; index++)
                {
                    var definitionIndex = visibleDefinitions[index];
                    var definition = definitions[definitionIndex];
                    var currentSize = GetIconSize(definition, iconSize);
                    iconLayout.Add(PrepareIcon(memberIndex, definitionIndex,
                        position + new Vector2(0f, (classJobIconSize - currentSize.Y) * 0.5f), currentSize));
                    position.X += currentSize.X + spacing;
                }
            }
        }

        private IconLayout PrepareIcon(int member, int definitionIndex, Vector2 position, Vector2 size)
        {
            var definition = definitions[definitionIndex];
            var state = tracker.GetState(member, definitionIndex);
            var text = string.Empty;
            var color = activeCountdownColor;
            var fontSize = size.Y * (definition.IsFood ? 0.6f : 0.8f);
            if (definition.IsFood)
            {
                if (state.StatusActive)
                    text = state.StatusText ?? string.Empty;
                color = OmniTheme.Color(KnownColor.PaleTurquoise.ToVector4());
            }
            else if (state.DisplayState == SkillMonitorDisplayState.Active)
                text = state.StatusText ?? string.Empty;
            else if (state.DisplayState == SkillMonitorDisplayState.Cooldown)
            {
                text = state.CooldownText ?? string.Empty;
                color = OmniTheme.Color(Vector4.One);
            }
            var textSize = text.Length == 0 ? Vector2.Zero
                : OmniTextMeasurements.Measure(text) * (fontSize / ImGui.GetFontSize());
            var textPosition = definition.IsFood
                ? new Vector2(position.X + (size.X - textSize.X) * 0.5f, position.Y + size.Y - textSize.Y * 0.55f)
                : position + (size - textSize) * 0.5f;
            var edge = new Vector2(fontSize / 20f + 1f);
            var minimum = Vector2.Min(position - placeholderSize, textPosition - edge);
            var maximum = Vector2.Max(position + size + placeholderSize, textPosition + textSize + edge);
            return new(definitionIndex, state, position, size, text, textPosition, fontSize, color, minimum, maximum);
        }

        private void DrawIcon(ImDrawListPtr drawList, IconLayout icon, HotbarSkin skin)
        {
            var definition = definitions[icon.Definition];
            var state = icon.State;
            var active = state.DisplayState == SkillMonitorDisplayState.Active;
            var coolingDown = !definition.IsFood && state.DisplayState == SkillMonitorDisplayState.Cooldown;
            var nativeFrame = !definition.IsFood && skin.Frame.Handle != nint.Zero;
            var nativeCooldown = coolingDown && skin.Cooldown.Handle != nint.Zero;
            var iconPosition = nativeFrame ? icon.Position + icon.Size * new Vector2(4f / 48f, 3f / 48f) : icon.Position;
            var iconSize = nativeFrame ? icon.Size * (40f / 48f) : icon.Size;
            FramedGameIcon.Draw(
                drawList,
                definition.IconID,
                iconPosition,
                iconSize,
                dimmed: state.DisplayState == SkillMonitorDisplayState.Inactive,
                active: active && !nativeFrame,
                drawFrame: !definition.IsFood && !nativeFrame,
                preserveAspectRatio: definition.IsFood);

            if (coolingDown && !nativeCooldown)
                DrawCooldownMask(drawList, icon.Position, icon.Size, state.CooldownProgress);

            if (nativeFrame)
            {
                drawList.AddImage(skin.Frame.TextureID, icon.Position, icon.Position + icon.Size,
                    skin.Frame.UVMinimum, skin.Frame.UVMaximum, OmniTheme.Color(Vector4.One));
                if (active)
                    FramedGameIcon.DrawActiveFrame(drawList, icon.Position, icon.Size);
            }
            else if (coolingDown && !nativeCooldown)
                FramedGameIcon.DrawFrame(drawList, icon.Position, icon.Size);

            if (nativeCooldown)
                skin.DrawCooldown(drawList, icon.Position, icon.Size, state.CooldownProgress);
            if (icon.Text.Length > 0)
                DrawOutlinedText(drawList, icon.Text, icon.TextPosition, icon.Color, icon.FontSize);
        }

        private readonly record struct IconLayout(int Definition, SkillMonitorRuntimeState State,
            Vector2 Position, Vector2 Size, string Text, Vector2 TextPosition, float FontSize, uint Color,
            Vector2 Minimum, Vector2 Maximum);

        private static void DrawOutlinedText
        (
            ImDrawListPtr drawList,
            string        text,
            Vector2       position,
            uint          color,
            float         fontSize
        )
        {
            var outline = OmniTheme.Color(KnownColor.Black.ToVector4() with
            {
                W = 0.95f
            });
            var edge = fontSize / 20f;
            var font = ImGui.GetFont();
            drawList.AddText(font, fontSize, position + new Vector2(-edge, 0f), outline, text);
            drawList.AddText(font, fontSize, position + new Vector2(edge, 0f), outline, text);
            drawList.AddText(font, fontSize, position + new Vector2(0f, -edge), outline, text);
            drawList.AddText(font, fontSize, position + new Vector2(0f, edge), outline, text);
            drawList.AddText(font, fontSize, position, color, text);
        }

        private static void DrawCooldownMask
        (
            ImDrawListPtr drawList,
            Vector2       position,
            Vector2       size,
            float         progress
        )
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
            var (endPoint, endEdge)     = RayHitRectEdge(center, end, position, rectMaximum);

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
            drawList.PathFillConvex(OmniTheme.Color(KnownColor.Black.ToVector4() with
            {
                W = 0.68f
            }));
        }

        private static (Vector2 Point, int Edge) RayHitRectEdge
        (
            Vector2 center,
            float   angle,
            Vector2 rectMinimum,
            Vector2 rectMaximum
        )
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

            void UpdateHit
            (
                float distance,
                int   edge,
                bool  horizontal
            )
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

                best    = distance;
                hit     = point;
                hitEdge = edge;
            }
        }

        private static Vector2 CornerOf
        (
            Vector2 rectMinimum,
            Vector2 rectMaximum,
            int     edge
        ) => edge switch
        {
            0 => new(rectMaximum.X, rectMinimum.Y),
            1 => rectMaximum,
            2 => new(rectMinimum.X, rectMaximum.Y),
            _ => rectMinimum
        };

        private static int NextClockwise(int edge) => (edge + 1) & 3;

        private static bool IsEndAheadClockwise
        (
            int     edge,
            Vector2 start,
            Vector2 end
        ) => edge switch
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

        private static Vector2 GetIconSize
        (
            SkillMonitorDefinition definition,
            Vector2                size
        ) =>
            definition.IsFood ? OmniTheme.StatusIconSize(size.Y * 1.1f) : size;

        private static bool IsActuallyVisible(AtkUnitBase* addon) =>
            addon != null &&
            addon->IsVisible &&
            addon->RootNode != null &&
            addon->RootNode->IsVisible() &&
            (addon->VisibilityFlags & 5) == 0;
    }

}
