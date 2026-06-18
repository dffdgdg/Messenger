using CommunityToolkit.Mvvm.ComponentModel;
using Core.Services.Abstractions;
using Core.ViewModels.Dialog;
using Shared.Dto.Call;

namespace Core.ViewModels.Call;

public partial class IncomingCallViewModel : DialogBaseViewModel
{
    private readonly ICallService _callService;
    private readonly CallInviteDto _invite;

    [ObservableProperty] public partial string CallerName { get; set; }
    [ObservableProperty] public partial string ChatName { get; set; }
    [ObservableProperty] public partial string? CallerAvatar { get; set; }
    [ObservableProperty] public partial bool IsGroupCall { get; set; }
    [ObservableProperty] public partial int ActiveParticipantsCount { get; set; }

    public string DeclineButtonText => IsGroupCall ? "Позже" : "Отклонить";

    public string AcceptButtonText => IsGroupCall ? "Войти" : "Принять";

    public string SubTitle => IsGroupCall ? $"Участников: {ActiveParticipantsCount}" : "Входящий звонок...";

    public event Action<CallInviteDto>? Accepted;

    public IncomingCallViewModel(ICallService callService, CallInviteDto invite)
    {
        _callService = callService;
        _invite = invite;
        Title = invite.IsGroupCall ? "Групповой звонок" : "Входящий звонок";
        CanCloseOnBackgroundClick = false;

        CallerName = invite.InitiatorName;
        ChatName = invite.ChatName;
        CallerAvatar = invite.InitiatorAvatar;
        IsGroupCall = invite.IsGroupCall;
        ActiveParticipantsCount = invite.ActiveParticipantsCount;
    }

    [RelayCommand]
    private async Task AcceptAsync() => await SafeExecuteAsync(async () =>
    {
        Accepted?.Invoke(_invite);
        await RequestCloseAsync();
    });

    [RelayCommand]
    private async Task DeclineAsync() => await SafeExecuteAsync(async () =>
    {
        if (!IsGroupCall)
            await _callService.DeclineCallAsync(_invite.CallId);

        await RequestCloseAsync();
    });

    protected override Task Cancel() => DeclineAsync();
}