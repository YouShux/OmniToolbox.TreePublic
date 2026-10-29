using System.Globalization;
using System.Text;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterWondrousTails
{
    private static class RewardText
    {
        internal static string Format
        (
            double current,
            double shuffle,
            bool   showCurrent,
            bool   showShuffle
        )
        {
            if (showCurrent && showShuffle)
                return $"（{FormatValue(current)}/{FormatValue(shuffle)}）";
            if (showCurrent)
                return $"（{FormatValue(current)}）";
            return showShuffle ? $"（{FormatValue(shuffle)}）" : string.Empty;
        }

        internal static byte[] Append
        (
            ReadOnlySpan<byte> nativeText,
            string             suffix
        )
        {
            nativeText = GetNativeText(nativeText);
            var result = new byte[nativeText.Length + Encoding.UTF8.GetByteCount(suffix) + 1];
            nativeText.CopyTo(result);
            Encoding.UTF8.GetBytes(suffix, result.AsSpan(nativeText.Length));
            return result;
        }

        internal static ReadOnlySpan<byte> GetNativeText(ReadOnlySpan<byte> nativeText)
        {
            if (!nativeText.IsEmpty && nativeText[^1] == 0)
                nativeText = nativeText[..^1];

            while (!nativeText.IsEmpty)
            {
                var text = nativeText.TrimEnd((byte)' ');
                int start;
                ReadOnlySpan<byte> probability;
                if (text.EndsWith("）"u8))
                {
                    start = text.LastIndexOf("（"u8);
                    if (start < 0)
                        break;
                    probability = text[(start + "（"u8.Length)..^"）"u8.Length];
                }
                else if (text.EndsWith(")"u8))
                {
                    start = text.LastIndexOf((byte)'(');
                    if (start < 0)
                        break;
                    probability = text[(start + 1)..^1];
                }
                else
                {
                    break;
                }

                var separator = probability.IndexOf((byte)'/');
                var valid = separator < 0
                    ? IsProbability(probability)
                    : IsProbability(probability[..separator]) && IsProbability(probability[(separator + 1)..]);
                if (!valid)
                    break;

                nativeText = text[..start].TrimEnd((byte)' ');
            }

            return nativeText;
        }

        private static bool IsProbability(ReadOnlySpan<byte> value)
        {
            value = value.Trim((byte)' ');
            return value.SequenceEqual("—"u8) ||
                   value.EndsWith("%"u8) &&
                   double.TryParse(value[..^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var probability) &&
                   probability is >= 0 and <= 100;
        }

        private static string FormatValue(double probability) =>
            double.IsFinite(probability) && probability is >= 0 and <= 1
                ? (probability * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%"
                : "—";
    }
}
