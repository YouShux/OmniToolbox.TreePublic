using System.Globalization;
using System.Linq;

namespace OmniToolbox.TreePublic;

internal sealed class ChatLogReplayAnonymizer
{
    private static readonly string[] ActionMarkers =
    [
        "正在发动",
        "发动了",
        "中断了发动",
        "正在咏唱",
        "咏唱了",
        "使用了",
        "施放了",
    ];

    private static readonly char[] InvalidNameCharacters =
        ['：', ':', '，', ',', '。', '.', '！', '!', '？', '?', '[', ']', '【', '】', '（', '）', '(', ')'];

    private readonly Dictionary<string, string> anonymousNames = new(StringComparer.Ordinal);
    private readonly List<(string Candidate, string Replacement)> replacements = [];

    public void Build(IReadOnlyList<ChatLogReplayEntry> entries, string prefix)
    {
        anonymousNames.Clear();
        replacements.Clear();
        foreach (var entry in entries)
        {
            Register(GetSenderCandidates(entry), prefix);
        }

        foreach (var entry in entries)
        {
            Register(GetMessageCandidates(entry.MessageText), prefix);
            Register(GetMessageCandidates(entry.NativeFormattedText), prefix);
        }

        foreach (var item in anonymousNames)
        {
            if (!string.IsNullOrWhiteSpace(item.Key))
            {
                replacements.Add((item.Key, item.Value));
            }
        }

        replacements.Sort(static (left, right) => right.Candidate.Length.CompareTo(left.Candidate.Length));
    }

    public string Replace(string text)
    {
        var result = text;
        foreach (var (candidate, replacement) in replacements)
        {
            result = result.Replace(candidate, replacement, StringComparison.Ordinal);
        }

        return result;
    }

    private void Register(IEnumerable<string> candidates, string prefix)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        string? replacement = null;
        foreach (var value in candidates)
        {
            var candidate = NormalizeCandidate(value);
            if (string.IsNullOrWhiteSpace(candidate) || !normalized.Add(candidate))
            {
                continue;
            }

            if (replacement is null && anonymousNames.TryGetValue(candidate, out var existing))
            {
                replacement = existing;
            }
        }

        if (normalized.Count == 0)
        {
            return;
        }

        replacement ??= FormatName(prefix, anonymousNames.Values.Distinct(StringComparer.Ordinal).Count() + 1);
        foreach (var candidate in normalized)
        {
            anonymousNames[candidate] = replacement;
        }
    }

    private static IEnumerable<string> GetSenderCandidates(ChatLogReplayEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SenderDisplayName))
        {
            yield return entry.SenderDisplayName;
            yield return entry.SenderDisplayName.Split('@', 2)[0];
        }

        if (!string.IsNullOrWhiteSpace(entry.SenderText))
        {
            yield return entry.SenderText;
        }
    }

    private static IEnumerable<string> GetMessageCandidates(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            yield break;
        }

        foreach (var line in source.Replace("\r", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (TryExtractActionPlayerName(line, out var name))
            {
                yield return name;
            }
        }
    }

    private static bool TryExtractActionPlayerName(string text, out string name)
    {
        name = string.Empty;
        var line = StripLeadingPrivateUse(text);
        var markerIndex = -1;
        foreach (var marker in ActionMarkers)
        {
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index > 0 && (markerIndex < 0 || index < markerIndex))
            {
                markerIndex = index;
            }
        }

        if (markerIndex <= 0)
        {
            return false;
        }

        var candidate = NormalizeCandidate(line[..markerIndex]);
        if (candidate.Length is < 2 or > 32 || candidate.IndexOfAny(InvalidNameCharacters) >= 0)
        {
            return false;
        }

        name = candidate;
        return true;
    }

    private static string NormalizeCandidate(string? value) =>
        StripLeadingPrivateUse(value ?? string.Empty).Trim();

    private static string StripLeadingPrivateUse(string text)
    {
        var value = text.Trim();
        while (value.Length > 0 && char.GetUnicodeCategory(value[0]) == UnicodeCategory.PrivateUse)
        {
            value = value[1..].TrimStart();
        }

        return value.Trim();
    }

    private static string FormatName(string prefix, int index)
    {
        var value = prefix.Trim();
        var digitStart = value.Length;
        while (digitStart > 0 && char.IsDigit(value[digitStart - 1]))
        {
            digitStart--;
        }

        if (digitStart == value.Length)
        {
            return $"{value} {index:00}";
        }

        return $"{value[..digitStart]}{index.ToString(
            $"D{value.Length - digitStart}",
            CultureInfo.InvariantCulture)}";
    }
}
