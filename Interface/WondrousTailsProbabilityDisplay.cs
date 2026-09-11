using System;
using System.Collections.Generic;
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
using OmniToolbox.Lifecycle;

namespace OmniToolbox.TreePublic;

public sealed unsafe class WondrousTailsProbabilityDisplay : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "天书概率助手",
        Description = "在天书界面实时显示至少 1 / 2 / 3 线的概率和重排平均参考（均匀模型精确值）。",
        Category = ModuleCategory.Interface,
        Author = "小朱诺诺的",
        SupportUrls = ["https://github.com/xiaozhunuonuode"]
    };

    private const string AddonName = "WeeklyBingo";
    private const uint InstructionTextNodeID = 34;
    private static readonly AddonEvent[] Events =
    [
        AddonEvent.PostSetup, AddonEvent.PreFinalize, AddonEvent.PostRefresh,
        AddonEvent.PostRequestedUpdate, AddonEvent.PostUpdate
    ];

    private FeatureLifetime? runtimeLifetime;
    private WondrousTailsProbabilityCalculator? calculator;
    private readonly bool[] cells = new bool[16];
    private readonly Dictionary<uint, InstructionSnapshot> snapshots = [];
    private readonly Dictionary<string, (string Text, bool Separate)> instructionTexts = new(StringComparer.Ordinal);
    private string rewardInstruction = string.Empty;
    private nint addonAddress;
    private int lastMask = -1;
    private (SeString ProbabilityLine, string AverageLine) displayLines;
    private bool updateErrorLogged;

    protected override void OnEnable()
    {
        calculator ??= new WondrousTailsProbabilityCalculator();
        var lifetime = new FeatureLifetime();
        try
        {
            instructionTexts.Clear();
            rewardInstruction = string.Empty;
            var data = DalamudServices.DataManager;
            var sheet = data.GetExcelSheet<Addon>();
            foreach (var rowID in new uint[] { 5604, 5605, 5607, 5608 })
            {
                if (!sheet.TryGetRow(rowID, out var row))
                {
                    continue;
                }
                var text = WondrousTailsInstructionText.Normalize(row.Text.ExtractText());
                if (text.Length != 0)
                {
                    instructionTexts[text] = WondrousTailsInstructionText.Prepare(text, rowID, data.Language);
                    if (rowID == 5605)
                    {
                        rewardInstruction = text;
                    }
                }
            }

            lifetime.Add(() => DalamudServices.AddonLifecycle.UnregisterListener(OnAddonEvent));
            foreach (var addonEvent in Events)
            {
                DalamudServices.AddonLifecycle.RegisterListener(addonEvent, AddonName, OnAddonEvent);
            }

            var addon = AddonHelper.GetByName(AddonName);
            if (addon != null)
            {
                Refresh(addon);
            }

            runtimeLifetime = lifetime;
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                RestoreAndClear();
            }
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            RestoreAndClear();
        }
    }

    private void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        try
        {
            if (args.Addon.Address == nint.Zero)
            {
                return;
            }

            var addon = (AtkUnitBase*)args.Addon.Address;
            if (type == AddonEvent.PreFinalize)
            {
                ClearState();
                return;
            }

            if (type == AddonEvent.PostSetup)
            {
                ClearState();
            }

            Refresh(addon);
            updateErrorLogged = false;
        }
        catch (Exception ex)
        {
            if (!updateErrorLogged)
            {
                DalamudServices.PluginLog.Error(ex, "Failed to update Wondrous Tails probability display.");
            }
            updateErrorLogged = true;
        }
    }

    private void Refresh(AtkUnitBase* addon)
    {
        if (addonAddress != (nint)addon)
        {
            ClearState();
            addonAddress = (nint)addon;
        }

        var playerState = PlayerState.Instance();
        if (playerState == null || calculator == null)
        {
            return;
        }

        var mask = 0;
        var stickerCount = 0;
        for (var index = 0; index < cells.Length; index++)
        {
            cells[index] = playerState->IsWeeklyBingoStickerPlaced(index);
            if (!cells[index])
            {
                continue;
            }
            mask |= 1 << index;
            stickerCount++;
        }

        if (stickerCount != playerState->WeeklyBingoNumPlacedStickers)
        {
            return;
        }

        if (lastMask != mask)
        {
            displayLines = WondrousTailsInstructionText.Format(calculator.Solve(cells), WondrousTailsProbabilityCalculator.GetShuffleAverage(stickerCount));
            lastMask = mask;
        }

        UpdateInstructionText(addon, mask);
    }

    private void UpdateInstructionText(AtkUnitBase* addon, int mask)
    {
        var node = addon->GetTextNodeById(InstructionTextNodeID);
        if (node == null || node->Type != NodeType.Text)
        {
            return;
        }

        var currentBytes = node->NodeText.AsSpan();
        snapshots.TryGetValue(InstructionTextNodeID, out var snapshot);
        if (snapshot != null && snapshot.NodeAddress != (nint)node)
        {
            snapshot = null;
        }
        if (snapshot != null)
        {
            var currentState = new WondrousTailsDisplayState(
                (nint)node, mask, node->GetHeight(), (ushort)node->TextFlags, node->LineSpacing);
            if (currentState.Matches(snapshot.AppliedState, currentBytes, snapshot.AppliedText))
            {
                return;
            }
        }

        var nativeBytes = snapshot != null && currentBytes.SequenceEqual(snapshot.AppliedText)
            ? snapshot.OriginalText.AsSpan(0, snapshot.OriginalText.Length - 1)
            : currentBytes;
        var nativeText = SeString.Parse(nativeBytes).TextValue;
        if (string.IsNullOrWhiteSpace(nativeText) || WondrousTailsInstructionText.HasProbabilityLines(nativeText))
        {
            return;
        }
        var (baseText, separate) = WondrousTailsInstructionText.Resolve(nativeText, instructionTexts, rewardInstruction);

        if (snapshot == null)
        {
            snapshot = new InstructionSnapshot((nint)node, currentBytes, node->GetHeight(), node->TextFlags);
            snapshots[node->NodeId] = snapshot;
        }
        else
        {
            snapshot.AppliedState = default;
            if (!currentBytes.SequenceEqual(snapshot.AppliedText))
            {
                snapshot.CaptureText(currentBytes);
            }
            if (node->GetHeight() != snapshot.AppliedHeight)
            {
                snapshot.OriginalHeight = node->GetHeight();
            }
            if (node->TextFlags != snapshot.AppliedFlags)
            {
                snapshot.OriginalFlags = node->TextFlags;
            }
        }

        var replacedText = WondrousTailsInstructionText.Build(baseText, displayLines.ProbabilityLine, displayLines.AverageLine, separate);
        var nativeLineSpacing = node->LineSpacing;
        snapshot.AppliedHeight = snapshot.OriginalHeight;
        snapshot.AppliedFlags = snapshot.OriginalFlags | TextFlags.MultiLine;
        node->TextFlags = snapshot.AppliedFlags;
        node->SetHeight(snapshot.AppliedHeight);
        var encodedText = replacedText.EncodeWithNullTerminator();
        if (!currentBytes.SequenceEqual(encodedText.AsSpan(0, encodedText.Length - 1)))
        {
            node->SetText(encodedText.AsSpan());
        }
        snapshot.AppliedText = node->NodeText.AsSpan().ToArray();
        snapshot.AppliedState = new WondrousTailsDisplayState(
            (nint)node, mask, snapshot.AppliedHeight, (ushort)snapshot.AppliedFlags, nativeLineSpacing);
    }

    private void RestoreAndClear()
    {
        try
        {
            var addon = AddonHelper.GetByName(AddonName);
            if (addon == null || (nint)addon != addonAddress)
            {
                return;
            }
            foreach (var (nodeID, snapshot) in snapshots)
            {
                var node = addon->GetTextNodeById(nodeID);
                if (node == null || (nint)node != snapshot.NodeAddress)
                {
                    continue;
                }
                if (node->NodeText.AsSpan().SequenceEqual(snapshot.AppliedText))
                {
                    node->SetText(snapshot.OriginalText.AsSpan());
                }
                if (node->GetHeight() == snapshot.AppliedHeight)
                {
                    node->SetHeight(snapshot.OriginalHeight);
                }
                if (node->TextFlags == snapshot.AppliedFlags)
                {
                    node->TextFlags = snapshot.OriginalFlags;
                }
            }
        }
        finally
        {
            ClearState();
        }
    }

    private void ClearState()
    {
        snapshots.Clear();
        addonAddress = nint.Zero;
        lastMask = -1;
        updateErrorLogged = false;
    }

    private sealed class InstructionSnapshot
    {
        internal readonly nint NodeAddress;
        internal byte[] OriginalText = [];
        internal byte[] AppliedText = [];
        internal ushort OriginalHeight;
        internal ushort AppliedHeight;
        internal TextFlags OriginalFlags;
        internal TextFlags AppliedFlags;
        internal WondrousTailsDisplayState AppliedState;

        internal InstructionSnapshot(nint address, ReadOnlySpan<byte> text, ushort height, TextFlags flags)
        {
            NodeAddress = address;
            CaptureText(text);
            OriginalHeight = AppliedHeight = height;
            OriginalFlags = AppliedFlags = flags;
        }

        internal void CaptureText(ReadOnlySpan<byte> text)
        {
            OriginalText = new byte[text.Length + 1];
            text.CopyTo(OriginalText);
        }
    }
}

internal readonly record struct WondrousTailsDisplayState(
    nint NodeAddress, int Mask, ushort Height, ushort Flags, byte LineSpacing)
{
    internal bool Matches(WondrousTailsDisplayState applied, ReadOnlySpan<byte> currentText, ReadOnlySpan<byte> appliedText) =>
        NodeAddress != nint.Zero && this == applied && currentText.SequenceEqual(appliedText);
}

internal sealed class WondrousTailsProbabilityCalculator
{
    private readonly Dictionary<int, long[]> possibleBoards = [];
    private static readonly double[] Error = [-1, -1, -1];

    internal WondrousTailsProbabilityCalculator() => CalculateBoards(0, 0, 0, 0, 0);

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

internal static class WondrousTailsInstructionText
{
    private const string ProbabilityPrefix = "连线概率：";
    private const string AveragePrefix = "重排平均：";
    private const ushort AboveAverageColor = 45;
    private const ushort BelowAverageColor = 17;

    internal static bool HasProbabilityLines(string text) =>
        text.Contains(ProbabilityPrefix, StringComparison.Ordinal) ||
        text.Contains(AveragePrefix, StringComparison.Ordinal);

    internal static (SeString ProbabilityLine, string AverageLine) Format(double[] values, double[] samples)
    {
        if (values[0] < 0)
        {
            return (ProbabilityPrefix + "读取失败", AveragePrefix + "-");
        }
        var probability = new SeStringBuilder().Append(ProbabilityPrefix);
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                probability.Append("  ");
            }
            var text = $"{index + 1}线 {values[index] * 100:F2}%";
            var comparison = samples[0] < 0 ? 0 : Math.Round(values[index] * 100, 2).CompareTo(Math.Round(samples[index] * 100, 2));
            if (comparison == 0)
            {
                probability.Append(text);
            }
            else
            {
                probability.AddUiForeground(text, comparison > 0 ? AboveAverageColor : BelowAverageColor);
            }
        }
        var average = AveragePrefix + (samples[0] < 0 ? "-" : FormatValues(samples));
        return (probability.Build(), average);
    }

    private static string FormatValues(double[] values) =>
        string.Join("  ", values.Select((value, index) => $"{index + 1}线 {value * 100:F2}%"));

    internal static string Normalize(string text) =>
        string.Join("\r", text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r').Split('\r')
            .Where(line => !line.StartsWith(ProbabilityPrefix, StringComparison.Ordinal) &&
                           !line.StartsWith(AveragePrefix, StringComparison.Ordinal))).TrimEnd('\r');

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
}
