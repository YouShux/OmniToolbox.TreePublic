using System.Collections.Frozen;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Dalamud.Services.Game;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace OmniToolbox.TreePublic;

public sealed unsafe class DuplicateStatusPrevention : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("DuplicateStatusPreventionTitle"),
        Description = OmniLoc.Get("DuplicateStatusPreventionDescription"),
        Category = ModuleCategory.Combat
    };

    private readonly DuplicateStatusPreventionConfig config;
    private long lastNotificationTick;

    public DuplicateStatusPrevention(DuplicateStatusPreventionConfig config)
    {
        this.config = config;
        NormalizeConfig(config);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => DuplicateStatusPreventionPanel.Draw(config);

    internal static void NormalizeConfig(DuplicateStatusPreventionConfig config)
    {
        config.EnabledActions ??= [];
        foreach (var action in DuplicateStatusPreventionRules.OrderedActions)
        {
            config.EnabledActions.TryAdd(action.Key, action.Key != 3);
        }

        foreach (var actionID in new List<uint>(config.EnabledActions.Keys))
        {
            if (!DuplicateStatusPreventionRules.Actions.ContainsKey(actionID))
            {
                config.EnabledActions.Remove(actionID);
            }
        }
    }

    protected override void OnEnable()
    {
        lastNotificationTick = 0;
        if (!UseActionManager.Instance().RegPreUseAction(OnPreUseAction))
        {
            throw new InvalidOperationException("Duplicate status prevention registration failed.");
        }
    }

    protected override void OnDisable() => UseActionManager.Instance().Unreg(OnPreUseAction);

    private void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionID,
        ref ulong targetID,
        ref uint extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint comboRouteID)
    {
        if (actionType is not (ActionType.Action or ActionType.GeneralAction))
        {
            return;
        }

        var adjustedActionID = actionType == ActionType.Action
            ? ActionManager.Instance()->GetAdjustedActionId(actionID)
            : actionID;
        if (!DuplicateStatusPreventionRules.Actions.TryGetValue(adjustedActionID, out var rule) ||
            !config.EnabledActions.TryGetValue(adjustedActionID, out var enabled) ||
            !enabled ||
            ActionManager.Instance()->GetActionStatus(actionType, adjustedActionID) != 0 ||
            !LuminaGetter.TryGetRow<LuminaAction>(adjustedActionID, out var action))
        {
            return;
        }

        var canTargetSelf = action.CanTargetSelf && adjustedActionID != 7535;
        var target = DService.Instance().ObjectTable.SearchByID(targetID);
        var detectionTarget = targetID is 0 or 0xE0000000 || targetID == LocalPlayerState.EntityID
            ? DService.Instance().ObjectTable.LocalPlayer
            : target as IBattleChara;
        if (canTargetSelf && (target is null || !ActionManager.CanUseActionOnTarget(adjustedActionID, target.ToStruct())))
        {
            detectionTarget = DService.Instance().ObjectTable.LocalPlayer;
        }

        if (!ShouldPrevent(rule, detectionTarget))
        {
            return;
        }

        isPrevented = true;
        if (!config.ChatNotify && !config.PopupNotify)
        {
            return;
        }

        var currentTick = Environment.TickCount64;
        if (lastNotificationTick != 0 && currentTick - lastNotificationTick < 5000)
        {
            return;
        }

        lastNotificationTick = currentTick;
        var details = string.Format(
            OmniLoc.Get("Feature.DuplicateStatusPrevention.Prevented"),
            action.Name);
        if (config.ChatNotify)
        {
            OmniNotifier.Chat($"{Info.Title} {details}");
        }

        if (config.PopupNotify)
        {
            OmniNotifier.Popup(Info.Title, details);
        }
    }

    private bool ShouldPrevent(DuplicateActionRule rule, IBattleChara? target)
    {
        if (rule.SecondStatuses is not null)
        {
            foreach (var status in rule.SecondStatuses)
            {
                if (status.Blocks(HasStatus(status, GetCurrentTarget())))
                {
                    return false;
                }
            }
        }

        foreach (var status in rule.Statuses)
        {
            if (status.Blocks(HasStatus(status, target)))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasStatus(DuplicateStatusRule rule, IBattleChara? target) => rule.DetectionTarget switch
    {
        StatusDetectionTarget.Self => HasStatus(DService.Instance().ObjectTable.LocalPlayer?.StatusList, rule.StatusID),
        StatusDetectionTarget.Member => PartyHasStatus(rule.StatusID),
        StatusDetectionTarget.Target => HasStatus(target?.StatusList, rule.StatusID),
        _ => false
    };

    private static IBattleChara? GetCurrentTarget() =>
        DService.Instance().ObjectTable.CreateObjectReference((nint)TargetSystem.Instance()->Target) as IBattleChara;

    private bool PartyHasStatus(uint statusID)
    {
        foreach (var partyMember in AgentHUD.Instance()->PartyMembers)
        {
            if (partyMember.Object is null || partyMember.EntityId == LocalPlayerState.EntityID)
            {
                continue;
            }

            if (DService.Instance().ObjectTable.CreateObjectReference((nint)partyMember.Object) is IBattleChara member &&
                HasStatus(member.StatusList, statusID))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasStatus(StatusList? statuses, uint statusID)
    {
        if (statuses is null || !statuses.TryGetStatus(statusID, out var status, out _) || status is null)
        {
            return false;
        }

        return status.GameData.Value.IsPermanent || status.RemainingTime > config.OverlapThreshold;
    }
}

internal static class DuplicateStatusPreventionPanel
{
    public static bool Draw(DuplicateStatusPreventionConfig config)
    {
        var changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.DuplicateStatusPrevention.OverlapThreshold"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(100f));
        var overlapThreshold = config.OverlapThreshold;
        if (OmniControls.InputFloat("##duplicateStatusOverlapThreshold", ref overlapThreshold, 0f, 0f, "%.1f"))
        {
            config.OverlapThreshold = overlapThreshold;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        OmenTools.ImGuiOm.ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DuplicateStatusPrevention.OverlapThreshold.Help"));
        ImGui.SameLine();
        var chatNotify = config.ChatNotify;
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.DuplicateStatusPrevention.ChatNotify"),
                ref chatNotify))
        {
            config.ChatNotify = chatNotify;
            changed = true;
        }

        ImGui.SameLine();
        var popupNotify = config.PopupNotify;
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.DuplicateStatusPrevention.PopupNotify"),
                ref popupNotify))
        {
            config.PopupNotify = popupNotify;
            changed = true;
        }

        ImGui.Spacing();

        var enabledCount = 0;
        foreach (var enabled in config.EnabledActions.Values)
        {
            if (enabled)
            {
                enabledCount++;
            }
        }

        ImGui.TextUnformatted(string.Format(
            OmniLoc.Get("Feature.DuplicateStatusPrevention.ActionCount"),
            enabledCount,
            config.EnabledActions.Count));

        var rowHeight = MathF.Max(ImGui.GetFrameHeightWithSpacing(), OmniTheme.Scale(34f));
        var visibleRows = Math.Clamp(config.EnabledActions.Count, 1, 5);
        using var cellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(ImGui.GetStyle().CellPadding.X, OmniTheme.Scale(3f)));
        using var framePadding = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, OmniTheme.Scale(3f)));
        using var table = ImRaii.Table(
            "##duplicateStatusActions",
            2,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                rowHeight * (visibleRows + 1) + ImGui.GetStyle().ItemSpacing.Y));
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Action"), ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.DuplicateStatusPrevention.Column.Status"), ImGuiTableColumnFlags.WidthStretch, 0.5f);
        OmniControls.ScrollableTableHeadersRow();

        foreach (var action in DuplicateStatusPreventionRules.OrderedActions)
        {
            if (!LuminaGetter.TryGetRow<LuminaAction>(action.Key, out var actionRow))
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = config.EnabledActions[action.Key];
            if (OmniControls.Checkbox($"##duplicateStatusAction{action.Key}", ref enabled))
            {
                config.EnabledActions[action.Key] = enabled;
                changed = true;
            }

            ImGui.SameLine();
            DrawAction(actionRow);

            ImGui.TableNextColumn();
            var first = true;
            foreach (var status in action.Value.Statuses)
            {
                DrawStatus(status, ref first);
            }

            if (action.Value.SecondStatuses is not null)
            {
                foreach (var status in action.Value.SecondStatuses)
                {
                    DrawStatus(status, ref first);
                }
            }
        }

        return changed;
    }

    private static void DrawAction(LuminaAction action)
    {
        var iconSize = new Vector2(MathF.Max(
            OmniTheme.Scale(24f),
            ImGui.GetTextLineHeightWithSpacing() + OmniTheme.Scale(6f)));
        FramedGameIcon.DrawAction(action, iconSize);
        ImGui.SameLine();

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(action.Name.ToString());
    }

    private static void DrawStatus(DuplicateStatusRule status, ref bool first)
    {
        if (!LuminaGetter.TryGetRow<LuminaStatus>(status.StatusID, out var statusRow))
        {
            return;
        }

        var iconSize = OmniTheme.StatusIconSize(MathF.Max(
            OmniTheme.Scale(24f),
            ImGui.GetTextLineHeightWithSpacing() + OmniTheme.Scale(10f)));
        if (!first && ImGui.GetContentRegionAvail().X >= iconSize.X + ImGui.GetStyle().ItemSpacing.X)
        {
            ImGui.SameLine();
        }

        if (ImageHelper.GetGameIcon(statusRow.Icon) is { } icon)
        {
            ImGui.Image(icon.Handle, iconSize);
        }
        else
        {
            ImGui.TextUnformatted(statusRow.Name.ToString());
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(string.Format(
                OmniLoc.Get("Feature.DuplicateStatusPrevention.StatusTooltip"),
                statusRow.Name,
                OmniLoc.Get(status.DetectionTarget switch
                {
                    StatusDetectionTarget.Self => "Feature.DuplicateStatusPrevention.Target.Self",
                    StatusDetectionTarget.Member => "Feature.DuplicateStatusPrevention.Target.Member",
                    _ => "Feature.DuplicateStatusPrevention.Target.Target"
                })));
        }

        first = false;
    }
}

internal enum StatusDetectionTarget
{
    Self,
    Member,
    Target
}

internal readonly record struct DuplicateStatusRule(
    uint StatusID,
    StatusDetectionTarget DetectionTarget,
    bool IsReverse = false)
{
    public bool Blocks(bool hasStatus) => IsReverse != hasStatus;
}

internal sealed record DuplicateActionRule(
    DuplicateStatusRule[] Statuses,
    DuplicateStatusRule[]? SecondStatuses = null);

internal static class DuplicateStatusPreventionRules
{
    private static readonly KeyValuePair<uint, DuplicateActionRule>[] OrderedActionData =
    [
        new(7549, new([new(1195, StatusDetectionTarget.Target)])),
        new(7560, new([new(1203, StatusDetectionTarget.Target)])),
        new(25857, new([new(2707, StatusDetectionTarget.Self)])),
        new(2887, new([new(860, StatusDetectionTarget.Target)])),
        new(16889, new([new(1951, StatusDetectionTarget.Self), new(1934, StatusDetectionTarget.Self), new(1826, StatusDetectionTarget.Self)])),
        new(16012, new([new(1951, StatusDetectionTarget.Self), new(1934, StatusDetectionTarget.Self), new(1826, StatusDetectionTarget.Self)])),
        new(7405, new([new(1951, StatusDetectionTarget.Self), new(1934, StatusDetectionTarget.Self), new(1826, StatusDetectionTarget.Self)])),
        new(7408, new([new(1202, StatusDetectionTarget.Self)])),
        new(7535, new([new(1193, StatusDetectionTarget.Target)])),
        new(7388, new([new(1457, StatusDetectionTarget.Self)])),
        new(3540, new([new(1362, StatusDetectionTarget.Self)])),
        new(7382, new([new(1174, StatusDetectionTarget.Target)])),
        new(25754, new([new(2682, StatusDetectionTarget.Target)])),
        new(7393, new([new(1178, StatusDetectionTarget.Target)])),
        new(16160, new([new(1839, StatusDetectionTarget.Self)])),
        new(25758, new([new(2683, StatusDetectionTarget.Target)])),
        new(16151, new([new(1835, StatusDetectionTarget.Target)])),
        new(7432, new([new(1218, StatusDetectionTarget.Target)])),
        new(25861, new([new(2708, StatusDetectionTarget.Target)])),
        new(7430, new([new(1217, StatusDetectionTarget.Self)])),
        new(25873, new([new(2717, StatusDetectionTarget.Target)])),
        new(188, new([new(1944, StatusDetectionTarget.Self)])),
        new(7863, new([new(2, StatusDetectionTarget.Target)])),
        new(7540, new([new(2, StatusDetectionTarget.Target)])),
        new(16, new([new(2, StatusDetectionTarget.Target)])),
        new(7546, new([new(1250, StatusDetectionTarget.Self)])),
        new(7548, new([new(2663, StatusDetectionTarget.Self)])),
        new(3557, new([new(786, StatusDetectionTarget.Self)])),
        new(83, new([new(116, StatusDetectionTarget.Self)])),
        new(69, new([new(110, StatusDetectionTarget.Self)])),
        new(7396, new([new(1182, StatusDetectionTarget.Self), new(1185, StatusDetectionTarget.Self)])),
        new(2248, new([new(638, StatusDetectionTarget.Target)])),
        new(7499, new([new(1233, StatusDetectionTarget.Self), new(3856, StatusDetectionTarget.Self)])),
        new(7421, new([new(1211, StatusDetectionTarget.Self)])),
        new(7518, new([new(1238, StatusDetectionTarget.Self)])),
        new(25801, new([new(2703, StatusDetectionTarget.Self)])),
        new(16508, new([new(304, StatusDetectionTarget.Self)])),
        new(16510, new([new(304, StatusDetectionTarget.Self)])),
        new(2876, new([new(851, StatusDetectionTarget.Self)])),
        new(34579, new([new(3643, StatusDetectionTarget.Target)])),
        new(34567, new([new(3712, StatusDetectionTarget.Target)])),
        new(118, new([new(141, StatusDetectionTarget.Self)])),
        new(7436, new([new(1221, StatusDetectionTarget.Target)])),
        new(16552, new([new(1878, StatusDetectionTarget.Self)])),
        new(3606, new([new(841, StatusDetectionTarget.Self)])),
        new(125, new([new(148, StatusDetectionTarget.Target)])),
        new(173, new([new(148, StatusDetectionTarget.Target)])),
        new(3603, new([new(148, StatusDetectionTarget.Target)])),
        new(24287, new([new(148, StatusDetectionTarget.Target)])),
        new(41634, new([new(148, StatusDetectionTarget.Target)])),
        new(49070, new([new(148, StatusDetectionTarget.Target)])),
        new(7523, new([new(148, StatusDetectionTarget.Target)])),
        new(18317, new([new(148, StatusDetectionTarget.Target)])),
        new(29057, new([new(1342, StatusDetectionTarget.Self)])),
        new(29234, new([new(3087, StatusDetectionTarget.Target, true)], [new(3089, StatusDetectionTarget.Target)])),
        new(3585, new([new(297, StatusDetectionTarget.Target, true)])),
        new(29415, new([new(3054, StatusDetectionTarget.Target), new(1302, StatusDetectionTarget.Target), new(3039, StatusDetectionTarget.Target)])),
        new(29228, new([new(3054, StatusDetectionTarget.Target)])),
        new(29081, new([new(3054, StatusDetectionTarget.Target)])),
        new(29258, new([new(3107, StatusDetectionTarget.Self)])),
        new(29264, new([new(2872, StatusDetectionTarget.Target)])),
        new(29395, new([new(3054, StatusDetectionTarget.Target), new(3248, StatusDetectionTarget.Target)])),
        new(29515, new([new(1302, StatusDetectionTarget.Target), new(3039, StatusDetectionTarget.Target)])),
        new(29414, new([new(3158, StatusDetectionTarget.Self)])),
        new(41624, new([new(4259, StatusDetectionTarget.Target)]))
    ];

    public static ReadOnlySpan<KeyValuePair<uint, DuplicateActionRule>> OrderedActions => OrderedActionData;

    public static FrozenDictionary<uint, DuplicateActionRule> Actions { get; } =
        OrderedActionData.ToFrozenDictionary();
}

[Serializable]
public sealed class DuplicateStatusPreventionConfig
{
    public float OverlapThreshold { get; set; } = 5f;
    public bool ChatNotify { get; set; } = true;
    public bool PopupNotify { get; set; }
    public Dictionary<uint, bool> EnabledActions { get; set; } = [];
}
