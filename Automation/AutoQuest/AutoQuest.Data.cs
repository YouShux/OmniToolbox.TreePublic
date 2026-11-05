using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Dalamud;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Quests;
using TreeTools.Threading;
using GameQuest = Lumina.Excel.Sheets.Quest;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    private sealed partial class QuestData : IAsyncDisposable
    {
        private readonly Dictionary<uint, QuestDefinition> definitions = [];
        private readonly Dictionary<uint, ScriptLoad> scripts = [];
        private readonly Dictionary<uint, Dictionary<uint, QuestLocation[]>> layers = [];
        private readonly HashSet<uint> loadingLayers = [];
        private readonly Dictionary<uint, string> layerErrors = [];
        private readonly List<Task> pending = [];
        private readonly CancellationTokenSource cancellation = new();
        private readonly Dictionary<uint, QuestTurnInEvidence> pendingTurnIns = [];
        private readonly HashSet<uint> completedRepeats = [];
        private readonly Dictionary<uint, Dictionary<int, string>> journalTexts = [];
        private Dictionary<string, uint>? systemSymbols;
        private FeatureLifetime? lifetime;
        private string? roundID;
        private ulong roundCharacterID;
        private QuestTarget[]? catalog;
        private ClassJob[]? classJobs;
        private bool loadingCatalog;
        private string? catalogError;
        private bool disposed;

        public bool CatalogReady => catalog is not null;
        public string? CatalogError => catalogError;

        public QuestTravelTarget? LocateSupply(uint territoryID, uint objectID, out string? error)
        {
            var locations = GetLayerPositions(territoryID, objectID, out error);
            if (locations.Length == 1)
                return new(territoryID, locations[0].Position, 3);
            error ??= "supply_location_unresolved";
            return null;
        }

        public void BeginRound(string operationID)
        {
            roundID = operationID;
            roundCharacterID = DalamudServices.PlayerState.ContentId;
            pendingTurnIns.Clear();
            completedRepeats.Clear();
        }

        public void EndRound(string operationID)
        {
            if (roundID != operationID) return;
            roundID = null;
            roundCharacterID = 0;
            pendingTurnIns.Clear();
            completedRepeats.Clear();
        }

        public void MarkTurnInRequested(QuestTarget target)
        {
            target = target.Normalize();
            if (target.Type != QuestTargetType.Quest || roundID is null ||
                roundCharacterID != DalamudServices.PlayerState.ContentId || pendingTurnIns.ContainsKey(target.ID) ||
                !TryDefinition(target.ID, out var definition) || !definition.Repeatable)
                return;
            var state = ReadState(target.ID);
            if (state.Accepted && state.Sequence == 255)
                pendingTurnIns[target.ID] = new(state.Completed, ReadDailyCompleted(target.ID));
        }

        public void Bind(FeatureLifetime scope)
        {
            if (lifetime is not null) return;
            lifetime = scope;
            scope.OnStopping(cancellation.Cancel);
            scope.AddAsync(DisposeAsync);
        }

        public QuestCatalogPage GetCatalog(QuestCatalogQuery query)
        {
            EnsureCatalog();
            var source = catalog ?? QuestBaseline.Targets;
            var search = query.Search?.Trim() ?? string.Empty;
            var filtered = source.Where(target => (query.Type is null || target.Type == query.Type) &&
                (search.Length == 0 || NameOf(target).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 target.ID.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.Ordinal))).ToArray();
            var offset = Math.Max(0, query.Offset);
            var entries = filtered.Skip(offset).Take(Math.Clamp(query.Limit, 1, 200)).Select(target =>
            {
                var evaluation = EvaluateTarget(target);
                return new QuestCatalogEntry(target, evaluation.Name, evaluation.Availability, evaluation.Coverage, evaluation.Blockers);
            }).ToArray();
            return new(filtered.Length, offset, entries, QuestBaseline.Hash, ResourceVersion());
        }

        public QuestEvaluation Evaluate(QuestRequest request)
        {
            if (request is null || !request.IsValid())
                return new(false, false, [], "request_invalid");
            var targets = ExpandTargets(request).Select(EvaluateTarget).ToArray();
            if (targets.Length == 0)
                return new(false, false, [], catalogError ?? (loadingCatalog ? "resource_loading" : "targets_empty"));
            var satisfied = request.Relation == QuestTargetRelation.Any
                ? targets.Any(static result => result.Availability == QuestAvailability.Satisfied)
                : targets.All(static result => result.Availability == QuestAvailability.Satisfied);
            var executable = satisfied || (request.Relation == QuestTargetRelation.Any
                ? targets.Any(static result => result.Availability is QuestAvailability.Ready or QuestAvailability.Waiting)
                : targets.All(static result => result.Availability is QuestAvailability.Satisfied or QuestAvailability.Ready or QuestAvailability.Waiting));
            return new(satisfied, executable, targets);
        }

        public unsafe QuestTarget[] ExpandTargets(QuestRequest request)
        {
            if (request.Mode == QuestRequestMode.Catalog)
            {
                EnsureCatalog();
                return catalog is null ? [] : OrderCatalog(catalog);
            }
            var targets = request.Targets.Where(static target => target is not null).Select(static target => target.Normalize()).Distinct().ToArray();
            if (request.Relation != QuestTargetRelation.All)
                return targets;
            var accepted = new HashSet<uint>();
            var manager = QuestManager.Instance();
            if (manager != null)
                foreach (var work in manager->NormalQuests)
                    if (work.QuestId != 0) accepted.Add(NormalizeID(work.QuestId));
            return targets.OrderBy(target => target.Type == QuestTargetType.Quest && accepted.Contains(target.ID) ? 0 : 1).ToArray();
        }

        public unsafe bool IsSatisfied(QuestTarget target)
        {
            target = target.Normalize();
            var manager = QuestManager.Instance();
            if (manager == null || !DService.Instance().ClientState.IsLoggedIn) return false;
            if (target.Type == QuestTargetType.UnlockLink)
                return UIState.Instance() != null && UIState.Instance()->IsUnlockLinkUnlocked(target.ID);
            if (target.Type != QuestTargetType.Quest || !IsValidQuestID(target.ID)) return false;
            var state = ReadState(target.ID);
            if (target.Sequence is { } sequence && state.Accepted && state.Sequence >= sequence) return true;
            if (!TryDefinition(target.ID, out var definition)) return false;
            if (!definition.Repeatable) return state.Completed;
            if (roundID is null || roundCharacterID != DalamudServices.PlayerState.ContentId) return false;
            if (completedRepeats.Contains(target.ID)) return true;
            if (!state.Accepted && pendingTurnIns.TryGetValue(target.ID, out var evidence) &&
                (!evidence.Completed && state.Completed || evidence.DailyCompleted != true && ReadDailyCompleted(target.ID) == true))
            {
                completedRepeats.Add(target.ID);
                pendingTurnIns.Remove(target.ID);
                return true;
            }
            return false;
        }

        public byte GetSequence(QuestTarget target) => target.Type == QuestTargetType.Quest
            ? ReadState(target.Normalize().ID).Sequence : (byte)0;

        public byte GetAcceptedClassJob(QuestTarget target) => target.Type == QuestTargetType.Quest
            ? ReadState(target.Normalize().ID).AcceptClassJob : (byte)0;

        public unsafe string ReadStateKey(QuestTarget target)
        {
            target = target.Normalize();
            if (target.Type != QuestTargetType.Quest)
                return $"{target.Type}:{target.ID}:{IsSatisfied(target)}";
            var state = ReadState(target.ID);
            return $"{target.ID}:{state.Accepted}:{state.Completed}:{state.Sequence}:{state.AcceptClassJob}:{Convert.ToHexString(state.Variables)}";
        }

        public bool IsStepComplete(QuestTarget target, QuestStep step)
        {
            if (step.Kind == QuestStepKind.SwitchJob) return LocalPlayerState.ClassJob == step.ClassJobID;
            if (step.Completion.ItemID != 0 && CountItem(step.Completion.ItemID, step.Completion.RequireHQ) >= step.Completion.Quantity)
                return true;
            var state = ReadState(target.Normalize().ID);
            if (step.Kind == QuestStepKind.AcceptQuest) return state.Accepted;
            if (step.Kind == QuestStepKind.CompleteQuest) return IsSatisfied(target) && !state.Accepted;
            if (!state.Accepted) return IsSatisfied(target);
            if (state.Sequence != step.Sequence) return true;
            if (step.Completion.TodoIndex is { } index && TryReadTodo(target.ID, index, out var completed) && completed)
                return true;
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            cancellation.Cancel();
            Task[] work;
            lock (pending) work = [.. pending];
            try
            {
                await Task.WhenAll(work).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            cancellation.Dispose();
        }

        private string NameOf(QuestTarget target) => definitions.TryGetValue(target.ID, out var definition)
            ? definition.Name : $"{target.Type} {target.ID}";

        private unsafe QuestTarget[] OrderCatalog(QuestTarget[] targets)
        {
            var accepted = new HashSet<uint>();
            var manager = QuestManager.Instance();
            if (manager != null)
                foreach (var work in manager->NormalQuests)
                    if (work.QuestId != 0) accepted.Add(NormalizeID(work.QuestId));
            var territoryID = DService.Instance().ClientState.TerritoryType;
            var position = DService.Instance().ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
            return targets.OrderBy(target => accepted.Contains(target.ID) ? 0 : 1)
                .ThenBy(target => definitions.TryGetValue(target.ID, out var definition) && definition.Issuer is { } issuer &&
                    issuer.TerritoryID == territoryID ? Vector3.DistanceSquared(position, issuer.Position) : float.MaxValue)
                .ThenBy(static target => target.Type).ThenBy(static target => target.ID).ToArray();
        }

        private void Track(Task task)
        {
            lock (pending) pending.Add(task);
            lifetime?.Track(task);
        }

        private void EnsureCatalog()
        {
            if (catalog is not null || loadingCatalog || disposed) return;
            loadingCatalog = true;
            Track(LoadCatalogAsync());
        }

        private async Task LoadCatalogAsync()
        {
            try
            {
                var targets = new HashSet<QuestTarget>(QuestBaseline.Targets);
                var ids = QuestBaseline.Targets.Where(static target => target.Type == QuestTargetType.Quest)
                    .Select(static target => target.ID).Distinct().ToList();
                var queued = new HashSet<uint>(ids);
                await FrameworkWorkScheduler.RunAsync(ids, id =>
                {
                    if (!TryDefinition(id, out var definition)) return;
                    foreach (var prerequisite in definition.Prerequisites)
                    {
                        if (!queued.Add(prerequisite.ID)) continue;
                        ids.Add(prerequisite.ID);
                        targets.Add(new(QuestTargetType.Quest, prerequisite.ID));
                    }
                }, cancellation.Token);
                catalog = targets.OrderBy(static target => target.Type).ThenBy(static target => target.ID).ToArray();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                catalogError = "quest_catalog_unavailable";
                DalamudServices.PluginLog.Warning(exception, "AutoQuest catalog could not be loaded.");
            }
            finally
            {
                loadingCatalog = false;
            }
        }

        private bool TryDefinition(uint id, [NotNullWhen(true)] out QuestDefinition? definition)
        {
            id = NormalizeID(id);
            if (definitions.TryGetValue(id, out var cached))
            {
                definition = cached;
                return true;
            }
            definition = null;
            if (!IsValidQuestID(id) || !LuminaGetter.TryGetRow<GameQuest>(id, out var row) || row.Name.IsEmpty)
                return false;
            definition = Capture(row);
            definitions[id] = definition;
            return true;
        }

        private QuestDefinition Capture(GameQuest row)
        {
            systemSymbols ??= LuminaGetter.Get<QuestSystemDefine>().Where(static value => !value.Text.IsEmpty && value.DefineValue >= 0)
                .ToDictionary(static value => value.Text.ToString(), static value => (uint)value.DefineValue, StringComparer.Ordinal);
            var symbols = new Dictionary<string, uint>(systemSymbols, StringComparer.Ordinal);
            foreach (var entry in row.QuestParams)
            {
                var name = entry.ScriptInstruction.ToString();
                if (name.Length != 0) symbols[name] = entry.ScriptArg;
            }
            if (LuminaGetter.TryGetSubRowAll<QuestDefineClient>(row.RowId, out var clientDefinitions))
                foreach (var entry in clientDefinitions)
                    if (!entry.Text.IsEmpty) symbols[entry.Text.ToString()] = entry.DefineVaue.RowId;
            var objectives = new List<QuestObjective>();
            for (var index = 0; index < row.TodoParams.Count; index++)
            {
                var todo = row.TodoParams[index];
                if (todo.ToDoCompleteSeq == 0) continue;
                var locations = todo.ToDoLocation.Where(static location => location.RowId != 0)
                    .Select(location => LuminaGetter.TryGetRow<Level>(location.RowId, out var level) ? Location(level) : null)
                    .OfType<QuestLocation>().ToArray();
                objectives.Add(new(index, todo.ToDoCompleteSeq, todo.ToDoQty, locations));
            }
            var issuer = LuminaGetter.TryGetRow<Level>(row.IssuerLocation.RowId, out var issuerLevel) ? Location(issuerLevel) : null;
            var prerequisites = row.PreviousQuest.Select((quest, index) => quest.RowId == 0 ? null :
                new QuestTarget(QuestTargetType.Quest, NormalizeID(quest.RowId), index == 0 && row.Unknown7 != 0 ? row.Unknown7 : null))
                .OfType<QuestTarget>().ToArray();
            return new(row.RowId, row.Name.ToString(), row.Id.ToString(), row.IsRepeatable, row.QuestRepeatFlag.RowId,
                row.IssuerStart.RowId, row.TargetEnd.RowId, issuer, prerequisites, row.PreviousQuestJoin,
                row.QuestLock.Where(static quest => quest.RowId != 0).Select(static quest => NormalizeID(quest.RowId)).ToArray(), row.QuestLockJoin,
                row.ClassJobCategory0.RowId, row.ClassJobCategory1.RowId, row.ClassJobLevel[0], row.ClassJobLevel[1],
                row.ClassJobRequired.RowId, row.GrandCompany.RowId, row.GrandCompanyRank.RowId, row.Expansion.RowId,
                row.Festival.RowId, row.BellStart, row.BellEnd, row.BeastTribe.RowId, row.BeastReputationRank.RowId,
                row.MountRequired.RowId, row.SatisfactionNpc.RowId, row.SatisfactionLevel, row.IsHouseRequired,
                row.InstanceContent.Where(static instance => instance.RowId != 0).Select(static instance => instance.RowId).ToArray(),
                row.InstanceContentJoin, symbols, [.. objectives], row.QuestListenerParams.Where(static listener => listener.Listener != 0)
                    .Select(static listener => new QuestListener(listener.Listener, listener.ConditionValue, listener.ActorSpawnSeq, listener.ActorDespawnSeq,
                        listener.QuestUInt8A, listener.ConditionType, listener.ConditionOperator, listener.Unknown0, listener.Unknown1)).ToArray());
        }

        private static QuestLocation? Location(Level level) => level.Territory.RowId == 0 ? null :
            new(level.Object.RowId, level.Territory.RowId, new(level.X, level.Y, level.Z), level.Radius, level.RowId, level.Type);

        private string TodoText(QuestDefinition definition, int index)
        {
            if (journalTexts.TryGetValue(definition.ID, out var cached)) return cached.GetValueOrDefault(index) ?? string.Empty;
            var texts = new Dictionary<int, string>();
            journalTexts[definition.ID] = texts;
            var separator = definition.ScriptName.LastIndexOf('_');
            if (separator < 0 || separator + 4 > definition.ScriptName.Length) return string.Empty;
            try
            {
                var sheet = DalamudServices.DataManager.GameData.Excel.GetSheet<RawRow>(name:
                    $"quest/{definition.ScriptName.Substring(separator + 1, 3)}/{definition.ScriptName}");
                foreach (var row in sheet)
                {
                    var key = row.ReadStringColumn(0).ToString();
                    var marker = key.LastIndexOf("_TODO_", StringComparison.Ordinal);
                    if (marker >= 0 && int.TryParse(key.AsSpan(marker + 6), out var todoIndex))
                        texts[todoIndex] = row.ReadStringColumn(1).ToString();
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException or FileNotFoundException)
            {
                DalamudServices.PluginLog.Warning(exception, "AutoQuest journal could not be loaded for {QuestID}.", definition.ID);
            }
            return texts.GetValueOrDefault(index) ?? string.Empty;
        }

        private QuestScript? GetScript(QuestDefinition definition, out string? error)
        {
            if (!scripts.TryGetValue(definition.ID, out var load))
            {
                load = new();
                scripts.Add(definition.ID, load);
                Track(LoadScriptAsync(definition, load));
            }
            error = load.Error ?? (load.Script is null ? "script_loading" : null);
            return load.Script;
        }

        private async Task LoadScriptAsync(QuestDefinition definition, ScriptLoad load)
        {
            try
            {
                var separator = definition.ScriptName.LastIndexOf('_');
                if (separator < 0 || separator + 4 > definition.ScriptName.Length)
                {
                    load.Error = "quest_script_unavailable";
                    return;
                }
                var path = $"game_script/quest/{definition.ScriptName.Substring(separator + 1, 3)}/{definition.ScriptName}.luab";
                var bytes = await Task.Run(() => DalamudServices.DataManager.GetFile<Lumina.Data.FileResource>(path)?.Data?.ToArray(), cancellation.Token).ConfigureAwait(false);
                if (bytes is null)
                {
                    await DalamudServices.Framework.RunOnFrameworkThread(() => load.Error = "quest_script_unavailable");
                    return;
                }
                var script = await Task.Run(() => QuestScript.Parse(bytes, cancellation.Token), cancellation.Token).ConfigureAwait(false);
                await DalamudServices.Framework.RunOnFrameworkThread(() =>
                {
                    load.Script = script;
                    load.Error = script.Error;
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                await DalamudServices.Framework.RunOnFrameworkThread(() => load.Error = "quest_script_unavailable");
                DalamudServices.PluginLog.Warning(exception, "AutoQuest script could not be loaded for {QuestID}.", definition.ID);
            }
        }

        private QuestLocation[] GetLayerPositions(uint territoryID, uint objectID, out string? error)
        {
            error = null;
            if (layers.TryGetValue(territoryID, out var positions)) return positions.GetValueOrDefault(objectID) ?? [];
            if (layerErrors.TryGetValue(territoryID, out error)) return [];
            if (loadingLayers.Add(territoryID))
            {
                if (!LuminaGetter.TryGetRow<TerritoryType>(territoryID, out var territory))
                {
                    error = layerErrors[territoryID] = "territory_unavailable";
                    return [];
                }
                var background = territory.Bg.ToString();
                Track(LoadLayersAsync(territoryID, background));
            }
            error = "resource_loading";
            return [];
        }

        private async Task LoadLayersAsync(uint territoryID, string background)
        {
            try
            {
                var separator = background.LastIndexOf('/');
                if (separator < 0) throw new InvalidDataException("territory_resource_unavailable");
                var placements = await Task.Run(() =>
                {
                    var found = new Dictionary<uint, List<QuestLocation>>();
                    foreach (var name in new[] { "planevent", "planmap", "planlive" })
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var file = DalamudServices.DataManager.GetFile<LgbFile>($"bg/{background[..separator]}/{name}.lgb");
                        if (file is null) continue;
                        foreach (var layer in file.Layers)
                            foreach (var instance in layer.InstanceObjects)
                            {
                                var objectID = instance.Object switch
                                {
                                    LayerCommon.ENPCInstanceObject npc => npc.ParentData.ParentData.BaseId,
                                    LayerCommon.EventInstanceObject obj => obj.ParentData.BaseId,
                                    _ => 0u
                                };
                                if (objectID == 0) continue;
                                if (!found.TryGetValue(objectID, out var locations)) found[objectID] = locations = [];
                                var position = instance.Transform.Translation;
                                locations.Add(new(objectID, territoryID, new(position.X, position.Y, position.Z), 0));
                            }
                    }
                    return found.ToDictionary(static entry => entry.Key, static entry => entry.Value.Distinct().ToArray());
                }, cancellation.Token).ConfigureAwait(false);
                await DalamudServices.Framework.RunOnFrameworkThread(() => layers[territoryID] = placements);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                await DalamudServices.Framework.RunOnFrameworkThread(() => layerErrors[territoryID] = "territory_resource_unavailable");
                DalamudServices.PluginLog.Warning(exception, "AutoQuest placements could not be loaded for {TerritoryID}.", territoryID);
            }
        }

        private static unsafe QuestState ReadState(uint id)
        {
            var manager = QuestManager.Instance();
            if (manager == null || !IsValidQuestID(id) || !DService.Instance().ClientState.IsLoggedIn) return new(false, false, 0, [], 0);
            var work = manager->GetQuestById((ushort)id);
            return new(manager->IsQuestAccepted(id), QuestManager.IsQuestComplete(id), QuestManager.GetQuestSequence(id),
                work == null ? [] : work->Variables.ToArray(), work == null ? (byte)0 : work->AcceptClassJob);
        }

        private static unsafe bool TryReadTodo(uint id, int index, out bool completed)
        {
            completed = false;
            var events = EventFramework.Instance();
            var player = DService.Instance().ObjectTable.LocalPlayer;
            if (events == null || player is null || index is < 0 or > 23) return false;
            var handler = events->GetEventHandlerById(NormalizeID(id));
            if (handler == null || handler->Info.EventId.ContentId != EventHandlerContent.Quest) return false;
            completed = ((QuestEventHandler*)handler)->IsTodoChecked((BattleChara*)player.Address, (byte)index);
            return true;
        }

        private static unsafe bool? ReadDailyCompleted(uint id)
        {
            var manager = QuestManager.Instance();
            if (manager == null || !IsValidQuestID(id) || !DService.Instance().ClientState.IsLoggedIn) return null;
            var work = manager->GetDailyQuestById((ushort)id);
            return work == null ? null : work->IsCompleted;
        }

        private static string ResourceVersion() => DalamudServices.DataManager.GameData.Repositories.TryGetValue("ffxiv", out var repository)
            ? repository.Version : "unknown";

        private static uint NormalizeID(uint id) => id is > 0 and < 0x10000 ? id | 0x10000 : id;
        private static bool IsValidQuestID(uint id) => id is > 0x10000 and < 0x20000;

        private sealed record QuestDefinition
        (
            uint ID, string Name, string ScriptName, bool Repeatable, uint RepeatFlag,
            uint IssuerID, uint TurnInID, QuestLocation? Issuer,
            QuestTarget[] Prerequisites, byte PrerequisiteJoin, uint[] Locks, byte LockJoin,
            uint PrimaryJobCategory, uint SecondaryJobCategory, ushort PrimaryLevel, ushort SecondaryLevel,
            uint RequiredJob, uint Company, uint CompanyRank, uint Expansion,
            uint Festival, ushort BellStart, ushort BellEnd, uint Tribe, uint TribeRank, uint Mount,
            uint Satisfaction, byte SatisfactionLevel, bool HouseRequired, uint[] Instances, byte InstanceJoin,
            Dictionary<string, uint> Symbols, QuestObjective[] Objectives, QuestListener[] Listeners
        );

        private sealed record QuestLocation(uint ObjectID, uint TerritoryID, Vector3 Position, float Radius, uint LevelID = 0, byte ObjectType = 0);
        private sealed record QuestListener(uint ObjectID, uint ConditionValue, byte Sequence, byte TodoIndex, byte EventType, byte ConditionType,
            byte ConditionOperator, byte Variable0, byte Variable1);
        private sealed record QuestObjective(int Index, byte CompleteSequence, ushort Quantity, QuestLocation[] Locations);
        private sealed record QuestState(bool Accepted, bool Completed, byte Sequence, byte[] Variables, byte AcceptClassJob);
        private sealed record QuestTurnInEvidence(bool Completed, bool? DailyCompleted);
        private sealed class ScriptLoad
        {
            public QuestScript? Script;
            public string? Error;
        }
    }

    private enum QuestStepKind { AcceptQuest, CompleteQuest, Interact, UseItem, Emote, Combat, WalkTo, Duty, Craft, Gather, Fish, Say, Action, SwitchJob, Unsupported }
    private sealed record QuestStepCompletion(int? TodoIndex = null, uint ItemID = 0, int Quantity = 0, bool RequireHQ = false);
    private sealed record QuestStep
    (
        QuestStepKind Kind,
        uint ObjectID,
        uint TerritoryID,
        Vector3? Position,
        uint Argument,
        byte Sequence,
        string StateKey,
        QuestStepCompletion Completion,
        int? ChoiceIndex = null,
        string? Text = null,
        int Quantity = 1,
        bool RequireHQ = false,
        uint BaitID = 0,
        bool AllowFlight = true,
        uint ClassJobID = 0
    );
}
