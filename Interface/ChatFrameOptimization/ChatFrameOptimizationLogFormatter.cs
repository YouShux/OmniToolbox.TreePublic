using System.Globalization;
using Dalamud.Game.Chat;
using Dalamud.Game.Text.Evaluator;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Utility;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions;
using OmenPlayerCharacter = OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds.IPlayerCharacter;

namespace OmniToolbox.TreePublic;

internal static class ChatFrameOptimizationLogFormatter
{
    public const string Version = "OmniToolbox.ChatLog.v2";

    public static ChatFrameOptimizationLogEntry Create(IChatMessage message)
    {
        var local = DateTimeOffset.Now;
        var sender = ResolveSender(message.Sender);
        var native = TryFormatNative(message);
        return new(
            Version,
            local.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            local.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            message.Timestamp,
            (ushort)message.LogKind,
            message.LogKind.ToString(),
            message.Sender.TextValue,
            message.Message.TextValue,
            sender.Name,
            sender.World,
            sender.DisplayName,
            sender.Job.Name,
            sender.Job.IconID,
            sender.Job.RowID,
            Convert.ToBase64String(message.Sender.Encode()),
            Convert.ToBase64String(message.Message.Encode()),
            native?.Text,
            native?.Macro,
            native?.PayloadBase64,
            GetPayloads(message.Sender),
            GetPayloads(message.Message),
            native?.Segments ?? []);
    }

    private static SenderInfo ResolveSender(SeString sender)
    {
        PlayerPayload? playerPayload = null;
        foreach (var payload in sender.Payloads)
        {
            if (payload is PlayerPayload player)
            {
                playerPayload = player;
                break;
            }
        }

        var name = playerPayload?.PlayerName.Trim() ?? string.Empty;
        var world = playerPayload?.World.ValueNullable?.Name.ExtractText().Trim() ?? string.Empty;
        var senderText = sender.TextValue.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            (name, world) = ParseSenderText(senderText);
        }

        return new(
            name,
            world,
            !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(world)
                ? $"{name}@{world}"
                : !string.IsNullOrWhiteSpace(name) ? name : senderText,
            ResolveSenderJob(name, world));
    }

    private static (string Name, string World) ParseSenderText(string senderText)
    {
        var text = senderText.Replace('\ue05d', '@').Trim();
        while (text.Length > 0 && char.GetUnicodeCategory(text[0]) == UnicodeCategory.PrivateUse)
        {
            text = text[1..].TrimStart();
        }

        var parts = text.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? (parts[0], parts[^1]) : (text, string.Empty);
    }

    private static SenderJobInfo ResolveSenderJob(string name, string world)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return default;
        }

        var services = DService.Instance();
        var local = services.ObjectTable.LocalPlayer;
        if (local is not null && IsPlayerMatch(
                local.Name,
                local.HomeWorld.ValueNullable?.Name.ExtractText(),
                name,
                world))
        {
            return CreateJobInfo(local.ClassJob);
        }

        SenderJobInfo partyJob = default;
        var partyMatches = 0;
        foreach (var member in services.PartyList)
        {
            if (!IsPlayerMatch(
                    member.Name.TextValue,
                    member.World.ValueNullable?.Name.ExtractText(),
                    name,
                    world))
            {
                continue;
            }

            partyJob = CreateJobInfo(member.ClassJob);
            partyMatches++;
        }

        if (partyMatches == 1)
        {
            return partyJob;
        }

        SenderJobInfo visibleJob = default;
        var visibleMatches = 0;
        foreach (var gameObject in services.ObjectTable.SearchObjects(
                     static gameObject => gameObject is OmenPlayerCharacter,
                     IObjectTable.CharactersRange))
        {
            if (gameObject is not OmenPlayerCharacter player ||
                !IsPlayerMatch(
                    player.Name,
                    player.HomeWorld.ValueNullable?.Name.ExtractText(),
                    name,
                    world))
            {
                continue;
            }

            visibleJob = CreateJobInfo(player.ClassJob);
            visibleMatches++;
        }

        return visibleMatches == 1 ? visibleJob : default;
    }

    private static bool IsPlayerMatch(
        string? candidateName,
        string? candidateWorld,
        string name,
        string world) =>
        string.Equals(candidateName?.Trim(), name.Trim(), StringComparison.Ordinal) &&
        (string.IsNullOrWhiteSpace(world) ||
         string.Equals(candidateWorld?.Trim(), world.Trim(), StringComparison.Ordinal));

    private static SenderJobInfo CreateJobInfo(RowRef<ClassJob> classJob) =>
        classJob.RowId == 0
            ? default
            : new(
                classJob.ValueNullable?.Name.ExtractText() ?? string.Empty,
                62000u + classJob.RowId,
                classJob.RowId);

    private static NativeChatLine? TryFormatNative(IChatMessage message)
    {
        try
        {
            var services = DService.Instance();
            var format = services.Data.GetExcelSheet<LogKind>().TryGetRow((uint)message.LogKind, out var row)
                ? row.Format
                : default;
            if (format.IsEmpty)
            {
                return null;
            }

            SeStringParameter[] parameters =
            [
                services.SeStringEvaluator.Evaluate(new ReadOnlySeString(message.Sender.Encode())),
                services.SeStringEvaluator.Evaluate(new ReadOnlySeString(message.Message.Encode()))
            ];
            var formatted = services.SeStringEvaluator.Evaluate(format, parameters);
            if (formatted.IsEmpty)
            {
                return null;
            }

            var payload = formatted.Data.ToArray();
            return new(
                formatted.ExtractText(),
                formatted.ToMacroString(),
                Convert.ToBase64String(payload),
                GetNativePayloads(SeString.Parse(payload)));
        }
        catch (Exception ex)
        {
            DService.Instance().Log.Warning(ex, "Chat log native formatting failed; structured fields were retained.");
            return null;
        }
    }

    private static IReadOnlyList<ChatFrameOptimizationPayloadEntry> GetPayloads(SeString value)
    {
        var result = new List<ChatFrameOptimizationPayloadEntry>(value.Payloads.Count);
        foreach (var payload in value.Payloads)
        {
            result.Add(new(
                payload.Type.ToString(),
                payload.GetType().Name,
                payload is TextPayload text ? text.Text ?? string.Empty : payload.ToString() ?? string.Empty,
                Convert.ToBase64String(payload.Encode())));
        }

        return result;
    }

    private static IReadOnlyList<ChatFrameOptimizationPayloadEntry> GetNativePayloads(SeString value)
    {
        var result = new List<ChatFrameOptimizationPayloadEntry>(value.Payloads.Count);
        foreach (var payload in value.Payloads)
        {
            result.Add(new(
                payload.Type.ToString(),
                payload.GetType().Name,
                payload is ITextProvider text ? text.Text : string.Empty,
                Convert.ToBase64String(payload.Encode())));
        }

        return result;
    }

    private readonly record struct SenderInfo(
        string Name,
        string World,
        string DisplayName,
        SenderJobInfo Job);

    private readonly record struct SenderJobInfo(string Name, uint IconID, uint RowID);

    private sealed record NativeChatLine(
        string Text,
        string Macro,
        string PayloadBase64,
        IReadOnlyList<ChatFrameOptimizationPayloadEntry> Segments);
}
