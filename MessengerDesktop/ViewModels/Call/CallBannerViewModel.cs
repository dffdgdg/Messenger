using MessengerDesktop.Services.Call;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Call;

public partial class CallBannerViewModel(ActiveCallStore store, ICallService callService) : ObservableObject
{
    public ActiveCallStore Store => store;

    [RelayCommand]
    private void ToggleCallUi() => store.ToggleCallUi();

    [RelayCommand]
    private async Task ToggleMuteAsync()
    {
        await callService.ToggleMuteAsync();
        OnPropertyChanged(nameof(IsMuted));
    }

    [RelayCommand]
    private async Task LeaveCallAsync()
    {
        await callService.LeaveCallAsync();
        store.Clear();
    }

    public bool IsMuted => callService.IsMuted;
    public bool IsInCall => store.IsInCall;
    public string ChatName => store.ActiveCall?.ChatName ?? string.Empty;
    public string DurationText => store.ActiveCall?.DurationText ?? string.Empty;
}