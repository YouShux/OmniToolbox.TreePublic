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
    private sealed record FoodResult(string Name, uint IconID, uint ItemLevel, bool IsHighQuality, string EffectText);
    private readonly record struct ItemDisplay(string Name, uint IconID, uint ItemLevel, bool IsHighQuality);
    private sealed record SearchSolution(GearOption[] Options, StatBlock Stats, double Damage, FoodResult? Food);
    private sealed record SearchState(StatBlock Stats, DamageKey Key, SearchState? Previous, GearOption? Option);
    private sealed class DominanceIndex
    {
        private readonly IReadOnlyList<SearchState> states;
        private readonly List<DamageBounds[]> levels = [];

        public DominanceIndex(IReadOnlyList<SearchState> states)
        {
            this.states = states;
            var bounds = BuildLeafBounds(states);
            levels.Add(bounds);
            while (bounds.Length > 1)
            {
                bounds = BuildParentBounds(bounds);
                levels.Add(bounds);
            }
        }

        public bool ContainsDominator(DamageKey candidate) =>
            states.Count > 0 && ContainsDominator(levels.Count - 1, 0, candidate);

        private bool ContainsDominator(int level, int nodeIndex, DamageKey candidate)
        {
            var bounds = levels[level][nodeIndex];
            if (!bounds.Maximum.Covers(candidate))
            {
                return false;
            }

            if (bounds.Minimum.Covers(candidate))
            {
                return true;
            }

            var childStart = nodeIndex * DominanceIndexFanout;
            if (level == 0)
            {
                var childEnd = Math.Min(childStart + DominanceIndexFanout, states.Count);
                for (var index = childStart; index < childEnd; index++)
                {
                    if (states[index].Key.Covers(candidate))
                    {
                        return true;
                    }
                }

                return false;
            }

            var children = levels[level - 1];
            var end = Math.Min(childStart + DominanceIndexFanout, children.Length);
            for (var index = childStart; index < end; index++)
            {
                if (ContainsDominator(level - 1, index, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static DamageBounds[] BuildLeafBounds(IReadOnlyList<SearchState> states)
        {
            var bounds = new DamageBounds[(states.Count + DominanceIndexFanout - 1) / DominanceIndexFanout];
            for (var index = 0; index < bounds.Length; index++)
            {
                var start = index * DominanceIndexFanout;
                var end = Math.Min(start + DominanceIndexFanout, states.Count);
                bounds[index] = DamageBounds.Create(states[start].Key);
                for (var stateIndex = start + 1; stateIndex < end; stateIndex++)
                {
                    bounds[index] = bounds[index].Include(states[stateIndex].Key);
                }
            }

            return bounds;
        }

        private static DamageBounds[] BuildParentBounds(IReadOnlyList<DamageBounds> children)
        {
            var bounds = new DamageBounds[(children.Count + DominanceIndexFanout - 1) / DominanceIndexFanout];
            for (var index = 0; index < bounds.Length; index++)
            {
                var start = index * DominanceIndexFanout;
                var end = Math.Min(start + DominanceIndexFanout, children.Count);
                bounds[index] = children[start];
                for (var childIndex = start + 1; childIndex < end; childIndex++)
                {
                    bounds[index] = bounds[index].Include(children[childIndex]);
                }
            }

            return bounds;
        }
    }

    private readonly record struct DamageBounds(DamageKey Minimum, DamageKey Maximum)
    {
        public static DamageBounds Create(DamageKey key) => new(key, key);

        public DamageBounds Include(DamageKey key) => new(
            DamageKey.MinimumOf(Minimum, key),
            DamageKey.MaximumOf(Maximum, key));

        public DamageBounds Include(DamageBounds bounds) => new(
            DamageKey.MinimumOf(Minimum, bounds.Minimum),
            DamageKey.MaximumOf(Maximum, bounds.Maximum));
    }
    private sealed record GearEvaluation(GearOption Option, FoodEvaluation Food);
    private sealed record CommonJobSearchState(
        JobCalculationPreparation Preparation,
        List<GearOption> Weapons,
        double PersonalBestDamage);
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
        int Value,
        uint RequiredItemLevel,
        string Name,
        uint IconID,
        byte AdvancedSlotMask);
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
        int SolverVersion)
    {
        public static CandidateCacheKey Create(CalculationRequest request, string gameDataVersion) => new(
            request.JobID,
            request.SyncItemLevel,
            request.TribeID,
            request.GearStage,
            request.OptimizationTarget == OptimizationTarget.SpecificGcd,
            gameDataVersion,
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
        public DateTime LastUsedUTC { get; set; }
        public BestInSlotGearConfig.SavedCalculation Calculation { get; set; } = new();

        public bool Matches(CalculationRequest request) =>
            JobID == request.JobID &&
            SyncItemLevel == request.SyncItemLevel &&
            TribeID == request.TribeID &&
            GearStage == (int)request.GearStage &&
            OptimizationTarget == (int)request.OptimizationTarget &&
            TargetGcdHundredths == request.TargetGcdHundredths;

        public static CachedResultEntry Create(CalculationRequest request, CalculationResult calculation) => new()
        {
            JobID = request.JobID,
            SyncItemLevel = request.SyncItemLevel,
            TribeID = request.TribeID,
            GearStage = (int)request.GearStage,
            OptimizationTarget = (int)request.OptimizationTarget,
            TargetGcdHundredths = request.TargetGcdHundredths,
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
        public int ItemBuildCount { get; set; }
        public int UpperBoundPrunedBuildCount { get; set; }
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
        public string StatText { get; set; } = string.Empty;
    }

    public sealed class SavedMateria
    {
        public string Name { get; set; } = string.Empty;
        public uint IconID { get; set; }
        public int Count { get; set; }
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
        public string Name { get; set; } = string.Empty;
        public uint IconID { get; set; }
        public uint ItemLevel { get; set; }
        public bool IsHighQuality { get; set; }
        public string EffectText { get; set; } = string.Empty;
    }
}
