using System.Globalization;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

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
    string? AbbreviationKey);

internal readonly record struct BetterCharacterPanelCalculationInput(
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
    BetterCharacterPanelGcdModifier? GcdModifier);

internal readonly record struct BetterCharacterPanelSpeedCalculation(
    BetterCharacterPanelStatInfo SpeedBonus,
    BetterCharacterPanelStatInfo MainGcd,
    BetterCharacterPanelAlternateGcd? AlternateGcd,
    BetterCharacterPanelStatInfo AlternateGcdInfo,
    BetterCharacterPanelGcdModifier? Modifier);

internal readonly record struct BetterCharacterPanelStats(
    int JobID,
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
        SpellSpeed = 46,
        Haste = 47
    }

    internal static double CalculateGcd(int level, int speed, int speedModifier) =>
        CombatStatFormula.CalculateGcd(level, speed, speedModifier);

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

        return Calculate(new BetterCharacterPanelCalculationInput(
            jobID,
            level,
            GetAttribute(playerState, IsCaster(jobID) ? Attribute.AttackMagicPotency : Attribute.AttackPower),
            GetAttribute(playerState, Attribute.CriticalHit),
            GetAttribute(playerState, Attribute.Determination),
            GetAttribute(playerState, Attribute.DirectHit),
            GetAttribute(playerState, IsCaster(jobID) ? Attribute.SpellSpeed : Attribute.SkillSpeed),
            GetAttribute(playerState, Attribute.Piety),
            GetAttribute(playerState, Attribute.Tenacity),
            GetWeaponBaseDamage(playerState, jobID),
            GetAttribute(playerState, Attribute.Defense),
            GetAttribute(playerState, Attribute.MagicDefense),
            GetGcdModifier(jobID, level, GetAttribute(playerState, Attribute.Haste))));
    }

    internal static BetterCharacterPanelStats Calculate(BetterCharacterPanelCalculationInput input)
    {
        var level = Math.Clamp(input.Level, 1, 100);
        var modifier = CombatStatFormula.GetLevelModifier(level);
        var critical = input.CriticalHit;
        var criticalRate = CalculateTier(critical, modifier.Sub, modifier.Div, 200d, 50d, 1000d);
        var criticalDamage = CalculateTier(critical, modifier.Sub, modifier.Div, 200d, 1400d, 1000d);
        var directHit = CalculateTier(
            input.DirectHit,
            modifier.Sub,
            modifier.Div,
            550d,
            0d,
            1000d);
        var determination = CalculateTier(
            input.Determination,
            modifier.Main,
            modifier.Div,
            140d,
            0d,
            1000d);
        var tenacityMitigation = CalculateTier(
            input.Tenacity,
            modifier.Sub,
            modifier.Div,
            200d,
            0d,
            1000d);
        var tenacityDamage = CalculateTier(
            input.Tenacity,
            modifier.Sub,
            modifier.Div,
            112d,
            0d,
            1000d);

        return new(
            input.JobID,
            level,
            0,
            0,
            criticalRate,
            criticalDamage,
            directHit,
            determination,
            CalculateDefense(input.Defense, modifier),
            CalculateDefense(input.MagicDefense, modifier),
            CalculateSpeed(
                input.Speed,
                modifier,
                GetAlternateGcd(input.JobID, level),
                input.GcdModifier),
            tenacityMitigation,
            tenacityDamage,
            CalculatePiety(input.Piety, modifier),
            CalculateExpectedOutput(
                input.JobID,
                level,
                input.AttackPower,
                input.WeaponDamage,
                determination.DisplayValue,
                criticalDamage.DisplayValue,
                criticalRate.DisplayValue,
                directHit.DisplayValue,
                UsesTenacity(input.JobID) ? tenacityDamage.DisplayValue : 0d,
                modifier));
    }

    public static bool IsCrafter(int jobID) => jobID is >= 8 and <= 15;

    public static bool IsGatherer(int jobID) => jobID is >= 16 and <= 18;

    public static bool UsesMind(int jobID) => jobID is 6 or 24 or 28 or 33 or 40;

    public static bool UsesTenacity(int jobID) => jobID is 1 or 3 or 19 or 21 or 32 or 37;

    public static bool UsesDexterity(int jobID) => jobID is 5 or 23 or 29 or 30 or 31 or 38 or 41;

    public static bool IsCaster(int jobID) => CombatStatFormula.IsCaster(jobID);

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
            BetterCharacterPanelTooltip.Speed => BuildSpeedTooltip(stats.Speed, IsCaster(stats.JobID)),
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

    private static BetterCharacterPanelStatInfo CalculateDefense(int defense, CombatLevelModifier modifier)
    {
        var displayValue = Math.Floor(15d * defense / modifier.Div) / 100d;
        var previousTier = (int)Math.Ceiling(displayValue * 100d * modifier.Div / 15d);
        var nextTier = (int)Math.Ceiling((displayValue + 0.01d) * 100d * modifier.Div / 15d);
        return new(displayValue, defense, previousTier, Math.Max(defense + 1, nextTier), modifier.Div / 15d);
    }

    private static BetterCharacterPanelStatInfo CalculatePiety(int piety, CombatLevelModifier modifier)
    {
        var extraMp = Math.Floor(150d * (piety - modifier.Main) / modifier.Div);
        var previousTier = (int)Math.Ceiling(extraMp * modifier.Div / 150d + modifier.Main);
        var nextTier = (int)Math.Ceiling((extraMp + 1d) * modifier.Div / 150d + modifier.Main);
        return new(extraMp + 200d, piety, previousTier, Math.Max(piety + 1, nextTier), modifier.Div / 150d);
    }

    private static BetterCharacterPanelSpeedCalculation CalculateSpeed(
        int speed,
        CombatLevelModifier modifier,
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
        int jobID,
        int level,
        int attackPower,
        int weaponBaseDamage,
        double determination,
        double criticalMultiplier,
        double criticalRate,
        double directRate,
        double tenacity,
        CombatLevelModifier modifier) => CalculateExpectedOutput(
        level,
        attackPower,
        weaponBaseDamage,
        CombatStatFormula.GetAttackModifier(jobID),
        UsesTenacity(jobID),
        IsCaster(jobID),
        CombatStatFormula.GetTraitModifier(jobID, level),
        determination,
        criticalMultiplier,
        criticalRate,
        directRate,
        tenacity,
        modifier);

    internal static BetterCharacterPanelExpectedOutput CalculateExpectedOutput(
        int level,
        int attackPower,
        int weaponBaseDamage,
        int attackModifier,
        bool usesTenacity,
        bool isCaster,
        double traitDamageMultiplier,
        double determination,
        double criticalMultiplier,
        double criticalRate,
        double directRate,
        double tenacity,
        CombatLevelModifier modifier)
    {
        if (weaponBaseDamage <= 0)
        {
            return default;
        }

        var weaponDamage = Math.Floor(
            weaponBaseDamage + modifier.Main * attackModifier / 1000d) / 100d;
        var levelAttackModifier = usesTenacity
            ? CombatStatFormula.GetTankAttackModifier(level)
            : CombatStatFormula.GetAttackModifierForLevel(level);
        var attack = Math.Floor(100d + levelAttackModifier * (attackPower - modifier.Main) / modifier.Main) / 100d;
        var baseMultiplier = Math.Floor(100d * attack * weaponDamage);
        var withDetermination = Math.Floor(baseMultiplier * (1d + determination));
        var withTenacity = Math.Floor(withDetermination * (1d + tenacity));
        var normalDamage = Math.Floor(withTenacity * traitDamageMultiplier);
        var averageDamage = Math.Floor(
            Math.Floor(normalDamage * (1d + (criticalMultiplier - 1d) * criticalRate))
            * (1d + directRate * 0.25d));
        var criticalDamage = Math.Floor(normalDamage * criticalMultiplier);

        var healPotency = Math.Floor(
            100d + GetHealModifier(level) * (attackPower - modifier.Main) / modifier.Main) / 100d;
        var healBaseMultiplier = Math.Floor(100d * healPotency * weaponDamage);
        var healWithDetermination = Math.Floor(healBaseMultiplier * (1d + determination));
        var healWithTenacity = Math.Floor(healWithDetermination * (1d + tenacity));
        var normalHeal = Math.Floor(healWithTenacity * (isCaster ? traitDamageMultiplier : 1d));
        var averageHeal = Math.Floor(normalHeal * (1d + (criticalMultiplier - 1d) * criticalRate));
        return new(
            averageDamage,
            normalDamage,
            criticalDamage,
            averageHeal,
            normalHeal,
            Math.Floor(normalHeal * criticalMultiplier));
    }

    private static int GetWeaponBaseDamage(PlayerState* playerState, int jobID)
    {
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
        {
            return 0;
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

        return weaponBaseDamage;
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

    private static BetterCharacterPanelGcdModifier? GetGcdModifier(int jobID, int level, int haste)
    {
        // 星空彩绘只影响星空领域内支持的技能。
        if (jobID == 42
            && level >= 82
            && LocalPlayerState.HasStatus(3688, out _)
            && LocalPlayerState.HasStatus(3689, out _))
        {
            return new(25, "Feature.BetterCharacterPanel.GcdModifier.AstralPrism");
        }

        if (haste == 100)
        {
            return null;
        }

        var abbreviationKey = (jobID, level) switch
        {
            (34, >= 18) when LocalPlayerState.HasStatus(1299, out _) =>
                "Feature.BetterCharacterPanel.GcdModifier.Shifu",
            (30, >= 45) => "Feature.BetterCharacterPanel.GcdModifier.Huton",
            (20 or 2, >= 1) => "Feature.BetterCharacterPanel.GcdModifier.GreasedLightning",
            (24, >= 30) when LocalPlayerState.HasStatus(157, out _) =>
                "Feature.BetterCharacterPanel.GcdModifier.PresenceOfMind",
            (23, >= 40) when LocalPlayerState.HasStatus(2214, out _)
                                  || LocalPlayerState.HasStatus(1932, out _) =>
                "Feature.BetterCharacterPanel.GcdModifier.ArmysPaeon",
            (25, >= 52) when LocalPlayerState.HasStatus(738, out _) =>
                "Feature.BetterCharacterPanel.GcdModifier.LeyLines",
            (41, >= 65) when LocalPlayerState.HasStatus(3669, out _) =>
                "Feature.BetterCharacterPanel.GcdModifier.Swiftscaled",
            _ => null
        };
        return new(100 - haste, abbreviationKey);
    }

    private static BetterCharacterPanelAlternateGcd? GetAlternateGcd(int jobID, int level) =>
        (jobID, level) switch
        {
            (25, >= 35) => new(350, "Feature.BetterCharacterPanel.AlternateGcd.FireIII"),
            (27 or 5, >= 6) => new(300, "Feature.BetterCharacterPanel.AlternateGcd.RubyRite"),
            (42, >= 60) => new(330, "Feature.BetterCharacterPanel.AlternateGcd.Frostbite"),
            _ => null
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
        var modifier = speed.Modifier?.AbbreviationKey is { } abbreviationKey
            ? Format(
                "Feature.BetterCharacterPanel.Tooltip.Speed.Modifier",
                OmniLoc.Get(abbreviationKey))
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
        rented.Builder.Append("\n").Append(Format(
            bodyKey,
            OmniNumberFormatter.Format((long)Math.Round(normal)),
            OmniNumberFormatter.Format((long)Math.Round(critical)),
            OmniNumberFormatter.Format((long)Math.Round(average))));
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
