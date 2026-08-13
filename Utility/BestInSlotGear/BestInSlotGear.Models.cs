using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using OmniToolbox.Items;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BestInSlotGear
{
    private sealed record CalculationResult(
        string? Error,
        uint JobID,
        string JobName,
        byte Level,
        int ItemLevelLimit,
        List<GearPiece> Items,
        StatBlock Stats,
        double Damage,
        double Gcd,
        FoodResult? Food,
        IReadOnlyList<CalculationResult> JobResults)
    {
        public static CalculationResult Failed(string error) =>
            new(error, 0, string.Empty, 0, 0, [], default, 0, 0, null, []);
    }

    private sealed record GearPiece(
        uint ItemID,
        GearSlot Slot,
        string Name,
        uint IconID,
        uint ItemLevel,
        bool IsHighQuality,
        bool IsUnique,
        bool OccupiesOffHand,
        IReadOnlyList<MateriaDisplay> Materia,
        IReadOnlyList<MateriaSelection?> Melds,
        IReadOnlyList<CustomStatValue>? CustomStats,
        string StatText);

    private sealed record GearOption(
        GearPiece[] Pieces,
        StatBlock Stats,
        CustomStatBudget? CustomStats = null,
        CustomStatSelection? CustomStatSelection = null);
    private sealed record FoodOption(
        uint ItemID,
        string Name,
        uint IconID,
        uint ItemLevel,
        bool IsHighQuality,
        IReadOnlyList<ItemParameterBonus> Bonuses);
    private sealed record FoodResult(
        uint ItemID,
        string Name,
        uint IconID,
        uint ItemLevel,
        bool IsHighQuality,
        string EffectText);
    private readonly record struct ItemDisplay(string Name, uint IconID, uint ItemLevel, bool IsHighQuality);
    private sealed record SearchSolution(GearOption[] Options, StatBlock Stats, double Damage, FoodResult? Food);
    private sealed record GearEvaluation(GearOption Option, FoodEvaluation Food);
    private sealed record CommonJobSearchState(
        JobCalculationPreparation Preparation,
        List<GearOption> Weapons,
        double PersonalBestDamage);
    private sealed record CommonJobSearchContext(
        CommonJobSearchState State,
        StatBlock[] SharedLowerBounds,
        StatBlock[] SharedUpperBounds,
        StatBlock WeaponLowerBound,
        StatBlock WeaponUpperBound,
        List<WeaponSearchCandidate> Weapons,
        List<WeaponSearchNode> WeaponNodes,
        FoodUpperBoundTable FoodUpperBounds,
        DamageFactorTable DamageFactors,
        double[] SharedDamageMultipliers,
        double WeaponDamageMultiplier,
        int MinimumSpeed,
        int MaximumSpeed);
    private sealed record CommonSearchSolution(
        GearOption[] SharedOptions,
        StatBlock SharedStats,
        IReadOnlyDictionary<uint, SearchSolution> JobSolutions);
    private readonly record struct FoodEvaluation(FoodOption? Food, StatBlock Stats, double Damage);
    private readonly record struct DamageUpperBound(double Damage, int Utility);
    private sealed record FoodUpperBoundTable(
        int[] MainStat,
        int[] Vitality,
        int[] CriticalHit,
        int[] Determination,
        int[] DirectHit,
        int[] Speed,
        int[] Piety,
        int[] Tenacity);
    private sealed record DamageFactorTable(
        double WeaponDamageBase,
        double TraitDamageMultiplier,
        double[] MainStat,
        double[] CriticalHit,
        double[] Determination,
        double[] DirectHit,
        double[] Tenacity);
    private readonly record struct WeaponSearchCandidate(
        GearOption Option,
        StatBlock UpperBound,
        DamageUpperBound SearchUpperBound);
    private readonly record struct WeaponSearchNode(
        int Start,
        int Count,
        StatBlock LowerBound,
        StatBlock UpperBound,
        int LeftChild,
        int RightChild);
    private enum DamageFactorKind
    {
        MainStat,
        CriticalHit,
        Determination,
        DirectHit,
        Tenacity,
        WeaponDamage
    }
    private readonly record struct MateriaDisplay(string Name, uint IconID, int Count);
    private readonly record struct MateriaGradeOption(
        uint ParameterID,
        byte Grade,
        int Value,
        uint RequiredItemLevel,
        string Name,
        uint IconID,
        byte AdvancedSlotMask);
    private readonly record struct MateriaSelection(uint ParameterID, byte Grade);
    private readonly record struct CustomStatValue(uint ParameterID, int Value);
    private readonly record struct CustomStatAllocation(uint ParameterID, int BigValue, int SmallValue);
    private readonly record struct CustomStatSelection(uint FirstBigParameterID, uint SecondBigParameterID, uint SmallParameterID);
    private sealed record CustomStatBudget(int Total, int MaxEachStat, CustomStatParameter[] Parameters);
    private readonly record struct CustomStatParameter(uint ParameterID, int EffectiveCap);
    private sealed record CustomAllocationTable(int[] ParameterIndices, double[,] Scores, short[,] Choices);
    private readonly record struct LevelProfile(byte Level, int Main, int Sub, int Div, int Det, int DetTrunc, int AttackPower, int AttackPowerTank);
    private sealed record CalculationInput(
        uint JobID,
        string JobName,
        byte Level,
        int ItemLevelLimit,
        IReadOnlyDictionary<GearSlot, List<GearOption>> OptionsBySlot,
        IReadOnlyList<FoodOption> Foods,
        StatBlock BaseStats,
        JobProfile Job,
        LevelProfile LevelProfile,
        OptimizationTarget OptimizationTarget,
        double TargetGcd,
        IReadOnlyDictionary<uint, JobCalculationPreparation>? CommonJobs);

    private sealed record PreparedCalculationData(
        uint JobID,
        string JobName,
        byte Level,
        int ItemLevelLimit,
        IReadOnlyDictionary<GearSlot, List<GearOption>> OptionsBySlot,
        IReadOnlyList<FoodOption> Foods,
        StatBlock BaseStats,
        JobProfile Job,
        LevelProfile LevelProfile,
        IReadOnlyDictionary<uint, JobCalculationPreparation>? CommonJobs);

    private readonly record struct CalculationRequest(
        uint JobID,
        int SyncItemLevel,
        uint TribeID,
        GearStage GearStage,
        OptimizationTarget OptimizationTarget,
        float TargetGcd)
    {
        public int TargetGcdHundredths => OptimizationTarget == OptimizationTarget.SpecificGcd
            ? (int)Math.Round(TargetGcd * 100f, MidpointRounding.AwayFromZero)
            : 0;

    }

    private readonly record struct CandidateCacheKey(
        uint JobID,
        int SyncItemLevel,
        uint TribeID,
        GearStage GearStage,
        bool PreserveSpeed,
        string GameDataVersion,
        long SourceRevision,
        int SolverVersion)
    {
        public static CandidateCacheKey Create(
            CalculationRequest request,
            string gameDataVersion,
            long sourceRevision) => new(
            request.JobID,
            request.SyncItemLevel,
            request.TribeID,
            request.GearStage,
            request.OptimizationTarget == OptimizationTarget.SpecificGcd,
            gameDataVersion,
            request.GearStage == GearStage.Opening ? sourceRevision : 0,
            BestInSlotGear.SolverVersion);
    }

    private sealed record CandidateCacheEntry(CandidateCacheKey Key, PreparedCalculationData Data);

    private sealed class ResultCacheDocument
    {
        public int SchemaVersion { get; set; }
        public int SolverVersion { get; set; }
        public string GameDataVersion { get; set; } = string.Empty;
        public List<CachedResultEntry> Results { get; set; } = [];
    }

    private sealed class CachedResultEntry
    {
        public uint JobID { get; set; }
        public int SyncItemLevel { get; set; }
        public uint TribeID { get; set; }
        public int GearStage { get; set; }
        public int OptimizationTarget { get; set; }
        public int TargetGcdHundredths { get; set; }
        public long SourceRevision { get; set; }
        public DateTime LastUsedUTC { get; set; }
        public BestInSlotGearConfig.SavedCalculation Calculation { get; set; } = new();

        public bool Matches(CalculationRequest request) =>
            JobID == request.JobID &&
            SyncItemLevel == request.SyncItemLevel &&
            TribeID == request.TribeID &&
            GearStage == (int)request.GearStage &&
            OptimizationTarget == (int)request.OptimizationTarget &&
            TargetGcdHundredths == request.TargetGcdHundredths;

        public static CachedResultEntry Create(
            CalculationRequest request,
            CalculationResult calculation,
            long sourceRevision) => new()
        {
            JobID = request.JobID,
            SyncItemLevel = request.SyncItemLevel,
            TribeID = request.TribeID,
            GearStage = (int)request.GearStage,
            OptimizationTarget = (int)request.OptimizationTarget,
            TargetGcdHundredths = request.TargetGcdHundredths,
            SourceRevision = request.GearStage == BestInSlotGear.GearStage.Opening ? sourceRevision : 0,
            LastUsedUTC = DateTime.UtcNow,
            Calculation = SaveCalculation(calculation)
        };
    }

    private sealed class CalculationPreparation(
        uint jobID,
        string jobName,
        byte level,
        int itemLevelLimit,
        ItemLevel syncItemLevelStats,
        List<Item> candidates,
        uint[] meldParameterIDs,
        IReadOnlyDictionary<uint, List<MateriaGradeOption>> materiaCatalog,
        IReadOnlyList<FoodOption> foods,
        StatBlock baseStats,
        JobProfile job,
        LevelProfile levelProfile,
        CalculationRequest request,
        CandidateCacheKey cacheKey,
        IReadOnlyDictionary<uint, JobCalculationPreparation>? commonJobs)
    {
        public uint JobID { get; } = jobID;
        public string JobName { get; } = jobName;
        public byte Level { get; } = level;
        public int ItemLevelLimit { get; } = itemLevelLimit;
        public ItemLevel SyncItemLevelStats { get; } = syncItemLevelStats;
        public List<Item> Candidates { get; } = candidates;
        public uint[] MeldParameterIDs { get; } = meldParameterIDs;
        public IReadOnlyDictionary<uint, List<MateriaGradeOption>> MateriaCatalog { get; } = materiaCatalog;
        public IReadOnlyList<FoodOption> Foods { get; } = foods;
        public StatBlock BaseStats { get; } = baseStats;
        public JobProfile Job { get; } = job;
        public LevelProfile LevelProfile { get; } = levelProfile;
        public CalculationRequest Request { get; } = request;
        public CandidateCacheKey CacheKey { get; } = cacheKey;
        public IReadOnlyDictionary<uint, JobCalculationPreparation>? CommonJobs { get; } = commonJobs;
        public bool PreserveSpeed => CacheKey.PreserveSpeed;
        public Dictionary<GearSlot, List<GearOption>> OptionsBySlot { get; } = [];
        public int CandidateIndex { get; set; }
    }

    private sealed class JobCalculationPreparation(
        uint jobID,
        string jobName,
        JobProfile job,
        StatBlock baseStats,
        IReadOnlyList<FoodOption> foods)
    {
        public uint JobID { get; } = jobID;
        public string JobName { get; } = jobName;
        public JobProfile Job { get; } = job;
        public StatBlock BaseStats { get; } = baseStats;
        public IReadOnlyList<FoodOption> Foods { get; } = foods;
        public Dictionary<GearSlot, List<GearOption>> OptionsBySlot { get; } = [];
    }

    private readonly record struct JobProfile(
        PlayerAttribute MainAttribute,
        int AttackStatModifier,
        double TraitDamageMultiplier,
        bool IsTank,
        bool UsesSpellSpeed,
        bool UsesMagicDamage,
        double GcdModifier,
        double PartyBonus);

    private readonly record struct DamageKey(
        int MainStat,
        int CriticalHit,
        int Determination,
        int DirectHit,
        int Tenacity,
        int WeaponDamage,
        int Speed)
    {
        public static DamageKey From(
            StatBlock stats,
            JobProfile job,
            bool preserveSpeed,
            int speedCap = int.MaxValue) =>
            new(
                stats.MainStat,
                stats.CriticalHit,
                stats.Determination,
                stats.DirectHit,
                job.IsTank ? stats.Tenacity : 0,
                stats.WeaponDamage,
                preserveSpeed ? Math.Min(stats.Speed, speedCap) : 0);

        public bool Dominates(DamageKey other) =>
            Covers(other) &&
            this != other;

        public bool Covers(DamageKey other) =>
            MainStat >= other.MainStat &&
            CriticalHit >= other.CriticalHit &&
            Determination >= other.Determination &&
            DirectHit >= other.DirectHit &&
            Tenacity >= other.Tenacity &&
            WeaponDamage >= other.WeaponDamage &&
            Speed >= other.Speed;

        public static DamageKey MinimumOf(DamageKey left, DamageKey right) => new(
            Math.Min(left.MainStat, right.MainStat),
            Math.Min(left.CriticalHit, right.CriticalHit),
            Math.Min(left.Determination, right.Determination),
            Math.Min(left.DirectHit, right.DirectHit),
            Math.Min(left.Tenacity, right.Tenacity),
            Math.Min(left.WeaponDamage, right.WeaponDamage),
            Math.Min(left.Speed, right.Speed));

        public static DamageKey MaximumOf(DamageKey left, DamageKey right) => new(
            Math.Max(left.MainStat, right.MainStat),
            Math.Max(left.CriticalHit, right.CriticalHit),
            Math.Max(left.Determination, right.Determination),
            Math.Max(left.DirectHit, right.DirectHit),
            Math.Max(left.Tenacity, right.Tenacity),
            Math.Max(left.WeaponDamage, right.WeaponDamage),
            Math.Max(left.Speed, right.Speed));
    }

    private readonly record struct StatBlock(
        int MainStat,
        int Vitality,
        int CriticalHit,
        int Determination,
        int DirectHit,
        int Speed,
        int Piety,
        int Tenacity,
        int WeaponDamage)
    {
        public int Utility => Vitality + Speed + Piety;

        public int GetParameter(uint parameterID, JobProfile job) => parameterID == (uint)job.MainAttribute
            ? MainStat
            : parameterID switch
            {
                VitalityParamID => Vitality,
                CriticalHitParamID => CriticalHit,
                DeterminationParamID => Determination,
                DirectHitParamID => DirectHit,
                SkillSpeedParamID when !job.UsesSpellSpeed => Speed,
                SpellSpeedParamID when job.UsesSpellSpeed => Speed,
                PietyParamID => Piety,
                TenacityParamID => Tenacity,
                _ => 0
            };

        public StatBlock WithParameter(uint parameterID, int value, JobProfile job) => parameterID == (uint)job.MainAttribute
            ? this with { MainStat = value }
            : parameterID switch
            {
                VitalityParamID => this with { Vitality = value },
                CriticalHitParamID => this with { CriticalHit = value },
                DeterminationParamID => this with { Determination = value },
                DirectHitParamID => this with { DirectHit = value },
                SkillSpeedParamID when !job.UsesSpellSpeed => this with { Speed = value },
                SpellSpeedParamID when job.UsesSpellSpeed => this with { Speed = value },
                PietyParamID => this with { Piety = value },
                TenacityParamID => this with { Tenacity = value },
                _ => this
            };

        public static StatBlock operator +(StatBlock left, StatBlock right) => new(
            left.MainStat + right.MainStat,
            left.Vitality + right.Vitality,
            left.CriticalHit + right.CriticalHit,
            left.Determination + right.Determination,
            left.DirectHit + right.DirectHit,
            left.Speed + right.Speed,
            left.Piety + right.Piety,
            left.Tenacity + right.Tenacity,
            left.WeaponDamage + right.WeaponDamage);
    }

    private enum GearSlot
    {
        MainHand,
        OffHand,
        Head,
        Body,
        Hands,
        Legs,
        Feet,
        Ears,
        Neck,
        Wrists,
        Ring
    }

    private enum OptimizationTarget
    {
        MaxDamage,
        SpecificGcd
    }

    private enum GearStage
    {
        Graduation,
        Opening
    }

    private enum EquipmentDisplay
    {
        All,
        Shared,
        Weapons
    }
}

[Serializable]
public sealed class BestInSlotGearConfig
{
    public List<SavedResult> SavedResults { get; set; } = [];

    public sealed class SavedResult
    {
        public string ID { get; set; } = string.Empty;
        public uint JobID { get; set; }
        public int ItemLevelLimit { get; set; }
        public int GearStage { get; set; }
        public int OptimizationTarget { get; set; }
        public float TargetGcd { get; set; }
        public SavedCalculation Calculation { get; set; } = new();
    }

    public sealed class SavedCalculation
    {
        public uint JobID { get; set; }
        public string JobName { get; set; } = string.Empty;
        public byte Level { get; set; }
        public int ItemLevelLimit { get; set; }
        public List<SavedGearPiece> Items { get; set; } = [];
        public SavedStats Stats { get; set; } = new();
        public double Damage { get; set; }
        public double Gcd { get; set; }
        public SavedFood? Food { get; set; }
        public List<SavedCalculation> JobResults { get; set; } = [];
    }

    public sealed class SavedGearPiece
    {
        public uint ItemID { get; set; }
        public int Slot { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint IconID { get; set; }
        public uint ItemLevel { get; set; }
        public bool IsHighQuality { get; set; }
        public bool IsUnique { get; set; }
        public bool OccupiesOffHand { get; set; }
        public List<SavedMateria> Materia { get; set; } = [];
        public List<SavedMeld?> Melds { get; set; } = [];
        public List<SavedCustomStat>? CustomStats { get; set; }
        public string StatText { get; set; } = string.Empty;
    }

    public sealed class SavedMateria
    {
        public string Name { get; set; } = string.Empty;
        public uint IconID { get; set; }
        public int Count { get; set; }
    }

    public sealed class SavedMeld
    {
        public uint ParameterID { get; set; }
        public byte Grade { get; set; }
    }

    public sealed class SavedCustomStat
    {
        public uint ParameterID { get; set; }
        public int Value { get; set; }
    }

    public sealed class SavedStats
    {
        public int MainStat { get; set; }
        public int Vitality { get; set; }
        public int CriticalHit { get; set; }
        public int Determination { get; set; }
        public int DirectHit { get; set; }
        public int Speed { get; set; }
        public int Piety { get; set; }
        public int Tenacity { get; set; }
        public int WeaponDamage { get; set; }
    }

    public sealed class SavedFood
    {
        public uint ItemID { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint IconID { get; set; }
        public uint ItemLevel { get; set; }
        public bool IsHighQuality { get; set; }
        public string EffectText { get; set; } = string.Empty;
    }
}
