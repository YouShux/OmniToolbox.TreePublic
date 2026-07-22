using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.General;

internal sealed class MitigationReplayStore
{
    private const int ExportVersion = 1;
    private const int MaxReplayRecords = 100_000;
    private const int MaxStatusesPerRecord = 64;
    private const int MaxTextLength = 4096;
    private const string ExportDirectoryName = "MitigationExports";
    private const string ExportExtension = ".omni-mitigation.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Export(MitigationCombatHistory history)
    {
        try
        {
            if (history.Records.Length == 0)
            {
                return;
            }

            Directory.CreateDirectory(ExportDirectory);
            var path = EnsureUniquePath(Path.Combine(ExportDirectory, BuildFileName(history)));
            File.WriteAllText(path, JsonSerializer.Serialize(ToDto(history), JsonOptions), Encoding.UTF8);
            DalamudServices.PluginLog.Information("Mitigation replay exported to {Path}.", path);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Mitigation replay export failed.");
        }
    }

    public MitigationCombatHistory? Import(string path)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<ReplayDto>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            if (TryCreateHistory(dto, out var history, out var error))
            {
                return history;
            }

            DalamudServices.PluginLog.Warning("Mitigation replay import rejected: {Reason}", error);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Mitigation replay import failed.");
        }

        return null;
    }

    public string[] GetImportableFiles()
    {
        try
        {
            if (!Directory.Exists(ExportDirectory))
            {
                return [];
            }

            var files = Directory.GetFiles(ExportDirectory, $"*{ExportExtension}");
            Array.Sort(files, static (left, right) =>
                File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left)));
            return files;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Mitigation replay file enumeration failed.");
            return [];
        }
    }

    public string ExportDirectory =>
        Path.Combine(DService.Instance().PI.GetPluginConfigDirectory(), ExportDirectoryName);

    private static ReplayDto ToDto(MitigationCombatHistory history)
    {
        var dto = new ReplayDto
        {
            Version = ExportVersion,
            ExportedUtc = DateTime.UtcNow,
            ZoneName = history.ZoneName,
            StartUtc = history.StartUtc,
            EndUtc = history.EndUtc,
            ElapsedLabel = history.ElapsedLabel
        };
        foreach (var record in history.Records)
        {
            var recordDto = new RecordDto
            {
                Kind = record.Kind,
                TimestampUtc = record.TimestampUtc,
                Elapsed = record.Elapsed,
                ActionName = record.ActionName,
                SourceName = record.SourceName,
                TargetName = record.TargetName,
                TargetShortName = record.TargetShortName,
                TargetJobName = record.TargetJobName,
                TargetJobRowId = record.TargetJobRowId,
                SourceKind = record.SourceKind,
                Damage = record.Damage,
                DamageKind = record.DamageKind,
                Blocked = record.Blocked,
                Parried = record.Parried,
                Missed = record.Missed,
                Invulnerable = record.Invulnerable,
                MitigationPercent = record.MitigationPercent,
                ShieldValue = record.ShieldValue,
                CurrentHp = record.CurrentHp
            };
            foreach (var status in record.Statuses)
            {
                recordDto.Statuses.Add(new()
                {
                    StatusId = status.StatusId,
                    Name = status.Name,
                    IconId = status.IconId,
                    RemainingSeconds = status.RemainingSeconds,
                    Value = status.Value,
                    StackCount = status.StackCount,
                    Category = status.Category,
                    Useful = status.Useful,
                    AffectsPercent = status.AffectsPercent
                });
            }

            dto.Records.Add(recordDto);
        }

        return dto;
    }

    private static bool TryCreateHistory(ReplayDto? dto, out MitigationCombatHistory history, out string error)
    {
        history = default;
        error = string.Empty;
        if (dto == null || dto.Version != ExportVersion)
        {
            error = "Unsupported or empty replay file.";
            return false;
        }

        if (dto.Records is not { Count: > 0 } || dto.Records.Count > MaxReplayRecords)
        {
            error = "Replay record count is invalid.";
            return false;
        }

        var records = new MitigationRecord[dto.Records.Count];
        for (var index = 0; index < records.Length; index++)
        {
            if (!TryCreateRecord(dto.Records[index], out records[index]))
            {
                error = $"Replay record {index + 1} is invalid.";
                return false;
            }
        }

        Array.Sort(records, static (left, right) => left.TimestampUtc.CompareTo(right.TimestampUtc));
        var startUtc = dto.StartUtc == default ? records[0].TimestampUtc : dto.StartUtc;
        var endUtc = dto.EndUtc == default ? records[^1].TimestampUtc : dto.EndUtc;
        if (startUtc == default || endUtc == default || endUtc < startUtc)
        {
            error = "Replay timestamps are invalid.";
            return false;
        }

        history = new(
            $"I{startUtc.Ticks:X}_{DateTime.UtcNow.Ticks:X}",
            records,
            startUtc,
            endUtc,
            string.IsNullOrWhiteSpace(dto.ElapsedLabel)
                ? MitigationText.FormatElapsed(endUtc - startUtc)
                : dto.ElapsedLabel,
            string.IsNullOrWhiteSpace(dto.ZoneName)
                ? OmniLoc.Get("Feature.MitigationMonitor.History.Imported")
                : dto.ZoneName);
        return true;
    }

    private static bool TryCreateRecord(RecordDto? dto, out MitigationRecord record)
    {
        record = default;
        if (dto == null ||
            !Enum.IsDefined(dto.Kind) ||
            !Enum.IsDefined(dto.SourceKind) ||
            !Enum.IsDefined(dto.DamageKind) ||
            dto.TimestampUtc == default ||
            !float.IsFinite(dto.MitigationPercent) ||
            dto.Statuses == null ||
            dto.Statuses.Count > MaxStatusesPerRecord ||
            !HasValidText(dto.ActionName) ||
            !HasValidText(dto.SourceName) ||
            !HasValidText(dto.TargetName) ||
            !HasValidText(dto.TargetShortName) ||
            !HasValidText(dto.TargetJobName))
        {
            return false;
        }

        var statuses = new ActiveMitigation[dto.Statuses.Count];
        for (var index = 0; index < statuses.Length; index++)
        {
            var status = dto.Statuses[index];
            if (status == null ||
                !Enum.IsDefined(status.Category) ||
                !float.IsFinite(status.RemainingSeconds) ||
                !HasValidText(status.Name))
            {
                return false;
            }

            statuses[index] = new(
                status.StatusId,
                status.Name,
                status.IconId,
                MathF.Max(0f, status.RemainingSeconds),
                status.Value,
                Math.Max(1, status.StackCount),
                status.Category,
                status.Useful,
                status.AffectsPercent);
        }

        record = new(
            dto.Kind,
            dto.TimestampUtc,
            dto.Elapsed,
            dto.ActionName,
            dto.SourceName,
            0,
            dto.TargetName,
            dto.TargetShortName,
            dto.TargetJobName,
            dto.TargetJobRowId,
            dto.SourceKind,
            dto.Damage,
            dto.DamageKind,
            dto.Blocked,
            dto.Parried,
            dto.Missed,
            dto.Invulnerable,
            dto.MitigationPercent,
            statuses,
            dto.ShieldValue,
            dto.CurrentHp);
        return true;
    }

    private static bool HasValidText(string? text) => text is { Length: <= MaxTextLength };

    private static string BuildFileName(MitigationCombatHistory history)
    {
        var timestamp = history.StartUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var zoneName = SanitizeFileName(string.IsNullOrWhiteSpace(history.ZoneName)
            ? OmniLoc.Get("Feature.MitigationMonitor.History.UnknownZone")
            : history.ZoneName);
        var elapsed = SanitizeFileName(string.IsNullOrWhiteSpace(history.ElapsedLabel) ? "00_00" : history.ElapsedLabel);
        return $"{timestamp}_{zoneName}_{elapsed}{ExportExtension}";
    }

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith(".omni-mitigation", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".omni-mitigation".Length];
        }

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}_{index}{ExportExtension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}_{DateTime.UtcNow.Ticks:X}{ExportExtension}");
    }

    private static string SanitizeFileName(string text)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result)
            ? OmniLoc.Get("Feature.MitigationMonitor.History.Unnamed")
            : result;
    }

    private sealed class ReplayDto
    {
        public int Version { get; set; } = ExportVersion;
        public DateTime ExportedUtc { get; set; }
        public string ZoneName { get; set; } = string.Empty;
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public string ElapsedLabel { get; set; } = string.Empty;
        public List<RecordDto?> Records { get; set; } = [];
    }

    private sealed class RecordDto
    {
        public MitigationRecordKind Kind { get; set; }
        public DateTime TimestampUtc { get; set; }
        public TimeSpan Elapsed { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string TargetShortName { get; set; } = string.Empty;
        public string TargetJobName { get; set; } = string.Empty;
        public uint TargetJobRowId { get; set; }
        public DamageSourceKind SourceKind { get; set; }
        public uint Damage { get; set; }
        public DamageKind DamageKind { get; set; }
        public bool Blocked { get; set; }
        public bool Parried { get; set; }
        public bool Missed { get; set; }
        public bool Invulnerable { get; set; }
        public float MitigationPercent { get; set; }
        public List<ActiveMitigationDto?> Statuses { get; set; } = [];
        public uint ShieldValue { get; set; }
        public uint CurrentHp { get; set; }
    }

    private sealed class ActiveMitigationDto
    {
        public uint StatusId { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint IconId { get; set; }
        public float RemainingSeconds { get; set; }
        public int Value { get; set; }
        public int StackCount { get; set; }
        public MitigationStatusCategory Category { get; set; }
        public bool Useful { get; set; }
        public bool AffectsPercent { get; set; }
    }
}
