namespace OmniToolbox.TreePublic;

internal readonly record struct CombatLevelModifier(int Main, int Sub, int Div);

internal static class CombatStatFormula
{
    private static readonly CombatLevelModifier[] LevelTable =
    [
        default,
        new(20, 56, 56), new(21, 57, 57), new(22, 60, 60), new(24, 62, 62), new(26, 65, 65),
        new(27, 68, 68), new(29, 70, 70), new(31, 73, 73), new(33, 76, 76), new(35, 78, 78),
        new(36, 82, 82), new(38, 85, 85), new(41, 89, 89), new(44, 93, 93), new(46, 96, 96),
        new(49, 100, 100), new(52, 104, 104), new(54, 109, 109), new(57, 113, 113), new(60, 116, 116),
        new(63, 122, 122), new(67, 127, 127), new(71, 133, 133), new(74, 138, 138), new(78, 144, 144),
        new(81, 150, 150), new(85, 155, 155), new(89, 162, 162), new(92, 168, 168), new(97, 173, 173),
        new(101, 181, 181), new(106, 188, 188), new(110, 194, 194), new(115, 202, 202), new(119, 209, 209),
        new(124, 215, 215), new(128, 223, 223), new(134, 229, 229), new(139, 236, 236), new(144, 244, 244),
        new(150, 253, 253), new(155, 263, 263), new(161, 272, 272), new(166, 283, 283), new(171, 292, 292),
        new(177, 302, 302), new(183, 311, 311), new(189, 322, 322), new(196, 331, 331), new(202, 341, 341),
        new(204, 342, 366), new(205, 344, 392), new(207, 345, 418), new(209, 346, 444), new(210, 347, 470),
        new(212, 349, 496), new(214, 350, 522), new(215, 351, 548), new(217, 352, 574), new(218, 354, 600),
        new(224, 355, 630), new(228, 356, 660), new(236, 357, 690), new(244, 358, 720), new(252, 359, 750),
        new(260, 360, 780), new(268, 361, 810), new(276, 362, 840), new(284, 363, 870), new(292, 364, 900),
        new(296, 365, 940), new(300, 366, 980), new(305, 367, 1020), new(310, 368, 1060), new(315, 370, 1100),
        new(320, 372, 1140), new(325, 374, 1180), new(330, 376, 1220), new(335, 378, 1260), new(340, 380, 1300),
        new(345, 382, 1360), new(350, 384, 1420), new(355, 386, 1480), new(360, 388, 1540), new(365, 390, 1600),
        new(370, 392, 1660), new(375, 394, 1720), new(380, 396, 1780), new(385, 398, 1840), new(390, 400, 1900),
        new(395, 402, 1988), new(400, 404, 2076), new(405, 406, 2164), new(410, 408, 2252), new(415, 410, 2340),
        new(420, 412, 2428), new(425, 414, 2516), new(430, 416, 2604), new(435, 418, 2692), new(440, 420, 2780)
    ];

    internal static CombatLevelModifier GetLevelModifier(int level) => LevelTable[Math.Clamp(level, 1, 100)];

    internal static double CalculateGcd(int level, int speed, int speedModifier) =>
        CalculateGcdValue(250, speed, GetLevelModifier(level), speedModifier) / 100d;

    internal static bool IsCaster(int jobID) =>
        jobID is 6 or 7 or 24 or 25 or 26 or 27 or 28 or 33 or 35 or 36 or 40 or 42;

    internal static int GetAttackModifier(int jobID) => jobID switch
    {
        1 => 95,
        2 => 100,
        3 => 100,
        4 => 105,
        5 => 105,
        6 => 105,
        7 => 105,
        19 => 100,
        20 => 110,
        21 => 105,
        22 => 115,
        23 => 115,
        24 => 115,
        25 => 115,
        26 => 105,
        27 => 115,
        28 => 115,
        29 => 100,
        30 => 110,
        31 => 115,
        32 => 105,
        33 => 115,
        34 => 112,
        35 => 115,
        36 => 115,
        37 => 100,
        38 => 115,
        39 => 115,
        40 => 115,
        41 => 110,
        42 => 115,
        _ => 0
    };

    internal static double GetTraitModifier(int jobID, int level)
    {
        if (jobID == 36)
        {
            return level switch
            {
                >= 50 => 1.5d,
                >= 40 => 1.4d,
                >= 30 => 1.3d,
                >= 20 => 1.2d,
                >= 10 => 1.1d,
                _ => 1d
            };
        }

        if (IsCaster(jobID))
        {
            return level switch
            {
                >= 40 => 1.3d,
                >= 20 => 1.1d,
                _ => 1d
            };
        }

        return (jobID, level) switch
        {
            (5 or 23 or 31, >= 40) => 1.2d,
            (5 or 23 or 31, >= 20) => 1.1d,
            (38, >= 60) => 1.2d,
            (38, >= 50) => 1.1d,
            _ => 1d
        };
    }

    internal static double GetAttackModifierForLevel(int level) => level switch
    {
        <= 50 => 75d,
        <= 70 => (level - 50) * 2.5d + 75d,
        <= 80 => (level - 70) * 4d + 125d,
        <= 90 => (level - 80) * 3d + 165d,
        _ => (level - 90) * 4.2d + 195d
    };

    internal static double GetTankAttackModifier(int level) => level switch
    {
        <= 80 => level + 35d,
        <= 90 => (level - 80) * 4.1d + 115d,
        _ => (level - 90) * 3.4d + 156d
    };

    private static int CalculateGcdValue(int gcd, int speed, CombatLevelModifier modifier, int speedModifier) =>
        (int)Math.Floor(
            Math.Floor((1000d + Math.Ceiling(130d * (modifier.Sub - speed) / modifier.Div)) * gcd / 100d)
            * (100d - speedModifier)
            / 1000d);
}
