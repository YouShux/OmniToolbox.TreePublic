using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiNotification;
using OmniToolbox.Notifications;

namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private sealed partial class HistoryWindow
    {
        private readonly ConcurrentQueue<TransferResult> transferResults = new();
        private Task? transfer;

        private void StartTransfer(bool importing, Guid? sessionID = null)
        {
            var owner = feature.characterID;
            if (!feature.IsEnabled || owner == 0 || feature.storage is not { } storage || transfer is not null)
                return;
            string content;
            try
            {
                content = importing ? ImGui.GetClipboardText() : string.Empty;
            }
            catch (Exception exception)
            {
                DalamudServices.PluginLog.Error(exception, "Failed to read loot history from the clipboard.");
                OmniNotifier.Popup(feature.Title, Text("TransferFailed"), NotificationType.Error);
                return;
            }
            var lifetime = feature.Activation;
            var random = page == HistoryPage.Random;
            transfer = Task.Run(async () =>
            {
                try
                {
                    TransferSession[] sessions;
                    string? copiedContent = null;
                    if (random)
                    {
                        RandomRoll[] records;
                        if (importing)
                            records = await storage.ImportRandomAsync(owner, content, lifetime.CancellationToken).ConfigureAwait(false);
                        else
                            (copiedContent, records) = storage.ExportRandom(owner, sessionID, lifetime.CancellationToken);
                        sessions = records.GroupBy(record => record.SessionID).Select(group =>
                        {
                            var record = group.First();
                            return new TransferSession(group.Min(entry => entry.Time), record.TerritoryID, record.DutyID);
                        }).ToArray();
                    }
                    else
                    {
                        LootRecord[] records;
                        if (importing)
                            records = await storage.ImportLootAsync(owner, content, lifetime.CancellationToken).ConfigureAwait(false);
                        else
                            (copiedContent, records) = storage.ExportLoot(owner, sessionID, lifetime.CancellationToken);
                        sessions = records.GroupBy(record => record.SessionID).Select(group =>
                        {
                            var record = group.First();
                            return new TransferSession(group.Min(entry => entry.Time), record.TerritoryID, record.DutyID);
                        }).ToArray();
                    }
                    transferResults.Enqueue(new(owner, importing, sessions, copiedContent));
                }
                catch (OperationCanceledException)
                {
                    transferResults.Enqueue(new(owner, importing, null, null));
                }
                catch (Exception exception)
                {
                    DalamudServices.PluginLog.Error(exception, "Failed to {Operation} loot history using the clipboard.",
                        importing ? "import" : "export");
                    transferResults.Enqueue(new(owner, importing, null, null));
                }
            });
            lifetime.Track(transfer);
        }

        private void FinishTransfer()
        {
            if (!transferResults.TryDequeue(out var result))
                return;
            transfer = null;
            if (result.CharacterID != feature.characterID)
                return;
            if (result.Sessions is not { } sessions)
            {
                OmniNotifier.Popup(feature.Title, Text("TransferFailed"), NotificationType.Error);
                return;
            }
            try
            {
                if (result.Content is { } content)
                    ImGui.SetClipboardText(content);
            }
            catch (Exception exception)
            {
                DalamudServices.PluginLog.Error(exception, "Failed to copy loot history to the clipboard.");
                OmniNotifier.Popup(feature.Title, Text("TransferFailed"), NotificationType.Error);
                return;
            }
            var message = sessions.Length == 0
                ? Text("NoNewRecords")
                : string.Join("\n", sessions.Select(session =>
                    string.Format(CultureInfo.CurrentCulture, Text(result.Importing ? "Imported" : "Exported"),
                        session.Time.ToLocalTime(), GetAreaName(session.TerritoryID, session.DutyID))));
            OmniNotifier.Popup(feature.Title, message, NotificationType.Success);
        }

        private sealed record TransferSession(DateTimeOffset Time, uint TerritoryID, uint DutyID);

        private sealed record TransferResult(ulong CharacterID, bool Importing, TransferSession[]? Sessions, string? Content);
    }
}
