using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using ContentFinderCondition = Lumina.Excel.Sheets.ContentFinderCondition;

namespace OmniToolbox.TreePublic;

public sealed unsafe class ReadyCheck(ReadyCheckConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ReadyCheckTitle"),
        Description = OmniLoc.Get("ReadyCheckDescription"),
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Combat/ReadyCheck-1.png"
    };

    private const uint InvalidEntityID = 0xE0000000;

    private readonly List<ReadyCheckMember> members = new(48);
    private readonly HashSet<uint> instancedTerritories = [];
    private FeatureLifetime? runtimeLifetime;
    private Hook<AgentReadyCheck.Delegates.InitiateReadyCheck>? initiateHook;
    private Hook<AgentReadyCheck.Delegates.EndReadyCheck>? endHook;
    private ISharedImmediateTexture? readyCheckTexture;
    private ISharedImmediateTexture? notPresentTexture;
    private bool instancedTerritoriesInitialized;
    private bool readyCheckActive;
    private bool overlayVisible;
    private long clearAfterTick;

    public override bool HasSettings => true;

    public override bool DrawSettings() => ReadyCheckPanel.Draw(config);

    protected override void OnEnable()
    {
        PopulateInstancedTerritories();
        readyCheckTexture ??= DalamudServices.TextureProvider.GetFromGame("ui/uld/ReadyCheck_hr1.tex");
        notPresentTexture ??= DalamudServices.TextureProvider.GetFromGameIcon(new GameIconLookup(61504));
        var lifetime = new FeatureLifetime();
        try
        {
            initiateHook = DService.Instance().Hook.HookFromAddress<AgentReadyCheck.Delegates.InitiateReadyCheck>(
                AgentReadyCheck.MemberFunctionPointers.InitiateReadyCheck,
                OnInitiateReadyCheck);
            lifetime.Add(initiateHook.Dispose);
            initiateHook.Enable();

            endHook = DService.Instance().Hook.HookFromAddress<AgentReadyCheck.Delegates.EndReadyCheck>(
                AgentReadyCheck.MemberFunctionPointers.EndReadyCheck,
                OnEndReadyCheck);
            lifetime.Add(endHook.Dispose);
            endHook.Enable();

            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
            {
                throw new InvalidOperationException("Ready-check update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            DalamudServices.PluginInterface.UiBuilder.Draw += DrawOverlay;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawOverlay);

            var services = DService.Instance();
            services.ClientState.Logout += OnLogout;
            lifetime.Add(() => services.ClientState.Logout -= OnLogout);
            services.ClientState.TerritoryChanged += OnTerritoryChanged;
            lifetime.Add(() => services.ClientState.TerritoryChanged -= OnTerritoryChanged);
            services.Condition.ConditionChange += OnConditionChanged;
            lifetime.Add(() => services.Condition.ConditionChange -= OnConditionChanged);
            runtimeLifetime = lifetime;
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                runtimeLifetime = null;
                initiateHook = null;
                endHook = null;
            }

            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            initiateHook = null;
            endHook = null;
            ClearState();
        }
    }

    private void OnInitiateReadyCheck(AgentReadyCheck* agent)
    {
        initiateHook!.Original(agent);
        try
        {
            if (!DService.Instance().ClientState.IsLoggedIn)
            {
                return;
            }

            readyCheckActive = true;
            overlayVisible = true;
            clearAfterTick = 0;
            ProcessReadyCheckResults();
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Ready-check start processing failed.");
        }
    }

    private void OnEndReadyCheck(AgentReadyCheck* agent)
    {
        endHook!.Original(agent);
        try
        {
            if (!DService.Instance().ClientState.IsLoggedIn)
            {
                return;
            }

            readyCheckActive = false;
            overlayVisible = true;
            ProcessReadyCheckResults();
            ScheduleOverlayClear();
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Ready-check completion processing failed.");
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            ClearState();
            return;
        }

        if (readyCheckActive)
        {
            ProcessReadyCheckResults();
            return;
        }

        CheckOverlayExpiration();
    }

    private void OnLogout(int _, int unusedReason) => ClearState();

    private void OnTerritoryChanged(uint _)
    {
        if (config.ClearOnTerritoryChanged)
        {
            ClearState();
        }
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag != ConditionFlag.InCombat || !value || !overlayVisible)
        {
            return;
        }

        if (config.ClearOnCombat ||
            config.ClearOnInstancedCombat && instancedTerritories.Contains(DService.Instance().ClientState.TerritoryType))
        {
            ClearState();
        }
    }

    private void PopulateInstancedTerritories()
    {
        if (instancedTerritoriesInitialized)
        {
            return;
        }

        foreach (var row in LuminaGetter.Get<ContentFinderCondition>())
        {
            if (row.TerritoryType.RowId != 0)
            {
                instancedTerritories.Add(row.TerritoryType.RowId);
            }
        }

        instancedTerritoriesInitialized = true;
    }

    private void ClearState()
    {
        readyCheckActive = false;
        overlayVisible = false;
        clearAfterTick = 0;
        members.Clear();
    }

    private void ScheduleOverlayClear() =>
        clearAfterTick = config.ClearAfterTime
            ? Environment.TickCount64 + Math.Clamp(config.ClearAfterSeconds, 30, 900) * 1_000L
            : 0;

    private void CheckOverlayExpiration()
    {
        if (!overlayVisible)
        {
            return;
        }

        if (!config.ClearAfterTime)
        {
            clearAfterTick = 0;
            return;
        }

        if (clearAfterTick == 0)
        {
            ScheduleOverlayClear();
        }

        if (Environment.TickCount64 >= clearAfterTick)
        {
            ClearState();
        }
    }

    private void ProcessReadyCheckResults()
    {
        var infoProxy = InfoProxyCrossRealm.Instance();
        var groupManager = GroupManager.Instance();
        if (infoProxy == null || groupManager == null)
        {
            return;
        }

        if (infoProxy->IsCrossRealm &&
            !infoProxy->IsInAllianceRaid &&
            groupManager->MainGroup.MemberCount < 1)
        {
            ProcessCrossWorldReadyCheckResults();
        }
        else
        {
            ProcessRegularReadyCheckResults(groupManager);
        }
    }

    private void ProcessRegularReadyCheckResults(GroupManager* groupManager)
    {
        var agent = AgentReadyCheck.Instance();
        if (agent == null)
        {
            return;
        }

        members.Clear();
        var readyCheckEntries = agent->ReadyCheckEntries;
        var partyCount = Math.Min((int)groupManager->MainGroup.MemberCount, 8);
        var localPlayerEntityID = DService.Instance().ObjectTable.LocalPlayer?.EntityID ?? 0;
        var foundSelf = false;
        for (var index = 0; index < partyCount; index++)
        {
            var partyMember = groupManager->MainGroup.GetPartyMemberByIndex(index);
            if (partyMember == null)
            {
                continue;
            }

            var isSelf = partyMember->EntityId == localPlayerEntityID;
            var statusIndex = isSelf ? 0 : foundSelf ? index : index + 1;
            foundSelf |= isSelf;
            if (statusIndex >= readyCheckEntries.Length ||
                !ShouldDrawStatus(readyCheckEntries[statusIndex].Status))
            {
                continue;
            }

            members.Add(new(
                partyMember->ContentId,
                partyMember->EntityId,
                readyCheckEntries[statusIndex].Status));
        }

        for (var index = partyCount; index < readyCheckEntries.Length; index++)
        {
            var entry = readyCheckEntries[index];
            var entityID = (uint)entry.ContentId;
            if (entry.ContentId == 0 ||
                entityID == InvalidEntityID ||
                !ShouldDrawStatus(entry.Status))
            {
                continue;
            }

            members.Add(new(0, entityID, entry.Status));
        }
    }

    private void ProcessCrossWorldReadyCheckResults()
    {
        var agent = AgentReadyCheck.Instance();
        if (agent == null)
        {
            return;
        }

        members.Clear();
        var readyCheckEntries = agent->ReadyCheckEntries;
        for (var index = 0; index < readyCheckEntries.Length; index++)
        {
            var entry = readyCheckEntries[index];
            if (!ShouldDrawStatus(entry.Status))
            {
                continue;
            }

            var member = entry.ContentId > uint.MaxValue
                ? InfoProxyCrossRealm.GetMemberByContentId(entry.ContentId)
                : InfoProxyCrossRealm.GetMemberByEntityId((uint)entry.ContentId);
            if (member != null)
            {
                members.Add(new(member->ContentId, member->EntityId, entry.Status));
            }
        }
    }

    private void DrawOverlay()
    {
        if (!overlayVisible || members.Count == 0)
        {
            return;
        }

        try
        {
            var infoProxy = InfoProxyCrossRealm.Instance();
            var groupManager = GroupManager.Instance();
            var agentHud = AgentHUD.Instance();
            if (infoProxy == null || groupManager == null || agentHud == null)
            {
                return;
            }

            var readyCheckTextureHandle = readyCheckTexture?.GetWrapOrDefault()?.Handle ?? default;
            var notPresentTextureHandle = notPresentTexture?.GetWrapOrDefault()?.Handle ?? default;
            if (readyCheckTextureHandle == nint.Zero && notPresentTextureHandle == nint.Zero)
            {
                return;
            }

            var drawList = ImGui.GetForegroundDrawList();
            var partyList = AddonHelper.GetByName<AddonPartyList>("_PartyList");
            var alliance1List = AddonHelper.GetByName<AddonAllianceListX>("_AllianceList1");
            var alliance2List = AddonHelper.GetByName<AddonAllianceListX>("_AllianceList2");
            var crossWorldAllianceList = AddonHelper.GetByName<AddonAlliance48>("Alliance48");
            foreach (var member in members)
            {
                if (!TryGetHudPosition(
                        member.ContentID,
                        member.EntityID,
                        infoProxy,
                        groupManager,
                        agentHud,
                        out var position))
                {
                    continue;
                }

                if (position.GroupNumber == 0)
                {
                    DrawOnPartyList(
                        position.MemberIndex,
                        member.Status,
                        partyList,
                        drawList,
                        readyCheckTextureHandle,
                        notPresentTextureHandle);
                }
                else if (position.CrossWorld)
                {
                    DrawOnCrossWorldAllianceList(
                        position.GroupNumber,
                        position.MemberIndex,
                        member.Status,
                        crossWorldAllianceList,
                        drawList,
                        readyCheckTextureHandle,
                        notPresentTextureHandle);
                }
                else if (position.GroupNumber is 1 or 2)
                {
                    DrawOnAllianceList(
                        position.MemberIndex,
                        member.Status,
                        position.GroupNumber == 1 ? alliance1List : alliance2List,
                        drawList,
                        readyCheckTextureHandle,
                        notPresentTextureHandle);
                }
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Ready-check overlay drawing failed.");
        }
    }

    private static bool TryGetHudPosition(
        ulong contentID,
        uint entityID,
        InfoProxyCrossRealm* infoProxy,
        GroupManager* groupManager,
        AgentHUD* agentHud,
        out PartyListPosition position)
    {
        position = default;
        if (contentID == 0 && entityID is 0 or InvalidEntityID)
        {
            return false;
        }

        if (groupManager->MainGroup.MemberCount > 0)
        {
            for (var index = 0; index < 8; index++)
            {
                var partyMember = agentHud->PartyMembers[index];
                if ((contentID > 0 && contentID == partyMember.ContentId) ||
                    (entityID is > 0 and not InvalidEntityID && entityID == partyMember.EntityId))
                {
                    position = new(false, 0, index);
                    return true;
                }
            }

            for (var index = 0; index < 40; index++)
            {
                if (entityID is > 0 and not InvalidEntityID && entityID == agentHud->RaidMemberIds[index])
                {
                    position = new(false, index / 8 + 1, index % 8);
                    return true;
                }
            }
        }
        else if (infoProxy->IsCrossRealm && contentID != 0)
        {
            var crossRealmMember = InfoProxyCrossRealm.GetMemberByContentId(contentID);
            if (crossRealmMember != null)
            {
                position = new(
                    !infoProxy->IsInAllianceRaid,
                    crossRealmMember->GroupIndex,
                    crossRealmMember->MemberIndex);
                return true;
            }
        }

        return false;
    }

    private void DrawOnPartyList(
        int index,
        ReadyCheckStatus status,
        AddonPartyList* partyList,
        ImDrawListPtr drawList,
        ImTextureID readyCheckTextureHandle,
        ImTextureID notPresentTextureHandle)
    {
        if (index is < 0 or > 7 || partyList == null || !IsActuallyVisible(&partyList->AtkUnitBase))
        {
            return;
        }

        var partyMember = partyList->PartyMembers[index];
        if (partyMember.PartyMemberComponent == null ||
            partyMember.PartyMemberComponent->OwnerNode == null ||
            partyMember.ClassJobIcon == null ||
            partyList->PartyListAtkResNode == null)
        {
            return;
        }

        var memberNode = partyMember.PartyMemberComponent->OwnerNode;
        var iconNode = partyMember.ClassJobIcon;
        var iconSize = new Vector2(iconNode->Width / 1.5f, iconNode->Height / 1.5f)
                       * Math.Clamp(config.PartyIconScale, 0.3f, 5f)
                       * partyList->Scale;
        var iconPosition = new Vector2(
            partyList->X + memberNode->AtkResNode.X * partyList->Scale +
            iconNode->X * partyList->Scale + iconNode->Width * partyList->Scale / 2f,
            partyList->Y + partyList->PartyListAtkResNode->Y +
            memberNode->AtkResNode.Y * partyList->Scale +
            iconNode->Y * partyList->Scale + iconNode->Height * partyList->Scale / 2f);
        DrawReadyCheckIcon(
            drawList,
            status,
            iconPosition + (new Vector2(-7f, -5f) + config.PartyIconOffset) * partyList->Scale,
            iconSize,
            readyCheckTextureHandle,
            notPresentTextureHandle);
    }

    private void DrawOnAllianceList(
        int index,
        ReadyCheckStatus status,
        AddonAllianceListX* allianceList,
        ImDrawListPtr drawList,
        ImTextureID readyCheckTextureHandle,
        ImTextureID notPresentTextureHandle)
    {
        if (index is < 0 or > 7 || allianceList == null || !IsActuallyVisible(&allianceList->AtkUnitBase))
        {
            return;
        }

        var allianceMember = allianceList->AllianceMembers[index];
        if (allianceMember.ComponentBase == null ||
            allianceMember.ComponentBase->OwnerNode == null ||
            allianceMember.ClassJobImageNode == null)
        {
            return;
        }

        var memberNode = allianceMember.ComponentBase->OwnerNode;
        var iconNode = allianceMember.ClassJobImageNode;
        var iconSize = new Vector2(iconNode->Width / 3f, iconNode->Height / 3f)
                       * Math.Clamp(config.AllianceIconScale, 0.3f, 5f)
                       * allianceList->Scale;
        var iconPosition = new Vector2(
            allianceList->X + memberNode->AtkResNode.X * allianceList->Scale +
            iconNode->X * allianceList->Scale + iconNode->Width * allianceList->Scale / 2f,
            allianceList->Y + memberNode->AtkResNode.Y * allianceList->Scale +
            iconNode->Y * allianceList->Scale + iconNode->Height * allianceList->Scale / 2f);
        DrawReadyCheckIcon(
            drawList,
            status,
            iconPosition + config.AllianceIconOffset * allianceList->Scale,
            iconSize,
            readyCheckTextureHandle,
            notPresentTextureHandle);
    }

    private void DrawOnCrossWorldAllianceList(
        int allianceIndex,
        int memberIndex,
        ReadyCheckStatus status,
        AddonAlliance48* allianceList,
        ImDrawListPtr drawList,
        ImTextureID readyCheckTextureHandle,
        ImTextureID notPresentTextureHandle)
    {
        if (allianceIndex is < 1 or > 5 ||
            memberIndex is < 0 or > 7 ||
            allianceList == null ||
            !IsActuallyVisible(&allianceList->AtkUnitBase))
        {
            return;
        }

        var alliance = allianceList->Alliances[allianceIndex - 1];
        if (alliance.ComponentBase == null || alliance.ComponentBase->OwnerNode == null)
        {
            return;
        }

        var member = alliance.Members[memberIndex];
        if (member.AtkComponentBase == null ||
            member.AtkComponentBase->OwnerNode == null ||
            member.ClassJobImageNode == null)
        {
            return;
        }

        var allianceNode = alliance.ComponentBase->OwnerNode;
        var memberNode = member.AtkComponentBase->OwnerNode;
        var iconNode = member.ClassJobImageNode;
        var iconSize = new Vector2(iconNode->Width / 2f, iconNode->Height / 2f)
                       * Math.Clamp(config.CrossWorldAllianceIconScale, 0.3f, 5f)
                       * allianceList->Scale;
        var iconPosition = new Vector2(
            allianceList->X + allianceNode->AtkResNode.X * allianceList->Scale +
            memberNode->AtkResNode.X * allianceList->Scale +
            iconNode->X * allianceList->Scale + iconNode->Width * allianceList->Scale / 2f,
            allianceList->Y + allianceNode->AtkResNode.Y * allianceList->Scale +
            memberNode->AtkResNode.Y * allianceList->Scale +
            iconNode->Y * allianceList->Scale + iconNode->Height * allianceList->Scale / 2f);
        DrawReadyCheckIcon(
            drawList,
            status,
            iconPosition + config.CrossWorldAllianceIconOffset * allianceList->Scale,
            iconSize,
            readyCheckTextureHandle,
            notPresentTextureHandle);
    }

    private static void DrawReadyCheckIcon(
        ImDrawListPtr drawList,
        ReadyCheckStatus status,
        Vector2 position,
        Vector2 size,
        ImTextureID readyCheckTextureHandle,
        ImTextureID notPresentTextureHandle)
    {
        var min = PixelSnap(position);
        var max = PixelSnap(position + size);
        if (status == ReadyCheckStatus.MemberNotPresent)
        {
            if (notPresentTextureHandle != nint.Zero)
            {
                drawList.AddImage(notPresentTextureHandle, min, max);
            }

            return;
        }

        if (readyCheckTextureHandle == nint.Zero)
        {
            return;
        }

        if (status == ReadyCheckStatus.NotReady)
        {
            drawList.AddImage(
                readyCheckTextureHandle,
                min,
                max,
                new Vector2(0.5f, 0f),
                Vector2.One);
        }
        else if (status == ReadyCheckStatus.Ready)
        {
            drawList.AddImage(
                readyCheckTextureHandle,
                min,
                max,
                Vector2.Zero,
                new Vector2(0.5f, 1f));
        }
    }

    private static bool ShouldDrawStatus(ReadyCheckStatus status) =>
        status is ReadyCheckStatus.Ready or ReadyCheckStatus.NotReady or ReadyCheckStatus.MemberNotPresent;

    private static bool IsActuallyVisible(AtkUnitBase* addon) =>
        addon != null &&
        addon->IsVisible &&
        addon->RootNode != null &&
        addon->RootNode->IsVisible() &&
        (addon->VisibilityFlags & 5) == 0;

    private static Vector2 PixelSnap(Vector2 value) =>
        new(MathF.Round(value.X), MathF.Round(value.Y));

    private readonly struct ReadyCheckMember(ulong contentID, uint entityID, ReadyCheckStatus status)
    {
        public ulong ContentID { get; } = contentID;

        public uint EntityID { get; } = entityID;

        public ReadyCheckStatus Status { get; } = status;
    }

    private readonly struct PartyListPosition(bool crossWorld, int groupNumber, int memberIndex)
    {
        public bool CrossWorld { get; } = crossWorld;

        public int GroupNumber { get; } = groupNumber;

        public int MemberIndex { get; } = memberIndex;
    }
}

internal static class ReadyCheckPanel
{
    public static bool Draw(ReadyCheckConfig config)
    {
        var changed = DrawClearSettings(config);
        ImGui.Spacing();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ReadyCheck.IconSettings"));
        using var table = ImRaii.Table(
            "##readyCheckIconSettings",
            3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupColumn(OmniLoc.Get("Feature.ReadyCheck.List"), ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.ReadyCheck.Offset"), ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.ReadyCheck.Scale"), ImGuiTableColumnFlags.WidthStretch, 0.8f);
        OmniControls.BeginTableHeaderRow();
        OmniControls.TableHeader(OmniLoc.Get("Feature.ReadyCheck.List"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.ReadyCheck.Offset"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.ReadyCheck.Scale"));
        var partyOffset = config.PartyIconOffset;
        var partyScale = config.PartyIconScale;
        changed |= DrawIconSettings(
            "party",
            "Feature.ReadyCheck.PartyList",
            ref partyOffset,
            ref partyScale);
        config.PartyIconOffset = partyOffset;
        config.PartyIconScale = partyScale;

        var allianceOffset = config.AllianceIconOffset;
        var allianceScale = config.AllianceIconScale;
        changed |= DrawIconSettings(
            "alliance",
            "Feature.ReadyCheck.AllianceList",
            ref allianceOffset,
            ref allianceScale);
        config.AllianceIconOffset = allianceOffset;
        config.AllianceIconScale = allianceScale;

        var crossWorldAllianceOffset = config.CrossWorldAllianceIconOffset;
        var crossWorldAllianceScale = config.CrossWorldAllianceIconScale;
        changed |= DrawIconSettings(
            "crossWorldAlliance",
            "Feature.ReadyCheck.CrossWorldAllianceList",
            ref crossWorldAllianceOffset,
            ref crossWorldAllianceScale);
        config.CrossWorldAllianceIconOffset = crossWorldAllianceOffset;
        config.CrossWorldAllianceIconScale = crossWorldAllianceScale;
        return changed;
    }

    private static bool DrawClearSettings(ReadyCheckConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##readyCheckClearSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##readyCheckClearTerritory", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##readyCheckClearCombat", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##readyCheckClearDutyCombat", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##readyCheckClearTime", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (DrawCheckbox(
                "Feature.ReadyCheck.ClearOnTerritoryChanged",
                "territory",
                config.ClearOnTerritoryChanged,
                out var clearOnTerritoryChanged))
        {
            config.ClearOnTerritoryChanged = clearOnTerritoryChanged;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox(
                "Feature.ReadyCheck.ClearOnCombat",
                "combat",
                config.ClearOnCombat,
                out var clearOnCombat))
        {
            config.ClearOnCombat = clearOnCombat;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox(
                "Feature.ReadyCheck.ClearOnInstancedCombat",
                "instancedCombat",
                config.ClearOnInstancedCombat,
                out var clearOnInstancedCombat))
        {
            config.ClearOnInstancedCombat = clearOnInstancedCombat;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox(
                "Feature.ReadyCheck.ClearAfterTime",
                "time",
                config.ClearAfterTime,
                out var clearAfterTime))
        {
            config.ClearAfterTime = clearAfterTime;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(10f));
        using (ImRaii.Disabled(!config.ClearAfterTime))
        {
            ImGui.SetNextItemWidth(
                ImGui.CalcTextSize("000").X + ImGui.GetStyle().FramePadding.X * 4f);
            var seconds = Math.Clamp(config.ClearAfterSeconds, 30, 900);
            if (OmniControls.InputInt("##readyCheckClearAfterSeconds", ref seconds, 0, 0))
            {
                config.ClearAfterSeconds = Math.Clamp(seconds, 30, 900);
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.ReadyCheck.Seconds"));
        }

        return changed;
    }

    private static bool DrawCheckbox(
        string labelKey,
        string id,
        bool current,
        out bool value)
    {
        value = current;
        return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##readyCheckClear{id}", ref value);
    }

    private static bool DrawIconSettings(
        string id,
        string labelKey,
        ref Vector2 offset,
        ref float scale)
    {
        var changed = false;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        OmniControls.TableTextCentered(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        OmniControls.DragFloat2(
            $"##readyCheck{id}Offset",
            ref offset,
            1f,
            -100f,
            100f,
            "%.0f",
            ImGui.GetContentRegionAvail().X);

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.TableNextColumn();
        OmniControls.DragFloat(
            $"##readyCheck{id}Scale",
            ref scale,
            0.05f,
            0.3f,
            5f,
            "%.2f",
            ImGui.GetContentRegionAvail().X,
            ImGuiSliderFlags.AlwaysClamp);

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        return changed;
    }
}

[Serializable]
public sealed class ReadyCheckConfig
{
    public bool ClearOnTerritoryChanged { get; set; } = true;
    public bool ClearOnCombat { get; set; }
    public bool ClearOnInstancedCombat { get; set; } = true;
    public bool ClearAfterTime { get; set; }
    public int ClearAfterSeconds { get; set; } = 60;
    public Vector2 PartyIconOffset { get; set; }
    public Vector2 AllianceIconOffset { get; set; }
    public Vector2 CrossWorldAllianceIconOffset { get; set; }
    public float PartyIconScale { get; set; } = 1f;
    public float AllianceIconScale { get; set; } = 1f;
    public float CrossWorldAllianceIconScale { get; set; } = 1f;
}
