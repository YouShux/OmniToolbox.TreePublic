using System.Numerics;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterWondrousTails
{
    internal sealed class Calculator
    {
        private readonly long[]?[] possibleBoards = new long[]?[1 << 16];

        internal Probabilities Solve(int mask)
        {
            if (mask is < 0 or >= 1 << 16 || BitOperations.PopCount((uint)mask) > 9)
            {
                return Probabilities.Unavailable;
            }

            var counts = CalculateBoards(mask);
            var divisor = (double)counts[0];
            return new(Math.Round(counts[1] / divisor, 4), Math.Round(counts[2] / divisor, 4), Math.Round(counts[3] / divisor, 4));
        }

        internal static Probabilities GetShuffleAverage(int stickersPlaced) =>
            stickersPlaced is >= 1 and <= 7
                ? new(6688d / 11440, 1208d / 11440, 24d / 11440)
                : Probabilities.Unavailable;

        private long[] CalculateBoards(int mask)
        {
            if (possibleBoards[mask] is { } result)
            {
                return result;
            }
            if (BitOperations.PopCount((uint)mask) == 9)
            {
                var lines = 0;
                for (var index = 0; index < 4; index++)
                {
                    var rowMask = 0x000F << (4 * index);
                    var columnMask = 0x1111 << index;
                    if ((mask & rowMask) == rowMask)
                        lines++;
                    if ((mask & columnMask) == columnMask)
                        lines++;
                }
                if ((mask & 0x8421) == 0x8421)
                    lines++;
                if ((mask & 0x1248) == 0x1248)
                    lines++;
                return possibleBoards[mask] = [1, lines >= 1 ? 1 : 0, lines >= 2 ? 1 : 0, lines >= 3 ? 1 : 0];
            }

            result = new long[4];
            for (var index = 0; index < 16; index++)
            {
                if ((mask & (1 << index)) != 0)
                    continue;
                var next = CalculateBoards(mask | (1 << index));
                for (var count = 0; count < result.Length; count++)
                    result[count] += next[count];
            }
            return possibleBoards[mask] = result;
        }
    }

    internal readonly record struct Probabilities(double OneLine, double TwoLines, double ThreeLines)
    {
        internal static readonly Probabilities Unavailable = new(-1, -1, -1);

        internal int Length => 3;

        internal double this[int index] => index switch
        {
            0 => OneLine,
            1 => TwoLines,
            2 => ThreeLines,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}
