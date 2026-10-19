using System.Collections.Frozen;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Dalamud.Services.Game;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace OmniToolbox.TreePublic;

public sealed partial class DuplicateStatusPrevention
{
    private static class Rules
    {
        private static readonly KeyValuePair<uint, ActionRule>[] OrderedActionData =
        [
            new(7549, new([new(1195, StatusDetectionTarget.Target)])),
        new(7560, new([new(1203, StatusDetectionTarget.Target)])),
        new(25857, new([new(2707, StatusDetectionTarget.Self)])),
        new(2887, new([new(860, StatusDetectionTarget.Target)])),
        new(16889, new([new(1951, StatusDetectionTarget.Self), new(1934, StatusDetectionTarget.Self), new(1826, StatusDetectionTarget.Self)])),
        new(16012, new([new(1951, StatusDetectionTarget.Self), new(1934, StatusDetectionTarget.Self), new(1826, StatusDetectionTarget.Self)])),
        new(7405, new([new(1951, StatusDetectionTarget.Self), new(1934, StatusDetectionTarget.Self), new(1826, StatusDetectionTarget.Self)])),
        new(7408, new([new(1202, StatusDetectionTarget.Self)])),
        new(7535, new([new(1193, StatusDetectionTarget.Target)])),
        new(7388, new([new(1457, StatusDetectionTarget.Self)])),
        new(3540, new([new(1362, StatusDetectionTarget.Self)])),
        new(7382, new([new(1174, StatusDetectionTarget.Target)])),
        new(25754, new([new(2682, StatusDetectionTarget.Target)])),
        new(7393, new([new(1178, StatusDetectionTarget.Target)])),
        new(16160, new([new(1839, StatusDetectionTarget.Self)])),
        new(25758, new([new(2683, StatusDetectionTarget.Target)])),
        new(16151, new([new(1835, StatusDetectionTarget.Target)])),
        new(7432, new([new(1218, StatusDetectionTarget.Target)])),
        new(25861, new([new(2708, StatusDetectionTarget.Target)])),
        new(7430, new([new(1217, StatusDetectionTarget.Self)])),
        new(25873, new([new(2717, StatusDetectionTarget.Target)])),
        new(188, new([new(1944, StatusDetectionTarget.Self)])),
        new(7863, new([new(2, StatusDetectionTarget.Target)])),
        new(7540, new([new(2, StatusDetectionTarget.Target)])),
        new(16, new([new(2, StatusDetectionTarget.Target)])),
        new(7546, new([new(1250, StatusDetectionTarget.Self)])),
        new(7548, new([new(2663, StatusDetectionTarget.Self)])),
        new(3557, new([new(786, StatusDetectionTarget.Self)])),
        new(83, new([new(116, StatusDetectionTarget.Self)])),
        new(69, new([new(110, StatusDetectionTarget.Self)])),
        new(7396, new([new(1182, StatusDetectionTarget.Self), new(1185, StatusDetectionTarget.Self)])),
        new(2248, new([new(638, StatusDetectionTarget.Target)])),
        new(7499, new([new(1233, StatusDetectionTarget.Self), new(3856, StatusDetectionTarget.Self)])),
        new(7421, new([new(1211, StatusDetectionTarget.Self)])),
        new(7518, new([new(1238, StatusDetectionTarget.Self)])),
        new(25801, new([new(2703, StatusDetectionTarget.Self)])),
        new(16508, new([new(304, StatusDetectionTarget.Self)])),
        new(16510, new([new(304, StatusDetectionTarget.Self)])),
        new(2876, new([new(851, StatusDetectionTarget.Self)])),
        new(34579, new([new(3643, StatusDetectionTarget.Target)])),
        new(34567, new([new(3712, StatusDetectionTarget.Target)])),
        new(118, new([new(141, StatusDetectionTarget.Self)])),
        new(7436, new([new(1221, StatusDetectionTarget.Target)])),
        new(16552, new([new(1878, StatusDetectionTarget.Self)])),
        new(3606, new([new(841, StatusDetectionTarget.Self)])),
        new(125, new([new(148, StatusDetectionTarget.Target)])),
        new(173, new([new(148, StatusDetectionTarget.Target)])),
        new(3603, new([new(148, StatusDetectionTarget.Target)])),
        new(24287, new([new(148, StatusDetectionTarget.Target)])),
        new(41634, new([new(148, StatusDetectionTarget.Target)])),
        new(49070, new([new(148, StatusDetectionTarget.Target)])),
        new(7523, new([new(148, StatusDetectionTarget.Target)])),
        new(18317, new([new(148, StatusDetectionTarget.Target)])),
        new(29057, new([new(1342, StatusDetectionTarget.Self)])),
        new(29234, new([new(3087, StatusDetectionTarget.Target, true)], [new(3089, StatusDetectionTarget.Target)])),
        new(3585, new([new(297, StatusDetectionTarget.Target, true)])),
        new(29415, new([new(3054, StatusDetectionTarget.Target), new(1302, StatusDetectionTarget.Target), new(3039, StatusDetectionTarget.Target)])),
        new(29228, new([new(3054, StatusDetectionTarget.Target)])),
        new(29081, new([new(3054, StatusDetectionTarget.Target)])),
        new(29258, new([new(3107, StatusDetectionTarget.Self)])),
        new(29264, new([new(2872, StatusDetectionTarget.Target)])),
        new(29395, new([new(3054, StatusDetectionTarget.Target), new(3248, StatusDetectionTarget.Target)])),
        new(29515, new([new(1302, StatusDetectionTarget.Target), new(3039, StatusDetectionTarget.Target)])),
        new(29414, new([new(3158, StatusDetectionTarget.Self)])),
        new(41624, new([new(4259, StatusDetectionTarget.Target)]))
        ];

        public static ReadOnlySpan<KeyValuePair<uint, ActionRule>> OrderedActions => OrderedActionData;

        public static FrozenDictionary<uint, ActionRule> Actions { get; } =
            OrderedActionData.ToFrozenDictionary();
    }

    private sealed record ActionRule(
        StatusRule[] Statuses,
        StatusRule[]? SecondStatuses = null);

    private readonly record struct StatusRule(
        uint StatusID,
        StatusDetectionTarget DetectionTarget,
        bool IsReverse = false)
    {
        public bool Blocks(bool hasStatus) => IsReverse != hasStatus;
    }

    private enum StatusDetectionTarget
    {
        Self,
        Member,
        Target
    }
}