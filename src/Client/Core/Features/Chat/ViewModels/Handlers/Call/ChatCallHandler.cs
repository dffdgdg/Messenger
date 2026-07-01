using Core.Features.Chat.ViewModels.Context;
using Core.Features.Chat.ViewModels.Shared;
using Core.Services.Call.Abstractions;
using Shared.Contracts.Call;
using System.Diagnostics;

namespace Core.Features.Chat.ViewModels.Handlers.Call;

public sealed partial class ChatCallHandler : ChatFeatureHandler
{
    private readonly ICallService _callService;
    private readonly ICallHubConnection _callHub;
    private readonly Func<Task> _openCallUi;
    private readonly Func<CallStateDto, string, bool, Task> _showCallView;

    private string? _chatActiveCallId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveCallBannerText))]
    public partial bool HasActiveCall { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveCallBannerText))]
    public partial int ActiveCallParticipantsCount { get; set; }

    [ObservableProperty]
    public partial bool IsInActiveCall { get; set; }

    public string ActiveCallBannerText => ActiveCallParticipantsCount > 0
        ? $"Идёт звонок · {ActiveCallParticipantsCount} участн."
        : "Идёт звонок";

    /// <param name="openCallUi">Делегат: открыть UI активного звонка (Parent.OpenCallUi)</param>
    /// <param name="showCallView">Делегат: показать окно звонка (Parent.ShowCallView)</param>
    public ChatCallHandler(ChatContext context, ICallService callService, ICallHubConnection callHub,
        Func<Task> openCallUi, Func<CallStateDto, string, bool, Task> showCallView) : base(context)
    {
        _callService = callService;
        _callHub = callHub;
        _openCallUi = openCallUi;
        _showCallView = showCallView;

        Subscribe();
    }

    // ── Инициализация состояния звонка при открытии чата ─────────────────

    public async Task InitAsync(CancellationToken ct)
    {
        try
        {
            CallStateDto? callState = null;

            if (_callHub.IsConnected)
            {
                callState = await _callHub.GetCallStateAsync(Ctx.ChatId);
            }
            else
            {
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (!_callHub.IsConnected && DateTime.UtcNow < deadline)
                    await Task.Delay(100, ct);

                if (_callHub.IsConnected)
                    callState = await _callHub.GetCallStateAsync(Ctx.ChatId);
            }

            if (callState == null) return;

            _chatActiveCallId = callState.CallId;
            HasActiveCall = true;
            ActiveCallParticipantsCount = callState.Participants.Count;
            IsInActiveCall = _callService.ActiveChatId == Ctx.ChatId;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CallHandler] InitAsync failed: {ex.Message}");
        }
    }

    // ── Команда ──────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task StartOrJoinCallAsync()
    {
        if (!IsAlive) return;

        try
        {
            if (_callService.IsInCall && _callService.ActiveChatId == Ctx.ChatId)
            {
                await _openCallUi();
                return;
            }

            if (_callService.IsInCall)
                await _callService.LeaveCallAsync();

            if (HasActiveCall)
            {
                var state = await _callHub.GetCallStateAsync(Ctx.ChatId);
                if (state == null) return;

                await _callService.JoinCallAsync(state.CallId, Ctx.ChatId);
                await Task.Delay(200);

                var freshState = await _callHub.GetCallStateAsync(Ctx.ChatId);
                var chatName = Ctx.Chat?.Name ?? string.Empty;
                await _showCallView(freshState ?? state, chatName, state.IsGroupCall);
            }
            else
            {
                await _callService.StartCallAsync(Ctx.ChatId);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CallHandler] StartOrJoinCall error: {ex.Message}");
        }
    }

    // ── Hub-события ───────────────────────────────────────────────────────

    private void Subscribe()
    {
        _callHub.ActiveCallStarted += OnActiveCallStarted;
        _callHub.ActiveCallUpdated += OnActiveCallUpdated;
        _callHub.ActiveCallEnded += OnActiveCallEnded;
        _callHub.IncomingCall += OnIncomingCall;
        _callService.CallStarted += OnCallStarted;
        _callService.CallEnded += OnCallEnded;
    }

    private void OnActiveCallStarted(CallStateDto state)
    {
        if (state.ChatId != Ctx.ChatId) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsAlive) return;
            _chatActiveCallId = state.CallId;
            HasActiveCall = true;
            ActiveCallParticipantsCount = state.Participants.Count;
        });
    }

    private void OnActiveCallUpdated(CallStateDto state)
    {
        if (state.ChatId != Ctx.ChatId) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsAlive) return;
            ActiveCallParticipantsCount = state.Participants.Count;
        });
    }

    private void OnActiveCallEnded(string callId) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsAlive) return;
        if (_chatActiveCallId != null && _chatActiveCallId != callId) return;
        _chatActiveCallId = null;
        HasActiveCall = false;
        ActiveCallParticipantsCount = 0;
        IsInActiveCall = false;
    });

    private void OnIncomingCall(CallInviteDto invite)
    {
        if (invite.ChatId != Ctx.ChatId) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsAlive) return;
            _chatActiveCallId = invite.CallId;
            HasActiveCall = true;
            ActiveCallParticipantsCount = invite.ActiveParticipantsCount;
        });
    }

    private void OnCallStarted() => Dispatcher.UIThread.Post(() =>
    {
        if (!IsAlive) return;
        IsInActiveCall = true;
    });

    private void OnCallEnded() => Dispatcher.UIThread.Post(() =>
    {
        if (!IsAlive) return;
        IsInActiveCall = false;
    });

    // ── Dispose ───────────────────────────────────────────────────────────

    protected override void DisposeManagedResources()
    {
        _callHub.ActiveCallStarted -= OnActiveCallStarted;
        _callHub.ActiveCallUpdated -= OnActiveCallUpdated;
        _callHub.ActiveCallEnded -= OnActiveCallEnded;
        _callHub.IncomingCall -= OnIncomingCall;
        _callService.CallStarted -= OnCallStarted;
        _callService.CallEnded -= OnCallEnded;
    }
}