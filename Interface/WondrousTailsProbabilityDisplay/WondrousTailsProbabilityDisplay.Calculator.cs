using System.Linq;
using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Helpers;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using TreeTools.Lifecycle;

namespace OmniToolbox.TreePublic;

public sealed partial class WondrousTailsProbabilityDisplay
{
    internal sealed class Calculator
    {
        private readonly Dictionary<int, long[]> possibleBoards = [];
        private static readonly double[] Error = [-1, -1, -1];

        internal Calculator() => CalculateBoards(0, 0, 0, 0, 0);

        internal double[] Solve(bool[] cells)
        {
            if (cells == null || cells.Length != 16 || !possibleBoards.TryGetValue(CellsToMask(cells), out var counts))
            {
                return Error;
            }
            var divisor = (double)counts[0];
            return counts.Skip(1).Select(c => Math.Round(c / divisor, 4)).ToArray();
        }

        internal static double[] GetShuffleAverage(int stickersPlaced) =>
            stickersPlaced is >= 1 and <= 7
                ? [6688d / 11440, 1208d / 11440, 24d / 11440]
                : Error;

        private long[] CalculateBoards(int mask, int numStickers, int numRows, int numCols, int numDiags)
        {
            if (possibleBoards.TryGetValue(mask, out var result))
            {
                return result;
            }
            if (numStickers == 9)
            {
                var lines = numRows + numCols + numDiags;
                return possibleBoards[mask] = [1, lines >= 1 ? 1 : 0, lines >= 2 ? 1 : 0, lines >= 3 ? 1 : 0];
            }

            result = possibleBoards[mask] = [0, 0, 0, 0];
            for (var r = 0; r < 4; r++)
            {
                for (var c = 0; c < 4; c++)
                {
                    if (MaskHasBit(mask, r, c))
                    {
                        continue;
                    }
                    var nextMask = mask | (1 << ((4 * r) + c));
                    var rows = MaskHasRow(nextMask, r) ? 1 : 0;
                    var cols = MaskHasCol(nextMask, c) ? 1 : 0;
                    var diag1 = r == c && MaskHasDiag1(nextMask) ? 1 : 0;
                    var diag2 = r == 3 - c && MaskHasDiag2(nextMask) ? 1 : 0;
                    var next = CalculateBoards(nextMask, numStickers + 1, numRows + rows, numCols + cols, numDiags + diag1 + diag2);
                    for (var i = 0; i < 4; i++)
                    {
                        result[i] += next[i];
                    }
                }
            }
            return result;
        }

        private static int CellsToMask(bool[] cells)
        {
            var mask = 0;
            for (var i = 0; i < 16; i++)
            {
                if (cells[i])
                {
                    mask |= 1 << i;
                }
            }
            return mask;
        }

        private static bool MaskHasBit(int mask, int r, int c) => (mask & (1 << ((4 * r) + c))) != 0;

        private static bool MaskHasRow(int mask, int r)
        {
            var rowMask = 0x000F << (4 * r);
            return (mask & rowMask) == rowMask;
        }

        private static bool MaskHasCol(int mask, int c)
        {
            var colMask = 0x1111 << c;
            return (mask & colMask) == colMask;
        }

        private static bool MaskHasDiag1(int mask) => (mask & 0x8421) == 0x8421;

        private static bool MaskHasDiag2(int mask) => (mask & 0x1248) == 0x1248;
    }

}
