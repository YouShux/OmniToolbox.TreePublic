using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using InteropGenerator.Runtime;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Lumina.Enums;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using LuminaAetheryte = Lumina.Excel.Sheets.Aetheryte;
using LuminaTerritoryType = Lumina.Excel.Sheets.TerritoryType;
using ModuleBase = OmniToolbox.Common.Module.Abstractions.ModuleBase;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class AetheryteModelReplacement(
    AetheryteModelReplacement.Settings config,
    System.Action saveConfig) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AetheryteModelReplacementTitle"),
        Description = OmniLoc.Get("AetheryteModelReplacementDescription"),
        Category = ModuleCategory.Interface,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Interface/AetheryteModelReplacement-1.png"
    };

    public override bool HasSettings => true;

    private static PresetScanResult? presetScanCache;
    private static string presetScanCacheVersion = string.Empty;
    private IReadOnlyList<AetherytePreset> presets = [];
    private PresetScanProgress? presetScanProgress;
    private bool presetScanFailed;
    private string gameDataVersion = string.Empty;
    private Hook<ResourceManager.Delegates.GetResourceSync>? getResourceSyncHook;
    private Hook<ResourceManager.Delegates.GetResourceAsync>? getResourceAsyncHook;
    private bool redirectSharedGroup;
    private Dictionary<RedirectKey, string> redirectTargets = [];
    private bool redirectsDirty = true;
    private uint currentTerritoryID;
    private FeatureLifetime? runtimeLifetime;

    internal bool IsPresetScanPending => presetScanCache is null && !presetScanFailed;

    protected override void OnEnable()
    {
        if (EnsureRules())
        {
            saveConfig();
        }

        presets = [];
        presetScanProgress = null;
        presetScanFailed = false;
        gameDataVersion = GetGameDataVersion();
        if (gameDataVersion.Length > 0 &&
            config.PresetCacheSchemaVersion == PRESET_CACHE_SCHEMA_VERSION &&
            string.Equals(config.PresetCacheGameDataVersion, gameDataVersion, StringComparison.Ordinal) &&
            config.CachedPresetScan is { } cached)
        {
            presets = cached.Presets;
            if (cached.IsComplete)
            {
                presetScanCache = cached;
                presetScanCacheVersion = gameDataVersion;
            }
            else if (cached.ScannedTerritories > 0)
            {
                presetScanProgress = PresetScanProgress.Create(cached);
            }
        }
        else if (!string.Equals(presetScanCacheVersion, gameDataVersion, StringComparison.Ordinal) ||
                 gameDataVersion.Length == 0)
        {
            presetScanCache = null;
        }

        presets = presetScanCache?.Presets ?? presets;
        var lifetime = new FeatureLifetime();
        try
        {
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 250))
            {
                throw new InvalidOperationException("Aetheryte model replacement update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            getResourceSyncHook = DService.Instance().Hook.HookFromMemberFunction(
                typeof(ResourceManager.MemberFunctionPointers),
                "GetResourceSync",
                (ResourceManager.Delegates.GetResourceSync)GetResourceSyncDetour);
            lifetime.Add(ReleaseResourceHooks);
            getResourceAsyncHook = DService.Instance().Hook.HookFromMemberFunction(
                typeof(ResourceManager.MemberFunctionPointers),
                "GetResourceAsync",
                (ResourceManager.Delegates.GetResourceAsync)GetResourceAsyncDetour);
            getResourceSyncHook.Enable();
            getResourceAsyncHook.Enable();
            Volatile.Write(ref currentTerritoryID, DService.Instance().ClientState.TerritoryType);
            UpdateResourceRedirect(true);
            var clientState = DService.Instance().ClientState;
            clientState.TerritoryChanged += OnTerritoryChanged;
            lifetime.Add(() => clientState.TerritoryChanged -= OnTerritoryChanged);
            runtimeLifetime = lifetime;
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            presetScanProgress = null;
            DisableResourceRedirect();
        }
    }

    public override bool DrawSettings()
    {
        var changed = Panel.Draw(this, config);
        redirectsDirty |= changed;
        return changed;
    }

    public override bool ResetSettings()
    {
        config.Rules = [];
        redirectsDirty = true;
        config.PresetCacheSchemaVersion = 0;
        config.PresetCacheGameDataVersion = string.Empty;
        config.CachedPresetScan = null;
        presetScanCache = null;
        presetScanCacheVersion = string.Empty;
        presets = [];
        presetScanProgress = null;
        presetScanFailed = false;
        saveConfig();
        DisableResourceRedirect();
        return true;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var services = DService.Instance();
        Volatile.Write(ref currentTerritoryID, services.ClientState.TerritoryType);
        if (!services.ClientState.IsLoggedIn)
        {
            DisableResourceRedirect();
            return;
        }

        UpdateResourceRedirect(false);
        if (services.Condition[ConditionFlag.BetweenAreas])
        {
            return;
        }

        ProcessPresetScan();
    }

    private void OnTerritoryChanged(uint _)
    {
        Volatile.Write(ref currentTerritoryID, DService.Instance().ClientState.TerritoryType);
        UpdateResourceRedirect(true);
    }

    internal IReadOnlyList<AetherytePreset> GetPresets()
    {
        if (presetScanCache is { } cached)
        {
            presets = cached.Presets;
            return presets;
        }

        return presets;
    }

    private static string GetGameDataVersion()
    {
        var dataManager = DalamudServices.DataManager;
        var repositories = dataManager.GameData.Repositories;
        if (repositories.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            '|',
            new[] { dataManager.Language.ToString() }
                .Concat(repositories.OrderBy(static entry => entry.Key)
                    .Select(static entry => $"{entry.Key}:{entry.Value.Version}")));
    }

    private void ProcessPresetScan()
    {
        if (presetScanCache is not null || presetScanFailed)
        {
            return;
        }

        try
        {
            if (presetScanProgress is null)
            {
                presetScanProgress = PresetScanProgress.Create();
            }

            var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 250;
            var progressed = false;
            while (Stopwatch.GetTimestamp() < deadline && presetScanProgress.ProcessNext())
            {
                progressed = true;
            }

            if (!presetScanProgress.IsComplete)
            {
                if (progressed)
                    SavePresetScanCheckpoint(presetScanProgress.BuildResult());
                return;
            }

            var completedScan = presetScanProgress.BuildResult();
            presetScanProgress = null;
            presets = completedScan.Presets;
            presetScanCache = completedScan;
            presetScanCacheVersion = gameDataVersion;
            SavePresetScanCheckpoint(completedScan);
        }
        catch (Exception)
        {
            presetScanProgress = null;
            presetScanFailed = true;
        }
    }

    private void SavePresetScanCheckpoint(PresetScanResult scan)
    {
        presets = scan.Presets;
        if (gameDataVersion.Length == 0)
        {
            return;
        }

        config.PresetCacheSchemaVersion = PRESET_CACHE_SCHEMA_VERSION;
        config.PresetCacheGameDataVersion = gameDataVersion;
        config.CachedPresetScan = scan;
        saveConfig();
    }

    private sealed class PresetScanProgress
    {
        private readonly List<IGrouping<uint, LuminaAetheryte>> rowsByTerritory;
        private readonly List<AetherytePreset> result = [];
        private int territoryIndex;
        private int bgFiles;
        private int eventFiles;
        private int aetheryteInstances;
        private int sharedGroupCount;
        private int pathMatches;
        private int boundMatches;

        private PresetScanProgress(
            List<IGrouping<uint, LuminaAetheryte>> rowsByTerritory,
            int territoryIndex,
            IEnumerable<AetherytePreset> cachedPresets)
        {
            this.rowsByTerritory = rowsByTerritory;
            this.territoryIndex = territoryIndex;
            result.AddRange(cachedPresets);
        }

        public bool IsComplete => territoryIndex >= rowsByTerritory.Count;

        public int ScannedTerritories => territoryIndex;

        public static PresetScanProgress Create(PresetScanResult? cached = null)
        {
            var rowsByTerritory = LuminaGetter.Get<LuminaAetheryte>()
                .Where(static row => row.IsAetheryte && row.Territory.RowId != 0)
                .GroupBy(static row => row.Territory.RowId)
                .ToList();
            return new(
                rowsByTerritory,
                cached?.ScannedTerritories ?? 0,
                cached?.Presets ?? []);
        }

        public bool ProcessNext()
        {
            if (IsComplete)
            {
                return false;
            }

            var territoryRows = rowsByTerritory[territoryIndex++];
            if (!LuminaGetter.TryGetRow<LuminaTerritoryType>(territoryRows.Key, out var territory))
            {
                return true;
            }

            var sharedGroupInstances = new Dictionary<uint, string>();
            var aetherytes = new Dictionary<uint, uint>();
            foreach (var fileType in new[] { LGBFileType.BG, LGBFileType.PlanMap, LGBFileType.PlanEvent, LGBFileType.Planner })
            {
                if (TryGetLgb(territory, fileType) is not { } layoutFile)
                {
                    continue;
                }

                if (fileType == LGBFileType.BG)
                {
                    bgFiles++;
                }
                else
                {
                    eventFiles++;
                }

                foreach (var layer in layoutFile.Layers)
                {
                    foreach (var instance in layer.InstanceObjects)
                    {
                        if (instance.AssetType == LayerEntryType.SharedGroup &&
                            instance.Object is LayerCommon.SharedGroupInstanceObject sharedGroup)
                        {
                            var path = NormalizeAssetPath(sharedGroup.AssetPath);
                            if (IsSharedGroupPath(path) && sharedGroupInstances.TryAdd(instance.InstanceId, path))
                            {
                                pathMatches++;
                            }
                        }
                        else if (instance.AssetType == LayerEntryType.Aetheryte &&
                                 instance.Object is LayerCommon.AetheryteInstanceObject aetheryte &&
                                 aetherytes.TryAdd(aetheryte.ParentData.BaseId, aetheryte.BoundInstanceID))
                        {
                            aetheryteInstances++;
                        }
                    }
                }
            }

            sharedGroupCount += sharedGroupInstances.Count;
            var territoryName = territory.PlaceName.ValueNullable?.Name.ToString() ?? $"Territory {territoryRows.Key}";
            foreach (var row in territoryRows)
            {
                if (!aetherytes.TryGetValue(row.RowId, out var boundInstanceID) ||
                    !sharedGroupInstances.TryGetValue(boundInstanceID, out var sharedGroupPath))
                {
                    continue;
                }

                boundMatches++;
                var name = row.PlaceName.ValueNullable?.Name.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Aetheryte {row.RowId}";
                }

                result.Add(new(row.RowId, territoryRows.Key, name, territoryName, sharedGroupPath));
            }

            return true;
        }

        public PresetScanResult BuildResult() =>
            new(
                result
                    .OrderBy(static preset => preset.TerritoryID)
                    .ThenBy(static preset => preset.RowID)
                    .ToArray(),
                rowsByTerritory.Sum(static rows => rows.Count()),
                rowsByTerritory.Count,
                bgFiles,
                eventFiles,
                aetheryteInstances,
                sharedGroupCount,
                pathMatches,
                boundMatches,
                territoryIndex);
    }

    private static string NormalizeAssetPath(string path) =>
        path.Trim().Replace('\\', '/').ToLowerInvariant();

    private static LgbFile? TryGetLgb(LuminaTerritoryType territory, LGBFileType fileType) =>
        LgbFile.TryGet(territory, fileType, out var file) ? file : null;

    private void ReleaseResourceHooks()
    {
        getResourceSyncHook?.Dispose();
        getResourceSyncHook = null;
        getResourceAsyncHook?.Dispose();
        getResourceAsyncHook = null;
    }

    internal bool EnsureRules()
    {
        var changed = false;
        if (config.Rules is null)
        {
            config.Rules = [];
            changed = true;
        }

        var seenSources = new HashSet<(uint TerritoryID, uint RowID)>();
        for (var index = 0; index < config.Rules.Count;)
        {
            var rule = config.Rules[index];
            if (!IsSharedGroupPath(rule.SourceSharedGroupPath) ||
                !IsSharedGroupPath(rule.TargetSharedGroupPath) ||
                rule.SourceTerritoryID == 0 ||
                rule.SourceAetheryteRowID == 0 ||
                !seenSources.Add((rule.SourceTerritoryID, rule.SourceAetheryteRowID)))
            {
                config.Rules.RemoveAt(index);
                changed = true;
                continue;
            }

            index++;
        }

        return changed;
    }

    private void DisableResourceRedirect()
    {
        if (Volatile.Read(ref redirectSharedGroup))
            Volatile.Write(ref redirectTargets, new Dictionary<RedirectKey, string>());
        Volatile.Write(ref redirectSharedGroup, false);
        redirectsDirty = true;
    }

    private void UpdateResourceRedirect(bool force)
    {
        if (!force && !redirectsDirty)
            return;

        if (EnsureRules())
        {
            saveConfig();
        }
        var rules = config.Rules
            .Where(static rule => rule.Enabled &&
                                  IsSharedGroupPath(rule.SourceSharedGroupPath) &&
                                  IsSharedGroupPath(rule.TargetSharedGroupPath))
            .Select(static rule => new
            {
                TerritoryID = rule.SourceTerritoryID,
                Source = NormalizeAssetPath(rule.SourceSharedGroupPath),
                Target = NormalizeAssetPath(rule.TargetSharedGroupPath)
            })
            .Where(static rule => !rule.Source.Equals(rule.Target, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var redirects = new Dictionary<RedirectKey, string>();
        foreach (var rule in rules)
        {
            redirects.TryAdd(new(rule.TerritoryID, rule.Source), rule.Target);
        }

        Volatile.Write(ref redirectTargets, redirects);
        Volatile.Write(ref redirectSharedGroup, redirects.Count > 0);
        redirectsDirty = false;
    }

    private ResourceHandle* GetResourceSyncDetour(
        ResourceManager* manager,
        ResourceCategory* category,
        uint* type,
        uint* hash,
        CStringPointer path,
        void* unknown,
        void* unkDebugPtr,
        uint unkDebugInt) => GetResource(true, manager, category, type, hash, path, unknown, false, unkDebugPtr, unkDebugInt);

    private ResourceHandle* GetResourceAsyncDetour(
        ResourceManager* manager,
        ResourceCategory* category,
        uint* type,
        uint* hash,
        CStringPointer path,
        void* unknown,
        bool isUnknown,
        void* unkDebugPtr,
        uint unkDebugInt) => GetResource(false, manager, category, type, hash, path, unknown, isUnknown, unkDebugPtr, unkDebugInt);

    private ResourceHandle* GetResource(
        bool synchronous,
        ResourceManager* manager,
        ResourceCategory* category,
        uint* type,
        uint* hash,
        CStringPointer path,
        void* unknown,
        bool isUnknown,
        void* unkDebugPtr,
        uint unkDebugInt)
    {
        var sourcePath = path.HasValue ? path.ToString().Trim() : string.Empty;
        var redirects = Volatile.Read(ref redirectTargets);
        if (Volatile.Read(ref redirectSharedGroup) &&
            redirects.TryGetValue(
                new(Volatile.Read(ref currentTerritoryID), NormalizeAssetPath(sourcePath)),
                out var targetPath) &&
            !sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
        {
            Span<byte> utf8Path = stackalloc byte[256];
            var targetLength = Encoding.UTF8.GetBytes(targetPath, utf8Path);
            if (targetLength > 0 && targetLength < utf8Path.Length)
            {
                utf8Path[targetLength] = 0;
                path = (byte*)Unsafe.AsPointer(ref utf8Path[0]);
                var targetHash = ComputeResourceHash(targetPath);
                *hash = targetHash;
            }
        }

        return synchronous
            ? getResourceSyncHook!.Original(manager, category, type, hash, path, unknown, unkDebugPtr, unkDebugInt)
            : getResourceAsyncHook!.Original(manager, category, type, hash, path, unknown, isUnknown, unkDebugPtr, unkDebugInt);
    }

    private static bool IsSharedGroupPath(string path)
    {
        const string PREFIX = "bgcommon/world/aet/shared/for_bg/sgbg_w_aet_";
        if (!path.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".sgb", StringComparison.OrdinalIgnoreCase) ||
            path.Length <= PREFIX.Length + 4)
        {
            return false;
        }

        foreach (var value in path.AsSpan(PREFIX.Length, 3))
        {
            if (value is < '0' or > '9')
            {
                return false;
            }
        }

        return path[PREFIX.Length + 3] == '_';
    }

    private static uint ComputeResourceHash(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320u;
            }
        }

        return ~crc;
    }

    private readonly record struct RedirectKey(uint TerritoryID, string SourcePath);


    #region 常量

    private const int PRESET_CACHE_SCHEMA_VERSION = 1;

    #endregion

    public sealed class Settings
    {
        public List<AetheryteModelReplacementRule> Rules { get; set; } = [];

        public int PresetCacheSchemaVersion { get; set; }

        public string PresetCacheGameDataVersion { get; set; } = string.Empty;

        public PresetScanResult? CachedPresetScan { get; set; }

    }
}
