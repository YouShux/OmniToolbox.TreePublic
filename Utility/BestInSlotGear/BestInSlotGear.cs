using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Newtonsoft.Json;
using OmniToolbox.Collections;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BestInSlotGear : ModuleBase
{
    private const uint VitalityParamID = 3;
    private const uint PietyParamID = 6;
    private const uint TenacityParamID = 19;
    private const uint DirectHitParamID = 22;
    private const uint CriticalHitParamID = 27;
    private const uint DeterminationParamID = 44;
    private const uint SkillSpeedParamID = 45;
    private const uint SpellSpeedParamID = 46;
    private const uint MainAttributeParamID = 55;
    private const uint SecondaryAttributeParamID = 56;
    private const uint TankPresetID = 1000;
    private const uint HealerPresetID = 1001;
    private const uint OccultItemSpecialBonusID = 9;
    private const uint OccultSetSpecialBonusID = 10;
    private const uint OccultSyncItemLevel = 700;
    private const int MinimumSyncItemLevel = 345;
    private const int MinimumCandidateItemLevel = 320;
    private const int CandidateItemLevelWindow = 25;
    private const int SolverVersion = 25;
    private const int CacheSchemaVersion = 3;
    private const int CandidateCacheCapacity = 4;
    private const int ResultCacheCapacity = 24;
    private const int SearchPruneBatchSize = 1024;
    private const int DominanceIndexFanout = 32;

    private static readonly int SearchPruneParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private static readonly JsonSerializerSettings CacheSerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    private readonly BestInSlotGearConfig config;
    private readonly CollectionSourceService collectionSourceService;
    private readonly ItemDataService itemDataService;
    private readonly List<ClassJob> jobs;
    private readonly List<Item> warmedGearItems = [];
    private readonly Dictionary<uint, List<Item>> warmedGearItemsByJob = [];
    private readonly Dictionary<uint, List<MateriaGradeOption>> warmedMateriaCatalog = [];
    private readonly List<FoodOption> warmedFoodOptions = [];
    private readonly HashSet<uint> unsupportedCustomItemIDs = [];
    private readonly Dictionary<CandidateCacheKey, LinkedListNode<CandidateCacheEntry>> candidateCache = [];
    private readonly LinkedList<CandidateCacheEntry> candidateCacheLru = [];
    private readonly List<CachedResultEntry> cachedResults = [];
    private readonly System.Action saveConfig;
    private IEnumerator<Item>? warmupEnumerator;
    private CancellationTokenSource? calculationCancellation;
    private CalculationPreparation? calculationPreparation;
    private Task<CalculationResult>? calculationTask;
    private CalculationRequest? pendingCalculation;
    private CalculationRequest? activeCalculation;
    private CalculationResult? result;
    private string? loadedSavedResultID;
    private string gameDataVersion = string.Empty;
    private uint openingStageItemLevelLimit = 999;
    private Vector2 expandedWindowSize;
    private uint selectedJobID = 19;
    private uint selectedDutyID;
    private int targetItemLevel;
    private float targetGcd = 2.5f;
    private GearStage gearStage = GearStage.Graduation;
    private OptimizationTarget optimizationTarget;
    private volatile float calculationProgress;
    private string calculationStatusKey = string.Empty;
    private float lastScaleValue;
    private bool initialized;
    private bool isCollapsed;
    private bool isWindowOpen;
    private bool restoreExpandedSize;
    private bool warmupComplete;

    public BestInSlotGear(
        BestInSlotGearConfig config,
        CollectionSourceService collectionSourceService,
        ItemDataService itemDataService,
        System.Action saveConfig)
    {
        this.config = config;
        this.collectionSourceService = collectionSourceService;
        this.itemDataService = itemDataService;
        this.saveConfig = saveConfig;
        jobs = LuminaGetter.Get<ClassJob>()
            .Where(job => JobProfiles.ContainsKey(job.RowId))
            .OrderBy(job => job.Role switch
            {
                1 => 0,
                4 => 1,
                2 => 2,
                3 when JobProfiles[job.RowId].UsesMagicDamage => 4,
                3 => 3,
                _ => 5
            })
            .ThenBy(static job => job.RowId)
            .ToList();
    }

    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BestInSlotGearTitle"),
        Description = OmniLoc.Get("BestInSlotGearDescription"),
        Category = ModuleCategory.Utility,
        SupportUrls =
        [
            "https://www.kdocs.cn/l/ceEcTzlFQBUy",
            "https://asvel.github.io/ffxiv-gearing/"
        ],
        Commands =
        [
            new ModuleCommand(
                "Feature.BestInSlotGear.CommandDescription",
                "/omni BIS配装")
        ]
    };

    public void OpenWindow() => isWindowOpen = true;

    protected override void OnEnable()
    {
        gameDataVersion = GetGameDataVersion();
        LoadResultCache();
        StartWarmup();
        DalamudServices.PluginInterface.UiBuilder.Draw += DrawWindow;
    }

    protected override void OnDisable()
    {
        DalamudServices.PluginInterface.UiBuilder.Draw -= DrawWindow;
        CancelCalculation();
        warmupEnumerator?.Dispose();
        warmupEnumerator = null;
        warmedGearItems.Clear();
        warmedGearItemsByJob.Clear();
        warmedMateriaCatalog.Clear();
        warmedFoodOptions.Clear();
        unsupportedCustomItemIDs.Clear();
        candidateCache.Clear();
        candidateCacheLru.Clear();
        warmupComplete = false;
        isWindowOpen = false;
    }

    private string ResultCachePath => Path.Combine(
        DalamudServices.PluginInterface.GetPluginConfigDirectory(),
        "TreeHouse",
        nameof(BestInSlotGear),
        "Cache.json");

    private void StartWarmup()
    {
        warmupEnumerator?.Dispose();
        warmedGearItems.Clear();
        warmedGearItemsByJob.Clear();
        warmedMateriaCatalog.Clear();
        warmedFoodOptions.Clear();
        unsupportedCustomItemIDs.Clear();
        openingStageItemLevelLimit = 999;
        BuildMateriaCatalog(warmedMateriaCatalog);
        warmupEnumerator = LuminaGetter.Get<Item>().GetEnumerator();
        warmupComplete = false;
    }

    private void ProcessWarmup()
    {
        if (warmupComplete || warmupEnumerator is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 2)
        {
            if (!warmupEnumerator.MoveNext())
            {
                warmupEnumerator.Dispose();
                warmupEnumerator = null;
                openingStageItemLevelLimit = BuildOpeningStageItemLevelLimit(warmedGearItems);
                warmupComplete = true;
                if (pendingCalculation is { } request)
                {
                    pendingCalculation = null;
                    BeginCalculationPreparation(request);
                }

                return;
            }

            var item = warmupEnumerator.Current;
            if (IsPotentialGear(item))
            {
                warmedGearItems.Add(item);
                foreach (var job in jobs)
                {
                    if (!item.ClassJobCategory.Value.IsClassJobIn(job.RowId))
                    {
                        continue;
                    }

                    if (!warmedGearItemsByJob.TryGetValue(job.RowId, out var items))
                    {
                        items = [];
                        warmedGearItemsByJob.Add(job.RowId, items);
                    }

                    items.Add(item);
                }
            }

            if (IsPotentialFood(item) && itemDataService.TryGetDetail(item.RowId, out var detail))
            {
                var bonuses = item.CanBeHq && detail.Effects.HQBonuses.Count > 0
                    ? detail.Effects.HQBonuses
                    : detail.Effects.NQBonuses;
                if (bonuses.Count > 0)
                {
                    warmedFoodOptions.Add(new(
                        item.RowId,
                        item.Name.ExtractText(),
                        item.Icon,
                        item.LevelItem.RowId,
                        item.CanBeHq,
                        bonuses));
                }
            }
        }
    }

    private static string GetGameDataVersion()
    {
        var versions = new List<string>(DalamudServices.DataManager.GameData.Repositories.Count + 1)
        {
            DalamudServices.DataManager.Language.ToString()
        };
        foreach (var repository in DalamudServices.DataManager.GameData.Repositories.OrderBy(static entry => entry.Key))
        {
            versions.Add($"{repository.Key}:{repository.Value.Version}");
        }

        return string.Join('|', versions);
    }

    private void LoadResultCache()
    {
        cachedResults.Clear();
        if (!File.Exists(ResultCachePath))
        {
            return;
        }

        try
        {
            var document = JsonConvert.DeserializeObject<ResultCacheDocument>(
                File.ReadAllText(ResultCachePath, Utf8),
                CacheSerializerSettings);
            if (document is null ||
                document.SchemaVersion != CacheSchemaVersion ||
                document.SolverVersion != SolverVersion ||
                !string.Equals(document.GameDataVersion, gameDataVersion, StringComparison.Ordinal) ||
                document.Results is null)
            {
                return;
            }

            document.Results.Sort(static (left, right) => right.LastUsedUTC.CompareTo(left.LastUsedUTC));
            cachedResults.AddRange(document.Results.Take(ResultCacheCapacity));
        }
        catch (Exception exception)
        {
            DalamudServices.PluginLog.Warning(exception, "Best-in-slot result cache load failed: {Path}", ResultCachePath);
        }
    }

    private bool TryGetCachedResult(CalculationRequest request, out CalculationResult cached)
    {
        for (var index = 0; index < cachedResults.Count; index++)
        {
            if (!cachedResults[index].Matches(request))
            {
                continue;
            }

            cachedResults[index].LastUsedUTC = DateTime.UtcNow;
            cached = RestoreCalculation(cachedResults[index].Calculation);
            return true;
        }

        cached = null!;
        return false;
    }

    private void CacheCompletedResult(CalculationRequest request, CalculationResult calculation)
    {
        cachedResults.RemoveAll(entry => entry.Matches(request));
        cachedResults.Add(CachedResultEntry.Create(request, calculation));
        cachedResults.Sort(static (left, right) => right.LastUsedUTC.CompareTo(left.LastUsedUTC));
        if (cachedResults.Count > ResultCacheCapacity)
        {
            cachedResults.RemoveRange(ResultCacheCapacity, cachedResults.Count - ResultCacheCapacity);
        }

        var path = ResultCachePath;
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                tempPath,
                JsonConvert.SerializeObject(new ResultCacheDocument
                {
                    SchemaVersion = CacheSchemaVersion,
                    SolverVersion = SolverVersion,
                    GameDataVersion = gameDataVersion,
                    Results = cachedResults
                }, CacheSerializerSettings),
                Utf8);
            File.Move(tempPath, path, true);
        }
        catch (Exception exception)
        {
            DalamudServices.PluginLog.Warning(exception, "Best-in-slot result cache save failed: {Path}", path);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception exception)
                {
                    DalamudServices.PluginLog.Warning(
                        exception,
                        "Best-in-slot temporary cache cleanup failed: {Path}",
                        tempPath);
                }
            }
        }
    }

    private bool TryGetCandidateCache(CandidateCacheKey key, out PreparedCalculationData data)
    {
        if (!candidateCache.TryGetValue(key, out var node))
        {
            data = null!;
            return false;
        }

        candidateCacheLru.Remove(node);
        candidateCacheLru.AddFirst(node);
        data = node.Value.Data;
        return true;
    }

    private void AddCandidateCache(CandidateCacheKey key, PreparedCalculationData data)
    {
        if (candidateCache.TryGetValue(key, out var existing))
        {
            existing.Value = new(key, data);
            candidateCacheLru.Remove(existing);
            candidateCacheLru.AddFirst(existing);
            return;
        }

        var node = candidateCacheLru.AddFirst(new CandidateCacheEntry(key, data));
        candidateCache.Add(key, node);
        if (candidateCache.Count <= CandidateCacheCapacity)
        {
            return;
        }

        var expired = candidateCacheLru.Last!;
        candidateCache.Remove(expired.Value.Key);
        candidateCacheLru.RemoveLast();
    }

    private void ShowCompletionPopup() => OmniNotifier.Popup(
        Info.Title,
        OmniLoc.Get("Feature.BestInSlotGear.CalculationComplete"),
        NotificationType.Success);

    private void StartCalculation(uint jobID, int syncItemLevel)
    {
        loadedSavedResultID = null;
        var request = new CalculationRequest(
            jobID,
            syncItemLevel,
            GetCurrentTribeID(),
            gearStage,
            optimizationTarget,
            MathF.Round(targetGcd, 2, MidpointRounding.AwayFromZero));
        if (syncItemLevel < MinimumSyncItemLevel ||
            syncItemLevel > 999 ||
            !LuminaGetter.TryGetRow<ItemLevel>((uint)syncItemLevel, out _) ||
            jobID is not (TankPresetID or HealerPresetID) && !JobProfiles.ContainsKey(jobID))
        {
            result = null;
            return;
        }

        DalamudServices.PluginLog.Information(
            "[BIS] Calculation requested: solverVersion={SolverVersion}, job={JobID}, sync={SyncItemLevel}, stage={GearStage}, target={OptimizationTarget}, targetGcd={TargetGcd:F2}.",
            SolverVersion,
            request.JobID,
            request.SyncItemLevel,
            request.GearStage,
            request.OptimizationTarget,
            request.TargetGcd);
        result = null;
        if (TryGetCachedResult(request, out var cached))
        {
            DalamudServices.PluginLog.Information(
                "[BIS] Completed-result cache hit: job={JobID}, sync={SyncItemLevel}.",
                request.JobID,
                request.SyncItemLevel);
            result = cached;
            ShowCompletionPopup();
            return;
        }

        activeCalculation = request;
        calculationProgress = 0.02f;
        calculationStatusKey = "Feature.BestInSlotGear.Progress.Collecting";
        var cacheKey = CandidateCacheKey.Create(request, gameDataVersion);
        if (TryGetCandidateCache(cacheKey, out var prepared))
        {
            DalamudServices.PluginLog.Information(
                "[BIS] Candidate cache hit: job={JobID}, sync={SyncItemLevel}.",
                request.JobID,
                request.SyncItemLevel);
            StartCalculationTask(prepared, request);
            return;
        }

        if (!warmupComplete)
        {
            DalamudServices.PluginLog.Information(
                "[BIS] Candidate cache miss; waiting for item-data warmup: job={JobID}, sync={SyncItemLevel}.",
                request.JobID,
                request.SyncItemLevel);
            pendingCalculation = request;
            return;
        }

        DalamudServices.PluginLog.Information(
            "[BIS] Candidate cache miss; preparing options: job={JobID}, sync={SyncItemLevel}.",
            request.JobID,
            request.SyncItemLevel);
        BeginCalculationPreparation(request);
    }

    private void BeginCalculationPreparation(CalculationRequest request)
    {
        if (request.JobID is TankPresetID or HealerPresetID)
        {
            BeginCommonCalculation(request);
            return;
        }

        if (!JobProfiles.TryGetValue(request.JobID, out var job) ||
            !LuminaGetter.TryGetRow<ClassJob>(request.JobID, out var classJob) ||
            !LuminaGetter.TryGetRow<ItemLevel>((uint)request.SyncItemLevel, out var syncItemLevelStats))
        {
            activeCalculation = null;
            calculationProgress = 0f;
            return;
        }

        var candidates = new List<Item>();
        if (!warmedGearItemsByJob.TryGetValue(request.JobID, out var jobItems))
        {
            jobItems = [];
        }

        foreach (var item in jobItems)
        {
            if (IsGearCandidate(item, request.JobID, request.GearStage, request.SyncItemLevel))
            {
                candidates.Add(item);
            }
        }

        if (candidates.Count == 0)
        {
            result = CalculationResult.Failed(OmniLoc.Get("Feature.BestInSlotGear.NoMatchingGear"));
            activeCalculation = null;
            calculationProgress = 0f;
            return;
        }

        var level = GetCalculationLevel(candidates, request.SyncItemLevel);
        if (!LevelProfiles.TryGetValue(level, out var levelProfile))
        {
            result = CalculationResult.Failed(string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.BestInSlotGear.UnsupportedLevel"),
                level));
            activeCalculation = null;
            calculationProgress = 0f;
            return;
        }

        var preserveSpeed = request.OptimizationTarget == OptimizationTarget.SpecificGcd;
        var meldParameterIDs = GetMeldParameterIDs(job, preserveSpeed);
        var foods = BuildFoodOptions(job, preserveSpeed, request.GearStage);
        var baseStats = BuildBaseStats(classJob, job, levelProfile, request.TribeID);
        SortGearCandidates(
            candidates,
            job,
            meldParameterIDs,
            warmedMateriaCatalog,
            syncItemLevelStats,
            preserveSpeed,
            request.GearStage);
        calculationCancellation = new();
        calculationPreparation = new(
            request.JobID,
            classJob.Name.ExtractText(),
            level,
            request.SyncItemLevel,
            syncItemLevelStats,
            candidates,
            meldParameterIDs,
            warmedMateriaCatalog,
            foods,
            baseStats,
            job,
            levelProfile,
            request,
            CandidateCacheKey.Create(request, gameDataVersion),
            null);
        calculationProgress = 0.1f;
        calculationStatusKey = "Feature.BestInSlotGear.Progress.BuildingOptions";
    }

    private void BeginCommonCalculation(CalculationRequest request)
    {
        if (!LuminaGetter.TryGetRow<ItemLevel>((uint)request.SyncItemLevel, out var syncItemLevelStats))
        {
            result = null;
            activeCalculation = null;
            calculationProgress = 0f;
            return;
        }

        var jobIDs = request.JobID == TankPresetID ? TankJobIDs : HealerJobIDs;
        var candidateIDs = new HashSet<uint>();
        var candidates = new List<Item>();
        foreach (var jobID in jobIDs)
        {
            if (!warmedGearItemsByJob.TryGetValue(jobID, out var jobItems))
            {
                continue;
            }

            foreach (var item in jobItems)
            {
                if (candidateIDs.Add(item.RowId) && IsGearCandidate(item, jobID, request.GearStage, request.SyncItemLevel))
                {
                    candidates.Add(item);
                }
            }
        }

        if (candidates.Count == 0)
        {
            result = CalculationResult.Failed(OmniLoc.Get("Feature.BestInSlotGear.NoMatchingGear"));
            activeCalculation = null;
            calculationProgress = 0f;
            return;
        }

        var level = GetCalculationLevel(candidates, request.SyncItemLevel);
        if (!LevelProfiles.TryGetValue(level, out var levelProfile))
        {
            result = CalculationResult.Failed(string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.BestInSlotGear.UnsupportedLevel"),
                level));
            activeCalculation = null;
            calculationProgress = 0f;
            return;
        }

        var commonJobs = new Dictionary<uint, JobCalculationPreparation>();
        foreach (var jobID in jobIDs)
        {
            if (!JobProfiles.TryGetValue(jobID, out var job) ||
                !LuminaGetter.TryGetRow<ClassJob>(jobID, out var classJob))
            {
                result = null;
                activeCalculation = null;
                calculationProgress = 0f;
                return;
            }

            commonJobs.Add(jobID, new(
                jobID,
                classJob.Name.ExtractText(),
                job,
                BuildBaseStats(classJob, job, levelProfile, request.TribeID),
                BuildFoodOptions(job, request.OptimizationTarget == OptimizationTarget.SpecificGcd, request.GearStage)));
        }

        var representative = commonJobs[jobIDs[0]];
        var meldParameterIDs = GetMeldParameterIDs(
            representative.Job,
            request.OptimizationTarget == OptimizationTarget.SpecificGcd);
        SortGearCandidates(
            candidates,
            representative.Job,
            meldParameterIDs,
            warmedMateriaCatalog,
            syncItemLevelStats,
            request.OptimizationTarget == OptimizationTarget.SpecificGcd,
            request.GearStage);
        calculationCancellation = new();
        calculationPreparation = new(
            request.JobID,
            GetJobPreview(request.JobID),
            level,
            request.SyncItemLevel,
            syncItemLevelStats,
            candidates,
            meldParameterIDs,
            warmedMateriaCatalog,
            representative.Foods,
            representative.BaseStats,
            representative.Job,
            levelProfile,
            request,
            CandidateCacheKey.Create(request, gameDataVersion),
            commonJobs);
        calculationProgress = 0.1f;
        calculationStatusKey = "Feature.BestInSlotGear.Progress.BuildingOptions";
    }

    private static uint[] GetMeldParameterIDs(JobProfile job, bool includeSpeed) => job.IsTank
        ? includeSpeed
            ? [CriticalHitParamID, DeterminationParamID, DirectHitParamID, TenacityParamID, SkillSpeedParamID]
            : [CriticalHitParamID, DeterminationParamID, DirectHitParamID, TenacityParamID]
        : includeSpeed
            ? [CriticalHitParamID, DeterminationParamID, DirectHitParamID, job.UsesSpellSpeed ? SpellSpeedParamID : SkillSpeedParamID]
            : [CriticalHitParamID, DeterminationParamID, DirectHitParamID];

    private static byte GetCalculationLevel(IReadOnlyList<Item> candidates, int syncItemLevel)
    {
        byte level = 0;
        foreach (var item in candidates)
        {
            if (item.LevelItem.RowId <= (uint)syncItemLevel)
            {
                level = Math.Max(level, item.LevelEquip);
            }
        }

        return level;
    }

    private static StatBlock BuildBaseStats(ClassJob classJob, JobProfile job, LevelProfile level, uint tribeID) => new(
        (int)Math.Floor(level.Main * job.AttackStatModifier / 100d) + GetTribeMainStatBonus(job.MainAttribute, tribeID),
        (int)Math.Floor(level.Main * classJob.ModifierVitality / 100d),
        level.Sub,
        level.Main,
        level.Sub,
        level.Sub,
        level.Main,
        level.Sub,
        0);

    private static uint GetCurrentTribeID()
    {
        var playerState = PlayerState.Instance();
        return playerState == null ? 0u : (uint)playerState->Tribe;
    }

    private static int GetTribeMainStatBonus(PlayerAttribute attribute, uint tribeID)
    {
        if (!LuminaGetter.TryGetRow<Tribe>(tribeID, out var tribe))
        {
            return 0;
        }

        return attribute switch
        {
            PlayerAttribute.Strength => tribe.STR,
            PlayerAttribute.Dexterity => tribe.DEX,
            PlayerAttribute.Intelligence => tribe.INT,
            PlayerAttribute.Mind => tribe.MND,
            _ => 0
        };
    }

    private void ProcessCalculationPreparation()
    {
        if (calculationPreparation is not { } preparation)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        while (preparation.CandidateIndex < preparation.Candidates.Count && stopwatch.ElapsedMilliseconds < 4)
        {
            var item = preparation.Candidates[preparation.CandidateIndex++];
            if (item.LevelEquip > preparation.Level || !TryGetSlot(item.EquipSlotCategory.Value, out var slot))
            {
                continue;
            }

            if (preparation.CommonJobs is { } commonJobs)
            {
                if (slot is not (GearSlot.MainHand or GearSlot.OffHand) &&
                    commonJobs.Keys.All(jobID => item.ClassJobCategory.Value.IsClassJobIn(jobID)))
                {
                    var sharedItemStats = BuildItemStats(item, preparation.Job, preparation.SyncItemLevelStats);
                    var sharedOptions = BuildUsefulItemOptions(
                        preparation.OptionsBySlot,
                        item,
                        slot,
                        sharedItemStats,
                        preparation.Job,
                        preparation.MeldParameterIDs,
                        preparation.MateriaCatalog,
                        preparation.SyncItemLevelStats,
                        preparation.PreserveSpeed,
                        out var sharedUpperBoundPruned);
                    preparation.ItemBuildCount++;
                    if (sharedUpperBoundPruned)
                    {
                        preparation.UpperBoundPrunedBuildCount++;
                    }

                    AddOptions(
                        preparation.OptionsBySlot,
                        slot,
                        sharedOptions,
                        preparation.Job,
                        preparation.PreserveSpeed);
                    foreach (var commonJob in commonJobs.Values)
                    {
                        AddOptions(
                            commonJob.OptionsBySlot,
                            slot,
                            sharedOptions,
                            commonJob.Job,
                            preparation.PreserveSpeed);
                    }

                    continue;
                }

                foreach (var commonJob in commonJobs.Values)
                {
                    if (!item.ClassJobCategory.Value.IsClassJobIn(commonJob.JobID))
                    {
                        continue;
                    }

                    var jobItemStats = BuildItemStats(item, commonJob.Job, preparation.SyncItemLevelStats);
                    var jobOptions = BuildUsefulItemOptions(
                        commonJob.OptionsBySlot,
                        item,
                        slot,
                        jobItemStats,
                        commonJob.Job,
                        preparation.MeldParameterIDs,
                        preparation.MateriaCatalog,
                        preparation.SyncItemLevelStats,
                        preparation.PreserveSpeed,
                        out var jobUpperBoundPruned);
                    preparation.ItemBuildCount++;
                    if (jobUpperBoundPruned)
                    {
                        preparation.UpperBoundPrunedBuildCount++;
                    }

                    AddOptions(
                        commonJob.OptionsBySlot,
                        slot,
                        jobOptions,
                        commonJob.Job,
                        preparation.PreserveSpeed);
                }

                continue;
            }

            var itemStats = BuildItemStats(item, preparation.Job, preparation.SyncItemLevelStats);
            var itemOptions = BuildUsefulItemOptions(
                preparation.OptionsBySlot,
                item,
                slot,
                itemStats,
                preparation.Job,
                preparation.MeldParameterIDs,
                preparation.MateriaCatalog,
                preparation.SyncItemLevelStats,
                preparation.PreserveSpeed,
                out var itemUpperBoundPruned);
            preparation.ItemBuildCount++;
            if (itemUpperBoundPruned)
            {
                preparation.UpperBoundPrunedBuildCount++;
            }

            AddOptions(
                preparation.OptionsBySlot,
                slot,
                itemOptions,
                preparation.Job,
                preparation.PreserveSpeed);
        }

        calculationProgress = 0.1f + 0.4f * preparation.CandidateIndex / preparation.Candidates.Count;
        if (preparation.CandidateIndex < preparation.Candidates.Count)
        {
            return;
        }

        DalamudServices.PluginLog.Information(
            "[BIS] Options prepared: job={JobName} ({JobID}), candidates={CandidateCount}, candidateItemLevels={MinimumCandidateItemLevel}-{MaximumCandidateItemLevel}, itemBuilds={ItemBuildCount}, upperBoundPruned={UpperBoundPrunedBuildCount}, foods={FoodCount}, slots=[{SlotOptions}].",
            preparation.JobName,
            preparation.JobID,
            preparation.Candidates.Count,
            preparation.Candidates.Min(static item => item.LevelItem.RowId),
            preparation.Candidates.Max(static item => item.LevelItem.RowId),
            preparation.ItemBuildCount,
            preparation.UpperBoundPrunedBuildCount,
            preparation.Foods.Count,
            string.Join(
                ", ",
                preparation.OptionsBySlot
                    .OrderBy(static entry => entry.Key)
                    .Select(static entry => $"{entry.Key}={entry.Value.Count}")));
        calculationPreparation = null;
        var prepared = new PreparedCalculationData(
            preparation.JobID,
            preparation.JobName,
            preparation.Level,
            preparation.ItemLevelLimit,
            preparation.OptionsBySlot,
            preparation.Foods,
            preparation.BaseStats,
            preparation.Job,
            preparation.LevelProfile,
            preparation.CommonJobs);
        AddCandidateCache(preparation.CacheKey, prepared);
        StartCalculationTask(prepared, preparation.Request);
    }

    private void StartCalculationTask(PreparedCalculationData prepared, CalculationRequest request)
    {
        calculationProgress = 0.52f;
        calculationStatusKey = "Feature.BestInSlotGear.Progress.Optimizing";
        calculationCancellation ??= new();
        var input = new CalculationInput(
            prepared.JobID,
            prepared.JobName,
            prepared.Level,
            prepared.ItemLevelLimit,
            prepared.OptionsBySlot,
            prepared.Foods,
            prepared.BaseStats,
            prepared.Job,
            prepared.LevelProfile,
            request.OptimizationTarget,
            request.TargetGcd,
            prepared.CommonJobs);
        var cancellationToken = calculationCancellation!.Token;
        calculationTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            DalamudServices.PluginLog.Information(
                "[BIS] Solver started: job={JobName} ({JobID}), sync={SyncItemLevel}, target={OptimizationTarget}.",
                input.JobName,
                input.JobID,
                input.ItemLevelLimit,
                input.OptimizationTarget);
            try
            {
                return Calculate(input, cancellationToken, progress => calculationProgress = progress);
            }
            finally
            {
                DalamudServices.PluginLog.Information(
                    "[BIS] Solver task ended: job={JobName} ({JobID}), sync={SyncItemLevel}, elapsedMs={ElapsedMilliseconds}.",
                    input.JobName,
                    input.JobID,
                    input.ItemLevelLimit,
                    stopwatch.ElapsedMilliseconds);
            }
        }, cancellationToken);
    }

    private static void AddOptions(
        Dictionary<GearSlot, List<GearOption>> optionsBySlot,
        GearSlot slot,
        IEnumerable<GearOption> options,
        JobProfile job,
        bool preserveSpeed)
    {
        if (!optionsBySlot.TryGetValue(slot, out var slotOptions))
        {
            slotOptions = [];
            optionsBySlot.Add(slot, slotOptions);
        }

        foreach (var option in options)
        {
            AddToSlotFrontier(slotOptions, option, slot, job, preserveSpeed);
        }
    }

    private static void AddToSlotFrontier(
        List<GearOption> options,
        GearOption candidate,
        GearSlot slot,
        JobProfile job,
        bool preserveSpeed)
    {
        if (candidate.CustomStats is not null)
        {
            foreach (var existing in options)
            {
                if (existing.CustomStats is not null &&
                    existing.Pieces[0].ItemID == candidate.Pieces[0].ItemID)
                {
                    return;
                }
            }

            options.Add(candidate);
            return;
        }

        if (candidate.CustomStatSelection is not null)
        {
            options.Add(candidate);
            return;
        }

        var candidateKey = DamageKey.From(candidate.Stats, job, preserveSpeed);
        for (var index = options.Count - 1; index >= 0; index--)
        {
            var existing = options[index];
            if (existing.CustomStats is not null)
            {
                continue;
            }

            var existingKey = DamageKey.From(existing.Stats, job, preserveSpeed);
            if (CanReplaceInSlot(existing, candidate, slot) &&
                (existingKey.Dominates(candidateKey) ||
                 existingKey == candidateKey && existing.Stats.Utility >= candidate.Stats.Utility))
            {
                return;
            }

            if (CanReplaceInSlot(candidate, existing, slot) &&
                (candidateKey.Dominates(existingKey) ||
                 candidateKey == existingKey && candidate.Stats.Utility > existing.Stats.Utility))
            {
                options.RemoveAt(index);
            }
        }

        options.Add(candidate);
    }

    private static bool CanReplaceInSlot(GearOption replacement, GearOption replaced, GearSlot slot)
    {
        if (slot == GearSlot.MainHand)
        {
            return replacement.Pieces[0].OccupiesOffHand == replaced.Pieces[0].OccupiesOffHand;
        }

        return slot != GearSlot.Ring ||
               !replacement.Pieces[0].IsUnique ||
               replacement.Pieces[0].ItemID == replaced.Pieces[0].ItemID;
    }

    private void ObserveCalculationTask()
    {
        if (calculationTask is not { IsCompleted: true } task)
        {
            return;
        }

        calculationTask = null;
        try
        {
            result = task.GetAwaiter().GetResult();
            if (result.Error is null)
            {
                if (activeCalculation is { } request)
                {
                    CacheCompletedResult(request, result);
                }

                ShowCompletionPopup();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            DalamudServices.PluginLog.Error(exception, "Failed to calculate best-in-slot gear.");
            result = CalculationResult.Failed(OmniLoc.Get("Feature.BestInSlotGear.CalculationFailed"));
        }
        finally
        {
            calculationCancellation?.Dispose();
            calculationCancellation = null;
            activeCalculation = null;
            calculationProgress = 0f;
            calculationStatusKey = string.Empty;
        }
    }

    private void CancelCalculation()
    {
        pendingCalculation = null;
        calculationPreparation = null;
        activeCalculation = null;
        if (calculationTask is not null)
        {
            calculationCancellation?.Cancel();
            calculationStatusKey = "Feature.BestInSlotGear.Progress.Cancelling";
            return;
        }

        calculationCancellation?.Dispose();
        calculationCancellation = null;
        calculationProgress = 0f;
        calculationStatusKey = string.Empty;
    }

    private static readonly GearSlot[] RequiredSlots =
    [
        GearSlot.Head,
        GearSlot.Body,
        GearSlot.Hands,
        GearSlot.Legs,
        GearSlot.Feet,
        GearSlot.Ears,
        GearSlot.Neck,
        GearSlot.Wrists
    ];

    private static readonly (GearSlot Slot, int RingIndex, int InventorySlot)[] ComparisonSlots =
    [
        (GearSlot.MainHand, 0, 0),
        (GearSlot.OffHand, 0, 1),
        (GearSlot.Head, 0, 2),
        (GearSlot.Body, 0, 3),
        (GearSlot.Hands, 0, 4),
        (GearSlot.Legs, 0, 6),
        (GearSlot.Feet, 0, 7),
        (GearSlot.Ears, 0, 8),
        (GearSlot.Neck, 0, 9),
        (GearSlot.Wrists, 0, 10),
        (GearSlot.Ring, 0, 12),
        (GearSlot.Ring, 1, 11)
    ];

    private static readonly uint[] PresetIDs = [TankPresetID, HealerPresetID];
    private static readonly uint[] UltimateDutyIDs = [1094, 1006, 908, 788, 694, 539, 280];
    private static readonly uint[] TankJobIDs = [19, 21, 32, 37];
    private static readonly uint[] HealerJobIDs = [24, 28, 33, 40];

    private static readonly Dictionary<byte, LevelProfile> LevelProfiles = new()
    {
        [50] = new(50, 202, 341, 341, 202, 5, 75, 53),
        [60] = new(60, 218, 354, 600, 600, 2, 100, 78),
        [70] = new(70, 292, 364, 900, 900, 1, 125, 105),
        [80] = new(80, 340, 380, 1300, 1300, 1, 165, 115),
        [90] = new(90, 390, 400, 1900, 1900, 1, 195, 156),
        [100] = new(100, 440, 420, 2780, 2780, 1, 237, 190)
    };

    private static readonly Dictionary<uint, JobProfile> JobProfiles = new()
    {
        [19] = new(PlayerAttribute.Strength, 100, 1d, true, false, false, 1d, 1.05d),
        [21] = new(PlayerAttribute.Strength, 105, 1d, true, false, false, 1d, 1.05d),
        [32] = new(PlayerAttribute.Strength, 105, 1d, true, false, false, 1d, 1.05d),
        [37] = new(PlayerAttribute.Strength, 100, 1d, true, false, false, 1d, 1.05d),
        [24] = new(PlayerAttribute.Mind, 115, 1.3d, false, true, true, 1d, 1.05d),
        [28] = new(PlayerAttribute.Mind, 115, 1.3d, false, true, true, 1d, 1.05d),
        [33] = new(PlayerAttribute.Mind, 115, 1.3d, false, true, true, 1d, 1.05d),
        [40] = new(PlayerAttribute.Mind, 115, 1.3d, false, true, true, 1d, 1.05d),
        [20] = new(PlayerAttribute.Strength, 110, 1d, false, false, false, 0.80d, 1.05d),
        [22] = new(PlayerAttribute.Strength, 115, 1d, false, false, false, 1d, 1.05d),
        [30] = new(PlayerAttribute.Dexterity, 110, 1d, false, false, false, 0.85d, 1.05d),
        [34] = new(PlayerAttribute.Strength, 112, 1d, false, false, false, 0.87d, 1.05d),
        [39] = new(PlayerAttribute.Strength, 115, 1d, false, false, false, 1d, 1.05d),
        [41] = new(PlayerAttribute.Dexterity, 110, 1d, false, false, false, 0.85d, 1.05d),
        [23] = new(PlayerAttribute.Dexterity, 115, 1.2d, false, false, false, 1d, 1.05d),
        [31] = new(PlayerAttribute.Dexterity, 115, 1.2d, false, false, false, 1d, 1.05d),
        [38] = new(PlayerAttribute.Dexterity, 115, 1.2d, false, false, false, 1d, 1.05d),
        [25] = new(PlayerAttribute.Intelligence, 115, 1.3d, false, true, true, 1d, 1.05d),
        [27] = new(PlayerAttribute.Intelligence, 115, 1.3d, false, true, true, 1d, 1.05d),
        [35] = new(PlayerAttribute.Intelligence, 115, 1.3d, false, true, true, 1d, 1.05d),
        [42] = new(PlayerAttribute.Intelligence, 115, 1.3d, false, true, true, 1d, 1.05d)
    };

}
