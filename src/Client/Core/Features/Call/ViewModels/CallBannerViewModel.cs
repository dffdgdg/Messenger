using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Call;
using Core.Services.Call.Abstractions;

namespace Core.Features.Call.ViewModels;

public sealed partial class CallBannerViewModel : ObservableObject, IDisposable
{
    private readonly ActiveCallStore _store;
    private readonly ICallService _callService;

    public ActiveCallStore Store => _store;

    public CallBannerViewModel(ActiveCallStore store, ICallService callService)
    {
        _store = store;
        _callService = callService;
        _callService.MuteChanged += OnMuteChanged;
    }

    private void OnMuteChanged(bool isMuted) => OnPropertyChanged(nameof(IsMuted));

    [RelayCommand]
    private void ToggleCallUi() => _store.ToggleCallUi();

    [RelayCommand]
    private async Task ToggleMuteAsync()
    {
        await _callService.ToggleMuteAsync();
        OnPropertyChanged(nameof(IsMuted));
    }

    [RelayCommand]
    private async Task LeaveCallAsync()
    {
        await _callService.LeaveCallAsync();
        _store.Clear();
    }

    public bool IsMuted => _callService.IsMuted;
    public bool IsInCall => _store.IsInCall;

    public string ChatName => _store.ActiveCall?.ChatName ?? string.Empty;
    public string DurationText => _store.ActiveCall?.DurationText ?? string.Empty;

    public void Dispose() => _callService.MuteChanged -= OnMuteChanged;
}