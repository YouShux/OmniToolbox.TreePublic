using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.Expansion.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Info.Lumina;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Operations;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoLogin
{
    private CharacterSwitchRequest? characterSwitch;

    public bool IsBusy => characterSwitch is not null || tasks?.IsBusy == true ||
                          manualTarget is not null || crossDataCenterTravelInProgress;

    /// <summary>等待已登记角色实际登录；取消仅停止本次后续操作。</summary>
    public async Task<CharacterSwitchResult> SwitchCharacterAsync(AutoLoginTarget target, CancellationToken cancellationToken)
    {
        CharacterSwitchRequest? request = null;
        try
        {
            Task<CharacterSwitchResult>? result = null;
            await DalamudServices.Framework.RunOnFrameworkThread(() =>
            {
                result = BeginCharacterSwitch(target, cancellationToken, out request);
            }).ConfigureAwait(false);
            if (result is null)
                return new(OperationState.Failed, "TaskFailed");

            return await result.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (request is not null)
            {
                await DalamudServices.Framework.RunOnFrameworkThread(() =>
                    CompleteCharacterSwitch(request, OperationState.Cancelled, "Cancelled")).ConfigureAwait(false);
                return await request.Completion.Task.ConfigureAwait(false);
            }
            return new(OperationState.Cancelled, "Cancelled");
        }
        catch (Exception)
        {
            if (request is not null)
            {
                await DalamudServices.Framework.RunOnFrameworkThread(() =>
                    CompleteCharacterSwitch(request, OperationState.Failed, "TaskFailed")).ConfigureAwait(false);
                return await request.Completion.Task.ConfigureAwait(false);
            }
            return new(OperationState.Failed, "TaskFailed");
        }
    }

    private unsafe Task<CharacterSwitchResult> BeginCharacterSwitch(
        AutoLoginTarget target, CancellationToken cancellationToken, out CharacterSwitchRequest? request)
    {
        request = null;
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(new CharacterSwitchResult(OperationState.Cancelled, "Cancelled"));
        if (!IsEnabled || tasks is null || tasks.IsDisposed || Activation.IsStopping)
            return RejectCharacterSwitch("ModuleDisabled");
        if (target is null || string.IsNullOrWhiteSpace(target.CharacterName) ||
            !Sheets.Worlds.TryGetValue(target.WorldID, out var world))
            return RejectCharacterSwitch("InvalidTarget");
        if (characterSwitch is not null || tasks.IsBusy || manualTarget is not null)
            return RejectCharacterSwitch("LoginBusy");
        if (crossDataCenterTravelInProgress || Addons.LobbyDKT->IsAddonAndNodesReady() ||
            (AddonHelper.TryGetByName("TitleConnect", out var connecting) && connecting->IsAddonAndNodesReady()))
            return RejectCharacterSwitch("UnsafeLoginState");

        var loggedIn = GameState.IsLoggedIn;
        if (loggedIn)
        {
            if (GameState.ContentFinderCondition != 0 ||
                (AddonHelper.TryGetByName("SelectYesno", out var confirmation) && confirmation->IsAddonAndNodesReady()))
                return RejectCharacterSwitch("UnsafeLoginState");
            GetCurrentAccountTargets();
        }
        else if (!IsCharacterSwitchLobbyReady())
        {
            return RejectCharacterSwitch("UnsafeLoginState");
        }

        var registered = config.Targets.FirstOrDefault(candidate => candidate.WorldID == target.WorldID &&
            candidate.CharacterName.Equals(target.CharacterName, StringComparison.Ordinal));
        if (registered is null)
            return RejectCharacterSwitch("InvalidTarget");

        var accountInfo = AccountInfo.Instance();
        if (accountInfo is null || accountInfo->AccountID == 0 || registered.AccountID == 0)
            return RejectCharacterSwitch("AccountUnavailable");
        if (registered.AccountID != accountInfo->AccountID ||
            (target.AccountID != 0 && target.AccountID != accountInfo->AccountID))
            return RejectCharacterSwitch("AccountMismatch");

        var player = DService.Instance().ObjectTable.LocalPlayer;
        if (loggedIn && player is not null && LocalPlayerState.ContentID != 0 &&
            player.Name.ToString().Equals(target.CharacterName, StringComparison.Ordinal) &&
            DService.Instance().PlayerState.HomeWorld.RowId == target.WorldID)
            return Task.FromResult(new CharacterSwitchResult(OperationState.Succeeded, string.Empty));

        var agent = AgentLobby.Instance();
        var loadedDataCenterID = agent == null ? 0 : GetLoadedDataCenterID(agent);
        if (loggedIn)
            loadedDataCenterID = LuminaWrapper.GetWorldDC(GameState.CurrentWorld);
        if (GameState.IsCN && loadedDataCenterID != world.DataCenter.RowId && !IsDataCenterLoginAvailable())
            return RejectCharacterSwitch("DataCenterUnavailable");

        var pendingRequest = new CharacterSwitchRequest(registered with { }, cancellationToken);
        foreach (var character in accountInfo->Characters)
        {
            if (character.ContentID != 0 && character.HomeWorld == target.WorldID &&
                character.Name.ToString().Equals(target.CharacterName, StringComparison.Ordinal))
            {
                pendingRequest.ExpectedContentID = character.ContentID;
                break;
            }
        }

        characterSwitch = pendingRequest;
        request = pendingRequest;
        manualTarget = request.Target;
        suspendedUntilLogin = false;
        if (loggedIn)
            EnqueueCharacterSwitchLogout(request);
        else
            EnqueueLogin(request.Target, request);
        return request.Completion.Task;
    }

    private void EnqueueCharacterSwitchLogout(CharacterSwitchRequest request)
    {
        EnqueueLoginStep(request, () =>
        {
            if (GameState.IsLoggedIn)
                ChatManager.Instance().SendMessage("/logout");
            return true;
        }, "Request logout");
        EnqueueLoginStep(request,
            () => !GameState.IsLoggedIn || AddonSelectYesnoEvent.ClickYes(),
            "Confirm logout", timeoutMS: CHARACTER_SELECT_TIMEOUT_MS);
        EnqueueLoginStep(request, () =>
        {
            if (GameState.IsLoggedIn || !IsCharacterSwitchLobbyReady())
                return false;
            EnqueueLogin(request.Target, request);
            return true;
        }, "Wait for logout", timeoutMS: 180_000);
    }

    private unsafe bool IsCharacterSwitchLobbyReady()
    {
        var agent = AgentLobby.Instance();
        if (agent == null || agent->IsLoggedIn || crossDataCenterTravelInProgress ||
            Addons.LobbyDKT->IsAddonAndNodesReady() ||
            (AddonHelper.TryGetByName("TitleConnect", out var connecting) && connecting->IsAddonAndNodesReady()))
            return false;

        return (AddonHelper.TryGetByName("_TitleMenu", out var title) && title->IsAddonAndNodesReady()) ||
               (AddonHelper.TryGetByName("_CharaSelectListMenu", out var characters) && characters->IsAddonAndNodesReady());
    }

    private void EnqueueLoginStep(CharacterSwitchRequest? request, Func<bool> action, string name, int timeoutMS = 0)
    {
        if (request is null)
        {
            tasks.Enqueue(action, name, timeoutMS);
            return;
        }

        tasks.Enqueue(
            () => RunCharacterSwitchStep(request, action),
            $"{nameof(SwitchCharacterAsync)}: {name}", timeoutMS,
            timeoutAction: () => CompleteCharacterSwitch(request, OperationState.Failed, "TaskTimeout"),
            exceptionAction: () => CompleteCharacterSwitch(request, OperationState.Failed, "TaskFailed"));
    }

    private bool RunCharacterSwitchStep(CharacterSwitchRequest request, Func<bool> action)
    {
        if (!CanContinueCharacterSwitch(request))
            return true;
        if (IsCharacterSwitchConnecting())
            return false;
        if (request.AwaitingLogin && GameState.IsLoggedIn)
            return TryCompleteCharacterSwitchLogin(request);
        return action();
    }

    private static unsafe bool IsCharacterSwitchConnecting() =>
        AddonHelper.TryGetByName("TitleConnect", out var connecting) && connecting->IsAddonAndNodesReady();

    private unsafe bool CanContinueCharacterSwitch(CharacterSwitchRequest request)
    {
        if (!ReferenceEquals(characterSwitch, request) || request.Result.HasValue)
            return false;
        if (request.CancellationToken.IsCancellationRequested)
        {
            CompleteCharacterSwitch(request, OperationState.Cancelled, "Cancelled");
            return false;
        }
        if (crossDataCenterTravelInProgress || Addons.LobbyDKT->IsAddonAndNodesReady())
        {
            CompleteCharacterSwitch(request, OperationState.Failed, "UnsafeLoginState");
            return false;
        }
        return true;
    }

    private unsafe bool TryCompleteCharacterSwitchLogin(CharacterSwitchRequest request)
    {
        if (!GameState.IsLoggedIn)
            return false;
        var player = DService.Instance().ObjectTable.LocalPlayer;
        var contentID = LocalPlayerState.ContentID;
        var homeWorldID = DService.Instance().PlayerState.HomeWorld.RowId;
        if (player is null || contentID == 0 || homeWorldID == 0)
            return false;

        var accountInfo = AccountInfo.Instance();
        if (accountInfo is null || accountInfo->AccountID == 0)
            return false;
        var matches = accountInfo->AccountID == request.Target.AccountID &&
                      player.Name.ToString().Equals(request.Target.CharacterName, StringComparison.Ordinal) &&
                      homeWorldID == request.Target.WorldID &&
                      (request.ExpectedContentID == 0 || contentID == request.ExpectedContentID);
        CompleteCharacterSwitch(request, matches ? OperationState.Succeeded : OperationState.Failed,
            matches ? string.Empty : "TargetMismatch");
        return true;
    }

    private async Task<bool> RequestCharacterSwitchDataCenterAsync(
        uint dataCenterID, bool requiresDataCenterLogin, CharacterSwitchRequest request, CancellationToken cancellationToken)
    {
        if (!CanContinueCharacterSwitch(request))
            return true;
        if (IsCharacterSwitchConnecting())
            return false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, request.CancellationToken);
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var succeeded = await RequestDataCenterLoginAsync(dataCenterID, requiresDataCenterLogin)
                .WaitAsync(cancellation.Token).ConfigureAwait(false);
            if (!succeeded)
                await DalamudServices.Framework.RunOnFrameworkThread(() =>
                    CompleteCharacterSwitch(request, OperationState.Failed, "DataCenterLoginFailed")).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            await DalamudServices.Framework.RunOnFrameworkThread(() =>
                CompleteCharacterSwitch(request, OperationState.Cancelled, "Cancelled")).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await DalamudServices.Framework.RunOnFrameworkThread(() =>
                CompleteCharacterSwitch(request, OperationState.Failed, "TaskFailed")).ConfigureAwait(false);
            return true;
        }
    }

    private void CompleteCharacterSwitch(CharacterSwitchRequest request, OperationState state, string error)
    {
        if (!ReferenceEquals(characterSwitch, request))
            return;
        if (request.CancellationToken.IsCancellationRequested)
        {
            state = OperationState.Cancelled;
            error = "Cancelled";
        }
        manualTarget = null;
        suspendedUntilLogin = state != OperationState.Succeeded;
        request.Result = new(state, error);
        tasks?.Abort();
        if (tasks is not { IsBusy: true })
            PublishCharacterSwitchResult(request);
    }

    private void OnLoginTasksIdle()
    {
        if (characterSwitch is { Result: not null } request)
            PublishCharacterSwitchResult(request);
    }

    private void PublishCharacterSwitchResult(CharacterSwitchRequest request)
    {
        if (!ReferenceEquals(characterSwitch, request) || request.Result is not { } result)
            return;
        characterSwitch = null;
        request.Completion.TrySetResult(result);
    }

    private static Task<CharacterSwitchResult> RejectCharacterSwitch(string error) =>
        Task.FromResult(new CharacterSwitchResult(OperationState.Failed, error));

    public readonly record struct CharacterSwitchResult(OperationState State, string Error);

    private sealed class CharacterSwitchRequest(AutoLoginTarget target, CancellationToken cancellationToken)
    {
        public AutoLoginTarget Target { get; } = target;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource<CharacterSwitchResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ulong ExpectedContentID { get; set; }
        public bool AwaitingLogin { get; set; }
        public CharacterSwitchResult? Result { get; set; }
    }
}
