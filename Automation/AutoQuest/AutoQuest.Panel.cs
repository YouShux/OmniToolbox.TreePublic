using System.Globalization;
using System.Threading.Tasks;
using OmenTools.Dalamud;
using OmniToolbox.Operations;
using OmniToolbox.Quests;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmniToolbox.UI.Widgets;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (OmniControls.SmallButton(Title, false))
            windowOpen = true;
        return false;
    }

    public override bool TryHandleCommand(string arguments)
    {
        var parts = arguments.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length == 0 ? "open" : parts[0].ToLowerInvariant();
        if (action == "open")
        {
            windowOpen = true;
            return true;
        }
        if ((action is "pause" or "resume" or "stop") && operations.Current is { } current)
            return action switch
            {
                "pause" => Pause(current.OperationID),
                "resume" => Resume(current.OperationID),
                _ => CancelOperation(current.OperationID)
            };
        if (action == "all")
        {
            InstanceLifetime.Track(StartFromPanelAsync(new() { Caller = "manual", Mode = QuestRequestMode.Catalog }));
            return true;
        }
        if (action == "start" && parts.Length == 2 && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0)
        {
            InstanceLifetime.Track(StartFromPanelAsync(new() { Caller = "manual", Targets = [new(QuestTargetType.Quest, id)] }));
            return true;
        }
        return false;
    }

    private async Task StartFromPanelAsync(QuestRequest request)
    {
        var result = await StartAsync(request, InstanceLifetime.CancellationToken);
        panelError = result.Error;
    }

    private void DrawWindow()
    {
        if (!windowOpen || Activation.IsStopping)
            return;
        using var font = Fonts.GetUIFont().Push();
        using var style = new OmniStyleScope();
        using var placement = new OmniWindowPlacement.WindowScope("###OmniAutoQuest");
        var work = OmniWindowPlacement.GetWorkArea("###OmniAutoQuest").Size;
        if (panelScale != OmniTheme.ScaleValue || panelViewport != work)
            panelRestore = true;
        panelScale = OmniTheme.ScaleValue;
        panelViewport = work;

        var startLabel = OmniLoc.Get(this, "StartSelected");
        var pauseLabel = OmniLoc.Get(this, "Pause");
        var resumeLabel = OmniLoc.Get(this, "Resume");
        var stopLabel = OmniLoc.Get(this, "Stop");
        var buttonSize = Vector2.Max(OmniControls.CompactButtonSize(startLabel),
            Vector2.Max(OmniControls.CompactButtonSize(pauseLabel),
                Vector2.Max(OmniControls.CompactButtonSize(resumeLabel), OmniControls.CompactButtonSize(stopLabel))));
        var inset = OmniTheme.WindowInset();
        var frameInset = OmniTheme.ChromeFrameInset();
        var controlsTop = OmniTheme.TitleBarHeight() + inset;
        var compactWidth = MathF.Max(OmniTheme.Scale(460f),
            OmniTextMeasurements.Measure(Title).X + OmniTheme.TitleExpandIconRight() * 2f + inset * 2f);
        var compactHeight = controlsTop + ImGui.GetFrameHeightWithSpacing() * 2f + inset + buttonSize.Y + frameInset * 2f;
        var defaultSize = OmniTheme.Scale(new Vector2(780f, 620f));
        if (panelCollapsed)
            ImGui.SetNextWindowSize(OmniTheme.ClampWindowSize(new(compactWidth, compactHeight)), ImGuiCond.Always);
        else
        {
            ImGui.SetNextWindowSizeConstraints(OmniTheme.ClampWindowSize(OmniTheme.Scale(new Vector2(360f, 300f))), work);
            ImGui.SetNextWindowSize(OmniTheme.ClampWindowSize(panelSize == Vector2.Zero ? defaultSize : OmniTheme.Scale(panelSize)),
                panelRestore ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
            panelRestore = false;
        }
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
        OmniWindowChrome.SetNextWindowPosition("###OmniAutoQuest");
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse |
                    ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar;
        if (panelCollapsed) flags |= ImGuiWindowFlags.NoResize;
        if (!ImGui.Begin("###OmniAutoQuest", flags))
        {
            ImGui.End();
            return;
        }
        try
        {
            var status = GetStatus();
            if (!panelCollapsed)
                panelSize = OmniTheme.Unscale(OmniTheme.PreserveWindowSize(ImGui.GetWindowSize(),
                    panelSize == Vector2.Zero ? defaultSize : OmniTheme.Scale(panelSize)));
            var (framePosition, frameSize) = OmniWindowChrome.GetFrameBounds(ImGui.GetWindowPos(), ImGui.GetWindowSize(), false);
            var chrome = OmniWindowChrome.Draw(framePosition, frameSize, false, Title,
                "##collapseAutoQuest", "##closeAutoQuest");
            if (chrome.CloseClicked) { windowOpen = false; return; }
            if (chrome.ToggleCollapse) { panelCollapsed = !panelCollapsed; panelRestore = true; return; }
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
                !OmniWindowChrome.IsTextInputActive && !ImGui.IsAnyItemActive() &&
                ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                windowOpen = false;
                return;
            }

            if (panelCollapsed)
            {
                var (compactContent, _) = OmniWindowChrome.GetContentBounds(framePosition, frameSize);
                ImGui.SetCursorScreenPos(compactContent);
                ImGui.TextUnformatted($"{OmniLoc.Get(this, "Phase." + status.Phase)} · {status.CurrentTarget?.ID.ToString(CultureInfo.InvariantCulture) ?? "-"} · {status.Step ?? string.Empty}");
                ImGui.SameLine();
                ImGui.TextUnformatted($"{status.Completed}/{status.Completed + status.Remaining}");
                ImGui.SetCursorScreenPos(compactContent + new Vector2(0, ImGui.GetFrameHeightWithSpacing()));
                DrawOperationControls(status, true, buttonSize);
                return;
            }

            var content = framePosition + new Vector2(inset, controlsTop);
            ImGui.SetCursorScreenPos(content);
            ImGui.TextUnformatted(OmniLoc.Get(this, "Status") + ": " + OmniLoc.Get(this, "Phase." + status.Phase));
            ImGui.SameLine();
            ImGui.TextUnformatted($"{status.Completed}/{status.Completed + status.Remaining} · {status.Blocked}");
            if (status.CurrentTarget is { } current)
            {
                ImGui.SetCursorScreenPos(content + new Vector2(0, ImGui.GetFrameHeightWithSpacing()));
                ImGui.TextUnformatted($"{current.ID} · {status.Sequence} · {status.Step}");
            }
            if (status.Reason is { Length: > 0 } reason)
            {
                ImGui.SetCursorScreenPos(content + new Vector2(0, ImGui.GetFrameHeightWithSpacing() * (status.CurrentTarget is null ? 1 : 2)));
                ImGui.TextWrapped(FormatBlocker(reason));
            }
            var controlsY = ImGui.GetCursorScreenPos().Y + inset;
            ImGui.SetCursorScreenPos(new(content.X, controlsY));
            DrawOperationControls(status, false, buttonSize);
            var resultY = controlsY + buttonSize.Y + inset;
            if (panelError is { Length: > 0 })
            {
                ImGui.SetCursorScreenPos(new(content.X, resultY));
                ImGui.TextWrapped(FormatBlocker(panelError));
                resultY = ImGui.GetCursorScreenPos().Y + inset;
            }
            ImGui.SetCursorScreenPos(new(content.X, resultY));
            if (ImGui.InputTextWithHint("##questSearch", OmniLoc.Get(this, "Search"), ref search, 256))
            {
                pageOffset = 0;
                nextCatalogRefresh = 0;
            }
            ImGui.SameLine();
            if (ImGui.BeginCombo("##questType", filter is { } type ? OmniLoc.Get(this, "Type." + type) : OmniLoc.Get(this, "AllTypes")))
            {
                if (ImGui.Selectable(OmniLoc.Get(this, "AllTypes"), filter is null))
                {
                    filter = null;
                    pageOffset = 0;
                    nextCatalogRefresh = 0;
                }
                foreach (var targetType in Enum.GetValues<QuestTargetType>())
                    if (ImGui.Selectable(OmniLoc.Get(this, "Type." + targetType), filter == targetType))
                    {
                        filter = targetType;
                        pageOffset = 0;
                        nextCatalogRefresh = 0;
                    }
                ImGui.EndCombo();
            }
            if (Environment.TickCount64 >= nextCatalogRefresh)
            {
                page = GetCatalog(new(pageOffset, 100, search, filter));
                nextCatalogRefresh = Environment.TickCount64 + 2_000;
            }
            if (page is not { } catalog)
                return;
            ImGui.TextUnformatted($"{catalog.Offset + 1}–{catalog.Offset + catalog.Entries.Length} / {catalog.Total}");
            if (ImGui.BeginTable("##quests", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY,
                    new Vector2(0, MathF.Max(120f, ImGui.GetContentRegionAvail().Y * 0.62f))))
            {
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 95 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn(OmniLoc.Get(this, "Name"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(OmniLoc.Get(this, "Status"), ImGuiTableColumnFlags.WidthFixed, 95 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn(OmniLoc.Get(this, "Details"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var entry in catalog.Entries)
                {
                    ImGui.PushID($"{entry.Target.Type}:{entry.Target.ID}");
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var isSelected = selected.Contains(entry.Target);
                    if (ImGui.Checkbox(entry.Target.ID.ToString(CultureInfo.InvariantCulture), ref isSelected))
                    {
                        if (isSelected) selected.Add(entry.Target);
                        else selected.Remove(entry.Target);
                    }
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.Name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(OmniLoc.Get(this, "Availability." + entry.Availability));
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(entry.Blockers.Length == 0 ? OmniLoc.Get(this, "Unverified") : FormatBlocker(entry.Blockers[0].Code));
                    if (ImGui.IsItemHovered() && entry.Blockers.Length != 0)
                        ImGui.SetTooltip(string.Join("\n", entry.Blockers.Select(blocker =>
                            $"{FormatBlocker(blocker.Code)}\n{blocker.Code}" + (blocker.RelatedID is { } id ? $" · ID: {id}" : string.Empty))));
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            using (ImRaii.Disabled(pageOffset == 0))
                if (OmniControls.SmallButton(OmniLoc.Get(this, "Previous"), false)) { pageOffset = Math.Max(0, pageOffset - 100); nextCatalogRefresh = 0; }
            ImGui.SameLine();
            using (ImRaii.Disabled(pageOffset + catalog.Entries.Length >= catalog.Total))
                if (OmniControls.SmallButton(OmniLoc.Get(this, "Next"), false)) { pageOffset += 100; nextCatalogRefresh = 0; }
            if (operations.Latest is { State: not OperationState.Running } latest && GetQuestOperation(latest.OperationID) is { } result)
            {
                ImGui.Separator();
                ImGui.TextUnformatted(OmniLoc.Get(this, latest.State == OperationState.Succeeded ? "Completed" : "RoundEnded"));
                ImGui.TextUnformatted($"{OmniLoc.Get(this, "Availability.Satisfied")}: {result.Targets.Count(static target => target.Availability == QuestAvailability.Satisfied)} / {result.Targets.Length}");
                foreach (var item in result.Targets.Where(static target => target.Availability != QuestAvailability.Satisfied).Take(8))
                    ImGui.TextWrapped($"{item.Target.ID}: {string.Join("; ", item.Blockers.Select(blocker => FormatBlocker(blocker.Code)))}");
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    private void DrawOperationControls(QuestServiceStatus status, bool compact, Vector2 buttonSize)
    {
        if (operations.Current is { } active)
        {
            var paused = status.Phase == QuestExecutionPhase.Paused;
            using (ImRaii.Disabled(starting || restoring))
                if (OmniControls.SmallButton(OmniLoc.Get(this, paused ? "Resume" : "Pause"), false, buttonSize))
                {
                    if (paused) Resume(active.OperationID);
                    else Pause(active.OperationID);
                }
            ImGui.SameLine();
            if (OmniControls.SmallButton(OmniLoc.Get(this, "Stop"), false, buttonSize))
                CancelOperation(active.OperationID);
            return;
        }

        var label = OmniLoc.Get(this, compact ? "StartAll" : "StartSelected");
        using (ImRaii.Disabled(starting || restoring || !compact && selected.Count == 0))
            if (OmniControls.SmallButton(label, false, buttonSize))
            {
                var request = compact
                    ? new QuestRequest { Caller = "manual", Mode = QuestRequestMode.Catalog }
                    : new QuestRequest { Caller = "manual", Targets = [.. selected.OrderBy(static item => item.Type).ThenBy(static item => item.ID)] };
                InstanceLifetime.Track(StartFromPanelAsync(request));
            }
        if (compact) return;
        ImGui.SameLine();
        using (ImRaii.Disabled(starting || restoring || operations.Current is not null))
            if (OmniControls.SmallButton(OmniLoc.Get(this, "StartAll"), false, buttonSize))
                InstanceLifetime.Track(StartFromPanelAsync(new() { Caller = "manual", Mode = QuestRequestMode.Catalog }));
    }

    private string FormatBlocker(string code)
    {
        var key = "Error." + code;
        if (TextKeys.Contains(key, StringComparer.Ordinal))
            return OmniLoc.Get(this, key);
        if (code.StartsWith("script_", StringComparison.Ordinal) || code.Contains("unresolved", StringComparison.Ordinal) ||
            code.Contains("unsupported", StringComparison.Ordinal) || code.Contains("unimplemented", StringComparison.Ordinal) ||
            code.Contains("unverified", StringComparison.Ordinal))
            return OmniLoc.Get(this, "Error.mechanism_unsupported");
        return $"{OmniLoc.Get(this, "Blocker")}: {code}";
    }

    private static readonly string[] TextKeys =
    [
        "Title", "Description", "CommandOpen", "CommandStart", "CommandAll", "CommandPause", "CommandResume", "CommandStop",
        "Status", "StartSelected", "StartAll", "Resume", "Pause", "Stop", "Search", "AllTypes", "Name", "Details", "Unverified", "Previous", "Next", "Completed", "RoundEnded", "Blocker",
        "Type.Quest", "Type.SatisfactionSupply", "Type.AlliedSociety", "Type.UnlockLink",
        "Availability.Satisfied", "Availability.Ready", "Availability.Waiting", "Availability.Blocked", "Availability.Unsupported",
        "Phase.Idle", "Phase.Starting", "Phase.Planning", "Phase.Travelling", "Phase.Interacting", "Phase.Fighting", "Phase.Duty", "Phase.Crafting", "Phase.Gathering", "Phase.Fishing", "Phase.Waiting", "Phase.Paused", "Phase.Stopping", "Phase.Finished",
        "Error.quest_busy", "Error.not_logged_in", "Error.module_unavailable", "Error.dialogue_choice_unresolved", "Error.mechanism_unsupported", "Error.resource_loading", "Error.script_loading", "Error.target_not_found", "Error.navigation_unavailable", "Error.autoduty_unavailable", "Error.autoduty_incompatible", "Error.duty_path_unsupported", "Error.player_party_required", "Error.artisan_unavailable", "Error.autohook_incompatible", "Error.material_source_unsupported",
        "Error.quest_job_level_or_gearset_required", "Error.quest_secondary_job_required", "Error.quest_expansion_unavailable", "Error.quest_grand_company_required", "Error.quest_grand_company_rank_required", "Error.quest_society_rank_required", "Error.quest_mount_required", "Error.quest_mutually_exclusive", "Error.quest_duty_prerequisite_required", "Error.quest_repeat_completion_unconfirmed", "Error.character_busy", "Error.external_interaction_busy", "Error.plugin_busy", "Error.navigation_busy", "Error.combat_busy", "Error.level_insufficient", "Error.gearset_unavailable", "Error.duty_incomplete", "Error.duty_locked", "Error.party_not_empty", "Error.request_invalid", "Error.request_incomplete", "Error.cancelled", "Error.character_changed_or_disconnected", "Error.npc_party_conditions_unmet", "Error.npc_level_insufficient",
        "Error.player_busy", "Error.npc_entry_unavailable", "Error.gil_insufficient", "Error.inventory_full", "Error.supply_purchase_unconfirmed", "Error.supply_shop_item_locked_or_unavailable", "Error.supply_shop_timeout", "Error.autohook_preset_restore_unsupported", "Error.npc_squadron_locked", "Error.npc_trust_locked", "Error.npc_squadron_members_busy", "Error.company_seals_insufficient", "Error.class_job_switch_unconfirmed", "Error.quest_prerequisite_blocked",
        "Error.navigation_timeout", "Error.navigation_unconfirmed", "Error.navigation_failed", "Error.navigation_ipc_failed",
        "Error.navigation_taken_over", "Error.navigation_not_owned", "Error.travel_ownership_changed", "Error.destination_unavailable",
        "Error.destination_unreachable", "Error.boundary_unreachable", "Error.boundary_transition_timeout", "Error.teleport_interrupted",
        "Error.teleport_timeout", "Error.teleport_arrival_unconfirmed", "Error.teleport_unconfirmed", "Error.teleport_rejected",
        "Error.teleport_failed", "Error.teleport_cancelled", "Error.teleport_unavailable", "Error.teleport_stop_pending", "Error.navigation_stop_pending",
        "Error.character_not_ready", "Error.character_not_ready_timeout", "Error.character_changed", "Error.interaction_timeout",
        "Error.module_disabled", "Error.quest_accept_unavailable", "Error.quest_reward_unavailable", "Error.quest_reward_unresolved",
        "Error.quest_reward_inventory_full", "Error.quest_reward_unique_owned", "Error.quest_reward_capacity_unresolved", "Error.handover_items_changed",
        "Error.handover_slot_mismatch", "Error.handover_quantity_unsupported", "Error.handover_no_progress", "Error.handover_context_busy",
        "Error.handover_item_unavailable", "Error.handover_items_unresolved", "Error.handover_item_requirement_unsupported", "Error.handover_unavailable",
        "Error.external_gathering_busy", "Error.gathering_no_progress", "Error.gathering_item_unavailable", "Error.gathering_job_required",
        "Error.dialogue_advance_unavailable", "Error.craft_start_unconfirmed", "Error.craft_result_incomplete", "Error.craft_timeout",
        "Error.artisan_ipc_failed", "Error.player_dead", "Error.execution_exception"
    ];
}
