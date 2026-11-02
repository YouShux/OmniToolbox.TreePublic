using System.Collections.Concurrent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using TreeTools;

namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private readonly object lootNoticeSync = new();
    private readonly ConcurrentQueue<(RecordingSession Session, LootRollNotice Notice)> lootNotices = new();
    private readonly Dictionary<(uint ChestID, uint ItemIndex), LiveLoot> liveLoot = [];
    private readonly Dictionary<(uint ChestID, uint ItemIndex), uint> untrackedLoot = [];
    private readonly Dictionary<uint, LootGroup> lootGroups = [];
    private bool lootTrackingSupported;

    private void OnLootNotice(LootRollNotice notice)
    {
        lock (lootNoticeSync)
        {
            if (recordingSession is { } session)
                lootNotices.Enqueue((session, notice));
        }
    }

    private void DrainLootNotices()
    {
        while (lootNotices.TryDequeue(out var entry))
        {
            if (entry.Session.CharacterID != characterID || characterID == 0)
                continue;
            ApplyLootNotice(entry.Session, entry.Notice);
        }
    }

    private void ApplyLootNotice(RecordingSession session, LootRollNotice notice)
    {
        if (notice.Kind == LootRollNoticeKind.Item)
        {
            if (notice.ChestID is 0 or 0xE0000000 || notice.ChestItemIndex > byte.MaxValue ||
                notice.ItemID == 0 || notice.Count == 0)
                return;
            var key = (notice.ChestID, notice.ChestItemIndex);
            if (untrackedLoot.TryGetValue(key, out var untrackedItem))
            {
                if (untrackedItem == notice.ItemID)
                    return;
                untrackedLoot.Remove(key);
            }
            if (liveLoot.TryGetValue(key, out var existing))
            {
                if (existing.Record.ItemID == notice.ItemID)
                    return;
                CloseLoot(key, existing, LootStatus.Ended);
            }
            if (!lootGroups.TryGetValue(notice.ItemID, out var group))
            {
                group = new();
                lootGroups.Add(notice.ItemID, group);
            }
            var record = new LootRecord(Guid.NewGuid(), notice.Time, notice.ChestID, notice.ChestItemIndex,
                notice.ItemID, notice.Count, LootStatus.Pending, CaptureLootRoster(), null, [])
            {
                TerritoryID = session.TerritoryID,
                DutyID = session.DutyID,
                SessionID = session.ID
            };
            var added = new LiveLoot(record, group);
            if (group.Members.Count >= HISTORY_LIMIT)
                group.Members.RemoveAll(entry => entry.Record.Status != LootStatus.Pending);
            group.Members.Add(added);
            if (group.Members.Count > 1 || untrackedLoot.ContainsValue(notice.ItemID))
                group.Ambiguous = true;
            liveLoot.Add(key, added);
            storage?.RecordLoot(characterID, record);
            return;
        }

        if (notice.Kind == LootRollNoticeKind.Removed)
        {
            foreach (var pair in liveLoot.Where(pair => pair.Key.ChestID == notice.ChestID).ToArray())
                CloseLoot(pair.Key, pair.Value, LootStatus.Ended);
            foreach (var key in untrackedLoot.Keys.Where(key => key.ChestID == notice.ChestID).ToArray())
                untrackedLoot.Remove(key);
            return;
        }

        if (notice.Kind is LootRollNoticeKind.Result or LootRollNoticeKind.Assignment)
        {
            if (!liveLoot.TryGetValue((notice.ChestID, notice.ChestItemIndex), out var localLoot))
                return;
            var local = DService.Instance().ObjectTable.LocalPlayer;
            if (local is null)
                return;
            var choice = LootChoice.Pending;
            if (notice.Kind == LootRollNoticeKind.Assignment)
            {
                if (notice.State == (uint)RollState.Rolled)
                    choice = LootChoice.Selected;
            }
            else
                choice = notice.Code switch
                {
                    0 => LootChoice.Need,
                    1 => LootChoice.Greed,
                    4 => LootChoice.Pass,
                    _ => LootChoice.Pending
                };
            if (choice == LootChoice.Pending)
                return;
            int? value = choice is LootChoice.Need or LootChoice.Greed && notice.Value is >= 1 and <= 99
                ? notice.Value : null;
            UpdateLootPlayer(localLoot, ResolveLootPlayer(local.EntityID, localLoot.Record) with
            {
                Choice = choice,
                Value = value
            });
            return;
        }

        if (notice.ActorID is 0 or 0xE0000000 ||
            !lootGroups.TryGetValue(notice.ItemID, out var candidates))
            return;
        if (notice.Kind == LootRollNoticeKind.Score &&
            (notice.Code is not (1 or 2) || notice.Value is < 1 or > 99))
            return;
        var playerChoice = notice.Kind == LootRollNoticeKind.Score ? notice.Code switch
        {
            1 => LootChoice.Need,
            2 => LootChoice.Greed,
            _ => LootChoice.Selected
        } : LootChoice.Selected;
        int? points = notice.Kind == LootRollNoticeKind.Score && notice.Value is >= 1 and <= 99
            ? notice.Value : null;
        var isAward = notice.Kind == LootRollNoticeKind.Award;
        if (candidates.Ambiguous)
        {
            foreach (var candidate in candidates.Members)
            {
                var observed = ResolveLootPlayer(notice.ActorID, candidate.Record);
                if (!isAward)
                    observed = observed with { Choice = playerChoice, Value = points };
                var observation = new LootObservation(notice.Time, observed, isAward);
                var observations = candidate.Record.Unassigned;
                if (observations.Length == MAX_UNASSIGNED_EVENTS)
                    observations = observations[1..];
                candidate.Record = candidate.Record with { Unassigned = [.. observations, observation] };
                storage?.RecordLoot(characterID, candidate.Record);
            }
            return;
        }

        var target = candidates.Members[0];
        if (target.Record.Count != notice.Count || target.Record.Status != LootStatus.Pending)
            return;
        var player = ResolveLootPlayer(notice.ActorID, target.Record);
        if (!isAward)
            UpdateLootPlayer(target, player with { Choice = playerChoice, Value = points });
        if (isAward)
        {
            var winner = target.Record.Participants.FirstOrDefault(entry => entry.EntityID == notice.ActorID) ?? player;
            target.Record = CompleteLootSettlement(target.Record with { Status = LootStatus.Awarded, Winner = winner });
            storage?.RecordLoot(characterID, target.Record);
            lootGroups.Remove(notice.ItemID);
        }
    }

    private void UpdateLootPlayer(LiveLoot loot, LootParticipant player)
    {
        var participants = loot.Record.Participants;
        var index = Array.FindIndex(participants, entry => entry.EntityID == player.EntityID);
        if (index >= 0)
        {
            var previous = participants[index];
            if (player.Choice == LootChoice.Selected && previous.Choice != LootChoice.Pending)
                return;
            if (player == previous)
                return;
            participants = [.. participants];
            participants[index] = player;
        }
        else if (participants.Length < MAX_LOOT_PARTICIPANTS)
            participants = [.. participants, player];
        else
            return;
        loot.Record = loot.Record with { Participants = participants };
        storage?.RecordLoot(characterID, loot.Record);
    }

    private LootParticipant[] CaptureLootRoster()
    {
        var players = new List<LootParticipant>();
        var services = DService.Instance();
        foreach (var member in services.PartyList)
        {
            var name = member.Name.TextValue;
            if (member.EntityId is 0 or 0xE0000000 || string.IsNullOrWhiteSpace(name) ||
                name.Length > MAX_PLAYER_NAME_LENGTH || players.Any(player => player.EntityID == member.EntityId))
                continue;
            players.Add(new(member.EntityId, name, member.World.RowId, LootChoice.Pending, null));
            if (players.Count == MAX_LOOT_PARTICIPANTS)
                break;
        }
        if (services.ObjectTable.LocalPlayer is { } local && players.All(player => player.EntityID != local.EntityID) &&
            players.Count < MAX_LOOT_PARTICIPANTS)
            players.Add(new(local.EntityID, local.Name, local.HomeWorld.RowId, LootChoice.Pending, null));
        return [.. players];
    }

    private LootParticipant ResolveLootPlayer(uint entityID, LootRecord record)
    {
        var known = record.Participants.FirstOrDefault(player => player.EntityID == entityID);
        if (known is not null)
            return known;
        foreach (var member in DService.Instance().PartyList)
        {
            if (member.EntityId == entityID && !string.IsNullOrWhiteSpace(member.Name.TextValue))
                return new(entityID, member.Name.TextValue, member.World.RowId, LootChoice.Pending, null);
        }
        if (DService.Instance().ObjectTable.SearchByEntityID(entityID) is IPlayerCharacter player)
            return new(entityID, player.Name, player.HomeWorld.RowId, LootChoice.Pending, null);
        return new(entityID, $"{OmniLoc.Get(this, "Feature.LootRollHistory.Unknown")} ({entityID:X8})",
            0, LootChoice.Pending, null);
    }

    private unsafe void SyncLootPresence()
    {
        if (!lootTrackingSupported || liveLoot.Count == 0 && untrackedLoot.Count == 0)
            return;
        var nativeLoot = Loot.Instance();
        if (nativeLoot == null)
            return;
        List<(uint ChestID, uint ItemIndex)>? missing = null;
        foreach (var pair in liveLoot)
        {
            if (!IsLootPresent(nativeLoot, pair.Key, pair.Value.Record.ItemID))
            {
                missing ??= [];
                missing.Add(pair.Key);
            }
        }
        if (missing is not null)
            foreach (var key in missing)
                CloseLoot(key, liveLoot[key], LootStatus.Ended);
        if (untrackedLoot.Count == 0)
            return;
        missing = null;
        foreach (var pair in untrackedLoot)
        {
            if (!IsLootPresent(nativeLoot, pair.Key, pair.Value))
            {
                missing ??= [];
                missing.Add(pair.Key);
            }
        }
        if (missing is not null)
            foreach (var key in missing)
                untrackedLoot.Remove(key);
    }

    private static unsafe bool IsLootPresent(Loot* loot, (uint ChestID, uint ItemIndex) key, uint itemID)
    {
        foreach (ref readonly var slot in loot->Items)
            if (slot.ChestObjectId == key.ChestID && slot.ChestItemIndex == key.ItemIndex &&
                slot.ItemId == itemID && slot.ItemCount != 0)
                return true;
        return false;
    }

    private unsafe void CaptureExistingLoot()
    {
        if (!lootTrackingSupported || characterID == 0)
            return;
        var nativeLoot = Loot.Instance();
        if (nativeLoot == null)
            return;
        foreach (ref readonly var slot in nativeLoot->Items)
        {
            if (slot.ChestObjectId is not (0 or 0xE0000000) && slot.ItemId != 0 && slot.ItemCount != 0)
                untrackedLoot[(slot.ChestObjectId, slot.ChestItemIndex)] = slot.ItemId;
        }
    }

    private void CloseLoot((uint ChestID, uint ItemIndex) key, LiveLoot loot, LootStatus status)
    {
        if (loot.Record.Status == LootStatus.Pending)
        {
            loot.Record = loot.Record with { Status = status };
            if (!loot.Group.Ambiguous)
                loot.Record = CompleteLootSettlement(loot.Record);
            storage?.RecordLoot(characterID, loot.Record);
        }
        liveLoot.Remove(key);
        if (lootGroups.TryGetValue(loot.Record.ItemID, out var group) && ReferenceEquals(group, loot.Group) &&
            !liveLoot.Values.Any(entry => ReferenceEquals(entry.Group, group)))
            lootGroups.Remove(loot.Record.ItemID);
    }

    private static LootRecord CompleteLootSettlement(LootRecord record)
    {
        if (record.Participants.Length != 2 || record.Unassigned.Length != 0 ||
            !record.Participants.Any(player => player.Choice == LootChoice.Selected && player.Value is null))
            return record;

        var allPassed = record.Status == LootStatus.Ended && record.Winner is null &&
                        record.Participants.Any(player => player.Choice == LootChoice.Pass) &&
                        record.Participants.All(player => player.Value is null &&
                                                          player.Choice is LootChoice.Selected or LootChoice.Pass);
        var greedAwarded = record.Status == LootStatus.Awarded &&
                           record.Winner is { Choice: LootChoice.Greed, Value: >= 1 and <= 99 } winner &&
                           record.Participants.Any(player => player.EntityID == winner.EntityID &&
                                                             player.Choice == LootChoice.Greed);
        if (!allPassed && !greedAwarded)
            return record;

        return record with
        {
            Participants = record.Participants.Select(player => player.Choice == LootChoice.Selected && player.Value is null
                ? player with { Choice = LootChoice.Pass } : player).ToArray()
        };
    }

    private void FinishLootTracking()
    {
        foreach (var pair in liveLoot.ToArray())
            CloseLoot(pair.Key, pair.Value, LootStatus.Interrupted);
        lootGroups.Clear();
        untrackedLoot.Clear();
        lootNotices.Clear();
    }

    private sealed class LiveLoot(LootRecord record, LootGroup group)
    {
        public LootRecord Record = record;
        public LootGroup Group { get; } = group;
    }

    private sealed class LootGroup
    {
        public List<LiveLoot> Members { get; } = [];
        public bool Ambiguous;
    }
}
