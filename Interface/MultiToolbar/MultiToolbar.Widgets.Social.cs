using System.Runtime.CompilerServices;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private TaskHelper? onlineStatusDetailTasks;
    private static readonly uint[] OnlineStatusIds = [47, 17, 12, 22, 21, 23, 32, 31, 27, 28, 30, 29];
    private static readonly uint[] SocietyAetheryteIds = [0, 19, 4, 16, 14, 7, 73, 76, 79, 105, 99, 128, 144, 143, 136, 169, 181, 175, 238, 206, 201];
    private bool DrawSocialWidgetPopup(MultiToolbarWidgetType type)
    {
        switch (type)
        {
            case MultiToolbarWidgetType.BattleEffects:
                DrawBattleEffectsPopup();
                return true;
            case MultiToolbarWidgetType.Societies:
                DrawSocietiesPopup();
                return true;
            case MultiToolbarWidgetType.OnlineStatus:
                DrawOnlineStatusPopup();
                return true;
            default:
                return false;
        }
    }

    private string? SocialWidgetLabel(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.BattleEffects => OmniLoc.Get("Feature.MultiToolbar.WidgetBattleEffects"),
        MultiToolbarWidgetType.Societies => GetTrackedSocietyLabel(),
        MultiToolbarWidgetType.OnlineStatus => GetOnlineStatusLabel(),
        _ => null,
    };

    private uint SocialWidgetGameIcon(MultiToolbarWidgetType type)
    {
        if (type == MultiToolbarWidgetType.Societies && TryGetSociety(config.TrackedSocietyID, out var society))
        {
            return society.Icon;
        }

        if (type == MultiToolbarWidgetType.OnlineStatus && TryGetCurrentOnlineStatus(out var status))
        {
            return status.Icon;
        }

        return type == MultiToolbarWidgetType.Societies ? 65042u : 0u;
    }

    private void DrawBattleEffectsPopup()
    {
        ImGui.TextUnformatted("特效强度");
        using var table = ImRaii.Table(
            "##multiToolbarBattleEffects",
            2,
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("##multiToolbarBattleEffectsLabel", ImGuiTableColumnFlags.WidthFixed,
            MathF.Max(ImGui.CalcTextSize("其他玩家特效").X, ImGui.CalcTextSize("PvP 敌方特效").X) + OmniTheme.Scale(8f));
        ImGui.TableSetupColumn("##multiToolbarBattleEffectsValue", ImGuiTableColumnFlags.WidthStretch);

        DrawGameConfigSlider("自身特效", "BattleEffectSelf", ["完全", "简单", "不显示"], true);
        DrawGameConfigSlider("队友特效", "BattleEffectParty", ["完全", "简单", "不显示"], true);
        DrawGameConfigSlider("其他玩家特效", "BattleEffectOther", ["完全", "简单", "不显示"], true);
        DrawGameConfigSlider("PvP 敌方特效", "BattleEffectPvPEnemyPc", ["完全", "简单", "不显示"], true);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("召唤兽尺寸");
        ImGui.TableNextColumn();
        ImGui.Dummy(Vector2.Zero);
        foreach (var (petName, key) in new[]
                 {
                     ("巴哈姆特", "BahamutSize"),
                     ("凤凰", "PhoenixSize"),
                     ("迦楼罗", "GarudaSize"),
                     ("泰坦", "TitanSize"),
                     ("伊弗利特", "IfritSize"),
                 })
        {
            DrawGameConfigSlider(petName, key, ["小", "中", "大"]);
        }
    }

    private void DrawGameConfigSlider(string label, string key, string[] values, bool reverse = false)
        => DrawGameConfigSlider(label, [key], values, reverse);

    private void DrawGameConfigSlider(string label, IReadOnlyList<string> keys, string[] values, bool reverse = false)
    {
        var configKey = keys.Count > 0 ? keys[0] : string.Empty;
        var raw = DService.Instance().GameConfig.UiConfig.TryGetUInt(configKey, out var configuredValue)
            ? (int)Math.Clamp(configuredValue, 0u, (uint)(values.Length - 1))
            : 0;
        var displayed = reverse && values.Length == 3 ? 2 - raw : raw;
        ImGui.TableNextRow();
        var valueWidth = ImGui.CalcTextSize("不显示").X;
        var rowHeight = OmniTheme.Scale(28f);
        ImGui.PushID($"multiToolbarSocial_{configKey}");
        ImGui.AlignTextToFramePadding();
        ImGui.TableSetColumnIndex(0);
        var labelSize = ImGui.CalcTextSize(label);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - labelSize.X));
        ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        var trackWidth = MathF.Max(OmniTheme.Scale(60f), ImGui.GetContentRegionAvail().X - valueWidth - OmniTheme.Scale(15f));
        var trackPosition = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##track", new Vector2(trackWidth, rowHeight));
        var trackMin = trackPosition + new Vector2(0f, OmniTheme.Scale(6f));
        var trackMax = trackPosition + new Vector2(trackWidth, rowHeight - OmniTheme.Scale(6f));
        var drawList = ImGui.GetWindowDrawList();
        var radius = OmniTheme.Scale(8f);
        drawList.AddRectFilled(trackMin, trackMax, ImGui.GetColorU32(WithAlpha(OmniTheme.Tokens.Background, 0.88f)), radius);
        drawList.AddRect(trackMin, trackMax, ImGui.GetColorU32(OmniTheme.Tokens.Border), radius);
        var fraction = values.Length <= 1 ? 0f : displayed / (float)(values.Length - 1);
        var knobWidth = MathF.Max(OmniTheme.Scale(32f), trackWidth / values.Length);
        var knobX = trackMin.X + (trackWidth - knobWidth) * fraction;
        var knobMin = new Vector2(knobX, trackMin.Y + OmniTheme.Scale(1f));
        var knobMax = new Vector2(knobX + knobWidth, trackMax.Y - OmniTheme.Scale(1f));
        drawList.AddRectFilled(knobMin, knobMax, ImGui.GetColorU32(OmniTheme.Tokens.Secondary), radius);
        if (ImGui.IsItemClicked() || ImGui.IsItemActive())
        {
            var mouseX = Math.Clamp(ImGui.GetIO().MousePos.X, trackMin.X, trackMax.X);
            var next = values.Length <= 1
                ? 0
                : (int)MathF.Round((mouseX - trackMin.X - knobWidth * 0.5f) / MathF.Max(1f, trackWidth - knobWidth) * (values.Length - 1));
            if (next != displayed)
            {
                displayed = Math.Clamp(next, 0, values.Length - 1);
                var value = reverse && values.Length == 3 ? 2 - displayed : displayed;
                foreach (var key in keys)
                {
                    DService.Instance().GameConfig.UiConfig.Set(key, (uint)value);
                }
            }
        }
        ImGui.SameLine(0f, OmniTheme.Scale(15f));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + OmniTheme.Scale(4f));
        ImGui.SetNextItemWidth(valueWidth);
        var valueIndex = reverse && values.Length == 3 ? 2 - displayed : displayed;
        ImGui.TextUnformatted(values[valueIndex]);
        ImGui.PopID();
    }

    private void DrawSocietiesPopup()
    {
        unsafe
        {
            var playerState = PlayerState.Instance();
            if (playerState is null)
            {
                ImGui.TextUnformatted("当前角色数据不可用");
                return;
            }

            var questManager = QuestManager.Instance();
            ImGui.TextDisabled(string.Format(OmniLoc.Get("Feature.MultiToolbar.SocietyAllowance"), questManager is null ? 0u : questManager->GetBeastTribeAllowance()));
            ImGui.Separator();
            var sheet = DService.Instance().Data.GetExcelSheet<BeastTribe>();
            if (sheet is null)
            {
                ImGui.TextUnformatted("部族数据不可用");
                return;
            }

            List<(BeastTribe Tribe, uint Rank, uint Current, uint Needed, string Name)> societies = [];
            foreach (var tribe in sheet)
            {
                if (tribe.RowId == 0 || tribe.RowId > byte.MaxValue)
                {
                    continue;
                }

                var id = (byte)tribe.RowId;
                var rank = playerState->GetBeastTribeRank(id);
                if (rank == 0)
                {
                    continue;
                }

                var current = playerState->GetBeastTribeCurrentReputation(id);
                var needed = playerState->GetBeastTribeNeededReputation(id);
                var name = tribe.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"部族 {tribe.RowId}";
                }

                societies.Add((tribe, rank, current, needed, name));
            }

            if (societies.Count == 0)
            {
                ImGui.TextDisabled("当前角色尚未解锁可显示的部族友好度。");
                return;
            }

            var groups = societies.GroupBy(society => society.Tribe.Expansion.RowId).OrderBy(group => group.Key).ToArray();
            var columns = societies.Count < 10 ? 1 : Math.Min(groups.Length, Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / OmniTheme.Scale(250f))));
            using var table = ImRaii.Table(
                "##multiToolbarSocieties",
                columns,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                new Vector2(ImGui.GetContentRegionAvail().X, 0f));
            if (!table)
            {
                return;
            }

            for (var columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                ImGui.TableSetupColumn($"##multiToolbarSocietyColumn{columnIndex}", ImGuiTableColumnFlags.WidthStretch);
            }
            for (var index = 0; index < groups.Length; index++)
            {
                if (index % columns == 0)
                {
                    ImGui.TableNextRow();
                }

                ImGui.TableNextColumn();
                var group = groups[index];
                ImGui.TextDisabled(group.First().Tribe.Expansion.Value.Name.ExtractText());
                ImGui.Separator();
                foreach (var society in group)
                {
                    DrawSocietyCard(society);
                }
            }
        }
    }

    private unsafe float GetSocietiesPopupWidth()
    {
        var playerState = PlayerState.Instance();
        var sheet = DService.Instance().Data.GetExcelSheet<BeastTribe>();
        if (playerState is null || sheet is null)
        {
            return 266f;
        }

        var count = 0;
        HashSet<uint> expansions = [];
        foreach (var tribe in sheet)
        {
            if (tribe.RowId is 0 or > byte.MaxValue || playerState->GetBeastTribeRank((byte)tribe.RowId) == 0)
            {
                continue;
            }

            count++;
            expansions.Add(tribe.Expansion.RowId);
        }

        return count < 10 ? 266f : Math.Max(1, expansions.Count) * 258f + 8f;
    }

    private void DrawSocietyCard((BeastTribe Tribe, uint Rank, uint Current, uint Needed, string Name) society)
    {
        var cardSize = new Vector2(ImGui.GetContentRegionAvail().X, MathF.Max(OmniTheme.Scale(50f), ImGui.GetTextLineHeight() * 2f + OmniTheme.Scale(16f)));
        var cardPosition = ImGui.GetCursorScreenPos();
        ImGui.PushID($"multiToolbarSociety_{society.Tribe.RowId}");
        ImGui.InvisibleButton("##card", cardSize);
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        if (ImGui.BeginPopupContextItem("##context"))
        {
            var tracked = config.TrackedSocietyID == society.Tribe.RowId;
            if (ImGui.Selectable(OmniLoc.Get("Feature.MultiToolbar.TrackSociety"), false, tracked ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
            {
                config.TrackedSocietyID = society.Tribe.RowId;
                saveConfig();
            }

            if (ImGui.Selectable(OmniLoc.Get("Feature.MultiToolbar.UntrackSociety"), false, tracked ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
            {
                config.TrackedSocietyID = 0u;
                saveConfig();
            }

            if (ImGui.Selectable(OmniLoc.Get("Feature.MultiToolbar.TeleportSociety")))
            {
                TeleportToSociety(society.Tribe.RowId);
            }

            ImGui.EndPopup();
        }

        ImGui.PopID();

        var drawList = ImGui.GetWindowDrawList();
        var cardEnd = cardPosition + cardSize;
        var radius = OmniTheme.Scale(OmniTheme.Tokens.ButtonRadius);
        var background = hovered || config.TrackedSocietyID == society.Tribe.RowId
            ? OmniTheme.HoverBackground
            : OmniTheme.Tokens.Surface;
        drawList.AddRectFilled(cardPosition, cardEnd, OmniTheme.Color(background), radius);
        drawList.AddRect(cardPosition, cardEnd, OmniTheme.Color(OmniTheme.Tokens.Border), radius, ImDrawFlags.None, OmniTheme.Scale(OmniTheme.Tokens.BorderThickness));

        var iconSize = OmniTheme.Scale(38f);
        var iconPosition = cardPosition + new Vector2(OmniTheme.Scale(6f), (cardSize.Y - iconSize) * 0.5f);
        if (ImageHelper.GetGameIcon(society.Tribe.Icon) is { } texture)
        {
            drawList.AddImage(texture.Handle, iconPosition, iconPosition + new Vector2(iconSize));
        }

        var currencyItemID = society.Tribe.CurrencyItem.RowId;
        var bodyX = iconPosition.X + iconSize + OmniTheme.Scale(6f);
        var bodyWidth = MathF.Max(1f, cardEnd.X - bodyX - iconSize - OmniTheme.Scale(12f));
        var textColor = ImGui.GetColorU32(OmniTheme.Tokens.Text);
        var mutedColor = ImGui.GetColorU32(WithAlpha(OmniTheme.Tokens.Text, 0.68f));
        var lineHeight = ImGui.GetTextLineHeight();
        drawList.PushClipRect(new Vector2(bodyX, cardPosition.Y), new Vector2(bodyX + bodyWidth, cardEnd.Y), true);
        drawList.AddText(new Vector2(bodyX, cardPosition.Y + OmniTheme.Scale(5f)), textColor, EllipsizeToolbarText(society.Name, bodyWidth));
        var (rank, rankName, needed) = GetSocietyRank(society.Tribe, society.Rank, society.Needed);
        var rankText = $"{rankName} ({rank}/{Math.Max(rank, society.Tribe.MaxRank)})";
        var progress = needed == 0 ? 1f : Math.Clamp(society.Current / (float)needed, 0f, 1f);
        var percentage = $"{(int)(progress * 100f)}%";
        var rankY = cardPosition.Y + OmniTheme.Scale(5f) + lineHeight;
        var percentageWidth = ImGui.CalcTextSize(percentage).X;
        drawList.PushClipRect(new Vector2(bodyX, rankY), new Vector2(bodyX + MathF.Max(0f, bodyWidth - percentageWidth - OmniTheme.Scale(4f)), rankY + lineHeight), true);
        drawList.AddText(new Vector2(bodyX, cardPosition.Y + OmniTheme.Scale(5f) + lineHeight), mutedColor, rankText);
        drawList.PopClipRect();
        drawList.AddText(new Vector2(bodyX + bodyWidth - percentageWidth, rankY), mutedColor, percentage);
        var barPosition = new Vector2(bodyX, cardEnd.Y - OmniTheme.Scale(8f));
        var barSize = new Vector2(bodyWidth, OmniTheme.Scale(3f));
        drawList.AddRectFilled(barPosition, barPosition + barSize, OmniTheme.Color(OmniTheme.Tokens.Background), OmniTheme.Scale(1f));
        drawList.AddRectFilled(
                barPosition,
                barPosition + new Vector2(barSize.X * progress, barSize.Y),
                OmniTheme.Color(OmniTheme.Tokens.Accent),
                OmniTheme.Scale(1f));
        drawList.PopClipRect();

        if (currencyItemID > 0 && LuminaGetter.TryGetRow<Item>(currencyItemID, out var currency) && ImageHelper.GetGameIcon((uint)currency.Icon) is { } currencyTexture)
        {
            var currencyPosition = new Vector2(cardEnd.X - iconSize - OmniTheme.Scale(6f), iconPosition.Y);
            drawList.AddRectFilled(currencyPosition, currencyPosition + new Vector2(iconSize), OmniTheme.Color(OmniTheme.Tokens.Background), OmniTheme.Scale(6f));
            drawList.AddRect(currencyPosition, currencyPosition + new Vector2(iconSize), OmniTheme.Color(OmniTheme.Tokens.Border), OmniTheme.Scale(6f));
            drawList.AddImage(currencyTexture.Handle, currencyPosition + new Vector2(OmniTheme.Scale(2f)), currencyPosition + new Vector2(iconSize - OmniTheme.Scale(2f)));
            var count = LocalPlayerState.GetItemCount(currencyItemID).ToString();
            var countPosition = currencyPosition + new Vector2(iconSize - ImGui.CalcTextSize(count).X, iconSize - lineHeight);
            drawList.AddText(countPosition + new Vector2(1f), OmniTheme.Color(OmniTheme.Tokens.Background), count);
            drawList.AddText(
                countPosition,
                textColor,
                count);
        }

        if (clicked)
        {
            TeleportToSociety(society.Tribe.RowId);
        }

        if (hovered)
        {
            OmniControls.HelpTooltip($"{society.Name}\n{rankText}\n{society.Current}/{needed} ({percentage})");
        }
    }

    private void TeleportToSociety(uint societyID)
    {
        if (societyID == 0 || societyID >= SocietyAetheryteIds.Length)
        {
            return;
        }

        var aetheryteID = SocietyAetheryteIds[societyID];
        _ = DService.Instance().Framework.RunOnFrameworkThread(() => teleportService.TryTeleport(aetheryteID));
        ClosePopup();
    }

    private static (uint Rank, string Name, uint Needed) GetSocietyRank(BeastTribe tribe, uint rank, uint needed)
    {
        if (!LuminaGetter.TryGetRow<BeastReputationRank>(rank, out var rankRow))
        {
            return (rank, rank.ToString(), needed);
        }

        var allied = tribe.Expansion.RowId != 0 && tribe.IntersocietalQuest.IsValid && QuestManager.IsQuestComplete(tribe.IntersocietalQuest.RowId);
        var name = (tribe.Expansion.RowId == 0 || allied ? rankRow.Name : rankRow.AlliedNames).ExtractText();
        return allied ? (rank + 1, name, 0u) : (rank, name, needed);
    }

    private void DrawOnlineStatusPopup()
    {
        var sheet = DService.Instance().Data.GetExcelSheet<OnlineStatus>();
        if (sheet is null)
        {
            ImGui.TextUnformatted("在线状态数据不可用");
            return;
        }

        unsafe
        {
            var info = InfoModule.Instance();
            var detail = info is null ? null : (InfoProxyDetail*)info->GetInfoProxyById(InfoProxyId.Detail);
            var playerState = PlayerState.Instance();
            var rowHeight = MathF.Max(ImGui.GetFrameHeight(), OmniTheme.Scale(28f));
            var iconSize = MathF.Max(GetPopupRowIconSize(), OmniTheme.Scale(22f));
            foreach (var statusId in OnlineStatusIds)
            {
                if (!sheet.TryGetRow(statusId, out var status))
                {
                    continue;
                }

                if (playerState is not null && !IsOnlineStatusAvailable(statusId, playerState))
                {
                    continue;
                }

                var selected = info is not null && info->IsOnlineStatusSet((byte)statusId);
                var label = status.Name.ExtractText();
                ImGui.PushID($"multiToolbarOnlineStatus_{statusId}");
                var rowPosition = ImGui.GetCursorScreenPos();
                var rowWidth = ImGui.GetContentRegionAvail().X;
                ImGui.InvisibleButton("##row", new Vector2(rowWidth, rowHeight));
                var hovered = ImGui.IsItemHovered();
                var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
                var rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
                ImGui.PopID();

                var drawList = ImGui.GetWindowDrawList();
                if (hovered || selected)
                {
                    var rowColor = hovered
                        ? ImGui.IsItemActive() ? ImGuiCol.HeaderActive : ImGuiCol.HeaderHovered
                        : ImGuiCol.Header;
                    drawList.AddRectFilled(
                        rowPosition,
                        rowPosition + new Vector2(rowWidth, rowHeight),
                        ImGui.GetColorU32(rowColor));
                }

                if (ImageHelper.GetGameIcon(status.Icon) is { } texture)
                {
                    var iconPosition = rowPosition + new Vector2(OmniTheme.Scale(6f), (rowHeight - iconSize) * 0.5f);
                    drawList.AddImage(texture.Handle, iconPosition, iconPosition + new Vector2(iconSize));
                }

                drawList.AddText(
                    rowPosition + new Vector2(OmniTheme.Scale(6f) + iconSize + OmniTheme.Scale(6f), (rowHeight - ImGui.GetTextLineHeight()) * 0.5f),
                    ImGui.GetColorU32(OmniTheme.Tokens.Text),
                    label);

                if (rightClicked)
                {
                    OpenOnlineStatusDetail();
                    continue;
                }

                if (!clicked)
                {
                    continue;
                }

                if (detail is not null)
                {
                    detail->SendOnlineStatusUpdate(statusId);
                    ClosePopup();
                }
            }
        }
    }

    private string GetTrackedSocietyLabel()
    {
        if (!TryGetSociety(config.TrackedSocietyID, out var society))
        {
            return OmniLoc.Get("Feature.MultiToolbar.WidgetSocieties");
        }

        unsafe
        {
            var playerState = PlayerState.Instance();
            if (playerState is null)
            {
                return society.Name.ExtractText();
            }

            var rank = playerState->GetBeastTribeRank((byte)society.RowId);
            var current = playerState->GetBeastTribeCurrentReputation((byte)society.RowId);
            var needed = playerState->GetBeastTribeNeededReputation((byte)society.RowId);
            var (_, rankName, required) = GetSocietyRank(society, rank, needed);
            var percentage = required == 0 ? 100 : (int)Math.Clamp(100u * current / required, 0u, 100u);
            return percentage is > 0 and < 100
                ? $"{society.Name.ExtractText()} {rankName} ({percentage}%)"
                : $"{society.Name.ExtractText()} {rankName}";
        }
    }

    private string GetOnlineStatusLabel() => TryGetCurrentOnlineStatus(out var status)
        ? status.Name.ExtractText()
        : OmniLoc.Get("Feature.MultiToolbar.WidgetOnlineStatus");

    private bool TryGetCurrentOnlineStatus(out OnlineStatus status)
    {
        status = default;
        var player = DService.Instance().ObjectTable.LocalPlayer;
        if (player is null || !player.OnlineStatus.IsValid)
        {
            return DService.Instance().Data.GetExcelSheet<OnlineStatus>()?.TryGetRow(47, out status) == true;
        }

        status = player.OnlineStatus.Value;
        if (status.RowId == 0)
        {
            return DService.Instance().Data.GetExcelSheet<OnlineStatus>()?.TryGetRow(47, out status) == true;
        }

        return true;
    }

    private bool TryGetSociety(uint id, out BeastTribe society)
    {
        society = default;
        return id != 0 && DService.Instance().Data.GetExcelSheet<BeastTribe>()?.TryGetRow(id, out society) == true;
    }

    private static unsafe bool IsOnlineStatusAvailable(uint statusID, PlayerState* playerState) => statusID switch
    {
        21 => IsGeneralActionUnlocked(13),
        23 => !LocalPlayerState.IsInParty,
        32 => playerState->IsNovice(),
        31 => playerState->IsReturner(),
        27 => playerState->IsMentor(),
        28 or 30 => playerState->IsBattleMentor(),
        29 => playerState->IsTradeMentor(),
        _ => true,
    };

    private static unsafe bool IsGeneralActionUnlocked(uint actionID)
    {
        var sheet = DService.Instance().Data.GetExcelSheet<GeneralAction>();
        var uiState = UIState.Instance();
        return sheet is not null &&
               uiState is not null &&
               sheet.TryGetRow(actionID, out var action) &&
               (action.UnlockLink == 0 || uiState->IsUnlockLinkUnlocked(action.UnlockLink));
    }

    private unsafe void OpenOnlineStatusDetail()
    {
        var infoModule = InfoModule.Instance();
        if (infoModule is null || infoModule->IsInCrossWorldDuty() ||
            onlineStatusDetailTasks?.IsBusy == true)
        {
            return;
        }

        var contentID = infoModule->GetLocalContentId();
        if (contentID == 0 || TryOpenOnlineStatusDetail(contentID))
        {
            return;
        }

        InfoProxyPartyMember.Instance()->RequestData();
        InfoProxyDetail.Instance()->RequestData();
        onlineStatusDetailTasks ??= new TaskHelper();
        onlineStatusDetailTasks.Enqueue(() => TryOpenOnlineStatusDetail(contentID), timeoutMS: 3000);
    }

    private static unsafe bool TryOpenOnlineStatusDetail(ulong contentID)
    {
        var infoModule = InfoModule.Instance();
        if (infoModule is null || infoModule->IsInCrossWorldDuty() ||
            infoModule->GetLocalContentId() != contentID)
        {
            return true;
        }

        var partyMember = InfoProxyPartyMember.Instance();
        var detail = InfoProxyDetail.Instance();
        if (partyMember is null || detail is null)
        {
            return true;
        }

        var characterData = partyMember->GetEntryByContentId(contentID);
        if (characterData is null)
        {
            return false;
        }

        var updateData = Unsafe.AsPointer(ref detail->UpdateData);
        var agentDetail = AgentDetail.Instance();
        if (agentDetail is null)
        {
            return true;
        }

        agentDetail->OpenForCharacterData(
            characterData,
            (InfoProxyDetail.DetailUpdateData*)updateData);
        return true;
    }
}
