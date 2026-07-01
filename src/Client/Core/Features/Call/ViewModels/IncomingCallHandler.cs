using Core.Services.Call;
using Core.Services.Call.Abstractions;
using System.Diagnostics;

namespace Core.Features.Call.ViewModels;

public class IncomingCallHandler : IDisposable
{
    private readonly ICallHubConnection _callHub;
    private readonly ICallService _callService;
    private readonly ActiveCallStore _activeCallStore;
    private readonly IAuthManager _auth;
    private readonly Func<IncomingCallViewModel, Task> _showDialog;
    private readonly Func<CallStateDto, string, bool, Task> _showCallView;
    private readonly Func<int, ChatDto?> _findChat;

    public IncomingCallHandler(
        ICallHubConnection callHub,
        ICallService callService,
        ActiveCallStore activeCallStore,
        IAuthManager auth,
        Func<IncomingCallViewModel, Task> showDialog,
        Func<CallStateDto, string, bool, Task> showCallView,
        Func<int, ChatDto?> findChat)
    {
        _callHub = callHub;
        _callService = callService;
        _activeCallStore = activeCallStore;
        _auth = auth;
        _showDialog = showDialog;
        _showCallView = showCallView;
        _findChat = findChat;

        _callHub.IncomingCall += OnIncomingCall;
    }

    private void OnIncomingCall(CallInviteDto invite) => Dispatcher.UIThread.Post(async () =>
    {
        try
        {
            if (_activeCallStore.IsInCall) return;

            var vm = new IncomingCallViewModel(_callService, invite);
            vm.Accepted += accepted => _ = HandleAcceptedAsync(accepted);
            await _showDialog(vm);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IncomingCallHandler] {ex.Message}");
        }
    });

    private async Task HandleAcceptedAsync(CallInviteDto invite)
    {
        var state = await JoinAndGetStateAsync(invite);
        var chatName = _findChat(invite.ChatId)?.Name ?? invite.ChatName;
        await _showCallView(state, chatName, invite.IsGroupCall);
    }

    private async Task<CallStateDto> JoinAndGetStateAsync(CallInviteDto invite)
    {
        await _callService.JoinCallAsync(invite.CallId, invite.ChatId);

        using var cts = new CancellationTokenSource(3000);
        try
        {
            var stateTcs = new TaskCompletionSource<CallStateDto>();
            void OnState(CallStateDto s)
            {
                if (s.CallId == invite.CallId) stateTcs.TrySetResult(s);
            }
            _callHub.CallStateUpdated += OnState;
            try { return await stateTcs.Task.WaitAsync(cts.Token); }
            finally { _callHub.CallStateUpdated -= OnState; }
        }
        catch (OperationCanceledException)
        {
            return await _callHub.GetCallStateAsync(invite.ChatId) ?? FallbackState(invite);
        }
    }

    private static CallStateDto FallbackState(CallInviteDto invite) => new()
    {
        CallId = invite.CallId,
        ChatId = invite.ChatId,
        InitiatorId = invite.InitiatorId,
        IsGroupCall = invite.IsGroupCall,
        StartedAt = DateTimeOffset.UtcNow,
        Participants = []
    };

    public void Dispose() => _callHub.IncomingCall -= OnIncomingCall;
}
