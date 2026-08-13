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
    SkillMonitorTracker tracker)
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

        var drawList = ImGui.GetForegroundDrawList();
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
            if (definitions[definitionIndex].AppliesTo(classJobID) &&
                IsEnabled(definitions[definitionIndex].ConfigID) &&
                ShouldShow(tracker.GetState(memberIndex, definitionIndex).DisplayState))
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
        var groupWidth = (visibleCount - 1) * spacing;
        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            if (definition.AppliesTo(classJobID) &&
                IsEnabled(definition.ConfigID) &&
                ShouldShow(tracker.GetState(memberIndex, definitionIndex).DisplayState))
            {
                groupWidth += GetIconSize(definition, iconSize).X;
            }
        }

        var position = new Vector2(
            memberNode->AtkResNode.ScreenX - spacing - groupWidth + config.Offset.X * partyList->Scale,
            iconNode->ScreenY + config.Offset.Y * partyList->Scale);

        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            var state = tracker.GetState(memberIndex, definitionIndex);
            if (!definition.AppliesTo(classJobID) || !IsEnabled(definition.ConfigID) || !ShouldShow(state.DisplayState))
            {
                continue;
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
            drawFrame: false,
            preserveAspectRatio: definition.IsFood);

        if (state.DisplayState == SkillMonitorDisplayState.Cooldown)
        {
            drawList.AddRectFilled(
                position,
                position + size,
                OmniTheme.Color(OmniTheme.Tokens.Surface with { W = 0.72f }));
            DrawCenteredText(drawList, state.CooldownText ?? string.Empty, position, size);
        }
        if (!definition.IsFood)
        {
            var framePadding = size * 0.045f;
            FramedGameIcon.DrawFrame(
                drawList,
                position - framePadding,
                size + framePadding * 2f,
                state.DisplayState == SkillMonitorDisplayState.Active);
        }
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

    private bool ShouldShow(SkillMonitorDisplayState state) => state switch
    {
        SkillMonitorDisplayState.Active => config.ShowActive,
        SkillMonitorDisplayState.Cooldown => config.ShowOnCooldown,
        SkillMonitorDisplayState.Ready => config.ShowOffCooldown,
        _ => config.ShowDefault
    };

    private static Vector2 GetIconSize(SkillMonitorDefinition definition, Vector2 size) =>
        definition.IsFood ? size * 0.75f : size;

    private static bool IsActuallyVisible(AtkUnitBase* addon) =>
        addon != null &&
        addon->IsVisible &&
        addon->RootNode != null &&
        addon->RootNode->IsVisible() &&
        (addon->VisibilityFlags & 5) == 0;
}
