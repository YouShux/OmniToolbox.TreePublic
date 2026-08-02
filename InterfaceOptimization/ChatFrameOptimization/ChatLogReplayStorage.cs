using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OmniToolbox.Config;
using OmniToolbox.Host;

namespace OmniToolbox.TreePublic;

internal sealed class ChatLogReplayStorage : IDisposable
{
    public const int MinLogFileSizeKb = 200;
    public const int MaxLogFileSizeKb = 2048;
    public const int MinLogFileCount = 1;
    public const int MaxLogFileCount = 200;

    private const string LogExtension = ".omnilog";

    private static readonly JsonSerializerOptions JSONOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ChatFrameOptimizationConfig config;
    private readonly Channel<StorageWorkItem> workItems = Channel.CreateUnbounded<StorageWorkItem>(new()
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly ConcurrentQueue<ChatLogReplayStorageResult> results = new();
    private readonly Task worker;
    private string? currentLogPath;
    private int disposed;

    public ChatLogReplayStorage(ChatFrameOptimizationConfig config)
    {
        this.config = config;
        DirectoryPath = GetDirectoryPath();
        worker = ProcessQueueAsync();
    }

    public string DirectoryPath { get; }

    public static string GetDirectoryPath() =>
        Path.Combine(DalamudServices.PluginInterface.GetPluginConfigDirectory(), "ChatLogs");

    public static int NormalizeFileSize(int value) =>
        Math.Clamp(value, MinLogFileSizeKb, MaxLogFileSizeKb);

    public static int NormalizeFileCount(int value) =>
        Math.Clamp(value, MinLogFileCount, MaxLogFileCount);

    public void QueueLog(ChatFrameOptimizationLogEntry entry) =>
        Queue(new WriteLogWorkItem(entry));

    public void RequestRefresh(int requestID) =>
        Queue(new RefreshFilesWorkItem(requestID));

    public void RequestRead(int requestID, string path) =>
        Queue(new ReadFileWorkItem(requestID, path));

    public void RequestPrune() => Queue(PruneLogsWorkItem.Instance);

    public bool TryDequeueResult(out ChatLogReplayStorageResult? result) =>
        results.TryDequeue(out result);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        workItems.Writer.TryComplete();
        try
        {
            worker.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Chat log storage worker failed during shutdown.");
        }
    }

    private void Queue(StorageWorkItem item)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            workItems.Writer.TryWrite(item);
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var item in workItems.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                switch (item)
                {
                    case WriteLogWorkItem write:
                        WriteLog(write.Entry);
                        break;
                    case RefreshFilesWorkItem refresh:
                        RefreshFiles(refresh.RequestID);
                        break;
                    case ReadFileWorkItem read:
                        ReadFile(read.RequestID, read.Path);
                        break;
                    case PruneLogsWorkItem:
                        PruneOldLogs();
                        break;
                }
            }
            catch (Exception ex)
            {
                DalamudServices.PluginLog.Warning(ex, "Chat log storage operation failed: {Operation}", item.GetType().Name);
                switch (item)
                {
                    case RefreshFilesWorkItem refresh:
                        results.Enqueue(new ChatLogReplayFileListResult(refresh.RequestID, false, []));
                        break;
                    case ReadFileWorkItem read:
                        results.Enqueue(new ChatLogReplayReadResult(read.RequestID, false, [], 0));
                        break;
                }
            }
        }
    }

    private void WriteLog(ChatFrameOptimizationLogEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JSONOptions);
        var path = GetWritableLogPath(
            Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine),
            out var created);
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        if (created)
        {
            PruneOldLogs();
        }
    }

    private string GetWritableLogPath(int nextBytes, out bool created)
    {
        System.IO.Directory.CreateDirectory(DirectoryPath);
        if (!string.IsNullOrWhiteSpace(currentLogPath) &&
            File.Exists(currentLogPath) &&
            new FileInfo(currentLogPath).Length + nextBytes <= NormalizeFileSize(config.LogMaxFileSizeKb) * 1024L)
        {
            created = false;
            return currentLogPath;
        }

        currentLogPath = Path.Combine(DirectoryPath, $"chat_{DateTime.Now:yyyyMMdd_HHmmss_fff}{LogExtension}");
        created = true;
        return currentLogPath;
    }

    private void PruneOldLogs()
    {
        if (!System.IO.Directory.Exists(DirectoryPath))
        {
            return;
        }

        var files = System.IO.Directory.GetFiles(DirectoryPath, $"*{LogExtension}")
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.CreationTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        var removeCount = files.Length - NormalizeFileCount(config.LogMaxFileCount);
        for (var index = 0; index < removeCount; index++)
        {
            try
            {
                files[index].Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DalamudServices.PluginLog.Warning(ex, "Failed to prune chat log file: {Path}", files[index].FullName);
            }
        }
    }

    private void RefreshFiles(int requestID)
    {
        if (!System.IO.Directory.Exists(DirectoryPath))
        {
            results.Enqueue(new ChatLogReplayFileListResult(requestID, true, []));
            return;
        }

        results.Enqueue(new ChatLogReplayFileListResult(
            requestID,
            true,
            System.IO.Directory.GetFiles(DirectoryPath, $"*{LogExtension}")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .Select(file => new ChatLogReplayFile(
                    file.FullName,
                    file.Name,
                    file.LastWriteTime,
                    file.Length))
                .ToArray()));
    }

    private void ReadFile(int requestID, string path)
    {
        var entries = new List<ChatLogReplayEntry>();
        var skipped = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseEntry(line, out var entry))
            {
                entries.Add(entry);
            }
            else
            {
                skipped++;
            }
        }

        results.Enqueue(new ChatLogReplayReadResult(requestID, true, entries, skipped));
    }

    private static bool TryParseEntry(string line, out ChatLogReplayEntry entry)
    {
        entry = null!;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!string.Equals(
                    GetString(root, "Version"),
                    ChatFrameOptimizationLogFormatter.Version,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var channelName = GetString(root, "ChatTypeName");
            var senderDisplayName = GetString(root, "SenderDisplayName");
            var senderText = GetString(root, "SenderText");
            var senderJobName = GetString(root, "SenderJobName");
            var messageText = GetString(root, "MessageText");
            var nativeText = GetString(root, "NativeFormattedText");
            var timeText = FormatTime(
                GetString(root, "TimestampLocal"),
                GetString(root, "TimestampUtc"));
            entry = new(
                timeText,
                string.IsNullOrWhiteSpace(channelName) ? "Other" : channelName,
                senderDisplayName,
                senderText,
                senderJobName,
                GetUInt(root, "SenderJobIconId"),
                messageText,
                nativeText,
                GetString(root, "NativeFormattedPayloadBase64"),
                $"{timeText} {channelName} {senderDisplayName} {senderText} {senderJobName} {messageText} {nativeText}");
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty,
        };
    }

    private static uint GetUInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               uint.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    private static string FormatTime(string local, string utc)
    {
        if (DateTimeOffset.TryParse(local, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
        {
            return localTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var utcTime)
            ? utcTime.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
            : "--:--";
    }

    private abstract record StorageWorkItem;

    private sealed record WriteLogWorkItem(ChatFrameOptimizationLogEntry Entry) : StorageWorkItem;

    private sealed record RefreshFilesWorkItem(int RequestID) : StorageWorkItem;

    private sealed record ReadFileWorkItem(int RequestID, string Path) : StorageWorkItem;

    private sealed record PruneLogsWorkItem : StorageWorkItem
    {
        public static PruneLogsWorkItem Instance { get; } = new();
    }
}
