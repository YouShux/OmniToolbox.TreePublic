using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using LuminaStatus = Lumina.Excel.Sheets.Status;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Game;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;

namespace OmniToolbox.TreePublic;

public sealed unsafe class RaiseDispelEnhancement(RaiseDispelEnhancementConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("RaiseDispelEnhancementTitle"),
        Description = OmniLoc.Get("RaiseDispelEnhancementDescription"),
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Combat/RaiseDispelEnhancement-1.png"
    };

    private const uint RaiseStatusID = 148;
    private const uint DispelIconID = 215530;
    private static readonly string[] DisplayColumns =
        ["Party", "Alliance", "ShowWorldIcon", "ShowWorldText", "CasterName", "CastProgress", "List"];
    private readonly Dictionary<ulong, ActorState> states = [];
    private readonly Dictionary<ulong, string> names = [];
    private readonly Dictionary<ulong, (int Index, ulong ObjectID)> actorObjects = [];
    private readonly Dictionary<uint, ISharedImmediateTexture> statusIconTextures = [];
    private FeatureLifetime? runtimeLifetime;
    private ISharedImmediateTexture? dispelIcon;
    private uint defaultRaiseIconID;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        using (var table = ImRaii.Table("##raiseDispelMatrix", 8,
                   ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##kind", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(78f));
                foreach (var key in DisplayColumns)
                {
                    ImGui.TableSetupColumn(OmniLoc.Get($"Feature.RaiseDispelEnhancement.{key}"));
                }
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                DrawMatrixLabel(string.Empty);
                foreach (var key in DisplayColumns)
                {
                    DrawMatrixLabel(OmniLoc.Get($"Feature.RaiseDispelEnhancement.{key}"));
                }
                changed |= DrawReminderRow("Raise", config.GetRaiseOptions());
                changed |= DrawMatrixCheckbox("raiseList", config.ShowRaiseOnList, value => config.ShowRaiseOnList = value);
                changed |= DrawReminderRow("Dispel", config.GetDispelOptions());
                changed |= DrawMatrixCheckbox("dispelList", config.ShowDispelOnList, value => config.ShowDispelOnList = value);
            }
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        using (var table = ImRaii.Table("##raiseDispelScales", 2, ImGuiTableFlags.SizingStretchSame,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawScaleSlider(config.IconScale, "Feature.RaiseDispelEnhancement.IconScale", "IconScale",
                    value => config.IconScale = value);
                ImGui.TableNextColumn();
                changed |= DrawScaleSlider(config.WorldTextScale, "Feature.RaiseDispelEnhancement.WorldTextScale", "WorldTextScale",
                    value => config.WorldTextScale = value);
            }
        }
        using (var table = ImRaii.Table("##raiseDispelColors", 4, ImGuiTableFlags.SizingStretchSame,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawColorEdit(config.RaiseListColor, "Feature.RaiseDispelEnhancement.RaiseListColor", "RaiseListColor",
                    value => config.RaiseListColor = value);
                ImGui.TableNextColumn();
                changed |= DrawColorEdit(config.DispelListColor, "Feature.RaiseDispelEnhancement.DispelListColor", "DispelListColor",
                    value => config.DispelListColor = value);
                ImGui.TableNextColumn();
                changed |= DrawColorEdit(config.SelfRaiseBackgroundColor, "Feature.RaiseDispelEnhancement.SelfRaiseWorldColor", "SelfRaiseBackgroundColor",
                    value => config.SelfRaiseBackgroundColor = value);
                ImGui.TableNextColumn();
                changed |= DrawColorEdit(config.OtherRaiseBackgroundColor, "Feature.RaiseDispelEnhancement.OtherRaiseWorldColor", "OtherRaiseBackgroundColor",
                    value => config.OtherRaiseBackgroundColor = value);
            }
        }
        return changed;
    }

    private static void DrawMatrixLabel(string label)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() +
            MathF.Max(0f, (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(label).X) * 0.5f));
        ImGui.TextUnformatted(label);
    }

    private static bool DrawReminderRow(string kind, RaiseDispelDisplayOptions options)
    {
        using var id = ImRaii.PushId(kind);
        ImGui.TableNextRow();
        DrawMatrixLabel(OmniLoc.Get($"Feature.RaiseDispelEnhancement.{kind}"));
        var changed = false;
        changed |= DrawMatrixCheckbox("ShowPartyFrame", options.ShowPartyFrame, value => options.ShowPartyFrame = value);
        changed |= DrawMatrixCheckbox("ShowAllianceFrame", options.ShowAllianceFrame, value => options.ShowAllianceFrame = value);
        changed |= DrawMatrixCheckbox("ShowWorldIcon", options.ShowWorldIcon, value => options.ShowWorldIcon = value);
        changed |= DrawMatrixCheckbox("ShowWorldText", options.ShowWorldText, value => options.ShowWorldText = value);
        changed |= DrawMatrixCheckbox("ShowCasterName", options.ShowCasterName, value => options.ShowCasterName = value);
        changed |= DrawMatrixCheckbox("ShowCastProgress", options.ShowCastProgress, value => options.ShowCastProgress = value);
        return changed;
    }

    private static bool DrawMatrixCheckbox(string id, bool value, Action<bool> setter)
    {
        ImGui.TableNextColumn();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() +
            MathF.Max(0f, (ImGui.GetContentRegionAvail().X - OmniTheme.CheckboxSize()) * 0.5f));
        if (!OmniControls.Checkbox($"##{id}", ref value))
        {
            return false;
        }
        setter(value);
        return true;
    }

    private static bool DrawColorEdit(
        uint current,
        string labelKey,
        string id,
        Action<uint> setter)
    {
        var color = ImGui.ColorConvertU32ToFloat4(current);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.SameLine(0f, OmniTheme.Scale(8f));
        if (!OmniControls.ColorEdit($"##raiseDispel{id}", ref color))
        {
            return false;
        }

        setter(ImGui.ColorConvertFloat4ToU32(color));
        return true;
    }

    private static bool DrawScaleSlider(
        float current,
        string labelKey,
        string id,
        Action<float> setter)
    {
        var value = Math.Clamp(current <= 0f ? 1f : current, 0.3f, 3f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.SameLine(0f, OmniTheme.Scale(8f));
        var changed = OmniControls.SliderFloat(
            $"##raiseDispel{id}",
            ref value,
            0.3f,
            3f,
            "%.2f",
            MathF.Max(1f, ImGui.GetContentRegionAvail().X));
        if (changed)
        {
            setter(value);
        }

        return changed;
    }

    protected override void OnEnable()
    {
        dispelIcon ??= DService.Instance().Texture.GetFromGameIcon(new GameIconLookup(DispelIconID));
        defaultRaiseIconID = LuminaGetter.TryGetRow<LuminaStatus>(RaiseStatusID, out var status) ? status.Icon : 0;
        var lifetime = new FeatureLifetime();
        try
        {
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 80))
            {
                throw new InvalidOperationException("Raise/dispel enhancement update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            DalamudServices.PluginInterface.UiBuilder.Draw += DrawOverlay;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawOverlay);
            runtimeLifetime = lifetime;
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
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
            Clear();
            CombatCharacterSnapshot.Clear();
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn || GameState.IsInPVPArea)
        {
            Clear();
            return;
        }

        try
        {
            ScanActors();
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Raise/dispel enhancement scan failed.");
        }
    }

    private void ScanActors()
    {
        states.Clear();
        names.Clear();
        actorObjects.Clear();

        CombatCharacterSnapshot.Refresh();
        var localPlayerEntityID = DService.Instance().ObjectTable.LocalPlayer?.EntityID ?? 0;
        foreach (var player in CombatCharacterSnapshot.Players)
        {
            var actorKey = GetActorKey(player);
            names[actorKey] = player.Name;
            actorObjects[actorKey] = (player.ObjectIndex, player.GameObjectID);

            if (player.IsDead)
            {
                if (TryGetRaiseStatus(player, out var raiseIconID, out var raiseSourceID))
                {
                    MarkRaised(
                        actorKey,
                        raiseIconID,
                        localPlayerEntityID != 0 && raiseSourceID == localPlayerEntityID);
                }
            }
            else
            {
                if (HasDispellableStatus(player))
                {
                    MarkDispellable(actorKey);
                }

                var castType = GetCastType(player.CastActionID);
                if (player.IsCasting && castType != CastType.None)
                {
                    var targetKey = GetCastTargetKey(player);
                    if (targetKey != 0)
                    {
                        MarkCast(targetKey, actorKey, castType, GetCastPercentage(player));
                    }
                }
            }

            if (states.Count >= 96)
            {
                break;
            }
        }
    }

    private void MarkRaised(ulong actorKey, uint iconID, bool raisedByLocalPlayer)
    {
        var state = GetState(actorKey);
        states[actorKey] = state with
        {
            HasRaisedStatus = true,
            RaisedByLocalPlayer = raisedByLocalPlayer,
            RaiseIconID = iconID == 0 ? state.RaiseIconID : iconID
        };
    }

    private void MarkDispellable(ulong actorKey)
    {
        var state = GetState(actorKey);
        states[actorKey] = state with { HasDispellableStatus = true };
    }

    private void MarkCast(ulong actorKey, ulong casterKey, CastType type, byte percentage)
    {
        var state = GetState(actorKey);
        states[actorKey] = state with
        {
            Caster = casterKey,
            Type = type,
            Percentage = percentage
        };
    }

    private ActorState GetState(ulong actorKey) =>
        states.TryGetValue(actorKey, out var state)
            ? state
            : new(0, CastType.None, false, false, false, 100, 0);

    private void DrawOverlay()
    {
        if (states.Count == 0)
        {
            return;
        }

        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn || GameState.IsInPVPArea)
        {
            Clear();
            return;
        }

        try
        {
            const ImGuiWindowFlags flags =
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoMouseInputs |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoBackground |
                ImGuiWindowFlags.NoNav;
            ImGuiHelpers.ForceNextWindowMainViewport();
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos);
            ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
            var windowOpen = ImGui.Begin("##omniRaiseDispelEnhancementOverlay", flags);
            try
            {
                if (!windowOpen)
                {
                    return;
                }

                var drawList = ImGui.GetWindowDrawList();
                foreach (var pair in states)
                {
                    DrawActor(drawList, pair.Key, pair.Value);
                }
            }
            finally
            {
                ImGui.End();
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Raise/dispel enhancement drawing failed.");
        }
    }

    private void DrawActor(ImDrawListPtr drawList, ulong actorKey, ActorState state)
    {
        var drawRaise = config.GetRaiseOptions().Enabled && (state.HasRaisedStatus || state.Type == CastType.Raise);
        var drawDispel = config.GetDispelOptions().Enabled && (state.HasDispellableStatus || state.Type == CastType.Dispel);
        if (!drawRaise && !drawDispel)
        {
            return;
        }

        if (TryGetListColor(actorKey, state, out var listColor, out var listType))
        {
            DrawFrameMarker(drawList, actorKey, state, listColor, listType);
        }

        if (!actorObjects.TryGetValue(actorKey, out var actor))
        {
            return;
        }

        // 状态扫描限频，世界坐标逐帧读取；校验身份以防对象槽位被复用。
        var services = DService.Instance();
        var gameObject = services.ObjectTable[actor.Index];
        if (gameObject != null && gameObject.GameObjectID == actor.ObjectID &&
            services.GameGUI.WorldToScreen(gameObject.Position, out var screenPosition))
        {
            DrawWorldMarker(drawList, screenPosition, state);
        }
    }

    private bool TryGetListColor(ulong actorKey, ActorState state, out uint color, out CastType type)
    {
        var hud = GetHudPosition(actorKey);
        var isParty = hud?.GroupNumber == 0;
        var dispel = config.GetDispelOptions();
        var raise = config.GetRaiseOptions();
        if (config.ShowDispelOnList && (isParty ? dispel.ShowPartyFrame : dispel.ShowAllianceFrame) && (state.HasDispellableStatus || state.Type == CastType.Dispel))
        {
            type = CastType.Dispel;
            color = config.DispelListColor;
            return true;
        }

        if (config.ShowRaiseOnList && (isParty ? raise.ShowPartyFrame : raise.ShowAllianceFrame) && (state.HasRaisedStatus || state.Type == CastType.Raise))
        {
            type = CastType.Raise;
            color = config.RaiseListColor;
            return true;
        }

        type = CastType.None;
        color = 0;
        return false;
    }

    private void DrawWorldMarker(ImDrawListPtr drawList, Vector2 screenPosition, ActorState state)
    {
        var (type, text) = GetWorldText(state);
        if (type == CastType.None)
        {
            return;
        }

        var options = type == CastType.Raise ? config.GetRaiseOptions() : config.GetDispelOptions();
        var scale = Math.Clamp(config.IconScale <= 0f ? 1f : config.IconScale, 0.3f, 3f) * ImGui.GetIO().FontGlobalScale;
        var iconSize = new Vector2(40.5f, 54.3f) * scale;
        if (options.ShowWorldIcon)
        {
            var iconHandle = GetWorldIconHandle(type, state);
            if (iconHandle != nint.Zero)
            {
                var iconMin = new Vector2(screenPosition.X - iconSize.X / 2f, screenPosition.Y - iconSize.Y);
                drawList.AddImage(iconHandle, iconMin, iconMin + iconSize);
            }
        }

        if (!options.ShowWorldText)
        {
            return;
        }

        var textScale = Math.Clamp(config.WorldTextScale <= 0f ? 1f : config.WorldTextScale, 0.3f, 3f);
        var textSize = ImGui.CalcTextSize(text) * textScale;
        var padding = ImGui.GetStyle().FramePadding * textScale;
        var textMin = new Vector2(
            screenPosition.X - textSize.X / 2f - padding.X,
            screenPosition.Y);
        var textMax = textMin + textSize + padding * 2f;
        var backgroundColor = state.Caster == 0 && state.HasRaisedStatus
            ? state.RaisedByLocalPlayer
                ? config.SelfRaiseBackgroundColor
                : config.OtherRaiseBackgroundColor
            : ImGui.ColorConvertFloat4ToU32(new(0.15f, 0.13f, 0.23f, 0.96f));
        drawList.AddRectFilled(
            textMin,
            textMax,
            backgroundColor,
            6f * textScale);
        drawList.AddRect(
            textMin,
            textMax,
            type == CastType.Dispel ? config.DispelWorldColor : config.RaiseWorldColor,
            6f * textScale,
            ImDrawFlags.None,
            1.5f * textScale);
        DrawShadowedText(drawList, text, textMin + padding, textScale);
    }

    private ImTextureID GetWorldIconHandle(CastType type, ActorState state)
    {
        var iconID = type == CastType.Dispel
            ? DispelIconID
            : state.RaiseIconID != 0 ? state.RaiseIconID : defaultRaiseIconID;
        if (iconID == 0)
        {
            return default;
        }

        ISharedImmediateTexture? texture;
        if (iconID == DispelIconID)
        {
            texture = dispelIcon;
        }
        else if (!statusIconTextures.TryGetValue(iconID, out texture))
        {
            texture = DService.Instance().Texture.GetFromGameIcon(new GameIconLookup(iconID));
            statusIconTextures[iconID] = texture;
        }

        return texture?.GetWrapOrDefault()?.Handle ?? default;
    }

    private (CastType Type, string Text) GetWorldText(ActorState state)
    {
        var options = state.Type == CastType.Raise ? config.GetRaiseOptions() : config.GetDispelOptions();
        var casterName = options.ShowCasterName ? GetCasterDisplayName(state) : string.Empty;
        if (state.Caster != 0)
        {
            if (state.Type == CastType.Raise && (config.GetRaiseOptions().ShowWorldIcon || config.GetRaiseOptions().ShowWorldText))
            {
                return (
                    CastType.Raise,
                    string.IsNullOrWhiteSpace(casterName)
                        ? OmniLoc.Get("Feature.RaiseDispelEnhancement.CastingRaise")
                        : string.Format(
                            OmniLoc.Get("Feature.RaiseDispelEnhancement.CastingRaiseWithCaster"),
                            casterName));
            }

            if (state.Type == CastType.Dispel && (config.GetDispelOptions().ShowWorldIcon || config.GetDispelOptions().ShowWorldText))
            {
                return (
                    CastType.Dispel,
                    string.IsNullOrWhiteSpace(casterName)
                        ? OmniLoc.Get("Feature.RaiseDispelEnhancement.CastingDispel")
                        : string.Format(
                            OmniLoc.Get("Feature.RaiseDispelEnhancement.CastingDispelWithCaster"),
                            casterName));
            }
        }

        if (state.HasDispellableStatus && (config.GetDispelOptions().ShowWorldIcon || config.GetDispelOptions().ShowWorldText))
        {
            return (CastType.Dispel, OmniLoc.Get("Feature.RaiseDispelEnhancement.NeedDispel"));
        }

        if (state.HasRaisedStatus && (config.GetRaiseOptions().ShowWorldIcon || config.GetRaiseOptions().ShowWorldText))
        {
            return (CastType.Raise, OmniLoc.Get("Feature.RaiseDispelEnhancement.Raised"));
        }

        return (CastType.None, string.Empty);
    }

    private string GetCasterDisplayName(ActorState state) =>
        state.Caster != 0 && names.TryGetValue(state.Caster, out var name) ? name : string.Empty;

    private void DrawFrameMarker(ImDrawListPtr drawList, ulong actorKey, ActorState state, uint color, CastType type)
    {
        var position = GetHudPosition(actorKey);
        if (position is null)
        {
            return;
        }

        var options = type == CastType.Raise ? config.GetRaiseOptions() : config.GetDispelOptions();
        var matchingCast = state.Type == type;
        var casterName = matchingCast && options.ShowCasterName ? GetCasterDisplayName(state) : string.Empty;
        var progress = matchingCast && options.ShowCastProgress ? state.Percentage : (byte)100;
        switch (position.Value.GroupNumber)
        {
            case 0 when options.ShowPartyFrame:
                DrawPartyRect(drawList, position.Value.MemberIndex, color, progress, casterName);
                break;
            case 1 when options.ShowAllianceFrame:
                DrawAllianceRect(drawList, "_AllianceList1", position.Value.MemberIndex, color, progress, casterName);
                break;
            case 2 when options.ShowAllianceFrame:
                DrawAllianceRect(drawList, "_AllianceList2", position.Value.MemberIndex, color, progress, casterName);
                break;
            default:
                if (position.Value.CrossWorld && options.ShowAllianceFrame)
                {
                    DrawCrossWorldAllianceRect(
                        drawList,
                        position.Value.GroupNumber,
                        position.Value.MemberIndex,
                        color,
                        progress,
                        casterName);
                }

                break;
        }
    }

    private static void DrawPartyRect(
        ImDrawListPtr drawList,
        int index,
        uint color,
        byte progress,
        string casterName)
    {
        var partyList = OmenTools.Interop.Game.Helpers.AddonHelper.GetByName<AddonPartyList>("_PartyList");
        if (index is < 0 or > 7 || partyList == null || !IsActuallyVisible(&partyList->AtkUnitBase))
        {
            return;
        }

        var member = partyList->PartyMembers[index];
        AtkResNode* node = null;
        if (member.TargetGlow != null)
        {
            node = (AtkResNode*)member.TargetGlow;
        }
        else if (member.PartyMemberComponent != null && member.PartyMemberComponent->OwnerNode != null)
        {
            node = &member.PartyMemberComponent->OwnerNode->AtkResNode;
        }

        if (node == null)
        {
            return;
        }

        var min = GetNodePosition(node) +
                  new Vector2(5f, 5f) * partyList->Scale +
                  ImGui.GetMainViewport().Pos;
        var size = new Vector2(node->Width, node->Height) * partyList->Scale -
                   new Vector2(8f, 10f) * partyList->Scale;
        DrawProgressRect(drawList, min, min + size, color, progress, 8f * partyList->Scale);
        DrawCasterName(drawList, min, size, casterName);
    }

    private static void DrawAllianceRect(
        ImDrawListPtr drawList,
        string addonName,
        int index,
        uint color,
        byte progress,
        string casterName)
    {
        var allianceList = OmenTools.Interop.Game.Helpers.AddonHelper.GetByName<AddonAllianceListX>(addonName);
        if (index is < 0 or > 7 || allianceList == null || !IsActuallyVisible(&allianceList->AtkUnitBase))
        {
            return;
        }

        var member = allianceList->AllianceMembers[index];
        var node = member.ComponentBase != null ? member.ComponentBase->OwnerNode : null;
        if (node == null)
        {
            return;
        }

        var min = GetNodePosition(&node->AtkResNode) +
                  new Vector2(5f, 4f) * allianceList->Scale +
                  ImGui.GetMainViewport().Pos;
        var size = new Vector2(node->AtkResNode.Width, node->AtkResNode.Height) * allianceList->Scale -
                   new Vector2(8f, 8f) * allianceList->Scale;
        DrawProgressRect(drawList, min, min + size, color, progress, 6f * allianceList->Scale);
        DrawCasterName(drawList, min, size, casterName);
    }

    private static void DrawCrossWorldAllianceRect(
        ImDrawListPtr drawList,
        int allianceIndex,
        int memberIndex,
        uint color,
        byte progress,
        string casterName)
    {
        var allianceList = OmenTools.Interop.Game.Helpers.AddonHelper.GetByName<AddonAlliance48>("Alliance48");
        if (allianceIndex is < 1 or > 5 ||
            memberIndex is < 0 or > 7 ||
            allianceList == null ||
            !IsActuallyVisible(&allianceList->AtkUnitBase))
        {
            return;
        }

        var member = allianceList->Alliances[allianceIndex - 1].Members[memberIndex];
        var node = member.AtkComponentBase != null ? member.AtkComponentBase->OwnerNode : null;
        if (node == null)
        {
            return;
        }

        var min = GetNodePosition(&node->AtkResNode) +
                  new Vector2(5f, 4f) * allianceList->Scale +
                  ImGui.GetMainViewport().Pos;
        var size = new Vector2(node->AtkResNode.Width, node->AtkResNode.Height) * allianceList->Scale -
                   new Vector2(8f, 8f) * allianceList->Scale;
        DrawProgressRect(drawList, min, min + size, color, progress, 6f * allianceList->Scale);
        DrawCasterName(drawList, min, size, casterName);
    }

    private static void DrawProgressRect(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        uint color,
        byte progress,
        float rounding)
    {
        progress = Math.Clamp(progress, (byte)0, (byte)100);
        if (progress >= 100)
        {
            drawList.AddRectFilled(min, max, color, rounding);
            drawList.AddRect(min, max, color | 0xFF000000, rounding, ImDrawFlags.None, 1.5f);
            return;
        }

        var splitX = min.X + (max.X - min.X) * progress / 100f;
        drawList.AddRectFilled(
            min,
            new Vector2(splitX, max.Y),
            color,
            rounding,
            ImDrawFlags.RoundCornersLeft);
        drawList.AddRectFilled(
            new Vector2(splitX, min.Y),
            max,
            ((color / 2) & 0xFF000000) | (color & 0x00FFFFFF),
            rounding,
            ImDrawFlags.RoundCornersRight);
        drawList.AddRect(min, max, color | 0xFF000000, rounding, ImDrawFlags.None, 1.5f);
    }

    private static void DrawCasterName(ImDrawListPtr drawList, Vector2 min, Vector2 size, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var textSize = ImGui.CalcTextSize(text);
        var position = new Vector2(
            min.X + MathF.Max(2f, size.X - textSize.X - 6f),
            min.Y + (size.Y - textSize.Y) * 0.5f);
        DrawShadowedText(drawList, text, position, 1f);
    }

    private static void DrawShadowedText(ImDrawListPtr drawList, string text, Vector2 position, float scale)
    {
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize() * MathF.Max(0.1f, scale);
        const float shadowOffset = 1f;
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                {
                    continue;
                }

                drawList.AddText(
                    font,
                    fontSize,
                    position + new Vector2(x, y) * shadowOffset,
                    0xBB000000,
                    text);
            }
        }

        drawList.AddText(font, fontSize, position, 0xFFFFFFFF, text);
    }

    private PartyListPosition? GetHudPosition(ulong actorKey)
    {
        var infoProxy = InfoProxyCrossRealm.Instance();
        var groupManager = GroupManager.Instance();
        var agentHud = AgentHUD.Instance();
        if (infoProxy == null || groupManager == null || agentHud == null)
        {
            return null;
        }

        if (groupManager->MainGroup.MemberCount > 0)
        {
            for (var index = 0; index < 8; index++)
            {
                var member = agentHud->PartyMembers[index];
                if (actorKey == member.ContentId || actorKey == member.EntityId)
                {
                    return new(false, 0, index);
                }
            }

            for (var index = 0; index < 40; index++)
            {
                if (actorKey == agentHud->RaidMemberIds[index])
                {
                    return new(false, index / 8 + 1, index % 8);
                }
            }
        }
        else if (infoProxy->IsCrossRealm)
        {
            var member = InfoProxyCrossRealm.GetMemberByContentId(actorKey);
            if (member == null)
            {
                member = InfoProxyCrossRealm.GetMemberByEntityId((uint)actorKey);
            }

            if (member == null)
            {
                return null;
            }

            return new(
                !infoProxy->IsInAllianceRaid,
                member->GroupIndex,
                member->MemberIndex);
        }

        return null;
    }

    private static bool TryGetRaiseStatus(IBattleChara player, out uint iconID, out uint sourceID)
    {
        iconID = 0;
        sourceID = 0;
        foreach (var status in player.StatusList)
        {
            if (status.StatusID is not (RaiseStatusID or 1140) ||
                !LuminaGetter.TryGetRow<LuminaStatus>(status.StatusID, out var row))
            {
                continue;
            }

            iconID = row.Icon;
            sourceID = status.SourceID;
            return true;
        }

        return false;
    }

    private bool HasDispellableStatus(IBattleChara player)
    {
        foreach (var status in player.StatusList)
        {
            if (status.StatusID == 0 || !LuminaGetter.TryGetRow<LuminaStatus>(status.StatusID, out var row))
            {
                continue;
            }

            if (row.CanDispel && !row.Name.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private static CastType GetCastType(uint actionID) => actionID switch
    {
        173 or 125 or 3603 or 18317 or 208 or 4247 or 4248 or 24859 or 7523 or 22345 or 20730 or 12996 or 24287 or 41634 => CastType.Raise,
        7568 or 3561 or 18318 => CastType.Dispel,
        _ => CastType.None
    };

    private static ulong GetCastTargetKey(IBattleChara player)
    {
        if (player.CastTargetObjectID == 0 ||
            CombatCharacterSnapshot.Find(player.CastTargetObjectID, 0) is not IPlayerCharacter target)
        {
            return 0;
        }

        return GetActorKey(target);
    }

    private static byte GetCastPercentage(IBattleChara player)
    {
        if (player.TotalCastTime <= 0f)
        {
            return 100;
        }

        return (byte)Math.Clamp(
            (int)MathF.Round(player.CurrentCastTime * 100f / player.TotalCastTime),
            0,
            100);
    }

    private static ulong GetActorKey(IBattleChara player)
    {
        var localPlayer = DService.Instance().ObjectTable.LocalPlayer;
        var contentID = DService.Instance().PlayerState.ContentId;
        if (contentID != 0 && localPlayer != null && player.EntityID == localPlayer.EntityID)
        {
            return contentID;
        }

        return player.EntityID != 0 ? player.EntityID : player.GameObjectID;
    }

    private static Vector2 GetNodePosition(AtkResNode* node)
    {
        var position = new Vector2(node->X, node->Y);
        var parent = node->ParentNode;
        while (parent != null)
        {
            position *= new Vector2(parent->ScaleX, parent->ScaleY);
            position += new Vector2(parent->X, parent->Y);
            parent = parent->ParentNode;
        }

        return position;
    }

    private static bool IsActuallyVisible(AtkUnitBase* addon) =>
        addon != null &&
        addon->IsVisible &&
        addon->RootNode != null &&
        addon->RootNode->IsVisible() &&
        (addon->VisibilityFlags & 5) == 0;

    private void Clear()
    {
        states.Clear();
        names.Clear();
        actorObjects.Clear();
    }

    private enum CastType : byte
    {
        None,
        Raise,
        Dispel
    }

    private readonly record struct ActorState(
        ulong Caster,
        CastType Type,
        bool HasRaisedStatus,
        bool RaisedByLocalPlayer,
        bool HasDispellableStatus,
        byte Percentage,
        uint RaiseIconID);

    private readonly struct PartyListPosition(bool crossWorld, int groupNumber, int memberIndex)
    {
        public bool CrossWorld { get; } = crossWorld;

        public int GroupNumber { get; } = groupNumber;

        public int MemberIndex { get; } = memberIndex;
    }
}

[Serializable]
public sealed class RaiseDispelEnhancementConfig
{
    public RaiseDispelDisplayOptions? Raise { get; set; }
    public RaiseDispelDisplayOptions? Dispel { get; set; }

    public RaiseDispelDisplayOptions GetRaiseOptions() => Raise ??= CreateDisplayOptions(ShowRaise);
    public RaiseDispelDisplayOptions GetDispelOptions() => Dispel ??= CreateDisplayOptions(ShowDispel);

    // 旧配置首次使用时继承共享开关；独立配置保存后不再受旧字段影响。
    private RaiseDispelDisplayOptions CreateDisplayOptions(bool enabled) => new()
    {
        ShowPartyFrame = enabled && ShowPartyFrame,
        ShowAllianceFrame = enabled && ShowAllianceFrame,
        ShowWorldIcon = enabled && ShowWorldIcon,
        ShowWorldText = enabled && ShowWorldText,
        ShowCasterName = ShowCasterName,
        ShowCastProgress = ShowCastProgress
    };

    public bool ShowRaise { get; set; } = true;

    public bool ShowDispel { get; set; } = true;

    public bool ShowPartyFrame { get; set; } = true;

    public bool ShowAllianceFrame { get; set; } = true;

    public bool ShowWorldIcon { get; set; } = true;

    public bool ShowWorldText { get; set; } = true;

    public bool ShowCasterName { get; set; } = true;

    public bool ShowCastProgress { get; set; } = true;

    public bool ShowRaiseOnList { get; set; } = true;

    public bool ShowDispelOnList { get; set; } = true;

    public float IconScale { get; set; } = 1f;

    public float WorldTextScale { get; set; } = 1f;

    public uint RaiseListColor { get; set; } = 0x60FF0000;

    public uint DispelListColor { get; set; } = 0x600000FF;

    public uint RaiseWorldColor { get; set; } = 0xC8143C0A;

    public uint SelfRaiseBackgroundColor { get; set; } = 0xF53B2126;

    public uint OtherRaiseBackgroundColor { get; set; } = 0xC8FF0000;

    public uint DispelWorldColor { get; set; } = 0xC8140A3C;
}


[Serializable]
public sealed class RaiseDispelDisplayOptions
{
    public bool ShowPartyFrame { get; set; } = true;
    public bool ShowAllianceFrame { get; set; } = true;
    public bool ShowWorldIcon { get; set; } = true;
    public bool ShowWorldText { get; set; } = true;
    public bool ShowCasterName { get; set; } = true;
    public bool ShowCastProgress { get; set; } = true;

    [Newtonsoft.Json.JsonIgnore]
    public bool Enabled => ShowPartyFrame || ShowAllianceFrame || ShowWorldIcon || ShowWorldText;
}
