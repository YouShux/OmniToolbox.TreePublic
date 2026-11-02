using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TreeTools.IO;

namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private sealed partial class HistoryStorage
    {
        public async Task<LootRecord[]> ImportLootAsync
        (
            ulong             characterID,
            string            content,
            CancellationToken cancellationToken = default
        )
        {
            if (characterID == 0)
                throw new ArgumentOutOfRangeException(nameof(characterID));

            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(content) || content.Length > MAX_LOOT_FILE_BYTES ||
                Encoding.UTF8.GetByteCount(content) > MAX_LOOT_FILE_BYTES)
                throw new InvalidDataException("Loot export size is invalid.");

            var imported = JsonSerializer.Deserialize<LootExport>(content, serializerOptions);
            if (imported is null || imported.Version != EXPORT_VERSION || imported.ExportedAt == default ||
                imported.Records is not { Length: <= MAX_LOOT_RECORDS } ||
                imported.Records.Any(entry => !IsValidImport(entry)) ||
                imported.Records.Select(entry => entry.ID).Distinct().Count() != imported.Records.Length)
                throw new InvalidDataException("Loot export contains invalid entries or an unsupported version.");

            if (imported.Records.Select(entry => entry.SessionID).Distinct().Count() > MAX_LOOT_SESSIONS)
                throw new InvalidDataException("Loot export contains too many sessions.");

            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<LootRecord[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new LootImport(imported.Records, cancellationToken, completion);
            if (!requests.Writer.TryWrite(new(characterID, Import: request)))
                throw new ObjectDisposedException(nameof(HistoryStorage));

            return await completion.Task.ConfigureAwait(false);
        }

        public (string Content, LootRecord[] Records) ExportLoot
        (
            ulong             characterID,
            Guid?             sessionID,
            CancellationToken cancellationToken = default
        )
        {
            if (characterID == 0)
                throw new ArgumentOutOfRangeException(nameof(characterID));

            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = GetSnapshot(characterID);
            if (snapshot is null || snapshot.LootStatus == HistoryStatus.ReadFailed)
                throw new InvalidOperationException("Loot history is not available for export.");

            var records = sessionID is { } selected
                ? snapshot.Loot.Where(entry => entry.SessionID == selected).ToArray()
                : snapshot.Loot;
            if (records.Length == 0)
                throw new InvalidOperationException("No loot history records are available for export.");
            if (records.Length > MAX_LOOT_RECORDS)
                throw new InvalidDataException("Loot export exceeds the supported record count.");
            var exported = new LootExport(EXPORT_VERSION, DateTimeOffset.UtcNow, records);
            var content = JsonSerializer.Serialize(exported, serializerOptions);
            if (Encoding.UTF8.GetByteCount(content) > MAX_LOOT_FILE_BYTES)
                throw new InvalidDataException("Loot export exceeds the supported size.");
            cancellationToken.ThrowIfCancellationRequested();
            return (content, records);
        }

        private async Task ApplyImportAsync(ulong characterID, CharacterHistory history, LootImport import)
        {
            try
            {
                import.CancellationToken.ThrowIfCancellationRequested();
                if (history.LootStatus == HistoryStatus.ReadFailed)
                    throw new InvalidOperationException("Existing loot history could not be read; import was not applied.");

                var originalIDs = history.Loot.Select(entry => entry.ID).ToHashSet();
                var existingIDs = new HashSet<Guid>(originalIDs);
                var merged = new List<LootRecord>(history.Loot);
                foreach (var entry in import.Records)
                {
                    if (existingIDs.Add(entry.ID))
                        merged.Add(entry.Status == LootStatus.Pending
                            ? entry with { Status = LootStatus.Interrupted }
                            : CompleteLootSettlement(entry));
                }
                merged.Sort(static (left, right) => right.Time.CompareTo(left.Time));
                TrimLoot(merged);

                var added = merged.Where(entry => !originalIDs.Contains(entry.ID)).ToArray();
                if (added.Length == 0)
                {
                    import.Completion.TrySetResult([]);
                    return;
                }

                var path = Path.Combine(directory, $"{characterID}.loot.json");
                var content = JsonSerializer.Serialize(merged, serializerOptions);
                if (merged.Count > MAX_LOOT_RECORDS || Encoding.UTF8.GetByteCount(content) > MAX_LOOT_FILE_BYTES)
                    throw new InvalidDataException("Merged loot history exceeds the supported file size or record count.");
                await AtomicFile.WriteAllTextAsync(path, content, Encoding.UTF8, import.CancellationToken)
                                .ConfigureAwait(false);

                history.Loot.Clear();
                history.Loot.AddRange(merged);
                history.LootDirty = false;
                history.LootStatus = HistoryStatus.Ready;
                snapshots[characterID] = new(history.Rolls.ToArray(), history.Loot.ToArray(),
                                             history.Status, history.LootStatus);
                import.Completion.TrySetResult(added);
            }
            catch (OperationCanceledException)
            {
                import.Completion.TrySetCanceled(import.CancellationToken);
            }
            catch (Exception exception)
            {
                import.Completion.TrySetException(exception);
            }
        }

        private static bool IsValidImport(LootRecord? record) =>
            IsValid(record) && record is not null &&
            (record.Status == LootStatus.Awarded) == (record.Winner is not null) &&
            record.Participants.Select(player => player.EntityID).Distinct().Count() == record.Participants.Length &&
            record.Participants.All(IsValidImportPlayer) &&
            (record.Winner is null || IsValidImportPlayer(record.Winner)) &&
            record.Unassigned.All(entry => IsValidImportPlayer(entry.Player));

        private static bool IsValidImportPlayer(LootParticipant player) =>
            player.EntityID != 0 &&
            (player.Value is null || player.Choice is LootChoice.Need or LootChoice.Greed);

        private sealed record LootImport(LootRecord[] Records, CancellationToken CancellationToken,
                                         TaskCompletionSource<LootRecord[]> Completion);

        private sealed record LootExport(int Version, DateTimeOffset ExportedAt, LootRecord[] Records);

        #region 常量

        private const int EXPORT_VERSION = 2;

        #endregion
    }
}
