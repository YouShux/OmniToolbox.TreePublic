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
    private readonly OmniWindowSize panelSize = new("###OmniAutoQuest");

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (OmniControls.SmallButton(Title, false))
            OpenWindow();
        return false;
    }

    public override bool TryHandleCommand(string arguments)
    {
        var text = arguments.Trim();
        if (text.Length == 0)
        {
            OpenWindow();
            return true;
        }
        if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
            return false;
        InstanceLifetime.Track(StartFromPanelAsync(new() { Caller = "manual", Targets = [new(QuestTargetType.Quest, id)] }));
        return true;
    }

    private async Task StartFromPanelAsync(QuestRequest request)
    {
        var result = await StartAsync(request, InstanceLifetime.CancellationToken);
        panelError = result.Error;
    }

    private void OpenWindow()
    {
        windowOpen = true;
        panelFocusRequested = true;
        panelCollapsed = false;
    }

    private void DrawWindow()
    {
        if (!windowOpen || Activation.IsStopping)
            return;
        using var font = Fonts.GetUIFont().Push();
        using var style = new OmniStyleScope();
        using var placement = new OmniWindowPlacement.WindowScope("###OmniAutoQuest");

        var running = operations.Current is not null;
        var runLabel = OmniLoc.Get(this, running ? "Stop" : "Start");
        var actionLabel = OmniLoc.Get(this, panelCollapsed ? "Expand" : "Collapse");
        var buttonSize = OmniAutomationPanel.MeasureButtons(runLabel, actionLabel);
        panelSize.Apply(new(780f, 620f), new(360f, 300f),
            panelCollapsed ? OmniAutomationPanel.MeasureCompactSize(Title, buttonSize) : null);
        if (panelFocusRequested)
        {
            ImGui.SetNextWindowFocus();
            panelFocusRequested = false;
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
            panelSize.SaveAfterResize();
            var (framePosition, frameSize) = OmniWindowChrome.GetFrameBounds(ImGui.GetWindowPos(), ImGui.GetWindowSize(), false);
            var chrome = OmniAutomationPanel.DrawChrome(framePosition, frameSize, Title,
                "##collapseAutoQuest", "##closeAutoQuest");
            if (chrome.CloseClicked) { windowOpen = false; return; }
            if (chrome.ToggleCollapse) { panelCollapsed = !panelCollapsed; return; }

            var statusText = OmniLoc.Get(this, "Status") + ": " + OmniLoc.Get(this, "Phase." + status.Phase);
            var layout = OmniAutomationPanel.Arrange(framePosition, frameSize, panelCollapsed, buttonSize,
                OmniTextMeasurements.Measure(statusText).X);
            if (!panelCollapsed)
            {
                ImGui.SetCursorScreenPos(layout.SelectorPosition);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(statusText);
            }
            var buttons = OmniAutomationPanel.DrawButtons(layout, runLabel, running, actionLabel,
                !running && (starting || restoring || selected.Count == 0));
            if (buttons.RunClicked)
            {
                if (operations.Current is { } active)
                    CancelOperation(active.OperationID);
                else
                    InstanceLifetime.Track(StartFromPanelAsync(new()
                    {
                        Caller = "manual",
                        Targets = [.. selected.OrderBy(static item => item.Type).ThenBy(static item => item.ID)]
                    }));
            }
            if (buttons.CollapseClicked)
            {
                panelCollapsed = !panelCollapsed;
                return;
            }
            if (panelCollapsed) return;

            ImGui.SetCursorScreenPos(layout.ContentPosition);
            using var childColor = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
            using var childStyle = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, OmniTheme.Scale(OmniTheme.Tokens.WindowPadding));
            using var child = ImRaii.Child("##autoQuestContent", layout.ContentSize, false,
                ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysUseWindowPadding);
            if (!child) return;

            ImGui.TextWrapped($"{status.Completed}/{status.Completed + status.Remaining} · {status.Blocked}");
            if (status.CurrentTarget is { } current)
                ImGui.TextWrapped($"{current.ID} · {status.Sequence} · {status.Step}");
            if (status.Reason is { Length: > 0 } reason)
                ImGui.TextWrapped(FormatBlocker(reason));
            DrawOperationControls(status);
            ImGui.Spacing();
            if (panelError is { Length: > 0 })
                ImGui.TextWrapped(FormatBlocker(panelError));
            var searchLabel = OmniLoc.Get(this, "Search");
            var typePreview = filter is { } type ? OmniLoc.Get(this, "Type." + type) : OmniLoc.Get(this, "AllTypes");
            var typeSize = OmniControls.MeasureCombo(OmniLoc.Get(this, "AllTypes"));
            foreach (var targetType in Enum.GetValues<QuestTargetType>())
                typeSize = Vector2.Max(typeSize, OmniControls.MeasureCombo(OmniLoc.Get(this, "Type." + targetType)));
            using (var filters = OmniControls.SettingsTable("##questFilters",
                       [OmniControls.MeasureInput(searchLabel), typeSize],
                       ["##search", "##type"], [1f, 0f], ImGuiTableFlags.NoPadOuterX))
            {
                if (filters)
                {
                    ImGui.TableNextColumn();
                    if (OmniControls.InputTextWithHint("##questSearch", searchLabel, ref search, 256,
                            ImGui.GetContentRegionAvail().X))
                    {
                        pageOffset = 0;
                        nextCatalogRefresh = 0;
                    }
                    ImGui.TableNextColumn();
                    using var combo = OmniControls.Combo("##questType", typePreview, ImGui.GetContentRegionAvail().X);
                    if (combo)
                    {
                        if (OmniControls.RoundedSelectable(OmniLoc.Get(this, "AllTypes"), filter is null))
                        {
                            filter = null;
                            pageOffset = 0;
                            nextCatalogRefresh = 0;
                        }
                        foreach (var targetType in Enum.GetValues<QuestTargetType>())
                            if (OmniControls.RoundedSelectable(OmniLoc.Get(this, "Type." + targetType), filter == targetType))
                            {
                                filter = targetType;
                                pageOffset = 0;
                                nextCatalogRefresh = 0;
                            }
                    }
                }
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
                    if (OmniControls.Checkbox(entry.Target.ID.ToString(CultureInfo.InvariantCulture), ref isSelected))
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

    private void DrawOperationControls(QuestServiceStatus status)
    {
        if (operations.Current is { } active)
        {
            var paused = status.Phase == QuestExecutionPhase.Paused;
            using (ImRaii.Disabled(starting || restoring))
                if (OmniControls.SmallButton(OmniLoc.Get(this, paused ? "Resume" : "Pause"), false))
                {
                    if (paused) Resume(active.OperationID);
                    else Pause(active.OperationID);
                }
            return;
        }

        using (ImRaii.Disabled(starting || restoring))
            if (OmniControls.SmallButton(OmniLoc.Get(this, "StartAll"), false))
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
        "Title", "Description", "CommandOpen",
        "Status", "Start", "StartSelected", "StartAll", "Resume", "Pause", "Stop", "Expand", "Collapse", "Search", "AllTypes", "Name", "Details", "Unverified", "Previous", "Next", "Completed", "RoundEnded", "Blocker",
        "Type.Quest", "Type.SatisfactionSupply", "Type.AlliedSociety", "Type.UnlockLink",
        "Availability.Satisfied", "Availability.Ready", "Availability.Waiting", "Availability.Blocked", "Availability.Unsupported",
        "Phase.Idle", "Phase.Starting", "Phase.Planning", "Phase.Travelling", "Phase.Interacting", "Phase.Fighting", "Phase.Duty", "Phase.Crafting", "Phase.Gathering", "Phase.Fishing", "Phase.Waiting", "Phase.Paused", "Phase.Stopping", "Phase.Finished",
        "Error.quest_busy", "Error.not_logged_in", "Error.module_unavailable", "Error.dialogue_choice_unresolved", "Error.mechanism_unsupported", "Error.resource_loading", "Error.script_loading", "Error.target_not_found", "Error.quest_combat_search_exhausted", "Error.navigation_unavailable", "Error.autoduty_unavailable", "Error.autoduty_incompatible", "Error.duty_path_unsupported", "Error.player_party_required", "Error.artisan_unavailable", "Error.autohook_incompatible", "Error.material_source_unsupported",
        "Error.quest_job_level_or_gearset_required", "Error.quest_secondary_job_required", "Error.quest_expansion_unavailable", "Error.quest_grand_company_required", "Error.quest_grand_company_rank_required", "Error.quest_society_rank_required", "Error.quest_mount_required", "Error.quest_mutually_exclusive", "Error.quest_duty_prerequisite_required", "Error.quest_repeat_completion_unconfirmed", "Error.character_busy", "Error.external_interaction_busy", "Error.plugin_busy", "Error.navigation_busy", "Error.combat_busy", "Error.combat_target_unconfirmed", "Error.level_insufficient", "Error.gearset_unavailable", "Error.duty_incomplete", "Error.duty_locked", "Error.party_not_empty", "Error.request_invalid", "Error.request_incomplete", "Error.cancelled", "Error.character_changed_or_disconnected", "Error.npc_party_conditions_unmet", "Error.npc_level_insufficient",
        "Error.player_busy", "Error.npc_entry_unavailable", "Error.gil_insufficient", "Error.inventory_full", "Error.supply_purchase_unconfirmed", "Error.supply_shop_item_locked_or_unavailable", "Error.supply_shop_timeout", "Error.autohook_preset_restore_unsupported", "Error.npc_squadron_locked", "Error.npc_trust_locked", "Error.npc_squadron_members_busy", "Error.company_seals_insufficient", "Error.class_job_switch_unconfirmed", "Error.quest_prerequisite_blocked",
        "Error.navigation_timeout", "Error.navigation_unconfirmed", "Error.navigation_failed", "Error.navigation_ipc_failed",
        "Error.navigation_taken_over", "Error.navigation_not_owned", "Error.travel_ownership_changed", "Error.destination_unavailable",
        "Error.quest_prerequisite_incomplete", "Error.quest_society_reputation_required", "Error.quest_capture_item_unresolved", "Error.navigation_route_exhausted",
        "Error.destination_unreachable", "Error.boundary_unreachable", "Error.boundary_transition_timeout", "Error.teleport_interrupted",
        "Error.teleport_timeout", "Error.teleport_arrival_unconfirmed", "Error.teleport_unconfirmed", "Error.teleport_rejected",
        "Error.teleport_failed", "Error.teleport_cancelled", "Error.teleport_unavailable", "Error.teleport_stop_pending", "Error.navigation_stop_pending",
        "Error.character_not_ready", "Error.character_not_ready_timeout", "Error.character_changed", "Error.interaction_timeout",
        "Error.module_disabled", "Error.quest_accept_unavailable", "Error.quest_accept_not_offered",
        "Error.quest_reward_unavailable", "Error.quest_reward_unresolved",
        "Error.quest_reward_inventory_full", "Error.quest_reward_unique_owned", "Error.quest_reward_capacity_unresolved", "Error.handover_items_changed",
        "Error.handover_slot_mismatch", "Error.handover_quantity_unsupported", "Error.handover_no_progress", "Error.handover_context_busy",
        "Error.handover_item_unavailable", "Error.handover_items_unresolved", "Error.handover_item_requirement_unsupported", "Error.handover_unavailable",
        "Error.external_gathering_busy", "Error.gathering_no_progress", "Error.gathering_item_unavailable", "Error.gathering_job_required",
        "Error.dialogue_advance_unavailable", "Error.craft_start_unconfirmed", "Error.craft_result_incomplete", "Error.craft_timeout",
        "Error.artisan_ipc_failed", "Error.player_dead", "Error.execution_exception"
    ];
}
