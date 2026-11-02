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
        public async Task<RandomRoll[]> ImportRandomAsync
        (
            ulong             characterID,
            string            content,
            CancellationToken cancellationToken = default
        )
        {
            if (characterID == 0)
                throw new ArgumentOutOfRangeException(nameof(characterID));

            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(content) || content.Length > MAX_FILE_BYTES ||
                Encoding.UTF8.GetByteCount(content) > MAX_FILE_BYTES)
                throw new InvalidDataException("Random roll export size is invalid.");

            var imported = JsonSerializer.Deserialize<RandomExport>(content, serializerOptions);
            if (imported is null || imported.Version != RANDOM_EXPORT_VERSION || imported.Type != RANDOM_EXPORT_TYPE ||
                imported.ExportedAt == default || imported.Records is not { Length: <= HISTORY_LIMIT } ||
                imported.Records.Any(roll => !IsValid(roll)))
                throw new InvalidDataException("Random roll export contains invalid entries or an unsupported format.");

            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<RandomRoll[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new RandomImport(imported.Records, cancellationToken, completion);
            if (!requests.Writer.TryWrite(new(characterID, RandomImport: request)))
                throw new ObjectDisposedException(nameof(HistoryStorage));

            return await completion.Task.ConfigureAwait(false);
        }

        public (string Content, RandomRoll[] Records) ExportRandom
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
            if (snapshot is null || snapshot.Status == HistoryStatus.ReadFailed)
                throw new InvalidOperationException("Random roll history is not available for export.");

            var records = sessionID is { } selected
                ? snapshot.Rolls.Where(roll => roll.SessionID == selected).ToArray()
                : snapshot.Rolls;
            if (records.Length is 0 or > HISTORY_LIMIT)
                throw new InvalidOperationException("No valid random roll history records are available for export.");

            var exported = new RandomExport(RANDOM_EXPORT_VERSION, RANDOM_EXPORT_TYPE, DateTimeOffset.UtcNow, records);
            var content = JsonSerializer.Serialize(exported, serializerOptions);
            if (Encoding.UTF8.GetByteCount(content) > MAX_FILE_BYTES)
                throw new InvalidDataException("Random roll export exceeds the supported size.");
            cancellationToken.ThrowIfCancellationRequested();
            return (content, records);
        }

        private async Task ApplyRandomImportAsync(ulong characterID, CharacterHistory history, RandomImport import)
        {
            try
            {
                import.CancellationToken.ThrowIfCancellationRequested();
                if (history.Status == HistoryStatus.ReadFailed)
                    throw new InvalidOperationException("Existing random roll history could not be read; import was not applied.");

                var original = history.Rolls.ToHashSet();
                var existing = new HashSet<RandomRoll>(original);
                var merged = new List<RandomRoll>(history.Rolls);
                foreach (var roll in import.Records)
                    if (existing.Add(roll))
                        merged.Add(roll);

                merged.Sort(static (left, right) => right.Time.CompareTo(left.Time));
                if (merged.Count > HISTORY_LIMIT)
                    merged.RemoveRange(HISTORY_LIMIT, merged.Count - HISTORY_LIMIT);
                var added = merged.Where(roll => !original.Contains(roll)).ToArray();
                if (added.Length == 0)
                {
                    import.Completion.TrySetResult([]);
                    return;
                }

                var path = Path.Combine(directory, $"{characterID}.json");
                var content = JsonSerializer.Serialize(merged, serializerOptions);
                if (Encoding.UTF8.GetByteCount(content) > MAX_FILE_BYTES)
                    throw new InvalidDataException("Merged random roll history exceeds the supported size.");
                await AtomicFile.WriteAllTextAsync(path, content, Encoding.UTF8, import.CancellationToken)
                                .ConfigureAwait(false);

                history.Rolls.Clear();
                history.Rolls.AddRange(merged);
                history.Dirty = false;
                history.Status = HistoryStatus.Ready;
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

        private sealed record RandomImport(RandomRoll[] Records, CancellationToken CancellationToken,
                                           TaskCompletionSource<RandomRoll[]> Completion);

        private sealed record RandomExport(int Version, string Type, DateTimeOffset ExportedAt, RandomRoll[] Records);

        #region 常量

        private const int RANDOM_EXPORT_VERSION = 2;
        private const string RANDOM_EXPORT_TYPE = "RandomRoll";

        #endregion
    }
}
