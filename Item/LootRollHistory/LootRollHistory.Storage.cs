using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;
using TreeTools.IO;

namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private sealed partial class HistoryStorage : IAsyncDisposable
    {
        private readonly string directory;
        private readonly Channel<HistoryRequest> requests =
            Channel.CreateUnbounded<HistoryRequest>(new()
            {
                SingleReader = true,
                AllowSynchronousContinuations = false
            });
        private readonly ConcurrentDictionary<ulong, HistorySnapshot> snapshots = new();
        private readonly Dictionary<ulong, CharacterHistory> histories = [];
        private readonly TaskCompletionSource initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly JsonSerializerOptions serializerOptions = new()
        {
            RespectRequiredConstructorParameters = true
        };
        private int lootLimit;

        public HistoryStorage(string directory, int lootLimit)
        {
            if (lootLimit is < 1 or > MAX_LOOT_SESSIONS)
                throw new ArgumentOutOfRangeException(nameof(lootLimit));

            this.directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            this.lootLimit = lootLimit;
            Completion = Task.Run(ProcessAsync);
        }

        public Task Initialization => initialization.Task;

        public Task Completion { get; }

        public void Load(ulong characterID)
        {
            if (characterID != 0)
                requests.Writer.TryWrite(new(characterID));
        }

        public void Record(ulong characterID, RandomRoll roll)
        {
            if (characterID == 0 || !IsValid(roll))
                throw new ArgumentException("Invalid random roll history entry.", nameof(roll));

            requests.Writer.TryWrite(new(characterID, Roll: roll));
        }

        public void RecordLoot(ulong characterID, LootRecord loot)
        {
            if (characterID == 0 || !IsValid(loot))
                throw new ArgumentException("Invalid loot history entry.", nameof(loot));

            var copy = loot with { Participants = [.. loot.Participants], Unassigned = [.. loot.Unassigned] };
            requests.Writer.TryWrite(new(characterID, Loot: copy));
        }

        public void SetLootLimit(int limit)
        {
            if (limit is < 1 or > MAX_LOOT_SESSIONS)
                throw new ArgumentOutOfRangeException(nameof(limit));

            requests.Writer.TryWrite(new(0, LootLimit: limit));
        }

        public void ClearLoot(ulong characterID)
        {
            if (characterID == 0)
                throw new ArgumentOutOfRangeException(nameof(characterID));

            requests.Writer.TryWrite(new(characterID, IsClear: true));
        }

        public HistorySnapshot? GetSnapshot(ulong characterID) =>
            snapshots.TryGetValue(characterID, out var snapshot) ? snapshot : null;

        public void Stop() => requests.Writer.TryComplete();

        public async ValueTask DisposeAsync()
        {
            Stop();
            await Completion.ConfigureAwait(false);
        }

        private async Task ProcessAsync()
        {
            try
            {
                await InitializeFormatAsync().ConfigureAwait(false);
                initialization.TrySetResult();
            }
            catch (Exception exception)
            {
                initialization.TrySetException(exception);
                requests.Writer.TryComplete(exception);
                while (requests.Reader.TryRead(out var request))
                {
                    request.Import?.Completion.TrySetException(exception);
                    request.RandomImport?.Completion.TrySetException(exception);
                }
                throw;
            }

            while (await requests.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                await Task.Delay(250).ConfigureAwait(false);
                var changedCharacters = new HashSet<ulong>();
                while (requests.Reader.TryRead(out var request))
                {
                    if (request.LootLimit is { } limit)
                    {
                        lootLimit = limit;
                        foreach (var (loadedID, loadedHistory) in histories)
                        {
                            if (!TrimLoot(loadedHistory.Loot))
                                continue;
                            loadedHistory.LootDirty = loadedHistory.LootStatus != HistoryStatus.ReadFailed;
                            changedCharacters.Add(loadedID);
                        }
                        continue;
                    }

                    if (!histories.TryGetValue(request.CharacterID, out var history))
                    {
                        history = await ReadAsync(request.CharacterID).ConfigureAwait(false);
                        histories.Add(request.CharacterID, history);
                        changedCharacters.Add(request.CharacterID);
                    }

                    if (request.Roll is { } roll)
                    {
                        history.Rolls.Insert(0, roll);
                        if (history.Rolls.Count > HISTORY_LIMIT)
                            history.Rolls.RemoveRange(HISTORY_LIMIT, history.Rolls.Count - HISTORY_LIMIT);
                        history.Dirty = history.Status != HistoryStatus.ReadFailed;
                        changedCharacters.Add(request.CharacterID);
                    }

                    if (request.Loot is { } loot)
                    {
                        var index = history.Loot.FindIndex(entry => entry.ID == loot.ID);
                        if (index >= 0)
                            history.Loot[index] = loot;
                        else
                        {
                            var position = history.Loot.FindIndex(entry => entry.Time < loot.Time);
                            if (position < 0)
                                position = history.Loot.Count;
                            history.Loot.Insert(position, loot);
                        }
                        TrimLoot(history.Loot);
                        history.LootDirty = history.LootStatus != HistoryStatus.ReadFailed;
                        changedCharacters.Add(request.CharacterID);
                    }

                    if (request.Import is { } import)
                        await ApplyImportAsync(request.CharacterID, history, import).ConfigureAwait(false);

                    if (request.RandomImport is { } randomImport)
                        await ApplyRandomImportAsync(request.CharacterID, history, randomImport).ConfigureAwait(false);

                    if (request.IsClear && history.LootStatus != HistoryStatus.ReadFailed)
                    {
                        history.Loot.Clear();
                        history.LootDirty = true;
                        changedCharacters.Add(request.CharacterID);
                    }
                }

                foreach (var characterID in changedCharacters)
                {
                    var history = histories[characterID];
                    snapshots[characterID] = new(history.Rolls.ToArray(), history.Loot.ToArray(),
                                                 history.Status, history.LootStatus);
                }

                await SaveDirtyAsync().ConfigureAwait(false);
            }

            await SaveDirtyAsync().ConfigureAwait(false);
        }

        private async Task InitializeFormatAsync()
        {
            if (!string.Equals(Path.GetFileName(directory), nameof(LootRollHistory), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected roll history directory.");

            var root = new DirectoryInfo(directory);
            if (root.Exists && (root.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Roll history directory is a reparse point.");

            var formatPath = Path.Combine(directory, ".format-version");
            if (File.Exists(formatPath))
            {
                if ((File.GetAttributes(formatPath) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Roll history format marker is a reparse point.");
                var version = await File.ReadAllTextAsync(formatPath).ConfigureAwait(false);
                if (version.Trim() != STORAGE_FORMAT_VERSION)
                    throw new InvalidDataException("Unsupported roll history storage format.");
                return;
            }

            if (root.Exists)
            {
                var pending = new Stack<DirectoryInfo>();
                pending.Push(root);
                while (pending.TryPop(out var current))
                {
                    foreach (var entry in current.EnumerateFileSystemInfos())
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                            throw new InvalidDataException("Roll history directory contains a reparse point.");
                        if (entry is DirectoryInfo child)
                            pending.Push(child);
                    }
                }
                FileSystem.DeleteDirectory(directory, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin,
                                           UICancelOption.ThrowException);
            }

            Directory.CreateDirectory(directory);
            await AtomicFile.WriteAllTextAsync(formatPath, STORAGE_FORMAT_VERSION, Encoding.UTF8).ConfigureAwait(false);
        }

        private async Task<CharacterHistory> ReadAsync(ulong characterID)
        {
            var history = new CharacterHistory();
            var path = Path.Combine(directory, $"{characterID}.json");
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                       4096, useAsync: true);
                if (stream.Length is <= 0 or > MAX_FILE_BYTES)
                    throw new InvalidDataException("Random roll history file size is invalid.");

                var bytes = new byte[(int)stream.Length];
                await stream.ReadExactlyAsync(bytes).ConfigureAwait(false);
                var rolls = JsonSerializer.Deserialize<RandomRoll[]>(bytes, serializerOptions);
                if (rolls is null || rolls.Length > HISTORY_LIMIT || rolls.Any(roll => !IsValid(roll)))
                    throw new InvalidDataException("Random roll history contains invalid entries.");

                history.Rolls.AddRange(rolls);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception exception)
            {
                history.Status = HistoryStatus.ReadFailed;
                history.Dirty = false;
                DalamudServices.PluginLog.Error(exception, "Failed to read random roll history: {Path}", path);
            }

            await ReadLootAsync(characterID, history).ConfigureAwait(false);
            return history;
        }

        private async Task ReadLootAsync(ulong characterID, CharacterHistory history)
        {
            var path = Path.Combine(directory, $"{characterID}.loot.json");
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                       4096, useAsync: true);
                if (stream.Length is <= 0 or > MAX_LOOT_FILE_BYTES)
                    throw new InvalidDataException("Loot history file size is invalid.");

                var bytes = new byte[(int)stream.Length];
                await stream.ReadExactlyAsync(bytes).ConfigureAwait(false);
                var loot = JsonSerializer.Deserialize<LootRecord[]>(bytes, serializerOptions);
                if (loot is null || loot.Length > MAX_LOOT_RECORDS || loot.Any(entry => !IsValid(entry)) ||
                    loot.Select(entry => entry.ID).Distinct().Count() != loot.Length)
                    throw new InvalidDataException("Loot history contains invalid entries.");

                var restoredRecords = new List<LootRecord>(loot.Length);
                foreach (var entry in loot)
                {
                    var restored = entry.Status == LootStatus.Pending
                        ? entry with { Status = LootStatus.Interrupted }
                        : CompleteLootSettlement(entry);
                    restoredRecords.Add(restored);
                    if (!ReferenceEquals(entry, restored))
                        history.LootDirty = true;
                }
                if (restoredRecords.Select(entry => entry.SessionID).Distinct().Count() > MAX_LOOT_SESSIONS)
                    throw new InvalidDataException("Loot history contains too many sessions.");

                history.Loot.AddRange(restoredRecords);
                history.Loot.Sort(static (left, right) => right.Time.CompareTo(left.Time));
                if (TrimLoot(history.Loot))
                    history.LootDirty = true;
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception exception)
            {
                history.LootStatus = HistoryStatus.ReadFailed;
                history.LootDirty = false;
                DalamudServices.PluginLog.Error(exception, "Failed to read loot history: {Path}", path);
            }
        }

        private async Task SaveDirtyAsync()
        {
            foreach (var (characterID, history) in histories)
            {
                if (!history.Dirty && !history.LootDirty)
                    continue;

                if (history.Dirty)
                {
                    var path = Path.Combine(directory, $"{characterID}.json");
                    try
                    {
                        var content = JsonSerializer.Serialize(history.Rolls, serializerOptions);
                        await AtomicFile.WriteAllTextAsync(path, content, Encoding.UTF8).ConfigureAwait(false);
                        history.Dirty = false;
                        history.Status = HistoryStatus.Ready;
                    }
                    catch (Exception exception)
                    {
                        if (history.Status != HistoryStatus.SaveFailed)
                            DalamudServices.PluginLog.Error(exception, "Failed to save random roll history: {Path}", path);
                        history.Status = HistoryStatus.SaveFailed;
                    }
                }

                if (history.LootDirty)
                {
                    var path = Path.Combine(directory, $"{characterID}.loot.json");
                    try
                    {
                        var content = JsonSerializer.Serialize(history.Loot, serializerOptions);
                        if (history.Loot.Count > MAX_LOOT_RECORDS || Encoding.UTF8.GetByteCount(content) > MAX_LOOT_FILE_BYTES)
                            throw new InvalidDataException("Loot history exceeds the supported file size or record count.");
                        await AtomicFile.WriteAllTextAsync(path, content, Encoding.UTF8).ConfigureAwait(false);
                        history.LootDirty = false;
                        history.LootStatus = HistoryStatus.Ready;
                    }
                    catch (Exception exception)
                    {
                        if (history.LootStatus != HistoryStatus.SaveFailed)
                            DalamudServices.PluginLog.Error(exception, "Failed to save loot history: {Path}", path);
                        history.LootStatus = HistoryStatus.SaveFailed;
                    }
                }

                snapshots[characterID] = new(history.Rolls.ToArray(), history.Loot.ToArray(),
                                             history.Status, history.LootStatus);
            }
        }

        private static bool IsValid(RandomRoll? roll) =>
            roll is not null && roll.SessionID != Guid.Empty && roll.Time != default &&
            !string.IsNullOrWhiteSpace(roll.PlayerName) && roll.PlayerName.Length <= MAX_PLAYER_NAME_LENGTH &&
            roll.HomeWorldID <= ushort.MaxValue && roll.TerritoryID <= ushort.MaxValue && roll.DutyID <= ushort.MaxValue &&
            roll.Maximum is >= 1 and <= DEFAULT_RANDOM_MAXIMUM && roll.Value >= 0 && roll.Value <= roll.Maximum;

        private static bool IsValid(LootRecord? loot) =>
            loot is not null && loot.ID != Guid.Empty && loot.SessionID != Guid.Empty && loot.Time != default &&
            loot.ChestID is not (0 or 0xE0000000) && loot.ChestItemIndex <= byte.MaxValue &&
            loot.ItemID > 0 && loot.Count > 0 && Enum.IsDefined(loot.Status) &&
            loot.TerritoryID <= ushort.MaxValue && loot.DutyID <= ushort.MaxValue &&
            loot.Participants is { Length: <= MAX_LOOT_PARTICIPANTS } &&
            loot.Participants.All(player => IsValid(player)) &&
            (loot.Winner is null || IsValid(loot.Winner)) &&
            loot.Unassigned is { Length: <= MAX_UNASSIGNED_EVENTS } &&
            loot.Unassigned.All(entry => entry is not null && entry.Time != default && IsValid(entry.Player));

        private static bool IsValid(LootParticipant? player) =>
            player is not null && player.EntityID != 0xE0000000 &&
            !string.IsNullOrWhiteSpace(player.PlayerName) && player.PlayerName.Length <= MAX_PLAYER_NAME_LENGTH &&
            player.HomeWorldID <= ushort.MaxValue && Enum.IsDefined(player.Choice) &&
            (player.Value is null or >= 1 and <= 99);

        private bool TrimLoot(List<LootRecord> loot)
        {
            var sessions = loot.GroupBy(entry => entry.SessionID)
                .Select(group => new
                {
                    ID = group.Key,
                    Time = group.Max(entry => entry.Time),
                    Pending = group.Any(entry => entry.Status == LootStatus.Pending)
                })
                .OrderByDescending(session => session.Time)
                .ToArray();
            var retained = sessions.Take(lootLimit).Select(session => session.ID).ToHashSet();
            foreach (var session in sessions)
            {
                if (session.Pending)
                    retained.Add(session.ID);
            }
            for (var index = sessions.Length - 1; index >= 0 && retained.Count > MAX_LOOT_SESSIONS; index--)
            {
                if (!sessions[index].Pending)
                    retained.Remove(sessions[index].ID);
            }
            return loot.RemoveAll(entry => !retained.Contains(entry.SessionID)) > 0;
        }

        private sealed record HistoryRequest(ulong CharacterID, RandomRoll? Roll = null, LootRecord? Loot = null,
                                            LootImport? Import = null, int? LootLimit = null, bool IsClear = false,
                                            RandomImport? RandomImport = null);

        private sealed class CharacterHistory
        {
            public List<RandomRoll> Rolls { get; } = [];
            public List<LootRecord> Loot { get; } = [];
            public HistoryStatus Status;
            public HistoryStatus LootStatus;
            public bool Dirty;
            public bool LootDirty;
        }

        #region 常量

        private const int MAX_FILE_BYTES = 1024 * 1024;
        private const int MAX_LOOT_FILE_BYTES = 32 * 1024 * 1024;
        private const int MAX_LOOT_RECORDS = 20000;
        private const string STORAGE_FORMAT_VERSION = "2";

        #endregion
    }

    private sealed record HistorySnapshot(RandomRoll[] Rolls, LootRecord[] Loot,
                                         HistoryStatus Status, HistoryStatus LootStatus);

    private enum HistoryStatus
    {
        Ready,
        ReadFailed,
        SaveFailed
    }
}
