using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;
using DutySlot = FFXIVClientStructs.Expansion.FFXIV.Client.UI.DutySlot;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class BetterWondrousTails : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface,
        Author = "小朱诺诺的、YouShu",
        RequiresPrivateProvider = true,
        SupportUrls = ["https://github.com/xiaozhunuonuode", "https://github.com/YouShux"]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "BetterWondrousTailsTitle",
            ["Description"] = "BetterWondrousTailsDescription",
            ["LineProbability"] = "Feature.BetterWondrousTails.LineProbability",
            ["ShuffleProbability"] = "Feature.BetterWondrousTails.ShuffleProbability",
            ["OpenDuty"] = "Feature.BetterWondrousTails.OpenDuty",
            ["OpenDutyHelp"] = "Feature.BetterWondrousTails.OpenDuty.Help"
        });

    private Settings                                  config                   = new();
    private Calculator?                               calculator;
    private readonly Dictionary<uint, RewardSnapshot> snapshots                = [];
    private readonly string[]                         suffixes                 = [string.Empty, string.Empty, string.Empty];
    private nint                                      addonAddress;
    private int                                       lastMask                 = -1;
    private bool                                      centerContentListPending;
    private Hook<DutySlot.Delegates.ReceiveEvent>?    DutySlotReceiveEventHook;

    protected override void OnStart(FeatureLifetime activation)
    {
        var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        activation.OnStopping(addonEvents.Dispose);
        addonEvents.Register(AddonEvent.PostSetup, ADDON_NAME, OnAddonEvent);
        addonEvents.Register(AddonEvent.PostRefresh, ADDON_NAME, OnAddonEvent);
        addonEvents.Register(AddonEvent.PostRequestedUpdate, ADDON_NAME, OnAddonEvent);
        addonEvents.Register(AddonEvent.PostUpdate, ADDON_NAME, OnAddonEvent);
        addonEvents.Register(AddonEvent.PreFinalize, ADDON_NAME, OnAddonEvent);
        addonEvents.Register(AddonEvent.PostSetup, CONTENT_LIST_ADDON_NAME, OnContentListEvent);
        addonEvents.Register(AddonEvent.PreDraw, CONTENT_LIST_ADDON_NAME, OnContentListEvent);
        addonEvents.Register(AddonEvent.PreFinalize, CONTENT_LIST_ADDON_NAME, OnContentListEvent);

        DutySlotReceiveEventHook = DService.Instance().Hook.HookFromMemberFunction(
            typeof(DutySlot.MemberFunctionPointers),
            nameof(DutySlot.MemberFunctionPointers.ReceiveEvent),
            (DutySlot.Delegates.ReceiveEvent)DutySlotReceiveEventDetour);
        activation.Add(DutySlotReceiveEventHook.Dispose);
        activation.OnStopping(DutySlotReceiveEventHook.Disable);
        RefreshSettings();
    }

    protected override void OnStop()
    {
        RestoreAndClear();
        centerContentListPending = false;
        DutySlotReceiveEventHook = null;
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var lineLabel = OmniLoc.Get(this, "LineProbability");
        var shuffleLabel = OmniLoc.Get(this, "ShuffleProbability");
        var dutyLabel = OmniLoc.Get(this, "OpenDuty");
        var helpSpacing = ImGui.GetStyle().ItemInnerSpacing.X;
        using var table = OmniControls.SettingsTable(
            "##betterWondrousTailsSettings",
            [OmniControls.MeasureCheckbox(lineLabel), OmniControls.MeasureCheckbox(shuffleLabel),
                OmniControls.MeasureGroup([OmniControls.MeasureCheckbox(dutyLabel), OmniControls.HelpIconSize()], helpSpacing)],
            ["##lineProbability", "##shuffleProbability", "##openDuty"],
            flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 4);
        if (!table)
            return false;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var changed = OmniControls.Checkbox(lineLabel, ref config.LineProbability);
        ImGui.TableNextColumn();
        changed |= OmniControls.Checkbox(shuffleLabel, ref config.ShuffleProbability);
        ImGui.TableNextColumn();
        changed |= OmniControls.Checkbox(dutyLabel, ref config.OpenDuty);
        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X, helpSpacing);
        OmniControls.HelpIcon(OmniLoc.Get(this, "OpenDutyHelp"));
        if (changed && IsEnabled)
            RefreshSettings();
        return changed;
    }

    public override bool ResetSettings()
    {
        if (config.LineProbability && config.ShuffleProbability && config.OpenDuty)
            return false;

        config.LineProbability    = true;
        config.ShuffleProbability = true;
        config.OpenDuty           = true;
        if (IsEnabled)
            RefreshSettings();
        return true;
    }

    private void RefreshSettings()
    {
        if (config.OpenDuty)
            DutySlotReceiveEventHook?.Enable();
        else
            DutySlotReceiveEventHook?.Disable();

        lastMask = -1;
        var addon = AddonHelper.GetByName(ADDON_NAME);
        if (addon != null)
            Refresh(addon);
    }

    private void OnAddonEvent
    (
        AddonEvent eventType,
        AddonArgs  args
    )
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            ClearState();
            return;
        }

        if (args.Addon.Address == nint.Zero)
            return;
        if (eventType == AddonEvent.PostSetup)
            ClearState();
        Refresh((AtkUnitBase*)args.Addon.Address);
    }

    private void Refresh(AtkUnitBase* addon)
    {
        if (addonAddress != (nint)addon)
        {
            ClearState();
            addonAddress = (nint)addon;
        }

        if (!config.LineProbability && !config.ShuffleProbability)
        {
            RestoreAndClear();
            return;
        }

        var player = PlayerState.Instance();
        if (player == null || !player->HasWeeklyBingoJournal || player->IsWeeklyBingoExpired())
        {
            RestoreAndClear();
            return;
        }

        var mask = 0;
        var stickerCount = 0;
        for (var index = 0; index < 16; index++)
        {
            if (!player->IsWeeklyBingoStickerPlaced(index))
                continue;
            mask |= 1 << index;
            stickerCount++;
        }

        if (stickerCount != player->WeeklyBingoNumPlacedStickers)
        {
            RestoreAndClear();
            return;
        }

        if (lastMask != mask)
        {
            var current = config.LineProbability ? (calculator ??= new()).Solve(mask) : Probabilities.Unavailable;
            var shuffle = Calculator.GetShuffleAverage(stickerCount);
            for (var index = 0; index < suffixes.Length; index++)
                suffixes[index] = RewardText.Format(current[index], shuffle[index], config.LineProbability, config.ShuffleProbability);
            lastMask = mask;
        }

        for (var index = 0; index < RewardNodeIDs.Length; index++)
            UpdateRewardText(addon, RewardNodeIDs[index], suffixes[index]);
    }

    private void UpdateRewardText
    (
        AtkUnitBase* addon,
        uint         nodeID,
        string       suffix
    )
    {
        var node = addon->GetTextNodeById(nodeID);
        if (node == null || node->Type != NodeType.Text)
            return;

        var current = node->NodeText.AsSpan();
        if (current.IsEmpty)
            return;

        snapshots.TryGetValue(nodeID, out var snapshot);
        if (snapshot == null || snapshot.NodeAddress != (nint)node)
        {
            snapshot          = new((nint)node, current, node->Width, node->TextFlags);
            snapshots[nodeID] = snapshot;
        }
        else if (!current.SequenceEqual(snapshot.AppliedText))
        {
            snapshot.OriginalText = RewardText.GetNativeText(current).ToArray();
            if (node->Width != snapshot.AppliedWidth)
                snapshot.OriginalWidth = node->Width;
            if (node->TextFlags != snapshot.AppliedFlags)
                snapshot.OriginalFlags = node->TextFlags;
        }

        if (snapshot.Suffix == suffix && current.SequenceEqual(snapshot.AppliedText))
            return;

        var updated = RewardText.Append(snapshot.OriginalText, suffix);
        node->TextFlags = snapshot.OriginalFlags & ~(TextFlags.WordWrap | TextFlags.MultiLine);
        node->SetText(updated.AsSpan());
        ushort textWidth = 0;
        ushort textHeight = 0;
        node->GetTextDrawSize(&textWidth, &textHeight);
        node->AtkResNode.SetWidth(Math.Max(snapshot.OriginalWidth, textWidth));
        snapshot.AppliedText  = node->NodeText.AsSpan().ToArray();
        snapshot.AppliedWidth = node->Width;
        snapshot.AppliedFlags = node->TextFlags;
        snapshot.Suffix       = suffix;
    }

    private void RestoreAndClear()
    {
        try
        {
            var addon = AddonHelper.GetByName(ADDON_NAME);
            if (addon == null || (nint)addon != addonAddress)
                return;

            foreach (var (nodeID, snapshot) in snapshots)
            {
                var node = addon->GetTextNodeById(nodeID);
                if (node == null || (nint)node != snapshot.NodeAddress)
                    continue;
                if (node->NodeText.AsSpan().SequenceEqual(snapshot.AppliedText))
                    node->SetText(RewardText.Append(snapshot.OriginalText, string.Empty).AsSpan());
                if (node->Width == snapshot.AppliedWidth)
                    node->AtkResNode.SetWidth(snapshot.OriginalWidth);
                if (node->TextFlags == snapshot.AppliedFlags)
                    node->TextFlags = snapshot.OriginalFlags;
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
        lastMask     = -1;
    }

    private sealed class Settings
    {
        public bool LineProbability    = true;
        public bool ShuffleProbability = true;
        public bool OpenDuty           = true;
    }

    private sealed class RewardSnapshot
    (
        nint               nodeAddress,
        ReadOnlySpan<byte> originalText,
        ushort             originalWidth,
        TextFlags          originalFlags
    )
    {
        internal readonly nint NodeAddress   = nodeAddress;
        internal byte[]        OriginalText  = RewardText.GetNativeText(originalText).ToArray();
        internal byte[]        AppliedText   = [];
        internal string        Suffix        = string.Empty;
        internal ushort        OriginalWidth = originalWidth;
        internal ushort        AppliedWidth;
        internal TextFlags     OriginalFlags = originalFlags;
        internal TextFlags     AppliedFlags;
    }

    private static readonly uint[] RewardNodeIDs = [78, 89, 100];

    #region 常量

    private const string ADDON_NAME              = "WeeklyBingo";
    private const string CONTENT_LIST_ADDON_NAME = "WeeklyBingoContentList";

    #endregion
}
