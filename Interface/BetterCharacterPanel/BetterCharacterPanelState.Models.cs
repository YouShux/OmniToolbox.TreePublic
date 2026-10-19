namespace OmniToolbox.TreePublic;

internal static unsafe partial class BetterCharacterPanelState
{
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

    internal readonly record struct Stats(
        int JobID,
        int Level,
        int CraftingPoints,
        int GatheringPoints,
        StatInfo CriticalRate,
        StatInfo CriticalDamage,
        StatInfo DirectHit,
        StatInfo Determination,
        StatInfo PhysicalMitigation,
        StatInfo MagicMitigation,
        SpeedCalculation Speed,
        StatInfo TenacityMitigation,
        StatInfo TenacityDamage,
        StatInfo Piety,
        ExpectedOutput Expected);

    internal readonly record struct StatInfo(
        double DisplayValue,
        int CurrentValue,
        int PrevTier,
        int NextTier,
        double PointsPerTier);

    internal readonly record struct SpeedCalculation(
        StatInfo SpeedBonus,
        StatInfo MainGcd,
        AlternateGcd? AlternateGcd,
        StatInfo AlternateGcdInfo,
        GcdModifier? Modifier);

    internal readonly record struct GcdModifier(
        int Modifier,
        string? AbbreviationKey);

    internal readonly record struct AlternateGcd(int Gcd, string NameKey);

    internal readonly record struct ExpectedOutput(
        double AverageDamage,
        double NormalDamage,
        double CriticalDamage,
        double AverageHeal,
        double NormalHeal,
        double CriticalHeal);

    internal readonly record struct CombatLevelModifier(int Main, int Sub, int Div);

    internal readonly record struct CalculationInput(
        int JobID,
        int Level,
        int AttackPower,
        int CriticalHit,
        int Determination,
        int DirectHit,
        int Speed,
        int Piety,
        int Tenacity,
        int WeaponDamage,
        int Defense,
        int MagicDefense,
        GcdModifier? GcdModifier);
}
