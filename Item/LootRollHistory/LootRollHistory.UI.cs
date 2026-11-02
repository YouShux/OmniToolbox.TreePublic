using System.Globalization;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private sealed partial class HistoryWindow
    {
        private readonly Dictionary<uint, string> worldNames = [];
        private HistorySnapshot? renderedSnapshot;
        private LootDisplayRow[] lootRows = [];
        private LootDisplayRow[] visibleLootRows = [];
        private DisplayRow[] randomRows = [];
        private DisplayRow[] visibleRandomRows = [];
        private SessionOption[] lootSessions = [];
        private SessionOption[] randomSessions = [];
        private LootParticipant[] participants = [];
        private LootRecord? detailRecord;
        private ulong renderedCharacterID;
        private long localizationRevision = -1;
        private Guid? selectedSession;
        private Guid? selectedRandomSession;
        private Guid selectedRecordID;
        private HistoryPage page;
        private bool restoreListScroll;
        private bool restoreRandomScroll;
        private float listScrollY;

        private SessionOption[] Sessions => page == HistoryPage.Random ? randomSessions : lootSessions;

        private Guid? SelectedSession => page == HistoryPage.Random ? selectedRandomSession : selectedSession;

        private void SelectSession(Guid? sessionID)
        {
            if (page == HistoryPage.Random)
            {
                selectedRandomSession = sessionID;
                visibleRandomRows = sessionID is { } randomSession
                    ? randomRows.Where(row => row.SessionID == randomSession).ToArray() : randomRows;
                restoreRandomScroll = true;
                return;
            }
            selectedSession = sessionID;
            visibleLootRows = sessionID is { } selected
                ? lootRows.Where(row => row.Record.SessionID == selected).ToArray() : lootRows;
            page = HistoryPage.Loot;
            listScrollY = 0f;
            restoreListScroll = true;
        }

        private void RefreshRows()
        {
            if (renderedCharacterID != feature.characterID)
            {
                renderedCharacterID = feature.characterID;
                renderedSnapshot = null;
                lootRows = [];
                visibleLootRows = [];
                randomRows = [];
                visibleRandomRows = [];
                lootSessions = [];
                randomSessions = [];
                participants = [];
                detailRecord = null;
                page = HistoryPage.Loot;
                selectedSession = null;
                selectedRandomSession = null;
                listScrollY = 0f;
                restoreListScroll = true;
                restoreRandomScroll = true;
            }
            var snapshot = feature.storage?.GetSnapshot(feature.characterID);
            if (ReferenceEquals(snapshot, renderedSnapshot) && localizationRevision == OmniLoc.Revision)
                return;
            renderedSnapshot = snapshot;
            localizationRevision = OmniLoc.Revision;
            worldNames.Clear();
            lootRows = snapshot?.Loot.Select(record =>
            {
                var name = LuminaWrapper.GetItemName(record.ItemID);
                if (string.IsNullOrWhiteSpace(name))
                    name = record.ItemID.ToString(CultureInfo.CurrentCulture);
                if (record.Count > 1)
                    name = $"{name} ×{record.Count.ToString(CultureInfo.CurrentCulture)}";
                var pendingPlayers = "—";
                var pendingNames = "—";
                if (record.Status == LootStatus.Pending)
                {
                    if (record.Unassigned.Length > 0 || record.Participants.Length == 0)
                        pendingNames = Text("PendingHint");
                    else
                    {
                        var pending = record.Participants.Where(player => player.Choice == LootChoice.Pending).ToArray();
                        if (pending.Length == 0)
                            pendingNames = Text("NoPending");
                        else
                        {
                            pendingPlayers = string.Join("、", pending.Select(player => player.PlayerName));
                            pendingNames = string.Join("\n          ", pending.Select(player => GetPlayerName(player.PlayerName, player.HomeWorldID)));
                        }
                    }
                }
                var time = record.Time.ToLocalTime();
                var info = string.Format(Text("RecordInfo"), name, GetAreaName(record.TerritoryID, record.DutyID),
                    time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture), pendingNames);
                return new LootDisplayRow(record, time.ToString("HH:mm:ss", CultureInfo.CurrentCulture), name, pendingPlayers, info);
            }).ToArray() ?? [];
            randomRows = snapshot?.Rolls.Select(roll => new DisplayRow(
                roll.SessionID,
                roll.Time.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                GetPlayerName(roll.PlayerName, roll.HomeWorldID), roll.Value.ToString(CultureInfo.CurrentCulture),
                roll.Maximum.ToString(CultureInfo.CurrentCulture))).ToArray() ?? [];
            lootSessions = snapshot?.Loot.GroupBy(record => record.SessionID)
                .Select(group =>
                {
                    var time = group.Min(entry => entry.Time).ToLocalTime();
                    var record = group.First();
                    return new SessionOption(group.Key,
                        $"{time:HH:mm:ss} {GetAreaName(record.TerritoryID, record.DutyID)}\n{time:yyyy/M/d tt h:mm:ss}");
                })
                .ToArray() ?? [];
            randomSessions = snapshot?.Rolls.GroupBy(roll => roll.SessionID)
                .Select(group =>
                {
                    var time = group.Min(entry => entry.Time).ToLocalTime();
                    var roll = group.First();
                    return new SessionOption(group.Key,
                        $"{time:HH:mm:ss} {GetAreaName(roll.TerritoryID, roll.DutyID)}\n{time:yyyy/M/d tt h:mm:ss}");
                })
                .ToArray() ?? [];
            if (selectedSession is { } sessionID && !lootSessions.Any(session => session.ID == sessionID))
            {
                selectedSession = null;
                listScrollY = 0f;
                restoreListScroll = true;
            }
            if (selectedRandomSession is { } randomSessionID && !randomSessions.Any(session => session.ID == randomSessionID))
            {
                selectedRandomSession = null;
                restoreRandomScroll = true;
            }
            visibleLootRows = selectedSession is { } selected
                ? lootRows.Where(row => row.Record.SessionID == selected).ToArray() : lootRows;
            visibleRandomRows = selectedRandomSession is { } selectedRandom
                ? randomRows.Where(row => row.SessionID == selectedRandom).ToArray() : randomRows;
            if (page == HistoryPage.Details)
                SetDetails(snapshot?.Loot.FirstOrDefault(record => record.ID == selectedRecordID));
        }

        private void DrawRecords()
        {
            if (page == HistoryPage.Loot)
            {
                DrawLootRecords();
                return;
            }
            if (feature.characterID == 0)
            {
                ImGui.TextUnformatted(Text("Login"));
                return;
            }
            if (renderedSnapshot is not { } snapshot)
            {
                ImGui.TextUnformatted(Text("Loading"));
                return;
            }
            if (transfer is not null)
                ImGui.TextDisabled(Text("Busy"));
            var status = page == HistoryPage.Random ? snapshot.Status : snapshot.LootStatus;
            if (status != HistoryStatus.Ready)
                ImGui.TextUnformatted(Text(status == HistoryStatus.ReadFailed ? "ReadFailed" : "SaveFailed"));
            if (page == HistoryPage.Random)
                DrawRandomRecords();
            else if (page == HistoryPage.Details && detailRecord is not null)
                DrawLootDetails(detailRecord);
            else
                DrawLootRecords();
        }

        private void DrawLootRecords()
        {
            var available = ImGui.GetContentRegionAvail();
            var headerPosition = ImGui.GetCursorScreenPos();
            var headerHeight = OmniTheme.Scale(26f);
            var controlsWidth = GetHeaderControlsWidth() + OmniTheme.Scale(8f);
            var padding = OmniTheme.Scale(6f);
            var timeWidth = ImGui.CalcTextSize("00:00:00").X + padding * 2f;
            var valueWidth = MathF.Max(ImGui.CalcTextSize(Text("Value")).X,
                ImGui.CalcTextSize("99").X) + padding * 2f;
            var flexibleWidth = MathF.Max(OmniTheme.Scale(80f),
                available.X - ImGui.GetStyle().ScrollbarSize - controlsWidth - timeWidth - valueWidth);
            ReadOnlySpan<float> widths = [timeWidth, flexibleWidth * 0.45f, flexibleWidth * 0.25f, valueWidth,
                flexibleWidth * 0.3f + controlsWidth];
            ImGui.GetWindowDrawList().AddRectFilled(headerPosition,
                headerPosition + new Vector2(available.X, headerHeight),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.035f)));
            var x = headerPosition.X;
            DrawLootHeader(Text("Time"), ref x, headerPosition.Y, widths[0], headerHeight);
            DrawLootHeader(Text("Item"), ref x, headerPosition.Y, widths[1], headerHeight);
            DrawLootHeader(Text("Winner"), ref x, headerPosition.Y, widths[2], headerHeight);
            DrawLootHeader(Text("Value"), ref x, headerPosition.Y, widths[3], headerHeight);
            DrawLootHeader(Text("Pending"), ref x, headerPosition.Y, widths[4] - controlsWidth, headerHeight, false);
            ImGui.SetCursorScreenPos(headerPosition + new Vector2(available.X - controlsWidth,
                (headerHeight - OmniTheme.Scale(20f)) * 0.5f));
            DrawHeaderControls();
            ImGui.SetCursorScreenPos(headerPosition);
            ImGui.Dummy(new Vector2(available.X, headerHeight));

            using var child = ImRaii.Child("##lootRecordsBody", new Vector2(available.X,
                MathF.Max(1f, ImGui.GetContentRegionAvail().Y)), false, ImGuiWindowFlags.NoBackground);
            if (!child)
                return;
            if (restoreListScroll)
            {
                ImGui.SetScrollY(listScrollY);
                restoreListScroll = false;
            }
            if (renderedSnapshot is { LootStatus: not HistoryStatus.Ready } snapshot)
                ImGui.TextDisabled(Text(snapshot.LootStatus == HistoryStatus.ReadFailed ? "ReadFailed" : "SaveFailed"));
            if (visibleLootRows.Length == 0)
            {
                var message = feature.characterID == 0 ? "Login" : renderedSnapshot is null ? "Loading" :
                    renderedSnapshot.LootStatus == HistoryStatus.ReadFailed ? "ReadFailed" :
                    !feature.lootTrackingSupported ? "Unsupported" : "Empty";
                OmniControls.DrawTextCentered(Text(message), ImGui.GetCursorScreenPos(), ImGui.GetContentRegionAvail(),
                    ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                return;
            }
            var rowHeight = ImGui.GetTextLineHeight() + padding;
            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(visibleLootRows.Length, rowHeight + ImGui.GetStyle().ItemSpacing.Y);
            while (clipper.Step())
            {
                for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                    DrawLootRow(index, widths, rowHeight);
            }
            clipper.End();
            clipper.Destroy();
        }

        private static void DrawLootHeader(string label, ref float x, float y, float width, float height, bool drawDivider = true)
        {
            var position = new Vector2(x, y);
            using (ImRaii.PushClipRect(position, position + new Vector2(width, height), true))
                OmniControls.DrawTextCentered(label, position, new Vector2(width, height),
                    ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            x += width;
            if (!drawDivider)
                return;
            ImGui.GetWindowDrawList().AddLine(new Vector2(x, y + OmniTheme.Scale(6f)),
                new Vector2(x, y + height - OmniTheme.Scale(6f)),
                ImGui.GetColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.Border] with { W = 0.46f }),
                OmniTheme.Scale(1f));
        }

        private void DrawLootRow(int index, ReadOnlySpan<float> widths, float height)
        {
            var row = visibleLootRows[index];
            var position = ImGui.GetCursorScreenPos();
            var size = new Vector2(MathF.Max(1f, ImGui.GetContentRegionAvail().X), height);
            if (ImGui.InvisibleButton($"##loot{row.Record.ID}", size))
            {
                listScrollY = ImGui.GetScrollY();
                selectedRecordID = row.Record.ID;
                SetDetails(row.Record);
                page = HistoryPage.Details;
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                ImGui.SetClipboardText(row.Info);
            var hovered = ImGui.IsItemHovered();
            if (hovered || index % 2 != 0)
                ImGui.GetWindowDrawList().AddRectFilled(position, position + size,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.08f : 0.025f)));
            if (hovered)
                OmniControls.HelpTooltip($"{row.Info}\n{Text("RowActions")}");
            var color = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            var winner = row.Record.Status == LootStatus.Awarded ? row.Record.Winner : null;
            var x = position.X;
            DrawLootCell(row.Time, new Vector2(x, position.Y), new Vector2(widths[0], height), color);
            x += widths[0];
            var itemPosition = new Vector2(x + OmniTheme.Scale(6f), position.Y + (height - ImGui.GetTextLineHeight()) * 0.5f);
            using (ImRaii.PushClipRect(new Vector2(x, position.Y), new Vector2(x + widths[1], position.Y + height), true))
            {
                ImGui.SetCursorScreenPos(itemPosition);
                ItemSelectionTable.DrawItemCell(row.Record.ItemID, ImGui.GetTextLineHeight(),
                    ImGui.GetTextLineHeight(), row.Name, false);
            }
            x += widths[1];
            DrawLootCell(winner?.PlayerName ?? "—", new Vector2(x, position.Y), new Vector2(widths[2], height), color);
            x += widths[2];
            DrawLootCell(winner?.Value?.ToString(CultureInfo.CurrentCulture) ?? "—",
                new Vector2(x, position.Y), new Vector2(widths[3], height), color);
            x += widths[3];
            using (ImRaii.PushClipRect(new Vector2(x, position.Y), new Vector2(x + widths[4], position.Y + height), true))
                ImGui.GetWindowDrawList().AddText(new Vector2(x + OmniTheme.Scale(6f),
                    position.Y + MathF.Max(0f, (height - ImGui.GetTextLineHeight()) * 0.5f)),
                    OmniTheme.Color(color), row.PendingPlayers);
            ImGui.SetCursorScreenPos(position);
            ImGui.Dummy(size);
        }

        private static void DrawLootCell(string text, Vector2 position, Vector2 size, Vector4 color)
        {
            using var clip = ImRaii.PushClipRect(position, position + size, true);
            OmniControls.DrawTextCentered(text, position, size, color);
        }

        private void SetDetails(LootRecord? record)
        {
            detailRecord = record;
            if (record is null)
            {
                participants = [];
                page = HistoryPage.Loot;
                restoreListScroll = true;
                return;
            }
            participants = record.Participants.OrderBy(player => player.Choice switch
            {
                LootChoice.Need => 0,
                LootChoice.Greed => 1,
                LootChoice.Selected => 2,
                LootChoice.Pending => 3,
                _ => 4
            }).ThenByDescending(player => player.Choice is LootChoice.Need or LootChoice.Greed ? player.Value ?? -1 : -1)
                .ThenBy(player => player.PlayerName, StringComparer.CurrentCulture).ThenBy(player => player.HomeWorldID).ToArray();
            if (record.Winner is { } winner)
                participants = [winner, .. participants.Where(player => player.EntityID != winner.EntityID)];
        }

        private void DrawLootDetails(LootRecord record)
        {
            using var child = ImRaii.Child("##lootDetails", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoBackground);
            if (!child)
                return;
            var row = Array.Find(lootRows, entry => entry.Record.ID == record.ID);
            ItemSelectionTable.DrawItemCell(record.ItemID, ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight(), row?.Name, false);
            ImGui.TextDisabled($"{record.Time.ToLocalTime():MM-dd HH:mm:ss} · {GetAreaName(record.TerritoryID, record.DutyID)}");
            if (record.Winner is null)
                ImGui.TextUnformatted(Text(record.Status switch
                {
                    LootStatus.Pending => "Waiting",
                    LootStatus.Ended when record.Unassigned.Length == 0 && record.Participants.Length > 0 &&
                                              record.Participants.All(player => player.Choice == LootChoice.Pass) => "AllPassed",
                    LootStatus.Ended => "Ended",
                    LootStatus.Interrupted => "Interrupted",
                    _ => "Unknown"
                }));

            using (var table = ImRaii.Table("##lootParticipants", 3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            {
                if (table)
                {
                    ImGui.TableSetupColumn(Text("Player"), ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn(Text("Choice"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 5f);
                    ImGui.TableSetupColumn(Text("Value"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3f);
                    OmniControls.BeginTableHeaderRow(ImGui.GetTextLineHeight());
                    OmniControls.TableHeader(Text("Player"), ImGui.GetTextLineHeight());
                    OmniControls.TableHeader(Text("Choice"), ImGui.GetTextLineHeight());
                    OmniControls.TableHeader(Text("Value"), ImGui.GetTextLineHeight());
                    foreach (var player in participants)
                    {
                        ImGui.TableNextRow();
                        if (record.Winner is { } awarded && player.EntityID == awarded.EntityID)
                            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                                ImGui.GetColorU32(OmniTheme.Tokens.Success with { W = 0.2f }));
                        ImGui.TableNextColumn();
                        DrawCenteredCell(GetPlayerName(player.PlayerName, player.HomeWorldID));
                        ImGui.TableNextColumn();
                        if (player.Choice == LootChoice.Pending)
                        {
                            DrawCenteredCell(Text(record.Unassigned.Length > 0 ? "Unknown" : "Pending"));
                            if (record.Unassigned.Length == 0)
                                OmniControls.HelpTooltip(Text("PendingHint"));
                        }
                        else
                            DrawCenteredCell(GetChoiceName(player.Choice));
                        ImGui.TableNextColumn();
                        DrawCenteredCell(player.Value?.ToString(CultureInfo.CurrentCulture) ?? "—");
                    }
                }
            }
            DrawLootObservations(record);
        }

        private void DrawLootObservations(LootRecord record)
        {
            if (record.Unassigned.Length == 0)
                return;
            ImGui.TextUnformatted(Text("Unassigned"));
            using var table = ImRaii.Table("##unassignedLoot", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
            if (!table)
                return;
            ImGui.TableSetupColumn(Text("Time"), ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("00:00:00").X);
            ImGui.TableSetupColumn(Text("Player"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Text("Choice"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 5f);
            ImGui.TableSetupColumn(Text("Value"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3f);
            OmniControls.BeginTableHeaderRow(ImGui.GetTextLineHeight());
            OmniControls.TableHeader(Text("Time"), ImGui.GetTextLineHeight());
            OmniControls.TableHeader(Text("Player"), ImGui.GetTextLineHeight());
            OmniControls.TableHeader(Text("Choice"), ImGui.GetTextLineHeight());
            OmniControls.TableHeader(Text("Value"), ImGui.GetTextLineHeight());
            foreach (var observation in record.Unassigned)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawCenteredCell(observation.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture));
                ImGui.TableNextColumn();
                DrawCenteredCell(GetPlayerName(observation.Player.PlayerName, observation.Player.HomeWorldID));
                ImGui.TableNextColumn();
                DrawCenteredCell(observation.IsAward ? Text("Obtained") : GetChoiceName(observation.Player.Choice));
                ImGui.TableNextColumn();
                DrawCenteredCell(observation.Player.Value?.ToString(CultureInfo.CurrentCulture) ?? "—");
            }
        }

        private void DrawRandomRecords()
        {
            if (visibleRandomRows.Length == 0)
            {
                OmniControls.DrawTextCentered(Text("Empty"), ImGui.GetCursorScreenPos(), ImGui.GetContentRegionAvail(),
                    ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                return;
            }
            using var table = ImRaii.Table("##randomHistory", 4,
                ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp, ImGui.GetContentRegionAvail());
            if (!table)
                return;
            if (restoreRandomScroll)
            {
                ImGui.SetScrollY(0f);
                restoreRandomScroll = false;
            }
            ImGui.TableSetupColumn(Text("Time"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 9f);
            ImGui.TableSetupColumn(Text("Player"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Text("Value"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3f);
            ImGui.TableSetupColumn(Text("Maximum"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 4f);
            ImGui.TableSetupScrollFreeze(0, 1);
            OmniControls.BeginTableHeaderRow(ImGui.GetTextLineHeight());
            OmniControls.TableHeader(Text("Time"), ImGui.GetTextLineHeight());
            OmniControls.TableHeader(Text("Player"), ImGui.GetTextLineHeight());
            OmniControls.TableHeader(Text("Value"), ImGui.GetTextLineHeight());
            OmniControls.TableHeader(Text("Maximum"), ImGui.GetTextLineHeight());
            foreach (var row in visibleRandomRows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawCenteredCell(row.Time);
                ImGui.TableNextColumn();
                DrawCenteredCell(row.Player);
                ImGui.TableNextColumn();
                DrawCenteredCell(row.Value);
                ImGui.TableNextColumn();
                DrawCenteredCell(row.Maximum);
            }
        }

        private static void DrawCenteredCell(string text)
        {
            var size = new Vector2(MathF.Max(1f, ImGui.GetContentRegionAvail().X), ImGui.GetTextLineHeight());
            var position = ImGui.GetCursorScreenPos();
            DrawLootCell(text, position, size, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            ImGui.Dummy(size);
        }

        private string GetChoiceName(LootChoice choice) => Text(choice switch
        {
            LootChoice.Selected => "Selected",
            LootChoice.Need => "Need",
            LootChoice.Greed => "Greed",
            LootChoice.Pass => "Pass",
            _ => "Unknown"
        });

        private string GetPlayerName(string name, uint homeWorldID)
        {
            if (!worldNames.TryGetValue(homeWorldID, out var world))
            {
                world = homeWorldID == 0 ? string.Empty : LuminaWrapper.GetWorldName(homeWorldID);
                worldNames[homeWorldID] = world;
            }
            return string.IsNullOrEmpty(world) ? name : $"{name}@{world}";
        }

        private string GetAreaName(uint territoryID, uint dutyID)
        {
            if (dutyID != 0)
            {
                var dutyName = LuminaWrapper.GetContentName(dutyID);
                if (!string.IsNullOrWhiteSpace(dutyName))
                    return dutyName;
            }
            if (territoryID == 0)
                return Text("UnknownArea");
            var name = LuminaWrapper.GetZonePlaceName(territoryID);
            return string.IsNullOrWhiteSpace(name) ? territoryID.ToString(CultureInfo.CurrentCulture) : name;
        }

        private string Text(string key) => OmniLoc.Get(feature, $"Feature.LootRollHistory.{key}");

        private sealed record DisplayRow(Guid SessionID, string Time, string Player, string Value, string Maximum);
        private sealed record LootDisplayRow(LootRecord Record, string Time, string Name, string PendingPlayers, string Info);
        private sealed record SessionOption(Guid ID, string Label);

        private enum HistoryPage
        {
            Loot,
            Details,
            Random
        }
    }
}
