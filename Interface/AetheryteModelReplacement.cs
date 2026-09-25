using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

public sealed unsafe class AetheryteModelReplacement(
    AetheryteModelReplacementConfig config,
    System.Action saveConfig) : ModuleBase
{
    private const int PRESET_CACHE_SCHEMA_VERSION = 1;
    private static PresetScanResult? presetScanCache;
    private static string presetScanCacheVersion = string.Empty;
    private IReadOnlyList<AetherytePreset> presets = [];
    private Task<PresetScanResult>? presetScanTask;
    private DateTime nextPresetScanUTC;
    private string gameDataVersion = string.Empty;
    private int lastLoggedPresetCount = -1;
    private Hook<ResourceManager.Delegates.GetResourceSync>? getResourceSyncHook;
    private Hook<ResourceManager.Delegates.GetResourceAsync>? getResourceAsyncHook;
    private bool redirectSharedGroup;
    private Dictionary<RedirectKey, string> redirectTargets = [];
    private string lastRedirectState = string.Empty;
    private uint currentTerritoryID;
    private FeatureLifetime? runtimeLifetime;

    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AetheryteModelReplacementTitle"),
        Description = OmniLoc.Get("AetheryteModelReplacementDescription"),
        Category = ModuleCategory.Interface
    };

    public override bool HasSettings => true;

    public override bool DrawSettings() => AetheryteModelReplacementPanel.Draw(this, config);

    public override bool ResetSettings()
    {
        config.OutdoorModelPath = string.Empty;
        config.OutdoorModelCategory = ResourceCategory.Bg;
        config.OutdoorSharedGroupPath = string.Empty;
        config.OutdoorSharedGroupCategory = ResourceCategory.BgCommon;
        config.SourceSharedGroupPath = string.Empty;
        config.TargetSharedGroupPath = string.Empty;
        config.Rules = [];
        saveConfig();
        DisableResourceRedirect();
        return true;
    }

    protected override void OnEnable()
    {
        EnsureRules();
        gameDataVersion = GetGameDataVersion();
        if (gameDataVersion.Length > 0 &&
            config.PresetCacheSchemaVersion == PRESET_CACHE_SCHEMA_VERSION &&
            string.Equals(config.PresetCacheGameDataVersion, gameDataVersion, StringComparison.Ordinal) &&
            config.CachedPresetScan is { Presets.Count: > 0 } cached)
        {
            presetScanCache = cached;
            presetScanCacheVersion = gameDataVersion;
        }
        else if (!string.Equals(presetScanCacheVersion, gameDataVersion, StringComparison.Ordinal) ||
                 gameDataVersion.Length == 0)
        {
            presetScanCache = null;
        }

        presets = presetScanCache?.Presets ?? [];
        nextPresetScanUTC = presetScanTask is null
            ? default
            : DateTime.UtcNow.AddSeconds(30);
        lastLoggedPresetCount = -1;
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
            DisableResourceRedirect();
        }
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

        var now = DateTime.UtcNow;
        if (presetScanTask is null ||
            (presets.Count == 0 && presetScanTask.IsCompleted && now >= nextPresetScanUTC))
        {
            nextPresetScanUTC = now.AddSeconds(30);
            presetScanTask = Task.Run(BuildPresets);
        }

        if (presetScanTask.IsCompletedSuccessfully)
        {
            var scan = presetScanTask.Result;
            presets = scan.Presets;
            if (scan.Presets.Count > 0)
            {
                presetScanCache = scan;
                presetScanCacheVersion = gameDataVersion;
                if (gameDataVersion.Length > 0)
                {
                    config.PresetCacheSchemaVersion = PRESET_CACHE_SCHEMA_VERSION;
                    config.PresetCacheGameDataVersion = gameDataVersion;
                    config.CachedPresetScan = scan;
                    saveConfig();
                }
            }

        }
        else if (presetScanTask.IsFaulted && lastLoggedPresetCount != -2)
        {
            lastLoggedPresetCount = -2;
            DService.Instance().Log.Error(
                presetScanTask.Exception?.GetBaseException(),
                "[AetheryteModelReplacement] 水晶预设扫描失败。");
        }

        return presets;
    }

    internal bool IsPresetScanPending => presetScanCache is null && presetScanTask is { IsCompleted: false };

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

    private static PresetScanResult BuildPresets()
    {
        var rowsByTerritory = LuminaGetter.Get<LuminaAetheryte>()
            .Where(static row => row.IsAetheryte && row.Territory.RowId != 0)
            .GroupBy(static row => row.Territory.RowId)
            .ToList();
        var result = new List<AetherytePreset>();
        var bgFiles = 0;
        var eventFiles = 0;
        var aetheryteInstances = 0;
        var sharedGroupCount = 0;
        var pathMatches = 0;
        var boundMatches = 0;

        foreach (var territoryRows in rowsByTerritory)
        {
            if (!LuminaGetter.TryGetRow<LuminaTerritoryType>(territoryRows.Key, out var territory))
            {
                continue;
            }

            var sharedGroupInstances = new Dictionary<uint, string>();
            var aetherytes = new Dictionary<uint, uint>();
            // 地图布局会把 BG、计划层的共享组和以太之光实例合并到同一实例 ID 空间。
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
                                 instance.Object is LayerCommon.AetheryteInstanceObject aetheryte)
                        {
                            if (aetherytes.TryAdd(aetheryte.ParentData.BaseId, aetheryte.BoundInstanceID))
                            {
                                aetheryteInstances++;
                            }
                        }
                    }
                }
            }

            sharedGroupCount += sharedGroupInstances.Count;

            var territoryName = territory.PlaceName.ValueNullable?.Name.ToString() ?? $"Territory {territoryRows.Key}";
            foreach (var row in territoryRows)
            {
                if (!aetherytes.TryGetValue(row.RowId, out var boundInstanceID))
                {
                    continue;
                }

                if (!sharedGroupInstances.TryGetValue(boundInstanceID, out var sharedGroupPath))
                {
                    continue;
                }

                boundMatches++;

                var name = row.PlaceName.ValueNullable?.Name.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Aetheryte {row.RowId}";
                }

                result.Add(new(
                    row.RowId,
                    territoryRows.Key,
                    name,
                    territoryName,
                    sharedGroupPath));
            }
        }

        return new(
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
            boundMatches);
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
        Volatile.Write(ref redirectTargets, new Dictionary<RedirectKey, string>());
        Volatile.Write(ref redirectSharedGroup, false);
        lastRedirectState = string.Empty;
    }

    private void UpdateResourceRedirect(bool force)
    {
        EnsureRules();
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
        var state = string.Join('|', rules.Select(static rule => $"{rule.TerritoryID}:{rule.Source}>{rule.Target}"));
        if (!force && state == lastRedirectState)
        {
            return;
        }

        var redirects = new Dictionary<RedirectKey, string>();
        foreach (var rule in rules)
        {
            redirects.TryAdd(new(rule.TerritoryID, rule.Source), rule.Target);
        }

        Volatile.Write(ref redirectTargets, redirects);
        Volatile.Write(ref redirectSharedGroup, redirects.Count > 0);
        lastRedirectState = state;
    }

    private ResourceHandle* GetResourceSyncDetour(
        ResourceManager* manager,
        ResourceCategory* category,
        uint* type,
        uint* hash,
        CStringPointer path,
        void* unknown,
        void* unkDebugPtr,
        uint unkDebugInt)
    {
        return GetResource(true, manager, category, type, hash, path, unknown, false, unkDebugPtr, unkDebugInt);
    }

    private ResourceHandle* GetResourceAsyncDetour(
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
        return GetResource(false, manager, category, type, hash, path, unknown, isUnknown, unkDebugPtr, unkDebugInt);
    }

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
            (redirects.TryGetValue(
                 new(Volatile.Read(ref currentTerritoryID), NormalizeAssetPath(sourcePath)),
                 out var targetPath) ||
             redirects.TryGetValue(new(0, NormalizeAssetPath(sourcePath)), out targetPath)) &&
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
        const string prefix = "bgcommon/world/aet/shared/for_bg/sgbg_w_aet_";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".sgb", StringComparison.OrdinalIgnoreCase) ||
            path.Length <= prefix.Length + 4)
        {
            return false;
        }

        foreach (var value in path.AsSpan(prefix.Length, 3))
        {
            if (value is < '0' or > '9')
            {
                return false;
            }
        }

        return path[prefix.Length + 3] == '_';
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

}

public sealed record PresetScanResult(
    IReadOnlyList<AetherytePreset> Presets,
    int AetheryteRows,
    int Territories,
    int BgFiles,
    int EventFiles,
    int AetheryteInstances,
    int SharedGroups,
    int PathMatches,
    int BoundMatches);

public sealed record AetherytePreset(
    uint RowID,
    uint TerritoryID,
    string Name,
    string TerritoryName,
    string SharedGroupPath)
{
    [Newtonsoft.Json.JsonIgnore]
    public string DisplayName => $"{Name} ({TerritoryName})";
}

internal static class AetheryteModelReplacementPanel
{
    public static bool Draw(AetheryteModelReplacement module, AetheryteModelReplacementConfig config)
    {
        var presets = module.GetPresets();
        if (module.IsPresetScanPending)
        {
            ImGui.TextUnformatted(OmniLoc.Get("Feature.AetheryteModelReplacement.PresetsLoading"));
            return false;
        }

        if (presets.Count == 0)
        {
            ImGui.TextUnformatted(OmniLoc.Get("Feature.AetheryteModelReplacement.PresetsUnavailable"));
            return false;
        }

        var changed = module.EnsureRules();
        var rules = config.Rules;
        var rowContentHeight = MathF.Max(
            OmniTheme.CheckboxSize(),
            MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight()));
        var deleteLabel = OmniLoc.Get("Feature.AetheryteModelReplacement.DeleteRule");
        var addLabel = OmniLoc.Get("Feature.AetheryteModelReplacement.AddRule");
        var deleteSize = OmniControls.CompactButtonSize(deleteLabel);
        var addSize = OmniControls.CompactButtonSize(addLabel);
        var actionLabel = OmniLoc.Get("Feature.AetheryteModelReplacement.Actions");
        var actionWidth = MathF.Max(deleteSize.X, addSize.X) +
                          ImGui.GetStyle().CellPadding.X * 2f;
        var visibleRows = Math.Min(5, Math.Max(1, rules.Count + 1));
        var tableHeight = rowContentHeight * (visibleRows + 1) +
                          ImGui.GetStyle().CellPadding.Y * (visibleRows + 2) * 2f +
                          ImGui.GetStyle().FrameBorderSize * 2f;
        using (var table = ImRaii.Table(
                   "##aetheryteModelReplacementSettings",
                   4,
                   ImGuiTableFlags.Borders |
                   ImGuiTableFlags.RowBg |
                   ImGuiTableFlags.ScrollY |
                   ImGuiTableFlags.SizingStretchProp,
                   new Vector2(ImGui.GetContentRegionAvail().X, tableHeight)))
        {
            if (!table)
            {
                return changed;
            }

            var enabledColumnWidth = OmniTheme.CheckboxSize() + ImGui.GetStyle().CellPadding.X * 2f;
            ImGui.TableSetupColumn("##aetheryteEnabled", ImGuiTableColumnFlags.WidthFixed, enabledColumnWidth);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.AetheryteModelReplacement.Source"),
                ImGuiTableColumnFlags.WidthStretch,
                1f);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.AetheryteModelReplacement.Target"),
                ImGuiTableColumnFlags.WidthStretch,
                1f);
            ImGui.TableSetupColumn(
                actionLabel,
                ImGuiTableColumnFlags.WidthFixed,
                actionWidth);
            ImGui.TableSetupScrollFreeze(0, 1);
            OmniControls.BeginTableHeaderRow(rowContentHeight);
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
            var allEnabled = rules.Count > 0 && rules.All(static rule => rule.Enabled);
            using (ImRaii.Disabled(rules.Count == 0))
            {
                OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
                if (OmniControls.Checkbox("##aetheryteAllEnabled", ref allEnabled))
                {
                    foreach (var rule in rules)
                    {
                        rule.Enabled = allEnabled;
                    }

                    changed = true;
                }
            }

            OmniControls.TableHeader(OmniLoc.Get("Feature.AetheryteModelReplacement.Source"), rowContentHeight);
            OmniControls.TableHeader(OmniLoc.Get("Feature.AetheryteModelReplacement.Target"), rowContentHeight);
            OmniControls.TableHeader(actionLabel, rowContentHeight);

            var removeIndex = -1;
            for (var index = 0; index < rules.Count; index++)
            {
                var rule = rules[index];
                ImGui.PushID($"aetheryteRule{index}");
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
                ImGui.TableNextColumn();
                var enabled = rule.Enabled;
                OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
                if (OmniControls.Checkbox("##enabled", ref enabled))
                {
                    rule.Enabled = enabled;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var usedSources = rules
                    .Where((candidate, candidateIndex) => candidateIndex != index)
                    .Select(static candidate => (candidate.SourceTerritoryID, candidate.SourceAetheryteRowID))
                    .ToHashSet();
                var sourceOptions = presets
                    .Where(preset => !usedSources.Contains((preset.TerritoryID, preset.RowID)) ||
                                     (preset.TerritoryID == rule.SourceTerritoryID &&
                                      preset.RowID == rule.SourceAetheryteRowID))
                    .ToArray();
                var source = sourceOptions.FirstOrDefault(preset =>
                                 preset.TerritoryID == rule.SourceTerritoryID &&
                                 preset.RowID == rule.SourceAetheryteRowID) ??
                             sourceOptions.FirstOrDefault(preset =>
                                 preset.SharedGroupPath.Equals(rule.SourceSharedGroupPath, StringComparison.OrdinalIgnoreCase)) ??
                             sourceOptions.FirstOrDefault();
                OmniControls.CenterTableItem(
                    new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()),
                    rowContentHeight);
                if (source is not null && DrawPresetCombo("source", sourceOptions, source, true, out var selectedSource))
                {
                    rule.SourceTerritoryID = selectedSource.TerritoryID;
                    rule.SourceAetheryteRowID = selectedSource.RowID;
                    rule.SourceSharedGroupPath = selectedSource.SharedGroupPath;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var target = presets.FirstOrDefault(preset =>
                                  preset.SharedGroupPath.Equals(rule.TargetSharedGroupPath, StringComparison.OrdinalIgnoreCase)) ??
                             presets[0];
                OmniControls.CenterTableItem(
                    new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()),
                    rowContentHeight);
                if (DrawPresetCombo("target", presets, target, false, out var selectedTarget))
                {
                    rule.TargetSharedGroupPath = selectedTarget.SharedGroupPath;
                    changed = true;
                }

                ImGui.TableNextColumn();
                OmniControls.CenterTableItem(deleteSize, rowContentHeight);
                if (OmniControls.SmallButton($"{deleteLabel}##delete", false, deleteSize))
                {
                    removeIndex = index;
                }

                ImGui.PopID();
            }

            if (removeIndex >= 0)
            {
                rules.RemoveAt(removeIndex);
                changed = true;
            }

            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            OmniControls.CenterTableItem(addSize, rowContentHeight);
            if (OmniControls.SmallButton($"{addLabel}##add", false, addSize))
            {
                changed |= AddRule(config, presets);
            }
        }

        return changed;
    }

    private static bool AddRule(
        AetheryteModelReplacementConfig config,
        IReadOnlyList<AetherytePreset> presets)
    {
        var usedSources = config.Rules
            .Select(static rule => (rule.SourceTerritoryID, rule.SourceAetheryteRowID))
            .ToHashSet();
        var source = presets.FirstOrDefault(preset => !usedSources.Contains((preset.TerritoryID, preset.RowID)));
        if (source is null)
        {
            return false;
        }

        var target = presets.FirstOrDefault(preset =>
                         !preset.SharedGroupPath.Equals(source.SharedGroupPath, StringComparison.OrdinalIgnoreCase)) ??
                     source;
        config.Rules.Add(new(source.SharedGroupPath, target.SharedGroupPath)
        {
            SourceTerritoryID = source.TerritoryID,
            SourceAetheryteRowID = source.RowID
        });
        return true;
    }

    private static bool DrawPresetCombo(
        string id,
        IReadOnlyList<AetherytePreset> options,
        AetherytePreset current,
        bool matchIdentity,
        out AetherytePreset selected)
    {
        selected = current;
        if (!OmniControls.BeginCombo(
                $"##aetheryteModelReplacement{id}",
                current.DisplayName,
                MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
        {
            return false;
        }

        var changed = false;
        foreach (var option in options)
        {
            var isSelected = matchIdentity
                ? option.TerritoryID == current.TerritoryID && option.RowID == current.RowID
                : option.SharedGroupPath.Equals(current.SharedGroupPath, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(option.DisplayName, isSelected))
            {
                selected = option;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }
}

[Serializable]
public sealed class AetheryteModelReplacementConfig
{
    public List<AetheryteModelReplacementRule> Rules { get; set; } = [];

    public int PresetCacheSchemaVersion { get; set; }

    public string PresetCacheGameDataVersion { get; set; } = string.Empty;

    public PresetScanResult? CachedPresetScan { get; set; }

    public string OutdoorModelPath { get; set; } = string.Empty;

    public ResourceCategory OutdoorModelCategory { get; set; } = ResourceCategory.Bg;

    public string OutdoorSharedGroupPath { get; set; } = string.Empty;

    public ResourceCategory OutdoorSharedGroupCategory { get; set; } = ResourceCategory.BgCommon;

    public string SourceSharedGroupPath { get; set; } = string.Empty;

    public string TargetSharedGroupPath { get; set; } = string.Empty;
}

[Serializable]
public sealed class AetheryteModelReplacementRule
{
    public AetheryteModelReplacementRule()
    {
    }

    public AetheryteModelReplacementRule(string sourceSharedGroupPath, string targetSharedGroupPath)
    {
        SourceSharedGroupPath = sourceSharedGroupPath;
        TargetSharedGroupPath = targetSharedGroupPath;
    }

    public uint SourceTerritoryID { get; set; }

    public uint SourceAetheryteRowID { get; set; }

    public bool Enabled { get; set; } = true;

    public string SourceSharedGroupPath { get; set; } = string.Empty;

    public string TargetSharedGroupPath { get; set; } = string.Empty;
}
