using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using OmniToolbox.Config;
using TinyPinyin;

namespace OmniToolbox.TreePublic;

internal sealed class AntiCensorshipProcessor(AntiCensorshipConfig config, Func<string, string> filter)
{
    private readonly Dictionary<string, byte[]> highlightCache = new(StringComparer.Ordinal);
    private readonly Queue<string> highlightOrder = new();

    public void Bypass(ref SeString text)
    {
        var plainText = text.TextValue;
        if (string.IsNullOrWhiteSpace(plainText) || plainText.StartsWith('/'))
        {
            return;
        }

        var builder = new SeStringBuilder();
        foreach (var payload in text.Payloads)
        {
            if (payload is not TextPayload textPayload)
            {
                builder.Add(payload);
                continue;
            }

            var original = textPayload.Text ?? string.Empty;
            var handled = Bypass(original);
            builder.Add(string.Equals(original, handled, StringComparison.Ordinal)
                ? textPayload
                : new TextPayload(handled));
        }

        text = builder.Build();
    }

    public void Highlight(ref SeString text)
    {
        var plainText = text.TextValue;
        if (string.IsNullOrWhiteSpace(plainText) || plainText.StartsWith('/'))
        {
            return;
        }

        var builder = new SeStringBuilder();
        foreach (var payload in text.Payloads)
        {
            if (payload is not TextPayload textPayload)
            {
                builder.Add(payload);
                continue;
            }

            builder.Append(Highlight(textPayload.Text ?? string.Empty));
        }

        text = builder.Build();
    }

    public bool TryApplyAutoHandledHighlight(ref SeString text)
    {
        byte[]? highlighted;
        lock (highlightCache)
        {
            if (highlightCache.Count == 0 ||
                !highlightCache.TryGetValue(Convert.ToBase64String(text.Encode()), out highlighted))
            {
                return false;
            }
        }

        text = SeString.Parse(highlighted);
        return true;
    }

    public void RememberAutoHandledHighlight(SeString handled, SeString original)
    {
        if (!config.EnableColoring || config.HighlightColor <= 0)
        {
            return;
        }

        var highlighted = BuildBypassedAndHighlighted(original);
        if (!string.Equals(handled.TextValue, highlighted.TextValue, StringComparison.Ordinal))
        {
            return;
        }

        var key = Convert.ToBase64String(handled.Encode());
        var encoded = highlighted.Encode();
        lock (highlightCache)
        {
            if (highlightCache.ContainsKey(key))
            {
                highlightCache[key] = encoded;
                return;
            }

            highlightCache[key] = encoded;
            highlightOrder.Enqueue(key);
            while (highlightOrder.Count > 128)
            {
                highlightCache.Remove(highlightOrder.Dequeue());
            }
        }
    }

    public void Clear()
    {
        lock (highlightCache)
        {
            highlightCache.Clear();
            highlightOrder.Clear();
        }
    }

    private string Bypass(string originalText)
    {
        if (string.IsNullOrEmpty(originalText))
        {
            return originalText;
        }

        var builder = new StringBuilder(originalText.Length);
        var segmentStart = 0;
        for (var index = 0; index < originalText.Length; index++)
        {
            if (TryGetProtectedSpan(originalText, index, out var endIndex))
            {
                if (segmentStart < index)
                {
                    builder.Append(ProcessCensoredSegment(originalText[segmentStart..index]));
                }

                builder.Append(originalText[index..(endIndex + 1)]);
                index = endIndex;
                segmentStart = endIndex + 1;
                continue;
            }

            if (originalText[index] != '<')
            {
                continue;
            }

            if (segmentStart < index)
            {
                builder.Append(ProcessCensoredSegment(originalText[segmentStart..index]));
            }

            builder.Append(originalText[index..]);
            segmentStart = originalText.Length;
            break;
        }

        if (segmentStart < originalText.Length)
        {
            builder.Append(ProcessCensoredSegment(originalText[segmentStart..]));
        }

        return builder.ToString();
    }

    private SeString Highlight(string originalText)
    {
        if (config.HighlightColor < 0 || string.IsNullOrEmpty(originalText))
        {
            return originalText;
        }

        var filtered = filter(originalText);
        if (string.Equals(filtered, originalText, StringComparison.Ordinal))
        {
            return originalText;
        }

        var builder = new SeStringBuilder();
        var insideTag = false;
        var insideCensored = false;
        for (var index = 0; index < originalText.Length; index++)
        {
            if (originalText[index] == '<')
            {
                insideTag = true;
            }

            if (insideTag)
            {
                builder.Append(originalText[index].ToString());
                if (originalText[index] == '>')
                {
                    insideTag = false;
                }

                continue;
            }

            var censored = index < filtered.Length && filtered[index] == '*' && originalText[index] != '*';
            if (censored && !insideCensored)
            {
                builder.Add(new UIForegroundPayload((ushort)config.HighlightColor));
                insideCensored = true;
            }
            else if (!censored && insideCensored)
            {
                builder.Add(UIForegroundPayload.UIForegroundOff);
                insideCensored = false;
            }

            builder.Append(originalText[index].ToString());
        }

        if (insideCensored)
        {
            builder.Add(UIForegroundPayload.UIForegroundOff);
        }

        return builder.Build();
    }

    private SeString BuildBypassedAndHighlighted(SeString text)
    {
        var plainText = text.TextValue;
        if (string.IsNullOrWhiteSpace(plainText) || plainText.StartsWith('/'))
        {
            return text;
        }

        var builder = new SeStringBuilder();
        foreach (var payload in text.Payloads)
        {
            if (payload is not TextPayload textPayload)
            {
                builder.Add(payload);
                continue;
            }

            builder.Append(BuildBypassedAndHighlighted(textPayload.Text ?? string.Empty));
        }

        return builder.Build();
    }

    private SeString BuildBypassedAndHighlighted(string originalText)
    {
        if (config.HighlightColor <= 0 || string.IsNullOrEmpty(originalText))
        {
            return Bypass(originalText);
        }

        var filtered = filter(originalText);
        if (string.Equals(filtered, originalText, StringComparison.Ordinal))
        {
            return originalText;
        }

        var builder = new SeStringBuilder();
        var insideTag = false;
        for (var index = 0; index < originalText.Length; index++)
        {
            if (originalText[index] == '<')
            {
                insideTag = true;
            }

            if (insideTag)
            {
                builder.Append(originalText[index].ToString());
                if (originalText[index] == '>')
                {
                    insideTag = false;
                }

                continue;
            }

            if (index >= filtered.Length || filtered[index] != '*' || originalText[index] == '*')
            {
                builder.Append(originalText[index].ToString());
                continue;
            }

            var start = index;
            while (index < originalText.Length &&
                   index < filtered.Length &&
                   filtered[index] == '*' &&
                   originalText[index] != '*')
            {
                index++;
            }

            builder.Add(new UIForegroundPayload((ushort)config.HighlightColor));
            builder.Append(Bypass(originalText[start..index]));
            builder.Add(UIForegroundPayload.UIForegroundOff);
            index--;
        }

        return builder.Build();
    }

    private string ProcessCensoredSegment(string text)
    {
        var result = text;
        var filtered = filter(result);
        if (string.Equals(filtered, result, StringComparison.Ordinal))
        {
            return result;
        }

        var processed = new HashSet<string>(StringComparer.Ordinal);
        while (!string.Equals(filtered, result, StringComparison.Ordinal) && processed.Add(result))
        {
            var resultRunes = GetRunes(result);
            var filteredRunes = GetRunes(filtered);
            var builder = new StringBuilder(result.Length);
            var resultIndex = 0;
            var filteredIndex = 0;
            while (resultIndex < resultRunes.Count)
            {
                var resultRune = resultRunes[resultIndex];
                Rune? filteredRune = filteredIndex < filteredRunes.Count
                    ? filteredRunes[filteredIndex]
                    : null;
                if (filteredRune.HasValue && filteredRune.Value == resultRune)
                {
                    builder.Append(resultRune.ToString());
                    resultIndex++;
                    filteredIndex++;
                    continue;
                }

                if (filteredRune is { Value: '*' })
                {
                    var nextClearFilteredIndex = filteredIndex;
                    while (nextClearFilteredIndex < filteredRunes.Count && filteredRunes[nextClearFilteredIndex].Value == '*')
                    {
                        nextClearFilteredIndex++;
                    }

                    if (nextClearFilteredIndex >= filteredRunes.Count)
                    {
                        ProcessCensoredWord(builder, resultRunes, resultIndex, resultRunes.Count - resultIndex);
                        resultIndex = resultRunes.Count;
                        filteredIndex = filteredRunes.Count;
                        continue;
                    }

                    var anchor = filteredRunes[nextClearFilteredIndex];
                    var nextClearResultIndex = resultIndex;
                    while (nextClearResultIndex < resultRunes.Count && resultRunes[nextClearResultIndex] != anchor)
                    {
                        nextClearResultIndex++;
                    }

                    if (nextClearResultIndex < resultRunes.Count)
                    {
                        ProcessCensoredWord(builder, resultRunes, resultIndex, nextClearResultIndex - resultIndex);
                        resultIndex = nextClearResultIndex;
                        filteredIndex = nextClearFilteredIndex;
                        continue;
                    }
                }

                builder.Append(resultRune.ToString());
                resultIndex++;
                if (filteredIndex < filteredRunes.Count)
                {
                    filteredIndex++;
                }
            }

            result = builder.ToString();
            filtered = filter(result);
        }

        return result;
    }

    private void ProcessCensoredWord(StringBuilder builder, IReadOnlyList<Rune> runes, int start, int count)
    {
        if (count == 1 && IsChineseRune(runes[start]))
        {
            builder.Append(PinyinHelper.GetPinyin(runes[start].ToString()).ToLowerInvariant());
            return;
        }

        for (var index = 0; index < count; index++)
        {
            builder.Append(runes[start + index].ToString());
            if (index + 1 < count)
            {
                builder.Append(config.Separator);
            }
        }
    }

    private static List<Rune> GetRunes(string text)
    {
        var runes = new List<Rune>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            runes.Add(rune);
        }

        return runes;
    }

    private static bool TryGetProtectedSpan(string text, int index, out int endIndex)
    {
        if (text[index] == '<')
        {
            endIndex = text.IndexOf('>', index + 1);
            return endIndex >= 0;
        }

        if (text[index] == '[' &&
            index + 6 <= text.Length &&
            text.AsSpan(index, 6).Equals("[stgy:", StringComparison.Ordinal))
        {
            endIndex = text.IndexOf(']', index + 6);
            return endIndex >= 0;
        }

        endIndex = -1;
        return false;
    }

    private static bool IsChineseRune(Rune rune) => rune.Value is
        >= 0x4E00 and <= 0x9FFF or
        >= 0x3400 and <= 0x4DBF or
        >= 0x20000 and <= 0x2A6DF or
        >= 0x2A700 and <= 0x2B73F or
        >= 0x2B740 and <= 0x2B81F or
        >= 0x2B820 and <= 0x2CEAF or
        >= 0x2CEB0 and <= 0x2EBEF or
        >= 0x30000 and <= 0x3134F or
        >= 0x31350 and <= 0x323AF or
        >= 0xF900 and <= 0xFAFF;
}
