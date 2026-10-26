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
using OmniToolbox.Config;

namespace OmniToolbox.TreePublic;

public sealed partial class WondrousTailsProbabilityDisplay
{
    internal static class InstructionText
    {
        internal static bool HasProbabilityLines(string text) =>
            text.Split('\r', '\n').Any(IsGeneratedLine);

        internal static (SeString ProbabilityLine, string AverageLine) Format(OmniModuleBase module, Probabilities values, Probabilities samples)
        {
            var probabilityPrefix = OmniLoc.Get(module, "Feature.WondrousTailsProbabilityDisplay.ProbabilityPrefix");
            var averagePrefix = OmniLoc.Get(module, "Feature.WondrousTailsProbabilityDisplay.AveragePrefix");
            if (values[0] < 0)
            {
                return (probabilityPrefix + OmniLoc.Get(module, "Feature.WondrousTailsProbabilityDisplay.ReadFailed"), averagePrefix + "-");
            }
            var probability = new SeStringBuilder().Append(probabilityPrefix);
            for (var index = 0; index < values.Length; index++)
            {
                if (index > 0)
                {
                    probability.Append("  ");
                }
                var text = FormatValue(module, index + 1, values[index]);
                var comparison = samples[0] < 0 ? 0 : Math.Round(values[index] * 100, 2).CompareTo(Math.Round(samples[index] * 100, 2));
                if (comparison == 0)
                {
                    probability.Append(text);
                }
                else
                {
                    probability.AddUiForeground(text, comparison > 0 ? ABOVE_AVERAGE_COLOR : BELOW_AVERAGE_COLOR);
                }
            }
            var average = averagePrefix + (samples[0] < 0 ? "-" : FormatValues(module, samples));
            return (probability.Build(), average);
        }

        private static string FormatValues(OmniModuleBase module, Probabilities values) =>
            $"{FormatValue(module, 1, values.OneLine)}  {FormatValue(module, 2, values.TwoLines)}  {FormatValue(module, 3, values.ThreeLines)}";

        private static string FormatValue(OmniModuleBase module, int lines, double probability) =>
            string.Format(OmniLoc.Get(module, "Feature.WondrousTailsProbabilityDisplay.ProbabilityValue"), lines, probability * 100);

        internal static string Normalize(string text) =>
            string.Join("\r", text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r').Split('\r')
                .Where(line => !IsGeneratedLine(line))).TrimEnd('\r');

        private static bool IsGeneratedLine(string text)
        {
            foreach (var language in UILanguages.All)
            {
                if (OmniLoc.TryGet("Feature.WondrousTailsProbabilityDisplay.ProbabilityPrefix", language, out var probabilityPrefix)
                    && text.StartsWith(probabilityPrefix, StringComparison.Ordinal))
                    return true;
                if (OmniLoc.TryGet("Feature.WondrousTailsProbabilityDisplay.AveragePrefix", language, out var averagePrefix)
                    && text.StartsWith(averagePrefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static (string Text, bool Separate) Prepare(string text, uint rowID, ClientLanguage language)
        {
            var normalized = Normalize(text);
            if (rowID == 5605)
            {
                var lines = normalized.Split('\r');
                var keep = language switch
                {
                    ClientLanguage.ChineseSimplified or ClientLanguage.Japanese when lines.Length == 3 => 2,
                    ClientLanguage.French when lines.Length == 2 => 1,
                    _ => 0
                };
                if (keep > 0 && lines.All(line => !string.IsNullOrWhiteSpace(line)))
                {
                    normalized = string.Join("\r", lines.Take(keep));
                }
                else if (language is ClientLanguage.English or ClientLanguage.German && lines.Length == 1)
                {
                    var sentenceEnd = normalized.IndexOf(". ", StringComparison.Ordinal);
                    if (sentenceEnd > 0 && !string.IsNullOrWhiteSpace(normalized[..sentenceEnd]) &&
                        !string.IsNullOrWhiteSpace(normalized[(sentenceEnd + 2)..]))
                    {
                        normalized = normalized[..(sentenceEnd + 1)];
                    }
                }
            }
            else if (rowID == 5608)
            {
                var lines = normalized.Split('\r');
                var keep = language switch
                {
                    ClientLanguage.ChineseSimplified or ClientLanguage.Japanese
                        when lines.Length == 5 && lines[3].Length == 0 && lines[2].Length != 0 => 2,
                    ClientLanguage.English or ClientLanguage.German
                        when lines.Length == 3 && lines[1].Length == 0 => 1,
                    ClientLanguage.French when lines.Length == 2 => 1,
                    _ => 0
                };
                if (keep > 0 && lines.Take(keep).All(line => line.Length != 0) && lines[^1].Length != 0)
                {
                    normalized = string.Join("\r", lines.Take(keep));
                }
            }
            return (normalized, rowID != 5605);
        }

        internal static (string Text, bool Separate) Resolve(
            string text, IReadOnlyDictionary<string, (string Text, bool Separate)> instructions, string rewardInstruction = "")
        {
            var normalized = Normalize(text);
            if (instructions.TryGetValue(normalized, out var instruction))
            {
                return instruction;
            }
            if (!string.IsNullOrWhiteSpace(rewardInstruction) &&
                instructions.TryGetValue(rewardInstruction, out instruction) &&
                normalized.Where(c => !char.IsWhiteSpace(c)).SequenceEqual(rewardInstruction.Where(c => !char.IsWhiteSpace(c))))
            {
                return instruction;
            }
            return (normalized, true);
        }

        internal static SeString Build(string baseText, SeString probabilityLine, string averageLine, bool separate = true) =>
            new SeStringBuilder().Append(baseText).Append(separate ? "\r\r" : "\r")
                .Append(probabilityLine).Append("\r").Append(averageLine).Build();

        #region 常量

        private const ushort ABOVE_AVERAGE_COLOR = 45;

        private const ushort BELOW_AVERAGE_COLOR = 17;

        #endregion
    }

}
