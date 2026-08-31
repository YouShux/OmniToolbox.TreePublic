using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.DutyState;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class ObsAutoRecording : ModuleBase
{
    private const string DefaultEndpoint = "ws://127.0.0.1:4455";

    private readonly ObsAutoRecordingConfig config;
    private RecordingSession? session;
    private bool countdownActive;
    private bool? lastCountdownState;
    private bool lastCountdownAgentPresent;
    private bool lastCountdownAgentActive;
    private bool lastCountdownShowing;

    public ObsAutoRecording(ObsAutoRecordingConfig config)
    {
        this.config = config;
        if (string.IsNullOrWhiteSpace(config.Endpoint))
        {
            config.Endpoint = DefaultEndpoint;
        }
    }

    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ObsAutoRecordingTitle"),
        Description = OmniLoc.Get("ObsAutoRecordingDescription"),
        Category = ModuleCategory.Combat
    };

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        var inputWidth = OmniTheme.Scale(210f);

        var popupNotification = config.PopupNotification;
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.ObsAutoRecording.PopupNotification"),
                ref popupNotification))
        {
            config.PopupNotification = popupNotification;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(14f));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ObsAutoRecording.Endpoint"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        var endpoint = config.Endpoint;
        OmniControls.InputTextWithHint(
            "##obsAutoRecordingEndpoint",
            DefaultEndpoint,
            ref endpoint,
            256);
        config.Endpoint = endpoint;
        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine(0f, OmniTheme.Scale(6f));
        OmniControls.HelpIcon(OmniLoc.Get("Feature.ObsAutoRecording.EndpointHelp"));

        ImGui.SameLine(0f, OmniTheme.Scale(14f));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ObsAutoRecording.Password"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        var password = config.Password;
        OmniControls.InputText(
            "##obsAutoRecordingPassword",
            ref password,
            256,
            ImGuiInputTextFlags.Password);
        config.Password = password;
        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine(0f, OmniTheme.Scale(6f));
        OmniControls.HelpIcon(OmniLoc.Get("Feature.ObsAutoRecording.PasswordHelp"));
        return changed;
    }

    protected override void OnEnable()
    {
        session = new();
        countdownActive = false;
        lastCountdownState = null;
        lastCountdownAgentPresent = false;
        lastCountdownAgentActive = false;
        lastCountdownShowing = false;
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 100))
        {
            session = null;
            throw new InvalidOperationException("OBS auto-recording update registration failed.");
        }

        DService.Instance().DutyState.DutyWiped += OnDutyWiped;
        DalamudServices.PluginLog.Information(
            "OBS auto-recording enabled. Endpoint={Endpoint}, PasswordConfigured={PasswordConfigured}.",
            config.Endpoint,
            !string.IsNullOrEmpty(config.Password));
    }

    protected override void OnDisable()
    {
        FrameworkManager.Instance().Unreg(OnFrameworkUpdate);
        DService.Instance().DutyState.DutyWiped -= OnDutyWiped;
        countdownActive = false;
        lastCountdownState = null;

        var current = session;
        session = null;
        if (current is null)
        {
            return;
        }

        current.Closing = true;
        _ = CleanupSessionAsync(current);
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        var agent = AgentCountDownSettingDialog.Instance();
        var agentPresent = agent != null;
        var agentActive = agentPresent && agent->Active;
        var showingCountdown = agentPresent && agent->ShowingCountdown;
        var timeRemaining = agentPresent ? agent->TimeRemaining : 0f;
        var isLoggedIn = DService.Instance().ClientState.IsLoggedIn;
        var isCountdown = isLoggedIn &&
                          agentPresent &&
                          (agentActive || showingCountdown) &&
                          timeRemaining > 0f;

        if (agentPresent != lastCountdownAgentPresent ||
            agentActive != lastCountdownAgentActive ||
            showingCountdown != lastCountdownShowing)
        {
            DalamudServices.PluginLog.Information(
                "OBS countdown state changed. AgentPresent={AgentPresent}, Active={Active}, Showing={Showing}, TimeRemaining={TimeRemaining:F2}, LoggedIn={LoggedIn}.",
                agentPresent,
                agentActive,
                showingCountdown,
                timeRemaining,
                isLoggedIn);
            lastCountdownAgentPresent = agentPresent;
            lastCountdownAgentActive = agentActive;
            lastCountdownShowing = showingCountdown;
        }

        if (isCountdown != lastCountdownState)
        {
            DalamudServices.PluginLog.Information(
                "OBS countdown detection={Detection}, TimeRemaining={TimeRemaining:F2}.",
                isCountdown,
                timeRemaining);
            lastCountdownState = isCountdown;
        }

        if (!isCountdown)
        {
            countdownActive = false;
            return;
        }

        if (countdownActive || session is not { } current)
        {
            return;
        }

        countdownActive = true;
        DalamudServices.PluginLog.Information("OBS countdown detected; starting recording.");
        _ = StartRecordingAsync(current);
    }

    private void OnDutyWiped(IDutyStateEventArgs args)
    {
        DalamudServices.PluginLog.Information(
            "OBS duty wipe event received. OwnsRecording={OwnsRecording}, Territory={Territory}.",
            session?.OwnsRecording ?? false,
            DService.Instance().ClientState.TerritoryType);
        if (session is { } current)
        {
            _ = StopRecordingAsync(current, true);
        }
    }

    private async Task StartRecordingAsync(RecordingSession current)
    {
        await current.OperationLock.WaitAsync();
        try
        {
            if (current.Closing || !ReferenceEquals(session, current) || current.OwnsRecording)
            {
                return;
            }

            var endpoint = ResolveEndpoint(config.Endpoint);
            DalamudServices.PluginLog.Information(
                "OBS recording start requested. Endpoint={Endpoint}, PasswordConfigured={PasswordConfigured}.",
                endpoint,
                !string.IsNullOrEmpty(config.Password));
            await current.Client.ConnectAsync(endpoint, config.Password, CancellationToken.None);
            await current.Client.SendRequestAsync("StartRecord", CancellationToken.None);
            current.RecordingEndpoint = endpoint;
            current.OwnsRecording = true;
            DalamudServices.PluginLog.Information("OBS recording started by countdown.");
            NotifyRecordingState("Feature.ObsAutoRecording.Started");
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "OBS countdown recording could not be started.");
            if (!current.Closing && ReferenceEquals(session, current))
            {
                NotifyFailure("Feature.ObsAutoRecording.StartFailed");
            }
        }
        finally
        {
            current.OperationLock.Release();
        }
    }

    private async Task StopRecordingAsync(RecordingSession current, bool notifyFailure)
    {
        await current.OperationLock.WaitAsync();
        try
        {
            if (current.Closing || !current.OwnsRecording)
            {
                return;
            }

            await current.Client.ConnectAsync(
                current.RecordingEndpoint ?? ResolveEndpoint(config.Endpoint),
                config.Password,
                CancellationToken.None);
            await current.Client.SendRequestAsync("StopRecord", CancellationToken.None);
            current.OwnsRecording = false;
            DalamudServices.PluginLog.Information("OBS recording stopped and saved after duty wipe.");
            NotifyRecordingState("Feature.ObsAutoRecording.Stopped");
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "OBS recording could not be stopped after duty wipe.");
            if (notifyFailure && !current.Closing && ReferenceEquals(session, current))
            {
                NotifyFailure("Feature.ObsAutoRecording.StopFailed");
            }
        }
        finally
        {
            current.OperationLock.Release();
        }
    }

    private async Task CleanupSessionAsync(RecordingSession current)
    {
        await current.OperationLock.WaitAsync();
        try
        {
            if (current.OwnsRecording)
            {
                try
                {
                    await current.Client.ConnectAsync(
                        current.RecordingEndpoint ?? ResolveEndpoint(config.Endpoint),
                        config.Password,
                        CancellationToken.None);
                    await current.Client.SendRequestAsync("StopRecord", CancellationToken.None);
                    current.OwnsRecording = false;
                }
                catch (Exception ex)
                {
                    DalamudServices.PluginLog.Warning(ex, "OBS recording could not be stopped while disabling the feature.");
                }
            }

            await current.Client.DisposeAsync();
        }
        finally
        {
            current.OperationLock.Release();
        }
    }

    private static Uri ResolveEndpoint(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("ws" or "wss"))
        {
            throw new InvalidOperationException("OBS WebSocket endpoint must use ws:// or wss://.");
        }

        return endpoint;
    }

    private void NotifyFailure(string key) =>
        _ = DalamudServices.Framework.RunOnFrameworkThread(() =>
            OmniNotifier.Popup(
                Info.Title,
                OmniLoc.Get(key),
                NotificationType.Error,
                true,
                false));

    private void NotifyRecordingState(string key)
    {
        if (!config.PopupNotification)
        {
            return;
        }

        _ = DalamudServices.Framework.RunOnFrameworkThread(() =>
            OmniNotifier.Popup(
                Info.Title,
                OmniLoc.Get(key),
                NotificationType.Info,
                true,
                false));
    }

    private sealed class RecordingSession
    {
        public ObsWebSocketClient Client { get; } = new();

        public SemaphoreSlim OperationLock { get; } = new(1, 1);

        public bool OwnsRecording { get; set; }

        public Uri? RecordingEndpoint { get; set; }

        public volatile bool Closing;
    }

    private sealed class ObsWebSocketClient : IAsyncDisposable
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

        private readonly SemaphoreSlim connectionLock = new(1, 1);
        private readonly SemaphoreSlim sendLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonDocument>> pendingRequests = [];
        private ClientWebSocket? socket;
        private CancellationTokenSource? receiveCancellation;
        private Task? receiveTask;
        private Uri? connectedEndpoint;
        private volatile bool identified;

        public async Task ConnectAsync(Uri endpoint, string password, CancellationToken cancellationToken)
        {
            await connectionLock.WaitAsync(cancellationToken);
            try
            {
                if (identified &&
                    socket?.State == WebSocketState.Open &&
                    connectedEndpoint == endpoint)
                {
                    return;
                }

                await DisconnectCoreAsync();
                var nextSocket = new ClientWebSocket();
                try
                {
                    await nextSocket.ConnectAsync(endpoint, cancellationToken);
                    using var hello = JsonDocument.Parse(
                        await ReceiveTextAsync(nextSocket, cancellationToken));
                    var authentication = BuildAuthentication(hello.RootElement, password);
                    await SendTextAsync(
                        nextSocket,
                        BuildIdentifyMessage(authentication),
                        cancellationToken);

                    using var identifiedResponse = JsonDocument.Parse(
                        await ReceiveTextAsync(nextSocket, cancellationToken));
                    if (!identifiedResponse.RootElement.TryGetProperty("op", out var op) || op.GetInt32() != 2)
                    {
                        throw new InvalidOperationException("OBS WebSocket identification failed.");
                    }

                    socket = nextSocket;
                    connectedEndpoint = endpoint;
                    identified = true;
                    DalamudServices.PluginLog.Information(
                        "OBS WebSocket identified successfully. Endpoint={Endpoint}.",
                        endpoint);
                    receiveCancellation = new();
                    receiveTask = ReceiveLoopAsync(nextSocket, receiveCancellation.Token);
                }
                catch
                {
                    nextSocket.Dispose();
                    throw;
                }
            }
            finally
            {
                connectionLock.Release();
            }
        }

        public async Task SendRequestAsync(string requestType, CancellationToken cancellationToken)
        {
            var currentSocket = socket;
            if (!identified || currentSocket?.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("OBS WebSocket is not connected.");
            }

            var requestID = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pendingRequests.TryAdd(requestID, completion))
            {
                throw new InvalidOperationException("OBS request ID collision.");
            }

            try
            {
                DalamudServices.PluginLog.Information("OBS request sent: {RequestType}.", requestType);
                await SendTextAsync(
                    currentSocket,
                    JsonSerializer.Serialize(new
                    {
                        op = 6,
                        d = new
                        {
                            requestType,
                            requestId = requestID
                        }
                    }),
                    cancellationToken);
                using var response = await completion.Task.WaitAsync(RequestTimeout, cancellationToken);
                ValidateRequestResponse(requestType, response.RootElement);
                DalamudServices.PluginLog.Information("OBS request completed successfully: {RequestType}.", requestType);
            }
            finally
            {
                pendingRequests.TryRemove(requestID, out _);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await connectionLock.WaitAsync();
            try
            {
                await DisconnectCoreAsync();
            }
            finally
            {
                connectionLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket currentSocket, CancellationToken cancellationToken)
        {
            Exception? failure = null;
            try
            {
                while (currentSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var message = await ReceiveTextAsync(currentSocket, cancellationToken);
                    var response = JsonDocument.Parse(message);
                    if (TryGetRequestID(response.RootElement, out var requestID) &&
                        pendingRequests.TryRemove(requestID, out var completion))
                    {
                        if (!completion.TrySetResult(response))
                        {
                            response.Dispose();
                        }
                    }
                    else
                    {
                        response.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                failure = ex;
                DalamudServices.PluginLog.Warning(ex, "OBS WebSocket receive loop stopped unexpectedly.");
            }
            finally
            {
                identified = false;
                CompletePending(failure ?? new WebSocketException("OBS WebSocket connection closed."));
            }
        }

        private async Task DisconnectCoreAsync()
        {
            identified = false;
            connectedEndpoint = null;
            var cancellation = receiveCancellation;
            var listener = receiveTask;
            var currentSocket = socket;
            receiveCancellation = null;
            receiveTask = null;
            socket = null;

            cancellation?.Cancel();
            currentSocket?.Abort();
            currentSocket?.Dispose();
            if (listener is not null)
            {
                try
                {
                    await listener;
                }
                catch (OperationCanceledException)
                {
                }
            }

            cancellation?.Dispose();
            CompletePending(new WebSocketException("OBS WebSocket disconnected."));
        }

        private async Task SendTextAsync(
            ClientWebSocket currentSocket,
            string message,
            CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await sendLock.WaitAsync(cancellationToken);
            try
            {
                await currentSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);
            }
            finally
            {
                sendLock.Release();
            }
        }

        private static async Task<string> ReceiveTextAsync(
            ClientWebSocket currentSocket,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using var message = new MemoryStream();
            while (true)
            {
                var result = await currentSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("OBS WebSocket connection closed by the server.");
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
                }
            }
        }

        private static string? BuildAuthentication(JsonElement hello, string password)
        {
            if (!hello.TryGetProperty("op", out var op) || op.GetInt32() != 0 ||
                !hello.TryGetProperty("d", out var data))
            {
                throw new InvalidOperationException("OBS WebSocket hello message is invalid.");
            }

            if (!data.TryGetProperty("authentication", out var authentication))
            {
                DalamudServices.PluginLog.Information("OBS WebSocket authentication is disabled by the server.");
                return null;
            }

            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    "OBS WebSocket requires authentication. Fill in the OBS password in the TreeHouse card.");
            }

            DalamudServices.PluginLog.Information("OBS WebSocket authentication is required; password is configured.");

            var salt = authentication.GetProperty("salt").GetString() ?? string.Empty;
            var challenge = authentication.GetProperty("challenge").GetString() ?? string.Empty;
            var secret = Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
            return Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
        }

        private static string BuildIdentifyMessage(string? authentication)
        {
            var data = new Dictionary<string, object>
            {
                ["rpcVersion"] = 1
            };
            if (authentication is not null)
            {
                data["authentication"] = authentication;
            }

            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["op"] = 1,
                ["d"] = data
            });
        }

        private static bool TryGetRequestID(JsonElement root, out string requestID)
        {
            requestID = string.Empty;
            if (!root.TryGetProperty("op", out var op) || op.GetInt32() != 7 ||
                !root.TryGetProperty("d", out var data) ||
                !data.TryGetProperty("requestId", out var requestIDElement))
            {
                return false;
            }

            requestID = requestIDElement.GetString() ?? string.Empty;
            return requestID.Length > 0;
        }

        private static void ValidateRequestResponse(string requestType, JsonElement root)
        {
            var status = root.GetProperty("d").GetProperty("requestStatus");
            if (status.GetProperty("result").GetBoolean())
            {
                return;
            }

            var code = status.TryGetProperty("code", out var codeElement)
                ? codeElement.GetInt32()
                : 0;
            var comment = status.TryGetProperty("comment", out var commentElement)
                ? commentElement.GetString()
                : null;
            throw new InvalidOperationException(
                $"OBS request {requestType} failed ({code}): {comment ?? "unknown error"}");
        }

        private void CompletePending(Exception exception)
        {
            foreach (var (requestID, completion) in pendingRequests)
            {
                if (pendingRequests.TryRemove(requestID, out _))
                {
                    completion.TrySetException(exception);
                }
            }
        }
    }
}

[Serializable]
public sealed class ObsAutoRecordingConfig
{
    public string Endpoint { get; set; } = "ws://127.0.0.1:4455";

    public string Password { get; set; } = string.Empty;

    public bool PopupNotification { get; set; } = true;
}
