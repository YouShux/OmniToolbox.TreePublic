using System.Reflection;

namespace OmniToolbox.TreePublic;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
internal sealed record ChatFrameOptimizationLogEntry(
    string Version,
    string TimestampLocal,
    string TimestampUTC,
    int GameTimestamp,
    ushort ChatType,
    string ChatTypeName,
    string SenderText,
    string MessageText,
    string SenderName,
    string SenderWorld,
    string SenderDisplayName,
    string SenderJobName,
    uint SenderJobIconID,
    uint SenderJobRowID,
    string SenderPayloadBase64,
    string MessagePayloadBase64,
    string? NativeFormattedText,
    string? NativeFormattedMacro,
    string? NativeFormattedPayloadBase64,
    IReadOnlyList<ChatFrameOptimizationPayloadEntry> SenderPayloads,
    IReadOnlyList<ChatFrameOptimizationPayloadEntry> MessagePayloads,
    IReadOnlyList<ChatFrameOptimizationPayloadEntry> NativeSegments);

[Obfuscation(Exclude = true, ApplyToMembers = true)]
internal sealed record ChatFrameOptimizationPayloadEntry(
    string Type,
    string Kind,
    string Text,
    string RawBase64);

internal sealed record ChatLogReplayFile(
    string Path,
    string Name,
    DateTime LastWriteTime,
    long Length);

internal sealed record ChatLogReplayEntry(
    string TimeText,
    string ChannelName,
    string SenderDisplayName,
    string SenderText,
    string SenderJobName,
    uint SenderJobIconID,
    string MessageText,
    string NativeFormattedText,
    string NativeFormattedPayloadBase64,
    string SearchText);

internal abstract record ChatLogReplayStorageResult(int RequestID, bool Succeeded);

internal sealed record ChatLogReplayFileListResult(
    int RequestID,
    bool Succeeded,
    IReadOnlyList<ChatLogReplayFile> Files) : ChatLogReplayStorageResult(RequestID, Succeeded);

internal sealed record ChatLogReplayReadResult(
    int RequestID,
    bool Succeeded,
    IReadOnlyList<ChatLogReplayEntry> Entries,
    int SkippedLineCount) : ChatLogReplayStorageResult(RequestID, Succeeded);
