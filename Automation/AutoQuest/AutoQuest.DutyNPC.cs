using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using DawnContentData = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentDawnInterface.DawnContentData;
using DawnMemberData = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentDawnInterface.DawnMemberData;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    private static unsafe string? SelectNPCMode(ContentFinderCondition content, out string? error)
    {
        error = "player_party_required";
        DawnContent? dawn = null;
        foreach (var row in LuminaGetter.Get<DawnContent>())
            if (row.Content.RowId == content.RowId) { dawn = row; break; }
        var hasSupport = false;
        if (dawn is { } entry && LuminaGetter.TryGetSubRowAll<DawnContentParticipable>(entry.RowId, out var participants))
            foreach (var participant in participants)
                if (participant.DawnQuestMember.RowId != 0) { hasSupport = true; break; }
        var atk = RaptureAtkModule.Instance();
        var nativeModes = atk == null ? 0 : atk->IsDawnSupported(content.RowId);
        hasSupport |= (nativeModes & 1) != 0;
        var hasTrust = dawn is { Unknown13: true } || (nativeModes & 2) != 0;
        GcArmyCapture? squadron = null;
        var squadronIndex = 0;
        foreach (var row in LuminaGetter.Get<GcArmyCapture>())
        {
            if (row.Content.RowId == 0) continue;
            if (row.Content.RowId == content.RowId) { squadron = row; break; }
            squadronIndex++;
        }
        if (!hasSupport && !hasTrust && squadron is null) return null;
        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn || !DalamudServices.PlayerState.IsLoaded)
        {
            error = "not_logged_in";
            return null;
        }
        if (services.Condition.IsBetweenAreas || !UIModule.IsScreenReady())
        {
            error = "resource_loading";
            return null;
        }
        if (services.PartyList.Length != 0)
        {
            error = "party_not_empty";
            return null;
        }
        var player = services.ObjectTable.LocalPlayer;
        if (player is null || !LuminaGetter.TryGetRow<ClassJob>(player.ClassJob.RowId, out var job))
        {
            error = "npc_player_state_unavailable";
            return null;
        }
        if (services.Condition[ConditionFlag.Unconscious])
        {
            error = "player_dead";
            return null;
        }
        if (services.Condition.IsOccupiedInEvent || services.Condition.IsCasting || player.IsCasting ||
            services.Condition.IsBoundByDuty || services.Condition.IsOnWorldTravel || services.Condition[ConditionFlag.InCombat])
        {
            error = "player_busy";
            return null;
        }
        if (job.IsLimitedJob || job.Role is not (1 or 2 or 3 or 4))
        {
            error = "npc_job_unsupported";
            return null;
        }
        if (!UIState.IsInstanceContentUnlocked(content.Content.RowId))
        {
            error = "duty_locked";
            return null;
        }
        if (player.Level < content.ClassJobLevelRequired)
        {
            error = "level_insufficient";
            return null;
        }
        string? firstFailure = null;
        if (hasSupport)
        {
            var agent = AgentDawnStory.Instance();
            error = "npc_support_state_unavailable";
            if (agent != null && atk != null)
            {
                if ((nativeModes & 1) == 0) error = "npc_support_unavailable";
                else if (!agent->IsAgentActive() && !agent->IsActivatable()) error = "npc_entry_unavailable";
                else if (agent->Data != null && EvaluateDawnNPCParty(content, &agent->Data->ContentData,
                    &agent->Data->MemberData, job.Role, false, out error)) return "Support";
            }
            firstFailure = error;
        }
        if (hasTrust)
        {
            var agent = AgentDawn.Instance();
            error = "npc_trust_state_unavailable";
            if (dawn is { } trustContent && agent != null && atk != null)
            {
                if (!UIState.IsInstanceContentCompleted(content.Content.RowId) &&
                    (trustContent.Unknown0 == 0 || !QuestManager.IsQuestComplete(trustContent.Unknown0))) error = "npc_trust_locked";
                else if ((nativeModes & 2) == 0) error = "npc_trust_unavailable";
                else if (!agent->IsAgentActive() && !agent->IsActivatable()) error = "npc_entry_unavailable";
                else if (agent->Data != null && EvaluateDawnNPCParty(content, &agent->Data->ContentData,
                    &agent->Data->MemberData, job.Role, true, out error)) return "Trust";
            }
            firstFailure ??= error;
        }
        if (squadron is { } capture)
        {
            if (EvaluateSquadron(content, capture, squadronIndex, job.Role, out error)) return "Squadron";
            firstFailure ??= error;
        }
        error = firstFailure ?? "npc_party_conditions_unmet";
        return null;
    }

    private static unsafe bool EvaluateDawnNPCParty(ContentFinderCondition content, DawnContentData* contents,
        DawnMemberData* data, byte playerRole, bool checkLevels, out string? error)
    {
        error = "npc_member_state_unavailable";
        if (contents->ContentEntryCount > contents->ContentEntries.Length ||
            contents->SelectedContentEntry >= contents->ContentEntryCount ||
            data->CurrentMembersIndex >= data->MemberCounts.Length) return false;
        var selected = contents->ContentEntries[contents->SelectedContentEntry];
        if (selected.ContentFinderConditionId != content.RowId) return false;
        if (selected.IsNotYetSupported || checkLevels && !selected.IsTrustAvailable)
        {
            error = "npc_party_conditions_unmet";
            return false;
        }
        var count = data->GetMemberCount(data->CurrentMembersIndex);
        var members = data->GetMembers(data->CurrentMembersIndex);
        if (members == null || count is 0 or > 12) return false;
        if (!TryGetNPCComposition(content, playerRole, out var needed))
        {
            error = "npc_party_conditions_unmet";
            return false;
        }
        Span<byte> roles = stackalloc byte[12];
        Span<byte> withoutLevels = stackalloc byte[12];
        roles.Clear();
        withoutLevels.Clear();
        for (var index = 0; index < count; index++)
        {
            var member = members[index];
            if (member.ClassJob == 0) continue;
            withoutLevels[index] = (byte)(member.Roles & ~member.BlockedRoles & 7);
            if (!checkLevels || member.Level >= content.ClassJobLevelRequired)
                roles[index] = withoutLevels[index];
        }
        if (HasNPCComposition(roles[..count], needed[0], needed[1], needed[2]))
        {
            error = null;
            return true;
        }
        error = checkLevels && HasNPCComposition(withoutLevels[..count], needed[0], needed[1], needed[2])
            ? "npc_level_insufficient" : "npc_party_conditions_unmet";
        return false;
    }

    private static unsafe bool EvaluateSquadron(ContentFinderCondition content, GcArmyCapture capture,
        int missionIndex, byte playerRole, out string? error)
    {
        error = "npc_squadron_state_unavailable";
        var player = PlayerState.Instance();
        var inventory = InventoryManager.Instance();
        if (player == null || inventory == null) return false;
        if (player->GrandCompany is not (1 or 2 or 3))
        {
            error = "npc_squadron_locked";
            return false;
        }
        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer || localPlayer.Level < capture.ClassJobLevelRequired)
        {
            error = "level_insufficient";
            return false;
        }
        if (inventory->GetCompanySeals(player->GrandCompany) < capture.RequiredSeals)
        {
            error = "company_seals_insufficient";
            return false;
        }
        var manager = GcArmyManager.Instance();
        var agents = AgentModule.Instance();
        var agent = agents == null ? null : agents->GetAgentByInternalId(AgentId.GcArmyCapture);
        if (manager == null || manager->Data == null || agent == null) return false;
        if (!agent->IsAgentActive() && !agent->IsActivatable())
        {
            error = "npc_entry_unavailable";
            return false;
        }
        var count = manager->GetMemberCount();
        if (count > manager->Data->Members.Length) return false;
        if (!TryGetNPCComposition(content, playerRole, out var needed) || needed[0] + needed[1] + needed[2] != 3)
        {
            error = "npc_party_conditions_unmet";
            return false;
        }
        Span<byte> roles = stackalloc byte[8];
        Span<byte> withoutLevels = stackalloc byte[8];
        Span<byte> withBusyMembers = stackalloc byte[8];
        roles.Clear();
        withoutLevels.Clear();
        withBusyMembers.Clear();
        for (uint index = 0; index < count; index++)
        {
            var member = manager->GetMember(index);
            if (member == null || !LuminaGetter.TryGetRow<ClassJob>(member->ClassJob, out var job)) continue;
            var role = job.Role switch { 1 => 1, 2 or 3 => 2, 4 => 4, _ => 0 };
            var ready = (member->Flags & (GcArmyMemberFlag.OnSquadronMission | GcArmyMemberFlag.InTraining)) == 0;
            if (ready) withoutLevels[(int)index] = (byte)role;
            if (member->Level < capture.ClassJobLevelRequired) continue;
            withBusyMembers[(int)index] = (byte)role;
            if (ready) roles[(int)index] = (byte)role;
        }
        if (!HasNPCComposition(roles, needed[0], needed[1], needed[2]))
        {
            if (HasNPCComposition(withoutLevels, needed[0], needed[1], needed[2])) error = "npc_level_insufficient";
            else if (HasNPCComposition(withBusyMembers, needed[0], needed[1], needed[2])) error = "npc_squadron_members_busy";
            else error = "npc_party_conditions_unmet";
            return false;
        }
        var addon = AddonHelper.GetByName<AddonGcArmyCapture>("GcArmyCapture");
        if (addon == null || !agent->IsAddonReady() || addon->MissionList == null ||
            missionIndex < 0 || missionIndex >= addon->MissionList->ListLength) return false;
        if (addon->MissionList->GetItemDisabledState(missionIndex))
        {
            error = "npc_squadron_conditions_unmet";
            return false;
        }
        error = null;
        return true;
    }

    private static bool TryGetNPCComposition(ContentFinderCondition content, byte playerRole, out int[] needed)
    {
        needed = [];
        if (!LuminaGetter.TryGetRow<ContentMemberType>(content.ContentMemberType.RowId, out var composition)) return false;
        needed =
        [
            composition.TanksPerParty - (playerRole == 1 ? 1 : 0),
            composition.MeleesPerParty + composition.RangedPerParty - (playerRole is 2 or 3 ? 1 : 0),
            composition.HealersPerParty - (playerRole == 4 ? 1 : 0)
        ];
        return needed.All(static count => count >= 0) && needed.Sum() is > 0 and <= 7;
    }

    private static bool HasNPCComposition(ReadOnlySpan<byte> roles, int tanks, int damage, int healers)
    {
        if (tanks + damage + healers == 0) return true;
        if (roles.Length < tanks + damage + healers) return false;
        var role = roles[0];
        var remaining = roles[1..];
        return tanks > 0 && (role & 1) != 0 && HasNPCComposition(remaining, tanks - 1, damage, healers) ||
            damage > 0 && (role & 2) != 0 && HasNPCComposition(remaining, tanks, damage - 1, healers) ||
            healers > 0 && (role & 4) != 0 && HasNPCComposition(remaining, tanks, damage, healers - 1) ||
            HasNPCComposition(remaining, tanks, damage, healers);
    }
}
