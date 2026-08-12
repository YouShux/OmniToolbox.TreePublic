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
    SkillMonitorTracker tracker)
{
    public void Draw()
    {
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            return;
        }

        var partyList = AddonHelper.GetByName<AddonPartyList>("_PartyList");
        if (partyList == null || !IsActuallyVisible(&partyList->AtkUnitBase))
        {
            return;
        }

        var drawList = ImGui.GetForegroundDrawList();
        var localEntityID = DService.Instance().ObjectTable.LocalPlayer?.EntityID ?? 0;
        for (var memberIndex = 0; memberIndex < 8; memberIndex++)
        {
            var member = tracker.GetMember(memberIndex);
            if (!member.Visible || (!config.ShowSelf && member.EntityID == localEntityID))
            {
                continue;
            }

            DrawMember(memberIndex, member.ClassJobID, partyList, drawList);
        }
    }

    private void DrawMember(int memberIndex, uint classJobID, AddonPartyList* partyList, ImDrawListPtr drawList)
    {
        var partyMember = partyList->PartyMembers[memberIndex];
        if (partyMember.PartyMemberComponent == null ||
            partyMember.PartyMemberComponent->OwnerNode == null ||
            partyMember.ClassJobIcon == null ||
            partyList->PartyListAtkResNode == null)
        {
            return;
        }

        var visibleCount = 0;
        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            if (definitions[definitionIndex].AppliesTo(classJobID) && IsEnabled(definitions[definitionIndex].ConfigID))
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
        var iconSize = new Vector2(classJobIconSize * Math.Clamp(config.IconScale, 0.5f, 2f));
        var spacing = Math.Clamp(config.IconSpacing, 0f, 12f) * partyList->Scale;
        var groupWidth = visibleCount * iconSize.X + (visibleCount - 1) * spacing;
        var position = new Vector2(
            memberNode->AtkResNode.ScreenX - spacing - groupWidth + config.Offset.X * partyList->Scale,
            iconNode->ScreenY + (classJobIconSize - iconSize.Y) * 0.5f + config.Offset.Y * partyList->Scale);

        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            if (!definition.AppliesTo(classJobID) || !IsEnabled(definition.ConfigID))
            {
                continue;
            }

            DrawIcon(drawList, definition, tracker.GetState(memberIndex, definitionIndex), position, iconSize);
            position.X += iconSize.X + spacing;
        }
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
            dimmed: state.DisplayState is SkillMonitorDisplayState.Unknown or SkillMonitorDisplayState.Inactive,
            drawFrame: false);

        if (state.DisplayState == SkillMonitorDisplayState.Cooldown)
        {
            drawList.AddRectFilled(
                position,
                position + size,
                OmniTheme.Color(OmniTheme.Tokens.Surface with { W = 0.72f }));
            DrawCenteredText(drawList, state.CooldownText ?? string.Empty, position, size);
        }
        else if (state.DisplayState == SkillMonitorDisplayState.Unknown)
        {
            DrawCenteredText(drawList, "?", position, size);
        }

        FramedGameIcon.DrawFrame(
            drawList,
            position,
            size,
            state.DisplayState == SkillMonitorDisplayState.Active);
    }

    private static void DrawCenteredText(ImDrawListPtr drawList, string text, Vector2 position, Vector2 size)
    {
        var textSize = ImGui.CalcTextSize(text);
        var textPosition = position + (size - textSize) * 0.5f;
        drawList.AddText(
            textPosition + Vector2.One,
            OmniTheme.Color(OmniTheme.Tokens.Surface with { W = 0.95f }),
            text);
        drawList.AddText(textPosition, OmniTheme.Color(OmniTheme.Tokens.Text), text);
    }

    private bool IsEnabled(uint actionID) =>
        config.EnabledActions.TryGetValue(actionID, out var enabled) && enabled;

    private static bool IsActuallyVisible(AtkUnitBase* addon) =>
        addon != null &&
        addon->IsVisible &&
        addon->RootNode != null &&
        addon->RootNode->IsVisible() &&
        (addon->VisibilityFlags & 5) == 0;
}
