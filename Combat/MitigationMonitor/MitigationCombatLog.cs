using System.Globalization;
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using LuminaTerritoryType = Lumina.Excel.Sheets.TerritoryType;

namespace OmniToolbox.TreePublic;

internal sealed class MitigationCombatLog(int replaySaveCount)
{
    private readonly object syncRoot = new();
    private readonly List<MitigationRecord> records = new(256);
    private readonly List<MitigationCombatHistory> history = new(40);
    private readonly Dictionary<uint, MitigationRecord> lastDamageByTarget = [];
    private readonly HashSet<uint> deadTargets = [];
    private DateTime? combatStartUTC;
    private long recordsVersion;
    private long historyVersion;
    private int replaySaveCount = Math.Clamp(replaySaveCount, 1, 300);
    private bool suppressRecordsUntilNextCombat;
    private bool sawOutOfCombatAfterWipe;

    public long RecordsVersion => Interlocked.Read(ref recordsVersion);

    public long HistoryVersion => Interlocked.Read(ref historyVersion);

    public bool ShouldSuppressWrites()
    {
        lock (syncRoot)
        {
            var inCombat = DService.Instance().Condition[ConditionFlag.InCombat];
            if (suppressRecordsUntilNextCombat && !inCombat)
            {
                sawOutOfCombatAfterWipe = true;
            }

            if (IsTransitionOrCutscene())
            {
                return true;
            }

            if (!suppressRecordsUntilNextCombat)
            {
                return false;
            }

            if (!inCombat || !sawOutOfCombatAfterWipe)
            {
                return true;
            }

            suppressRecordsUntilNextCombat = false;
            sawOutOfCombatAfterWipe = false;
            return false;
        }
    }

    public void AddDamage(MitigationRecord record)
    {
        lock (syncRoot)
        {
            combatStartUTC ??= record.TimestampUTC;
            record = record with { Elapsed = record.TimestampUTC - combatStartUTC.Value };
            AddRecordNoLock(record);
            if (!record.Missed && !record.Invulnerable)
            {
                lastDamageByTarget[record.TargetEntityID] = record;
            }
        }
    }

    public void UpdateFriendly(FriendlySnapshot target, DateTime now)
    {
        lock (syncRoot)
        {
            if (target.IsAlive)
            {
                deadTargets.Remove(target.EntityID);
                return;
            }

            if (!deadTargets.Add(target.EntityID))
            {
                return;
            }

            var record = lastDamageByTarget.TryGetValue(target.EntityID, out var lastDamage)
                ? lastDamage with
                {
                    Kind = MitigationRecordKind.Defeated,
                    TimestampUTC = now,
                    Elapsed = GetElapsedNoLock(now),
                    ActionName = BuildDefeatedActionName(lastDamage)
                }
                : new(
                    MitigationRecordKind.Defeated,
                    now,
                    GetElapsedNoLock(now),
                    string.Format(
                        CultureInfo.CurrentCulture,
                        OmniLoc.Get("Feature.MitigationMonitor.Record.DefeatedGeneric"),
                        MitigationText.BuildTargetName(target.JobName, target.TargetName)),
                    string.Empty,
                    target.EntityID,
                    target.TargetName,
                    target.TargetShortName,
                    target.JobName,
                    target.JobRowID,
                    DamageSourceKind.Skill,
                    0,
                    DamageKind.Special,
                    false,
                    false,
                    false,
                    false,
                    0f,
                    [],
                    target.ShieldValue,
                    target.CurrentHp);
            AddRecordNoLock(record);
        }
    }

    public void RecordWipe(DateTime now)
    {
        lock (syncRoot)
        {
            if (records.Count == 0)
            {
                ClearCurrentNoLock();
                return;
            }

            var hasWipeRecord = false;
            foreach (var record in records)
            {
                if (record.Kind == MitigationRecordKind.Wipe)
                {
                    hasWipeRecord = true;
                    break;
                }
            }

            if (!hasWipeRecord)
            {
                AddRecordNoLock(new(
                    MitigationRecordKind.Wipe,
                    now,
                    GetElapsedNoLock(now),
                    OmniLoc.Get("Feature.MitigationMonitor.Record.Wipe"),
                    string.Empty,
                    0,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0,
                    DamageSourceKind.Skill,
                    0,
                    DamageKind.Special,
                    false,
                    false,
                    false,
                    false,
                    0f,
                    [],
                    0,
                    0));
            }

            suppressRecordsUntilNextCombat = true;
            sawOutOfCombatAfterWipe = false;
            FinalizeCurrentNoLock(now);
        }
    }

    public void SetReplaySaveCount(int value)
    {
        lock (syncRoot)
        {
            replaySaveCount = Math.Clamp(value, 1, 300);
            TrimHistoryNoLock();
        }
    }

    public long CopyRecords(string? historyKey, long knownVersion, List<MitigationRecord> destination)
    {
        lock (syncRoot)
        {
            var version = historyKey == null ? recordsVersion : historyVersion;
            if (version == knownVersion)
            {
                return version;
            }

            destination.Clear();
            if (historyKey == null)
            {
                for (var index = records.Count - 1; index >= 0; index--)
                {
                    destination.Add(records[index]);
                }

                return version;
            }

            var item = FindHistoryNoLock(historyKey);
            if (item.HasValue)
            {
                for (var index = item.Value.Records.Length - 1; index >= 0; index--)
                {
                    destination.Add(item.Value.Records[index]);
                }
            }

            return version;
        }
    }

    public long CopyHistory(long knownVersion, List<MitigationCombatHistory> destination)
    {
        lock (syncRoot)
        {
            if (historyVersion == knownVersion)
            {
                return historyVersion;
            }

            destination.Clear();
            destination.AddRange(history);
            return historyVersion;
        }
    }

    public string AddImported(MitigationCombatHistory item)
    {
        lock (syncRoot)
        {
            var key = CreateUniqueHistoryKeyNoLock(item.Key);
            history.Insert(0, item with { Key = key });
            historyVersion++;
            TrimHistoryNoLock();
            return key;
        }
    }

    public void ClearRealtime()
    {
        lock (syncRoot)
        {
            ClearCurrentNoLock();
            suppressRecordsUntilNextCombat = false;
            sawOutOfCombatAfterWipe = false;
        }
    }

    public void ClearAll()
    {
        lock (syncRoot)
        {
            ClearCurrentNoLock();
            history.Clear();
            historyVersion++;
            suppressRecordsUntilNextCombat = false;
            sawOutOfCombatAfterWipe = false;
        }
    }

    private void AddRecordNoLock(MitigationRecord record)
    {
        records.Add(record);
        recordsVersion++;
    }

    private void FinalizeCurrentNoLock(DateTime now)
    {
        if (records.Count == 0)
        {
            return;
        }

        var snapshot = records.ToArray();
        var key = now.Ticks.ToString("X", CultureInfo.InvariantCulture);
        var startUTC = combatStartUTC ?? snapshot[0].TimestampUTC;
        history.Insert(0, new(
            key,
            snapshot,
            startUTC,
            now,
            MitigationText.FormatElapsed(now - startUTC),
            GetCurrentZoneDisplayName()));
        historyVersion++;
        TrimHistoryNoLock();
        ClearCurrentNoLock();
    }

    private void TrimHistoryNoLock()
    {
        var changed = false;
        while (history.Count > replaySaveCount)
        {
            history.RemoveAt(history.Count - 1);
            changed = true;
        }

        if (changed)
        {
            historyVersion++;
        }
    }

    private void ClearCurrentNoLock()
    {
        records.Clear();
        recordsVersion++;
        lastDamageByTarget.Clear();
        deadTargets.Clear();
        combatStartUTC = null;
    }

    private TimeSpan GetElapsedNoLock(DateTime now)
    {
        combatStartUTC ??= now;
        return now - combatStartUTC.Value;
    }

    private MitigationCombatHistory? FindHistoryNoLock(string key)
    {
        foreach (var item in history)
        {
            if (item.Key == key)
            {
                return item;
            }
        }

        return null;
    }

    private string CreateUniqueHistoryKeyNoLock(string baseKey)
    {
        var key = string.IsNullOrWhiteSpace(baseKey) ? $"I{DateTime.UtcNow.Ticks:X}" : baseKey;
        if (!FindHistoryNoLock(key).HasValue)
        {
            return key;
        }

        for (var index = 2; index < 1000; index++)
        {
            if (!FindHistoryNoLock($"{key}_{index}").HasValue)
            {
                return $"{key}_{index}";
            }
        }

        return $"{key}_{Guid.NewGuid():N}";
    }

    private static string BuildDefeatedActionName(MitigationRecord record) =>
        string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get("Feature.MitigationMonitor.Record.DefeatedBy"),
            MitigationText.BuildTargetName(record.TargetJobName, record.TargetName),
            record.ActionName,
            record.Damage,
            MitigationText.GetDamageKindText(record.DamageKind));

    private static bool IsTransitionOrCutscene()
    {
        var condition = DService.Instance().Condition;
        return condition[ConditionFlag.BetweenAreas] ||
               condition[ConditionFlag.BetweenAreas51] ||
               condition[ConditionFlag.WatchingCutscene] ||
               condition[ConditionFlag.WatchingCutscene78] ||
               condition[ConditionFlag.OccupiedInCutSceneEvent];
    }

    private static string GetCurrentZoneDisplayName()
    {
        var territoryID = DService.Instance().ClientState.TerritoryType;
        if (territoryID == 0 || !LuminaGetter.TryGetRow<LuminaTerritoryType>(territoryID, out var row))
        {
            return territoryID == 0 ? string.Empty : $"#{territoryID}";
        }

        var contentName = row.ContentFinderCondition.ValueNullable?.Name.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(contentName))
        {
            return contentName;
        }

        var placeName = row.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(placeName) ? $"#{territoryID}" : placeName;
    }
}
