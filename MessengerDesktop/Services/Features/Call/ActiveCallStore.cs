using MessengerDesktop.ViewModels.Call;

namespace MessengerDesktop.Services.Features.Call;

public sealed class ActiveCallStore : ObservableObject
{
    private CallViewModel? _activeCall;
    private bool _isCallUiOpen;

    public CallViewModel? ActiveCall
    {
        get => _activeCall;
        set
        {
            if (SetProperty(ref _activeCall, value))
            {
                OnPropertyChanged(nameof(IsInCall));

                if (value == null)
                    IsCallUiOpen = false;
            }
        }
    }

    public bool IsCallUiOpen
    {
        get => _isCallUiOpen;
        set => SetProperty(ref _isCallUiOpen, value);
    }

    public bool IsInCall => _activeCall != null;

    public void ToggleCallUi() => IsCallUiOpen = !IsCallUiOpen;

    public void OpenCallUi() => IsCallUiOpen = true;

    public void CloseCallUi() => IsCallUiOpen = false;

    public void Clear()
    {
        ActiveCall = null;
        IsCallUiOpen = false;
    }
}