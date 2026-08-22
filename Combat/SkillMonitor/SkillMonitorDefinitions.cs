using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace OmniToolbox.TreePublic;

internal readonly record struct SkillMonitorDefinition(
    uint ActionID,
    uint StatusID,
    int CooldownMilliseconds,
    int ActiveMilliseconds,
    ulong ClassJobs,
    uint IconID,
    string Name,
    SkillMonitorGroup Group,
    bool IsCustom = false)
{
    public uint ConfigID => ActionID == 0 ? 0x80000000u | StatusID : ActionID;

    public bool StatusOnly => ActionID == 0;

    public bool IsFood => ActionID == 0 && StatusID == 48;

    public bool AppliesTo(uint classJobID) =>
        classJobID < 64 && (ClassJobs & (1UL << (int)classJobID)) != 0;
}

public enum SkillMonitorGroup
{
    Tank,
    Healer,
    Dps,
    General
}

internal static class SkillMonitorDefinitions
{
    private const ulong Tanks = (1UL << 19) | (1UL << 21) | (1UL << 32) | (1UL << 37);
    private const ulong Healers = (1UL << 24) | (1UL << 28) | (1UL << 33) | (1UL << 40);
    private const ulong Melee = (1UL << 20) | (1UL << 22) | (1UL << 30) | (1UL << 34) | (1UL << 39) | (1UL << 41);
    private const ulong Ranged = (1UL << 23) | (1UL << 31) | (1UL << 38);
    private const ulong Casters = (1UL << 25) | (1UL << 27) | (1UL << 35) | (1UL << 42);
    private const ulong AllCombat = Tanks | Healers | Melee | Ranged | Casters | (1UL << 36);

    internal static readonly (SkillMonitorGroup Group, uint[] JobIDs)[] JobGroups =
    [
        (SkillMonitorGroup.Tank, [19, 21, 32, 37]),
        (SkillMonitorGroup.Healer, [33, 24, 40, 28]),
        (SkillMonitorGroup.Dps, [20, 22, 30, 34, 39, 41, 23, 31, 38, 25, 27, 35, 42])
    ];

    private static readonly RawDefinition[] Defaults =
    [
        new(7531, 1191, 90, 20, Tanks, SkillMonitorGroup.Tank),
        new(7535, 1193, 60, 15, Tanks, SkillMonitorGroup.Tank),
        new(7548, 1209, 120, 6, Tanks, SkillMonitorGroup.Tank),
        new(30, 82, 420, 10, 1UL << 19, SkillMonitorGroup.Tank),
        new(36920, 3829, 120, 15, 1UL << 19, SkillMonitorGroup.Tank),
        new(25746, 2674, 5, 8, 1UL << 19, SkillMonitorGroup.Tank),
        new(7382, 1174, 10, 8, 1UL << 19, SkillMonitorGroup.Tank),
        new(3540, 726, 90, 30, 1UL << 19, SkillMonitorGroup.Tank),
        new(7385, 1175, 120, 18, 1UL << 19, SkillMonitorGroup.Tank),
        new(22, 77, 90, 10, 1UL << 19, SkillMonitorGroup.Tank),
        new(43, 409, 240, 10, 1UL << 21, SkillMonitorGroup.Tank),
        new(36923, 3832, 120, 15, 1UL << 21, SkillMonitorGroup.Tank),
        new(40, 87, 90, 10, 1UL << 21, SkillMonitorGroup.Tank),
        new(25751, 2678, 25, 8, 1UL << 21, SkillMonitorGroup.Tank),
        new(16464, 1857, 25, 8, 1UL << 21, SkillMonitorGroup.Tank),
        new(7388, 1457, 90, 30, 1UL << 21, SkillMonitorGroup.Tank),
        new(3638, 810, 300, 10, 1UL << 32, SkillMonitorGroup.Tank),
        new(36927, 3835, 120, 15, 1UL << 32, SkillMonitorGroup.Tank),
        new(3634, 746, 60, 10, 1UL << 32, SkillMonitorGroup.Tank),
        new(7393, 1178, 15, 7, 1UL << 32, SkillMonitorGroup.Tank),
        new(25754, 2682, 60, 10, 1UL << 32, SkillMonitorGroup.Tank),
        new(16471, 1894, 90, 15, 1UL << 32, SkillMonitorGroup.Tank),
        new(16152, 1836, 360, 10, 1UL << 37, SkillMonitorGroup.Tank),
        new(36935, 3838, 120, 15, 1UL << 37, SkillMonitorGroup.Tank),
        new(16140, 1832, 90, 20, 1UL << 37, SkillMonitorGroup.Tank),
        new(25758, 2683, 25, 8, 1UL << 37, SkillMonitorGroup.Tank),
        new(16160, 1839, 90, 15, 1UL << 37, SkillMonitorGroup.Tank),

        new(16536, 1872, 120, 20, 1UL << 24, SkillMonitorGroup.Healer),
        new(7433, 0, 0, 1, 1UL << 24, SkillMonitorGroup.Healer),
        new(3569, 1911, 0, 1, 1UL << 24, SkillMonitorGroup.Healer),
        new(25861, 2708, 60, 8, 1UL << 24, SkillMonitorGroup.Healer),
        new(188, 299, 30, 15, 1UL << 28, SkillMonitorGroup.Healer),
        new(25868, 2711, 120, 20, 1UL << 28, SkillMonitorGroup.Healer),
        new(37014, 3884, 0, 1, 1UL << 28, SkillMonitorGroup.Healer),
        new(16545, 0, 0, 1, 1UL << 28, SkillMonitorGroup.Healer),
        new(25867, 2710, 60, 10, 1UL << 28, SkillMonitorGroup.Healer),
        new(16559, 1892, 120, 20, 1UL << 33, SkillMonitorGroup.Healer),
        new(25874, 2718, 0, 1, 1UL << 33, SkillMonitorGroup.Healer),
        new(3613, 849, 60, 18, 1UL << 33, SkillMonitorGroup.Healer),
        new(25873, 2717, 60, 8, 1UL << 33, SkillMonitorGroup.Healer),
        new(24298, 2618, 30, 15, 1UL << 40, SkillMonitorGroup.Healer),
        new(24310, 3003, 120, 20, 1UL << 40, SkillMonitorGroup.Healer),
        new(37035, 3898, 0, 1, 1UL << 40, SkillMonitorGroup.Healer),
        new(24311, 2613, 120, 15, 1UL << 40, SkillMonitorGroup.Healer),
        new(24303, 2619, 45, 15, 1UL << 40, SkillMonitorGroup.Healer),
        new(7561, 167, 0, 1, Healers, SkillMonitorGroup.Healer),

        new(7549, 1195, 90, 10, Melee, SkillMonitorGroup.Dps),
        new(7560, 1203, 90, 10, Casters, SkillMonitorGroup.Dps),
        new(7394, 1179, 120, 10, 1UL << 20, SkillMonitorGroup.Dps),
        new(65, 102, 120, 15, 1UL << 20, SkillMonitorGroup.Dps),
        new(2241, 488, 120, 20, 1UL << 30, SkillMonitorGroup.Dps),
        new(36962, 3853, 15, 4, 1UL << 34, SkillMonitorGroup.Dps),
        new(24404, 2598, 30, 5, 1UL << 39, SkillMonitorGroup.Dps),
        new(7405, 1934, 120, 15, 1UL << 23, SkillMonitorGroup.Dps),
        new(7408, 1202, 120, 15, 1UL << 23, SkillMonitorGroup.Dps),
        new(16889, 1951, 120, 15, 1UL << 31, SkillMonitorGroup.Dps),
        new(2887, 860, 120, 10, 1UL << 31, SkillMonitorGroup.Dps),
        new(16012, 1826, 120, 15, 1UL << 38, SkillMonitorGroup.Dps),
        new(16014, 1827, 120, 15, 1UL << 38, SkillMonitorGroup.Dps),
        new(16015, 0, 60, 1, 1UL << 38, SkillMonitorGroup.Dps),
        new(157, 168, 120, 20, 1UL << 25, SkillMonitorGroup.Dps),
        new(25799, 2702, 60, 30, 1UL << 27, SkillMonitorGroup.Dps),
        new(25857, 2707, 120, 10, 1UL << 35, SkillMonitorGroup.Dps),
        new(34685, 3686, 120, 10, 1UL << 42, SkillMonitorGroup.Dps),
        new(34686, 3687, 1, 10, 1UL << 42, SkillMonitorGroup.Dps),

        new(3, 50, 60, 10, AllCombat, SkillMonitorGroup.General),
        new(0, 48, 0, 0, AllCombat, SkillMonitorGroup.General)
    ];

    public static SkillMonitorDefinition[] Create(IReadOnlyList<SkillMonitorCustomActionConfig>? customActions = null)
    {
        var definitions = new List<SkillMonitorDefinition>(Defaults.Length + (customActions?.Count ?? 0));
        for (var index = 0; index < Defaults.Length; index++)
        {
            var raw = Defaults[index];
            uint iconID;
            string name;
            var cooldownMilliseconds = raw.CooldownSeconds * 1_000;
            if (raw.ActionID != 0 && LuminaGetter.TryGetRow<LuminaAction>(raw.ActionID, out var action))
            {
                iconID = action.Icon;
                name = action.Name.ToString();
                if (cooldownMilliseconds == 0)
                {
                    cooldownMilliseconds = action.Recast100ms * 100;
                }
            }
            else if (raw.StatusID != 0 && LuminaGetter.TryGetRow<LuminaStatus>(raw.StatusID, out var status))
            {
                iconID = status.Icon;
                name = status.Name.ToString();
            }
            else
            {
                iconID = 0;
                name = (raw.ActionID == 0 ? raw.StatusID : raw.ActionID).ToString();
            }

            definitions.Add(new(
                raw.ActionID,
                raw.StatusID,
                cooldownMilliseconds,
                raw.ActiveSeconds * 1_000,
                raw.ClassJobs,
                iconID,
                name,
                raw.Group));
        }

        if (customActions is not null)
        {
            for (var index = 0; index < customActions.Count; index++)
            {
                if (TryCreateCustom(customActions[index], out var definition))
                {
                    definitions.Add(definition);
                }
            }
        }

        return definitions.ToArray();
    }

    public static bool TryCreateCustom(
        SkillMonitorCustomActionConfig config,
        out SkillMonitorDefinition definition)
    {
        definition = default;
        if (config.ActionID == 0 ||
            !LuminaGetter.TryGetRow<LuminaAction>(config.ActionID, out var action) ||
            action.Icon == 0 ||
            action.Recast100ms == 0 ||
            string.IsNullOrWhiteSpace(action.Name.ToString()))
        {
            return false;
        }

        ulong classJobs = 0;
        if (action.ClassJobCategory.IsValid)
        {
            for (uint classJobID = 1; classJobID < 64; classJobID++)
            {
                if (action.ClassJobCategory.Value.IsClassJobIn(classJobID))
                {
                    classJobs |= 1UL << (int)classJobID;
                }
            }
        }

        classJobs &= AllCombat;
        if (classJobs == 0)
        {
            classJobs = AllCombat;
        }

        definition = new(
            config.ActionID,
            0,
            action.Recast100ms * 100,
            1_000,
            classJobs,
            action.Icon,
            action.Name.ToString(),
            GetGroup(classJobs),
            true);
        return true;
    }

    private static SkillMonitorGroup GetGroup(ulong classJobs)
    {
        var isTank = (classJobs & Tanks) != 0;
        var isHealer = (classJobs & Healers) != 0;
        var isDPS = (classJobs & (Melee | Ranged | Casters)) != 0;
        if ((isTank ? 1 : 0) + (isHealer ? 1 : 0) + (isDPS ? 1 : 0) != 1)
        {
            return SkillMonitorGroup.General;
        }

        return isTank ? SkillMonitorGroup.Tank : isHealer ? SkillMonitorGroup.Healer : SkillMonitorGroup.Dps;
    }

    private readonly record struct RawDefinition(
        uint ActionID,
        uint StatusID,
        int CooldownSeconds,
        int ActiveSeconds,
        ulong ClassJobs,
        SkillMonitorGroup Group);
}
