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

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class WondrousTailsProbabilityDisplay : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface,
        Author = "小朱诺诺的",
        SupportUrls = ["https://github.com/xiaozhunuonuode"]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "WondrousTailsProbabilityDisplayTitle",
            ["Description"] = "WondrousTailsProbabilityDisplayDescription",
            ["Feature.WondrousTailsProbabilityDisplay.ProbabilityPrefix"] = "Feature.WondrousTailsProbabilityDisplay.ProbabilityPrefix",
            ["Feature.WondrousTailsProbabilityDisplay.AveragePrefix"] = "Feature.WondrousTailsProbabilityDisplay.AveragePrefix",
            ["Feature.WondrousTailsProbabilityDisplay.ReadFailed"] = "Feature.WondrousTailsProbabilityDisplay.ReadFailed",
            ["Feature.WondrousTailsProbabilityDisplay.ProbabilityValue"] = "Feature.WondrousTailsProbabilityDisplay.ProbabilityValue"
        });

    private static readonly AddonEvent[] Events =
    [
        AddonEvent.PostSetup, AddonEvent.PreFinalize, AddonEvent.PostRefresh,
        AddonEvent.PostRequestedUpdate, AddonEvent.PostUpdate
    ];

    private Calculator? calculator;
    private readonly bool[] cells = new bool[16];
    private readonly Dictionary<uint, InstructionSnapshot> snapshots = [];
    private readonly Dictionary<string, (string Text, bool Separate)> instructionTexts = new(StringComparer.Ordinal);
    private string rewardInstruction = string.Empty;
    private nint addonAddress;
    private int lastMask = -1;
    private long localizationRevision = -1;
    private (SeString ProbabilityLine, string AverageLine) displayLines;

    protected override void OnStart(FeatureLifetime activation)
    {
        calculator ??= new Calculator();
        var lifetime = Activation;
        lifetime.Add(() =>
        {
            RestoreAndClear();
        });
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
            var text = InstructionText.Normalize(row.Text.ExtractText());
            if (text.Length != 0)
            {
                instructionTexts[text] = InstructionText.Prepare(text, rowID, data.Language);
                if (rowID == 5605)
                {
                    rewardInstruction = text;
                }
            }
        }

        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(OnAddonEvent));
        foreach (var addonEvent in Events)
        {
            DalamudServices.AddonLifecycle.RegisterListener(addonEvent, ADDON_NAME, OnAddonEvent);
        }

        var addon = AddonHelper.GetByName(ADDON_NAME);
        if (addon != null)
        {
            Refresh(addon);
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
        }
        catch (Exception)
        {
        }
    }

    private void Refresh(AtkUnitBase* addon)
    {
        if (localizationRevision != OmniLoc.Revision)
        {
            RestoreAndClear();
            localizationRevision = OmniLoc.Revision;
        }

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
            displayLines = InstructionText.Format(this, calculator.Solve(cells), Calculator.GetShuffleAverage(stickerCount));
            lastMask = mask;
        }

        UpdateInstructionText(addon, mask);
    }

    private void UpdateInstructionText(AtkUnitBase* addon, int mask)
    {
        var node = addon->GetTextNodeById(INSTRUCTION_TEXT_NODE_ID);
        if (node == null || node->Type != NodeType.Text)
        {
            return;
        }

        var currentBytes = node->NodeText.AsSpan();
        snapshots.TryGetValue(INSTRUCTION_TEXT_NODE_ID, out var snapshot);
        if (snapshot != null && snapshot.NodeAddress != (nint)node)
        {
            snapshot = null;
        }
        if (snapshot != null)
        {
            var currentState = new DisplayState(
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
        if (string.IsNullOrWhiteSpace(nativeText) || InstructionText.HasProbabilityLines(nativeText))
        {
            return;
        }
        var (baseText, separate) = InstructionText.Resolve(nativeText, instructionTexts, rewardInstruction);

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

        var replacedText = InstructionText.Build(baseText, displayLines.ProbabilityLine, displayLines.AverageLine, separate);
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
        snapshot.AppliedState = new DisplayState(
            (nint)node, mask, snapshot.AppliedHeight, (ushort)snapshot.AppliedFlags, nativeLineSpacing);
    }

    private void RestoreAndClear()
    {
        try
        {
            var addon = AddonHelper.GetByName(ADDON_NAME);
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
        internal DisplayState AppliedState;

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

    #region 常量

    private const string ADDON_NAME = "WeeklyBingo";

    private const uint INSTRUCTION_TEXT_NODE_ID = 34;

    #endregion
}
