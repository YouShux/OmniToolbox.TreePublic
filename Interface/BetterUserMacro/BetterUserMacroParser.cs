using System.Text;

namespace OmniToolbox.TreePublic;

internal static class BetterUserMacroParser
{
    private static readonly byte[] SimplifiedIconCommand =
        [0xE5, 0xAE, 0x8F, 0xE5, 0x9B, 0xBE, 0xE6, 0xA0, 0x87];

    private static readonly byte[] TraditionalIconCommand =
        [0xE5, 0xAE, 0x8F, 0xE5, 0x9C, 0x96, 0xE6, 0xA8, 0x99];

    public static bool TryFindCustomIcon(ReadOnlySpan<byte> text, out uint iconID)
    {
        iconID = 0;
        while (!text.IsEmpty)
        {
            var lineEnd = text.IndexOf((byte)'\n');
            var line = lineEnd < 0 ? text : text[..lineEnd];
            if (TryParseCustomIcon(line, out iconID))
            {
                return true;
            }

            if (lineEnd < 0)
            {
                return false;
            }

            text = text[(lineEnd + 1)..];
        }

        return false;
    }

    public static bool TryParseCustomIcon(ReadOnlySpan<byte> line, out uint iconID)
    {
        iconID = 0;
        line = Trim(line);
        if (!ConsumeAscii(ref line, "/omni"u8))
        {
            return false;
        }

        line = Trim(line);
        if (!ConsumeCommand(ref line, SimplifiedIconCommand) &&
            !ConsumeCommand(ref line, TraditionalIconCommand))
        {
            return false;
        }

        line = Trim(line);
        if (line.IsEmpty)
        {
            return false;
        }

        uint value = 0;
        var digits = 0;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current is 0 or (byte)'\r' or (byte)' ' or (byte)'\t')
            {
                break;
            }

            if (current is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            var digit = (uint)(current - (byte)'0');
            if (value > (uint.MaxValue - digit) / 10)
            {
                return false;
            }

            value = value * 10 + digit;
            digits++;
        }

        if (digits == 0 || value == 0)
        {
            return false;
        }

        iconID = value;
        return true;
    }

    public static bool TryParseActionName(string line, out string actionName)
    {
        actionName = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var span = line.AsSpan().Trim();
        if (!ConsumeCommand(ref span, "/ac") && !ConsumeCommand(ref span, "/action"))
        {
            return false;
        }

        span = span.TrimStart();
        if (span.IsEmpty)
        {
            return false;
        }

        if (span[0] is '"' or '\u201c' or '\u201d')
        {
            var closingQuote = span[0] == '\u201c' ? '\u201d' : span[0];
            var closeIndex = span[1..].IndexOf(closingQuote);
            if (closeIndex < 0)
            {
                return false;
            }

            actionName = span.Slice(1, closeIndex).Trim().ToString();
            return actionName.Length > 0;
        }

        var targetIndex = span.IndexOf(" <".AsSpan(), StringComparison.Ordinal);
        var name = targetIndex >= 0 ? span[..targetIndex] : span;
        actionName = name.Trim().ToString();
        return actionName.Length > 0;
    }

    public static string Decode(ReadOnlySpan<byte> value)
    {
        var length = value.IndexOf((byte)0);
        if (length >= 0)
        {
            value = value[..length];
        }

        return value.IsEmpty ? string.Empty : Encoding.UTF8.GetString(value);
    }

    private static bool ConsumeCommand(ref ReadOnlySpan<char> line, string command)
    {
        if (!line.StartsWith(command.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (line.Length > command.Length && !char.IsWhiteSpace(line[command.Length]))
        {
            return false;
        }

        line = line[command.Length..];
        return true;
    }

    private static bool ConsumeAscii(ref ReadOnlySpan<byte> line, ReadOnlySpan<byte> command)
    {
        if (!line.StartsWith(command))
        {
            return false;
        }

        if (line.Length > command.Length && !IsWhitespace(line[command.Length]))
        {
            return false;
        }

        line = line[command.Length..];
        return true;
    }

    private static bool ConsumeCommand(ref ReadOnlySpan<byte> line, ReadOnlySpan<byte> command)
    {
        if (!line.StartsWith(command))
        {
            return false;
        }

        if (line.Length > command.Length && !IsWhitespace(line[command.Length]))
        {
            return false;
        }

        line = line[command.Length..];
        return true;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> line)
    {
        var start = 0;
        while (start < line.Length && IsWhitespace(line[start]))
        {
            start++;
        }

        var end = line.Length;
        while (end > start && IsWhitespace(line[end - 1]))
        {
            end--;
        }

        return line[start..end];
    }

    private static bool IsWhitespace(byte value) =>
        value is 0 or (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
