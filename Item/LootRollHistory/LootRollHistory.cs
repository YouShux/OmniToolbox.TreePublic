using System.IO;
using Dalamud.Game.ClientState;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using TreeTools;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class LootRollHistory : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        RequiresPrivateProvider = true,
        Commands =
        [
            new ModuleCommand("Feature.LootRollHistory.CommandDescription", OmniCommandCatalog.Format("lootrollhistory"))
        ]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "LootRollHistoryTitle",
            ["Description"] = "LootRollHistoryDescription",
            ["Feature.LootRollHistory.CommandDescription"] = "Command.lootrollhistory.Description",
            ["Feature.LootRollHistory.Open"] = "Feature.LootRollHistory.Open",
            ["Feature.LootRollHistory.Back"] = "Feature.LootRollHistory.Back",
            ["Feature.LootRollHistory.Time"] = "Feature.LootRollHistory.Time",
            ["Feature.LootRollHistory.Player"] = "Feature.LootRollHistory.Player",
            ["Feature.LootRollHistory.Value"] = "Feature.LootRollHistory.Value",
            ["Feature.LootRollHistory.Maximum"] = "Feature.LootRollHistory.Maximum",
            ["Feature.LootRollHistory.Empty"] = "Feature.LootRollHistory.Empty",
            ["Feature.LootRollHistory.Login"] = "Feature.LootRollHistory.Login",
            ["Feature.LootRollHistory.Loading"] = "Feature.LootRollHistory.Loading",
            ["Feature.LootRollHistory.ReadFailed"] = "Feature.LootRollHistory.ReadFailed",
            ["Feature.LootRollHistory.SaveFailed"] = "Feature.LootRollHistory.SaveFailed",
            ["Feature.LootRollHistory.Loot"] = "Feature.LootRollHistory.Loot",
            ["Feature.LootRollHistory.Random"] = "Feature.LootRollHistory.Random",
            ["Feature.LootRollHistory.RandomHelp"] = "Feature.LootRollHistory.RandomHelp",
            ["Feature.LootRollHistory.Item"] = "Feature.LootRollHistory.Item",
            ["Feature.LootRollHistory.State"] = "Feature.LootRollHistory.State",
            ["Feature.LootRollHistory.Choice"] = "Feature.LootRollHistory.Choice",
            ["Feature.LootRollHistory.Winner"] = "Feature.LootRollHistory.Winner",
            ["Feature.LootRollHistory.Unassigned"] = "Feature.LootRollHistory.Unassigned",
            ["Feature.LootRollHistory.Unsupported"] = "Feature.LootRollHistory.Unsupported",
            ["Feature.LootRollHistory.Need"] = "Feature.LootRollHistory.Need",
            ["Feature.LootRollHistory.Greed"] = "Feature.LootRollHistory.Greed",
            ["Feature.LootRollHistory.Pass"] = "Feature.LootRollHistory.Pass",
            ["Feature.LootRollHistory.Pending"] = "Feature.LootRollHistory.Pending",
            ["Feature.LootRollHistory.PendingHint"] = "Feature.LootRollHistory.PendingHint",
            ["Feature.LootRollHistory.NoPending"] = "Feature.LootRollHistory.NoPending",
            ["Feature.LootRollHistory.NoNewRecords"] = "Feature.LootRollHistory.NoNewRecords",
            ["Feature.LootRollHistory.RecordInfo"] = "Feature.LootRollHistory.RecordInfo",
            ["Feature.LootRollHistory.RowActions"] = "Feature.LootRollHistory.RowActions",
            ["Feature.LootRollHistory.Selected"] = "Feature.LootRollHistory.Selected",
            ["Feature.LootRollHistory.AllPassed"] = "Feature.LootRollHistory.AllPassed",
            ["Feature.LootRollHistory.Awarded"] = "Feature.LootRollHistory.Awarded",
            ["Feature.LootRollHistory.Ended"] = "Feature.LootRollHistory.Ended",
            ["Feature.LootRollHistory.Interrupted"] = "Feature.LootRollHistory.Interrupted",
            ["Feature.LootRollHistory.Unknown"] = "Feature.LootRollHistory.Unknown",
            ["Feature.LootRollHistory.Waiting"] = "Feature.LootRollHistory.Waiting",
            ["Feature.LootRollHistory.Obtained"] = "Feature.LootRollHistory.Obtained",
            ["Feature.LootRollHistory.AllAreas"] = "Feature.LootRollHistory.AllAreas",
            ["Feature.LootRollHistory.UnknownArea"] = "Feature.LootRollHistory.UnknownArea",
            ["Feature.LootRollHistory.Import"] = "Feature.LootRollHistory.Import",
            ["Feature.LootRollHistory.Export"] = "Feature.LootRollHistory.Export",
            ["Feature.LootRollHistory.Imported"] = "Feature.LootRollHistory.Imported",
            ["Feature.LootRollHistory.Exported"] = "Feature.LootRollHistory.Exported",
            ["Feature.LootRollHistory.TransferFailed"] = "Feature.LootRollHistory.TransferFailed",
            ["Feature.LootRollHistory.Busy"] = "Feature.LootRollHistory.Busy",
            ["Feature.LootRollHistory.Collapse"] = "Feature.LootRollHistory.Collapse",
            ["Feature.LootRollHistory.Expand"] = "Feature.LootRollHistory.Expand",
            ["Feature.LootRollHistory.Lock"] = "Feature.LootRollHistory.Lock",
            ["Feature.LootRollHistory.Unlock"] = "Feature.LootRollHistory.Unlock",
            ["Feature.LootRollHistory.Settings"] = "Feature.LootRollHistory.Settings",
            ["Feature.LootRollHistory.Scale"] = "Feature.LootRollHistory.Scale",
            ["Feature.LootRollHistory.Opacity"] = "Feature.LootRollHistory.Opacity",
            ["Feature.LootRollHistory.HideKey"] = "Feature.LootRollHistory.HideKey",
            ["Feature.LootRollHistory.Unbound"] = "Feature.LootRollHistory.Unbound",
            ["Feature.LootRollHistory.Capturing"] = "Feature.LootRollHistory.Capturing",
            ["Feature.LootRollHistory.ClearKey"] = "Feature.LootRollHistory.ClearKey",
            ["Feature.LootRollHistory.Close"] = "Feature.LootRollHistory.Close",
            ["Feature.LootRollHistory.History"] = "Feature.LootRollHistory.History",
            ["Feature.LootRollHistory.Modifier"] = "Feature.LootRollHistory.Modifier",
            ["Feature.LootRollHistory.Modifier.None"] = "Feature.LootRollHistory.Modifier.None",
            ["Feature.LootRollHistory.Modifier.Control"] = "Feature.LootRollHistory.Modifier.Control",
            ["Feature.LootRollHistory.Modifier.Shift"] = "Feature.LootRollHistory.Modifier.Shift",
            ["Feature.LootRollHistory.Modifier.Alt"] = "Feature.LootRollHistory.Modifier.Alt",
            ["Feature.LootRollHistory.ReplaySaveCount"] = "Feature.LootRollHistory.ReplaySaveCount",
            ["Feature.LootRollHistory.ClearRecords"] = "Feature.LootRollHistory.ClearRecords"
        });

    private Settings        config;
    private HistoryStorage? storage;
    private HistoryWindow?  window;
    private ulong           characterID;
    private RecordingSession? recordingSession;
    private bool            hasDutyStarted;
    private Action?         openSettingsAction;

    internal LootRollHistory(Settings config, Action saveConfig)
    {
        this.config = config;
        SaveHostConfig = saveConfig;
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        NormalizeSettings();
        characterID = 0;
        recordingSession = null;
        hasDutyStarted = false;
        lootTrackingSupported = false;
        var historyStorage = new HistoryStorage(Path.Combine(
            DService.Instance().PI.GetPluginConfigDirectory(), "TreeHouse", nameof(LootRollHistory)), config.ReplaySaveCount);
        storage = historyStorage;
        activation.Add(() => storage = null);
        activation.AddAsync(historyStorage.DisposeAsync);
        activation.Track(historyStorage.Completion);
        activation.TrackStartup(historyStorage.Initialization);
        activation.OnStopping(historyStorage.Stop);

        var historyWindow = new HistoryWindow(this);
        window = historyWindow;
        activation.Add(() => window = null);
        activation.OnStopping(historyWindow.Close);
        activation.OnStopping(() => DalamudServices.PluginInterface.UiBuilder.Draw -= historyWindow.Draw);
        DalamudServices.PluginInterface.UiBuilder.Draw += historyWindow.Draw;

        var clientState = DService.Instance().ClientState;
        activation.OnStopping(() => clientState.ZoneInit -= OnZoneInit);
        clientState.ZoneInit += OnZoneInit;

        var dutyState = DService.Instance().DutyState;
        activation.OnStopping(() => dutyState.DutyStarted -= OnDutyStarted);
        dutyState.DutyStarted += OnDutyStarted;
        activation.OnStopping(() => dutyState.DutyWiped -= OnDutyProgress);
        dutyState.DutyWiped += OnDutyProgress;
        activation.OnStopping(() => dutyState.DutyRecommenced -= OnDutyProgress);
        dutyState.DutyRecommenced += OnDutyProgress;
        activation.OnStopping(() => dutyState.DutyCompleted -= OnDutyProgress);
        dutyState.DutyCompleted += OnDutyProgress;

        activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
            throw new InvalidOperationException("Roll history update registration failed.");

        randomPatterns = [CreateRandomPattern(DEFAULT_RANDOM_MESSAGE_ID), CreateRandomPattern(CUSTOM_RANDOM_MESSAGE_ID)];
        activation.OnStopping(() => DalamudServices.ChatGUI.ChatMessage -= OnRandomMessage);
        DalamudServices.ChatGUI.ChatMessage += OnRandomMessage;

        RefreshCharacter();
        var observer = new LootRollObserver(OnLootNotice);
        activation.Add(observer);
        lootTrackingSupported = observer.IsSupported;
        CaptureExistingLoot();
        activation.OnStopping(() =>
        {
            observer.Dispose();
            lock (lootNoticeSync)
            {
                DrainLootNotices();
                FinishLootTracking();
                recordingSession = null;
            }
            lootTrackingSupported = false;
        });
    }

    public override bool HasSettings => true;

    public override bool TryHandleCommand(string command, string arguments)
    {
        if (!string.Equals(command, "lootrollhistory", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(arguments) || window is null)
            return false;
        window.ToggleVisibility();
        return true;
    }

    public void SetOpenSettingsAction(Action action) => openSettingsAction = action;

    public override bool DrawSettings()
    {
        using var disabled = ImRaii.Disabled(!IsEnabled);
        return DrawWindowSettings();
    }

    private void ClearRecords()
    {
        if (characterID == 0 || storage?.GetSnapshot(characterID) is not { LootStatus: not HistoryStatus.ReadFailed })
            return;
        lock (lootNoticeSync)
        {
            FinishLootTracking();
            storage.ClearLoot(characterID);
            CaptureExistingLoot();
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        RefreshCharacter();
        DrainLootNotices();
        SyncLootPresence();
        window?.UpdateHotkey();
    }

    private void RefreshCharacter()
    {
        var services = DService.Instance();
        if (services.ClientState.IsLoggedIn && !services.PlayerState.IsLoaded)
            return;
        var currentID = services.ClientState.IsLoggedIn
            ? services.PlayerState.ContentId
            : 0;
        if (characterID == currentID)
            return;

        lock (lootNoticeSync)
        {
            DrainLootNotices();
            FinishLootTracking();
            characterID = currentID;
            recordingSession = currentID == 0 ? null : new(currentID, Guid.NewGuid(),
                services.ClientState.TerritoryType, GameState.ContentFinderCondition);
            hasDutyStarted = services.DutyState.IsDutyStarted;
        }
        if (currentID != 0)
        {
            storage?.Load(currentID);
            CaptureExistingLoot();
        }
    }

    private void OnZoneInit(ZoneInitEventArgs args)
    {
        RefreshCharacter();
        if (characterID == 0)
            return;
        var territoryID = args.TerritoryType.RowId;
        var dutyID = args.ContentFinderCondition.RowId;
        var previous = recordingSession;
        var continueSession = previous is not null && previous.DutyID == dutyID &&
                              (dutyID != 0 || previous.TerritoryID == territoryID);
        ChangeRecordingSession(territoryID, dutyID, continueSession);
    }

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        RefreshCharacter();
        if (characterID == 0 || args.ContentFinderCondition.RowId == 0)
            return;
        var continueSession = recordingSession?.DutyID == args.ContentFinderCondition.RowId && !hasDutyStarted;
        ChangeRecordingSession(args.TerritoryType.RowId, args.ContentFinderCondition.RowId, continueSession);
        hasDutyStarted = true;
    }

    private void OnDutyProgress(IDutyStateEventArgs args)
    {
        if (recordingSession is { DutyID: not 0 } current && current.DutyID == args.ContentFinderCondition.RowId)
            hasDutyStarted = true;
    }

    private void ChangeRecordingSession(uint territoryID, uint dutyID, bool continueSession)
    {
        lock (lootNoticeSync)
        {
            DrainLootNotices();
            if (!continueSession)
            {
                FinishLootTracking();
                hasDutyStarted = false;
            }
            var id = continueSession && recordingSession is { } previous ? previous.ID : Guid.NewGuid();
            recordingSession = new(characterID, id, territoryID, dutyID);
        }
        if (!continueSession)
            CaptureExistingLoot();
    }

    private sealed record RandomRoll(DateTimeOffset Time, string PlayerName, uint HomeWorldID, int Value, int Maximum)
    {
        public uint TerritoryID { get; init; }
        public uint DutyID { get; init; }
        public Guid SessionID { get; init; }
    }

    private sealed record RecordingSession(ulong CharacterID, Guid ID, uint TerritoryID, uint DutyID);

    #region 常量

    private const int HISTORY_LIMIT = 200;
    private const int MAX_PLAYER_NAME_LENGTH = 128;
    private const int DEFAULT_RANDOM_MAXIMUM = 999;
    private const uint DEFAULT_RANDOM_MESSAGE_ID = 856;
    private const uint CUSTOM_RANDOM_MESSAGE_ID = 3887;

    #endregion
}
