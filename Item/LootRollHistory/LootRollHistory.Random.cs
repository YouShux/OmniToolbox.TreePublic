using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Excel.Sheets;
using Lumina.Text.Expressions;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private Regex[] randomPatterns = [];

    private void OnRandomMessage(IHandleableChatMessage message)
    {
        if (message.LogKind != XivChatType.RandomNumber)
            return;

        RefreshCharacter();
        if (recordingSession is not { } session || storage is null)
            return;

        var original = SeString.Parse(message.OriginalMessage.Data.Span);
        var text = original.TextValue;
        foreach (var pattern in randomPatterns)
        {
            var match = pattern.Match(text);
            if (!match.Success || !int.TryParse(match.Groups["value"].ValueSpan, NumberStyles.None,
                                               CultureInfo.InvariantCulture, out var value))
                continue;
            var maximum = DEFAULT_RANDOM_MAXIMUM;
            if (match.Groups["maximum"].Success &&
                !int.TryParse(match.Groups["maximum"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out maximum))
                continue;
            if (maximum is < 1 or > DEFAULT_RANDOM_MAXIMUM || value < 0 || value > maximum)
                return;

            var playerLink = original.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            var name = playerLink?.PlayerName ?? match.Groups["player"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > MAX_PLAYER_NAME_LENGTH)
                return;
            var worldID = playerLink?.World.RowId ?? 0;
            if (playerLink is null)
            {
                var services = DService.Instance();
                var worlds = services.PartyList.Where(member => member.Name.TextValue == name)
                                     .Select(member => member.World.RowId).ToList();
                if (services.PlayerState.CharacterName == name)
                    worlds.Add(services.PlayerState.HomeWorld.RowId);
                var knownWorlds = worlds.Where(id => id != 0).Distinct().Take(2).ToArray();
                if (knownWorlds.Length == 1)
                    worldID = knownWorlds[0];
            }
            storage.Record(session.CharacterID, new(DateTimeOffset.UtcNow, name, worldID, value, maximum)
            {
                TerritoryID = session.TerritoryID,
                DutyID = session.DutyID,
                SessionID = session.ID
            });
            return;
        }
        DalamudServices.PluginLog.Warning("Roll history could not parse a /random chat message: {Message}", text);
    }

    private static Regex CreateRandomPattern(uint messageID)
    {
        if (!LuminaGetter.TryGetRow<LogMessage>(messageID, out var row))
            throw new InvalidDataException($"Random roll message {messageID} was not found.");

        var pattern = new StringBuilder(@"\A");
        foreach (var payload in row.Text.AsSpan())
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                pattern.Append(Regex.Escape(Encoding.UTF8.GetString(payload.Body)));
                continue;
            }
            switch (payload.MacroCode)
            {
                case MacroCode.If:
                    pattern.Append(@"(?<player>.+?)");
                    break;
                case MacroCode.Icon:
                    break;
                case MacroCode.Num when payload.TryGetExpression(out var expression) &&
                                       expression.TryGetParameterExpression(out var type, out var operand) &&
                                       type == (byte)ExpressionType.LocalNumber && operand.TryGetUInt(out var index):
                    pattern.Append(index switch
                    {
                        2 => @"(?<value>[0-9]{1,3})",
                        3 => @"(?<maximum>[0-9]{1,3})",
                        _ => throw new InvalidDataException($"Random roll message {messageID} contains an unexpected number.")
                    });
                    break;
                default:
                    throw new InvalidDataException($"Random roll message {messageID} contains an unsupported macro.");
            }
        }
        pattern.Append(@"\z");
        return new(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking);
    }
}
