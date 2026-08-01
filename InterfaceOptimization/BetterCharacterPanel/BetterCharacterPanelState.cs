using System.Globalization;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

internal enum BetterCharacterPanelTooltip
{
    MainStat,
    Vitality,
    ExpectedDamage,
    ExpectedHeal,
    CriticalHit,
    DirectHit,
    Determination,
    Defense,
    MagicDefense,
    Speed,
    Tenacity,
    Piety
}

internal readonly record struct BetterCharacterPanelStatInfo(
    double DisplayValue,
    int CurrentValue,
    int PrevTier,
    int NextTier,
    double PointsPerTier);

internal readonly record struct BetterCharacterPanelExpectedOutput(
    double AverageDamage,
    double NormalDamage,
    double CriticalDamage,
    double AverageHeal,
    double NormalHeal,
    double CriticalHeal);

internal readonly record struct BetterCharacterPanelAlternateGcd(int Gcd, string NameKey);

internal readonly record struct BetterCharacterPanelGcdModifier(
    int Modifier,
    string AbbreviationKey);

internal readonly record struct BetterCharacterPanelSpeedCalculation(
    BetterCharacterPanelStatInfo SpeedBonus,
    BetterCharacterPanelStatInfo MainGcd,
    BetterCharacterPanelAlternateGcd? AlternateGcd,
    BetterCharacterPanelStatInfo AlternateGcdInfo,
    BetterCharacterPanelGcdModifier? Modifier);

internal readonly record struct BetterCharacterPanelStats(
    int jobID,
    int Level,
    int CraftingPoints,
    int GatheringPoints,
    BetterCharacterPanelStatInfo CriticalRate,
    BetterCharacterPanelStatInfo CriticalDamage,
    BetterCharacterPanelStatInfo DirectHit,
    BetterCharacterPanelStatInfo Determination,
    BetterCharacterPanelStatInfo PhysicalMitigation,
    BetterCharacterPanelStatInfo MagicMitigation,
    BetterCharacterPanelSpeedCalculation Speed,
    BetterCharacterPanelStatInfo TenacityMitigation,
    BetterCharacterPanelStatInfo TenacityDamage,
    BetterCharacterPanelStatInfo Piety,
    BetterCharacterPanelExpectedOutput Expected);

internal static unsafe class BetterCharacterPanelState
{
    private const nint CharacterPanelEquipmentDataOffset = 0x2490;
    private const int PhysicalDamageIndex = 20;
    private const int MagicDamageIndex = 21;
    private const int HighQualityDamageBonusIndex = 33;
    private const int EquipmentLevelIndex = 39;
    private const ushort TooltipTitleColor = 8;
    private const ushort TooltipHighlightColor = 33;
    private const ushort TooltipGoodColor = 43;
    private const ushort TooltipWasteColor = 31;

    private enum Attribute
    {
        Piety = 6,
        MaxGp = 10,
        MaxCp = 11,
        Tenacity = 19,
        AttackPower = 20,
        Defense = 21,
        DirectHit = 22,
        MagicDefense = 24,
        CriticalHit = 27,
        AttackMagicPotency = 33,
        Determination = 44,
        SkillSpeed = 45,
        SpellSpeed = 46
    }

    private readonly record struct LevelModifier(int Main, int Sub, int Div);

    private static readonly LevelModifier[] LevelTable =
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

    public static BetterCharacterPanelStats Calculate(PlayerState* playerState)
    {
        var level = Math.Clamp((int)playerState->CurrentLevel, 1, 100);
        var jobID = (int)playerState->CurrentClassJobId;
        if (IsCrafter(jobID) || IsGatherer(jobID))
        {
            return new(
                jobID,
                level,
                GetAttribute(playerState, Attribute.MaxCp),
                GetAttribute(playerState, Attribute.MaxGp),
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default);
        }

        var modifier = LevelTable[level];
        var critical = GetAttribute(playerState, Attribute.CriticalHit);
        var criticalRate = CalculateTier(critical, modifier.Sub, modifier.Div, 200d, 50d, 1000d);
        var criticalDamage = CalculateTier(critical, modifier.Sub, modifier.Div, 200d, 1400d, 1000d);
        var directHit = CalculateTier(
            GetAttribute(playerState, Attribute.DirectHit),
            modifier.Sub,
            modifier.Div,
            550d,
            0d,
            1000d);
        var determination = CalculateTier(
            GetAttribute(playerState, Attribute.Determination),
            modifier.Main,
            modifier.Div,
            140d,
            0d,
            1000d);
        var tenacityMitigation = CalculateTier(
            GetAttribute(playerState, Attribute.Tenacity),
            modifier.Sub,
            modifier.Div,
            200d,
            0d,
            1000d);
        var tenacityDamage = CalculateTier(
            GetAttribute(playerState, Attribute.Tenacity),
            modifier.Sub,
            modifier.Div,
            112d,
            0d,
            1000d);

        return new(
            jobID,
            level,
            GetAttribute(playerState, Attribute.MaxCp),
            GetAttribute(playerState, Attribute.MaxGp),
            criticalRate,
            criticalDamage,
            directHit,
            determination,
            CalculateDefense(GetAttribute(playerState, Attribute.Defense), modifier),
            CalculateDefense(GetAttribute(playerState, Attribute.MagicDefense), modifier),
            CalculateSpeed(
                GetAttribute(playerState, IsCaster(jobID) ? Attribute.SpellSpeed : Attribute.SkillSpeed),
                modifier,
                GetAlternateGcd(jobID, level),
                GetGcdModifier(jobID, level)),
            tenacityMitigation,
            tenacityDamage,
            CalculatePiety(GetAttribute(playerState, Attribute.Piety), modifier),
            CalculateExpectedOutput(
                playerState,
                jobID,
                determination.DisplayValue,
                criticalDamage.DisplayValue,
                criticalRate.DisplayValue,
                directHit.DisplayValue,
                UsesTenacity(jobID) ? tenacityDamage.DisplayValue : 0d,
                modifier));
    }

    public static bool IsCrafter(int jobID) => jobID is >= 8 and <= 15;

    public static bool IsGatherer(int jobID) => jobID is >= 16 and <= 18;

    public static bool UsesMind(int jobID) => jobID is 6 or 24 or 28 or 33 or 40;

    public static bool UsesTenacity(int jobID) => jobID is 1 or 3 or 19 or 21 or 32 or 37;

    public static bool UsesDexterity(int jobID) => jobID is 5 or 23 or 29 or 30 or 31 or 38 or 41;

    public static bool IsCaster(int jobID) =>
        jobID is 6 or 7 or 24 or 25 or 26 or 27 or 28 or 33 or 35 or 36 or 40 or 42;

    public static ReadOnlySeString BuildTooltip(
        BetterCharacterPanelTooltip tooltip,
        in BetterCharacterPanelStats stats) => tooltip switch
    {
        BetterCharacterPanelTooltip.MainStat => OmniLoc.Get("Feature.BetterCharacterPanel.Tooltip.MainStat"),
        BetterCharacterPanelTooltip.Vitality => OmniLoc.Get("Feature.BetterCharacterPanel.Tooltip.Vitality"),
        BetterCharacterPanelTooltip.ExpectedDamage => BuildExpectedTooltip(
            "Feature.BetterCharacterPanel.Tooltip.ExpectedDamage.Title",
            "Feature.BetterCharacterPanel.Tooltip.ExpectedDamage.Body",
            stats.Expected.NormalDamage,
            stats.Expected.CriticalDamage,
            stats.Expected.AverageDamage),
        BetterCharacterPanelTooltip.ExpectedHeal => BuildExpectedTooltip(
            "Feature.BetterCharacterPanel.Tooltip.ExpectedHeal.Title",
            "Feature.BetterCharacterPanel.Tooltip.ExpectedHeal.Body",
            stats.Expected.NormalHeal,
            stats.Expected.CriticalHeal,
            stats.Expected.AverageHeal),
        BetterCharacterPanelTooltip.CriticalHit => BuildTierTooltip(
            "Feature.BetterCharacterPanel.Tooltip.CriticalHit.Title",
            "Feature.BetterCharacterPanel.Tooltip.CriticalHit.Body",
            stats.CriticalRate,
            stats.CriticalRate.PointsPerTier),
        BetterCharacterPanelTooltip.DirectHit => BuildTierTooltip(
            "Feature.BetterCharacterPanel.Tooltip.DirectHit.Title",
            "Feature.BetterCharacterPanel.Tooltip.DirectHit.Body",
            stats.DirectHit,
            stats.DirectHit.PointsPerTier),
        BetterCharacterPanelTooltip.Determination => BuildTierTooltip(
            "Feature.BetterCharacterPanel.Tooltip.Determination.Title",
            "Feature.BetterCharacterPanel.Tooltip.Determination.Body",
            stats.Determination,
            stats.Determination.PointsPerTier),
        BetterCharacterPanelTooltip.Defense => BuildTierTooltip(
            "Feature.BetterCharacterPanel.Tooltip.Defense.Title",
            "Feature.BetterCharacterPanel.Tooltip.Defense.Body",
            stats.PhysicalMitigation,
            stats.PhysicalMitigation.PointsPerTier),
        BetterCharacterPanelTooltip.MagicDefense => BuildTierTooltip(
            "Feature.BetterCharacterPanel.Tooltip.MagicDefense.Title",
            "Feature.BetterCharacterPanel.Tooltip.MagicDefense.Body",
            stats.MagicMitigation,
            stats.MagicMitigation.PointsPerTier),
        BetterCharacterPanelTooltip.Speed => BuildSpeedTooltip(stats.Speed, IsCaster(stats.jobID)),
        BetterCharacterPanelTooltip.Tenacity => BuildTenacityTooltip(stats.TenacityMitigation, stats.TenacityDamage),
        BetterCharacterPanelTooltip.Piety => BuildTierTooltip(
            "Feature.BetterCharacterPanel.Tooltip.Piety.Title",
            "Feature.BetterCharacterPanel.Tooltip.Piety.Body",
            stats.Piety,
            stats.Piety.PointsPerTier),
        _ => throw new ArgumentOutOfRangeException(nameof(tooltip), tooltip, null)
    };

    private static BetterCharacterPanelStatInfo CalculateTier(
        int currentValue,
        int baseValue,
        int div,
        double coefficient,
        double offset,
        double displayScale)
    {
        var displayValue = Math.Floor(coefficient * (currentValue - baseValue) / div + offset) / displayScale;
        var previousTier = (int)Math.Ceiling(
            (displayValue * displayScale - offset - 0.0000001d) * div / coefficient + baseValue);
        var nextTier = (int)Math.Ceiling(
            ((displayValue + 1d / displayScale) * displayScale - offset - 0.0000001d) * div / coefficient + baseValue);
        return new(displayValue, currentValue, previousTier, Math.Max(currentValue + 1, nextTier), div / coefficient);
    }

    private static BetterCharacterPanelStatInfo CalculateDefense(int defense, LevelModifier modifier)
    {
        var displayValue = Math.Floor(15d * defense / modifier.Div) / 100d;
        var previousTier = (int)Math.Ceiling(displayValue * 100d * modifier.Div / 15d);
        var nextTier = (int)Math.Ceiling((displayValue + 0.01d) * 100d * modifier.Div / 15d);
        return new(displayValue, defense, previousTier, Math.Max(defense + 1, nextTier), modifier.Div / 15d);
    }

    private static BetterCharacterPanelStatInfo CalculatePiety(int piety, LevelModifier modifier)
    {
        var extraMp = Math.Floor(150d * (piety - modifier.Main) / modifier.Div);
        var previousTier = (int)Math.Ceiling(extraMp * modifier.Div / 150d + modifier.Main);
        var nextTier = (int)Math.Ceiling((extraMp + 1d) * modifier.Div / 150d + modifier.Main);
        return new(extraMp + 200d, piety, previousTier, Math.Max(piety + 1, nextTier), modifier.Div / 150d);
    }

    private static BetterCharacterPanelSpeedCalculation CalculateSpeed(
        int speed,
        LevelModifier modifier,
        BetterCharacterPanelAlternateGcd? alternateGcd,
        BetterCharacterPanelGcdModifier? gcdModifier)
    {
        var speedModifier = gcdModifier?.Modifier ?? 0;

        int SpeedValue(int gcd, double value) =>
            (int)Math.Floor(
                Math.Floor((1000d + Math.Ceiling(130d * (modifier.Sub - value) / modifier.Div)) * gcd / 100d)
                * (100d - speedModifier)
                / 1000d);

        int TierValue(double currentGcd, int gcd) =>
            -(int)Math.Floor(
                Math.Floor(
                    Math.Ceiling(currentGcd * 100d * 1000d / (100d - speedModifier) - 0.01d)
                    * 100d
                    / gcd
                    - 1000.01d)
                * modifier.Div
                / 130d
                - modifier.Sub);

        var speedBonus = CalculateTier(speed, modifier.Sub, modifier.Div, 130d, 0d, 1000d);
        var baseGcd = speedModifier == 0 ? 250 : SpeedValue(250, modifier.Sub);
        var mainGcdValue = SpeedValue(250, speed) / 100d;
        var mainGcd = new BetterCharacterPanelStatInfo(
            mainGcdValue,
            speed,
            mainGcdValue < baseGcd / 100d ? TierValue(mainGcdValue + 0.01d, 250) : modifier.Sub,
            Math.Max(speed + 1, TierValue(mainGcdValue, 250)),
            modifier.Div / 130d * 1000d / baseGcd);

        var alternateInfo = default(BetterCharacterPanelStatInfo);
        if (alternateGcd is { } alternate)
        {
            var alternateBaseGcd = speedModifier == 0
                ? alternate.Gcd
                : SpeedValue(alternate.Gcd, modifier.Sub);
            var alternateValue = SpeedValue(alternate.Gcd, speed) / 100d;
            alternateInfo = new(
                alternateValue,
                speed,
                alternateValue < alternateBaseGcd / 100d
                    ? TierValue(alternateValue + 0.01d, alternate.Gcd)
                    : modifier.Sub,
                Math.Max(speed + 1, TierValue(alternateValue, alternate.Gcd)),
                modifier.Div / 130d * 1000d / alternateBaseGcd);
        }

        return new(speedBonus, mainGcd, alternateGcd, alternateInfo, gcdModifier);
    }

    private static BetterCharacterPanelExpectedOutput CalculateExpectedOutput(
        PlayerState* playerState,
        int jobID,
        double determination,
        double criticalMultiplier,
        double criticalRate,
        double directRate,
        double tenacity,
        LevelModifier modifier)
    {
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
        {
            return default;
        }

        var equipmentData = (ushort*)((nint)inventoryManager + CharacterPanelEquipmentDataOffset);
        var weaponBaseDamage = equipmentData[IsCaster(jobID) ? MagicDamageIndex : PhysicalDamageIndex]
                               + equipmentData[HighQualityDamageBonusIndex];
        if (GetLevelBasedItemLevel(playerState) is { } syncedItemLevel
            && equipmentData[EquipmentLevelIndex] > playerState->CurrentLevel
            && LuminaGetter.TryGetRow<ItemLevel>(syncedItemLevel, out var itemLevel))
        {
            weaponBaseDamage = Math.Min(weaponBaseDamage, itemLevel.PhysicalDamage);
        }

        var level = Math.Clamp((int)playerState->CurrentLevel, 1, 100);
        var attackPower = GetAttribute(
            playerState,
            IsCaster(jobID) ? Attribute.AttackMagicPotency : Attribute.AttackPower);
        var weaponDamage = Math.Floor(weaponBaseDamage + modifier.Main * GetAttackModifier(jobID) / 1000d) / 100d;
        var levelAttackModifier = UsesTenacity(jobID)
            ? GetTankAttackModifier(level)
            : GetAttackModifierForLevel(level);
        var attack = Math.Floor(100d + levelAttackModifier * (attackPower - modifier.Main) / modifier.Main) / 100d;
        var baseMultiplier = Math.Floor(100d * attack * weaponDamage);
        var withDetermination = Math.Floor(baseMultiplier * (1d + determination));
        var withTenacity = Math.Floor(withDetermination * (1d + tenacity));
        var normalDamage = Math.Floor(withTenacity * GetTraitModifier(jobID, level));
        var averageDamage = Math.Floor(
            Math.Floor(normalDamage * (1d + (criticalMultiplier - 1d) * criticalRate))
            * (1d + directRate * 0.25d));
        var criticalDamage = Math.Floor(normalDamage * criticalMultiplier);

        var healPotency = Math.Floor(
            100d + GetHealModifier(level) * (attackPower - modifier.Main) / modifier.Main) / 100d;
        var healBaseMultiplier = Math.Floor(100d * healPotency * weaponDamage);
        var healWithDetermination = Math.Floor(healBaseMultiplier * (1d + determination));
        var healWithTenacity = Math.Floor(healWithDetermination * (1d + tenacity));
        var normalHeal = Math.Floor(
            healWithTenacity * (IsCaster(jobID) ? GetTraitModifier(jobID, level) : 1d));
        var averageHeal = Math.Floor(normalHeal * (1d + (criticalMultiplier - 1d) * criticalRate));
        return new(
            averageDamage,
            normalDamage,
            criticalDamage,
            averageHeal,
            normalHeal,
            Math.Floor(normalHeal * criticalMultiplier));
    }

    private static uint? GetLevelBasedItemLevel(PlayerState* playerState)
    {
        if (!playerState->IsLevelSynced)
        {
            return null;
        }

        var level = Math.Clamp((int)playerState->CurrentLevel, 1, 100);
        return (uint)(level switch
        {
            100 => 790,
            >= 93 => 660 + (level - 93) * 3,
            >= 91 => 650 + (level - 91) * 5,
            90 => 660,
            >= 83 => 530 + (level - 83) * 3,
            >= 81 => 520 + (level - 81) * 5,
            80 => 530,
            >= 73 => 400 + (level - 73) * 3,
            >= 71 => 390 + (level - 71) * 5,
            70 => 400,
            >= 63 => 270 + (level - 63) * 3,
            >= 61 => 260 + (level - 61) * 5,
            60 => 270,
            >= 53 => 130 + (level - 53) * 3,
            >= 51 => 120 + (level - 51) * 5,
            50 => 130,
            _ => level
        });
    }

    private static BetterCharacterPanelGcdModifier? GetGcdModifier(int jobID, int level) =>
        (jobID, level) switch
        {
            (34, >= 78) => new(13, "Feature.BetterCharacterPanel.GcdModifier.Shifu"),
            (34, >= 18) => new(10, "Feature.BetterCharacterPanel.GcdModifier.Shifu"),
            (30, >= 45) => new(15, "Feature.BetterCharacterPanel.GcdModifier.Huton"),
            (20, >= 76) => new(20, "Feature.BetterCharacterPanel.GcdModifier.GreasedLightning"),
            (20, >= 40) => new(15, "Feature.BetterCharacterPanel.GcdModifier.GreasedLightning"),
            (20 or 2, >= 20) => new(10, "Feature.BetterCharacterPanel.GcdModifier.GreasedLightning"),
            (20 or 2, >= 1) => new(5, "Feature.BetterCharacterPanel.GcdModifier.GreasedLightning"),
            (24, >= 30) => new(20, "Feature.BetterCharacterPanel.GcdModifier.PresenceOfMind"),
            (23, >= 40) => new(16, "Feature.BetterCharacterPanel.GcdModifier.ArmysPaeon"),
            (25, >= 52) => new(15, "Feature.BetterCharacterPanel.GcdModifier.LeyLines"),
            (41, >= 65) => new(15, "Feature.BetterCharacterPanel.GcdModifier.Swiftscaled"),
            (42, >= 82) => new(25, "Feature.BetterCharacterPanel.GcdModifier.AstralPrism"),
            _ => null
        };

    private static BetterCharacterPanelAlternateGcd? GetAlternateGcd(int jobID, int level) =>
        (jobID, level) switch
        {
            (25, >= 35) => new(350, "Feature.BetterCharacterPanel.AlternateGcd.FireIII"),
            (27 or 5, >= 6) => new(300, "Feature.BetterCharacterPanel.AlternateGcd.RubyRite"),
            (42, >= 60) => new(330, "Feature.BetterCharacterPanel.AlternateGcd.Frostbite"),
            _ => null
        };

    private static int GetAttackModifier(int jobID) => jobID switch
    {
        1 => 95, 2 => 100, 3 => 100, 4 => 105, 5 => 105, 6 => 105, 7 => 105,
        19 => 100, 20 => 110, 21 => 105, 22 => 115, 23 => 115, 24 => 115,
        25 => 115, 26 => 105, 27 => 115, 28 => 115, 29 => 100, 30 => 110,
        31 => 115, 32 => 105, 33 => 115, 34 => 112, 35 => 115, 36 => 115,
        37 => 100, 38 => 115, 39 => 115, 40 => 115, 41 => 110, 42 => 115,
        _ => 0
    };

    private static double GetTraitModifier(int jobID, int level)
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

    private static double GetAttackModifierForLevel(int level) => level switch
    {
        <= 50 => 75d,
        <= 70 => (level - 50) * 2.5d + 75d,
        <= 80 => (level - 70) * 4d + 125d,
        <= 90 => (level - 80) * 3d + 165d,
        _ => (level - 90) * 4.2d + 195d
    };

    private static double GetTankAttackModifier(int level) => level switch
    {
        <= 80 => level + 35d,
        <= 90 => (level - 80) * 4.1d + 115d,
        _ => (level - 90) * 3.4d + 156d
    };

    private static double GetHealModifier(int level) => level switch
    {
        < 60 => level * 1.5d + 10d,
        < 70 => (level - 60) * 2d + 100d,
        < 80 => 120d,
        _ => (level - 80) * 2.5d + 120.8d
    };

    private static int GetAttribute(PlayerState* playerState, Attribute attribute) =>
        playerState->Attributes[(int)attribute];

    private static ReadOnlySeString BuildTierTooltip(
        string titleKey,
        string bodyKey,
        BetterCharacterPanelStatInfo info,
        double pointsPerTier)
    {
        using var rented = new RentedSeStringBuilder();
        AppendColored(rented, OmniLoc.Get(titleKey), TooltipTitleColor);
        rented.Builder.Append("\n").Append(Format(bodyKey, pointsPerTier));
        AppendTierLine(rented, info);
        return rented.Builder.ToReadOnlySeString();
    }

    private static ReadOnlySeString BuildTenacityTooltip(
        BetterCharacterPanelStatInfo mitigation,
        BetterCharacterPanelStatInfo damage)
    {
        using var rented = new RentedSeStringBuilder();
        AppendColored(
            rented,
            OmniLoc.Get("Feature.BetterCharacterPanel.Tooltip.Tenacity.Title"),
            TooltipTitleColor);
        rented.Builder
            .Append("\n")
            .Append(Format("Feature.BetterCharacterPanel.Tooltip.Tenacity.Mitigation", mitigation.PointsPerTier));
        AppendTierLine(rented, mitigation);
        rented.Builder.Append("\n").Append(Format(
            "Feature.BetterCharacterPanel.Tooltip.Tenacity.Damage",
            damage.PointsPerTier));
        AppendTierLine(rented, damage);
        return rented.Builder.ToReadOnlySeString();
    }

    private static ReadOnlySeString BuildSpeedTooltip(
        BetterCharacterPanelSpeedCalculation speed,
        bool caster)
    {
        var modifier = speed.Modifier is { } gcdModifier
            ? Format(
                "Feature.BetterCharacterPanel.Tooltip.Speed.Modifier",
                OmniLoc.Get(gcdModifier.AbbreviationKey))
            : string.Empty;

        using var rented = new RentedSeStringBuilder();
        AppendColored(
            rented,
            OmniLoc.Get(caster
                ? "Feature.BetterCharacterPanel.Tooltip.Speed.SpellTitle"
                : "Feature.BetterCharacterPanel.Tooltip.Speed.SkillTitle"),
            TooltipTitleColor);
        rented.Builder.Append("\n").Append(Format(
            "Feature.BetterCharacterPanel.Tooltip.Speed.Body",
            speed.MainGcd.DisplayValue,
            modifier,
            speed.MainGcd.PrevTier,
            Math.Max(0, speed.MainGcd.NextTier - speed.MainGcd.CurrentValue),
            speed.MainGcd.PointsPerTier));
        AppendTierLine(rented, speed.SpeedBonus);
        if (speed.AlternateGcd is { } alternateGcd)
        {
            rented.Builder.Append(Format(
                "Feature.BetterCharacterPanel.Tooltip.Speed.Alternate",
                OmniLoc.Get(alternateGcd.NameKey),
                speed.AlternateGcdInfo.DisplayValue,
                Math.Max(0, speed.AlternateGcdInfo.NextTier - speed.AlternateGcdInfo.CurrentValue)));
        }

        rented.Builder.Append(Format(
            "Feature.BetterCharacterPanel.Tooltip.Speed.DotHot",
            speed.SpeedBonus.PointsPerTier));
        return rented.Builder.ToReadOnlySeString();
    }

    private static ReadOnlySeString BuildExpectedTooltip(
        string titleKey,
        string bodyKey,
        double normal,
        double critical,
        double average)
    {
        using var rented = new RentedSeStringBuilder();
        AppendColored(rented, OmniLoc.Get(titleKey), TooltipTitleColor);
        rented.Builder.Append("\n").Append(Format(bodyKey, normal, critical, average));
        return rented.Builder.ToReadOnlySeString();
    }

    private static void AppendTierLine(
        RentedSeStringBuilder rented,
        BetterCharacterPanelStatInfo info)
    {
        var wasted = Math.Max(0, info.CurrentValue - info.PrevTier);
        rented.Builder.Append("\n").Append(OmniLoc.Get("Feature.BetterCharacterPanel.Tooltip.Tier.Wasted"));
        AppendColored(
            rented,
            wasted.ToString(CultureInfo.CurrentCulture),
            wasted == 0 ? TooltipGoodColor : TooltipWasteColor);
        rented.Builder.Append(OmniLoc.Get("Feature.BetterCharacterPanel.Tooltip.Tier.Next"));
        AppendColored(
            rented,
            Math.Max(0, info.NextTier - info.CurrentValue).ToString(CultureInfo.CurrentCulture),
            TooltipHighlightColor);
        rented.Builder.Append(OmniLoc.Get("Feature.BetterCharacterPanel.Tooltip.Tier.Suffix"));
    }

    private static void AppendColored(RentedSeStringBuilder rented, string text, ushort color)
    {
        rented.Builder.PushColorType(color).Append(text).PopColorType();
    }

    private static string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, OmniLoc.Get(key), values);
}
