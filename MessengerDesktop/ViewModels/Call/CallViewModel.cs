using MessengerDesktop.Services.Call;
using MessengerShared.DTO.Call;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Call;

public partial class CallViewModel : BaseViewModel
{
    private readonly ICallService _callService;
    private readonly ICallHubConnection _hub;
    private readonly ActiveCallStore _store;

    [ObservableProperty] public partial string CallId { get; set; } = string.Empty;
    [ObservableProperty] public partial int ChatId { get; set; }
    [ObservableProperty] public partial string ChatName { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsMuted { get; set; }
    [ObservableProperty] public partial bool IsGroupCall { get; set; }
    [ObservableProperty] public partial string DurationText { get; set; } = "0:00";

    public ObservableCollection<CallParticipantViewModel> Participants { get; } = [];

    public bool IsSingleParticipant => Participants.Count == 1;

    public bool ShowWaitingState => Participants.Count <= 1;

    public int GridColumns => Participants.Count switch
    { 1 => 1, 2 => 2, <= 4 => 2, <= 6 => 3, <= 9 => 3, _ => 4 };

    public int GridRows => Participants.Count switch
    { 1 => 1, 2 => 1, <= 4 => 2, <= 6 => 2, <= 9 => 3, _ => 3 };

    private readonly DispatcherTimer _durationTimer;
    private DateTime _callStartedAt;

    public CallViewModel(ICallService callService, ICallHubConnection hub, ActiveCallStore store)
    {
        _callService = callService;
        _hub = hub;
        _store = store;

        _durationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _durationTimer.Tick += OnDurationTick;

        SubscribeHubEvents();
    }

    public void Initialize(CallStateDto state, string chatName, bool isGroupCall)
    {
        CallId = state.CallId;
        ChatId = state.ChatId;
        ChatName = chatName;
        IsGroupCall = isGroupCall;
        _callStartedAt = state.StartedAt;

        Participants.Clear();
        foreach (var p in state.Participants)
            Participants.Add(new CallParticipantViewModel(p));

        OnParticipantsChanged();
        _durationTimer.Start();
    }

    public double ParticipantAvatarSize => Participants.Count switch
    {
        1 => 100, 2 => 80, <= 4 => 56, <= 9 => 40, _ => 32
    };

    private void OnParticipantsChanged()
    {
        OnPropertyChanged(nameof(IsSingleParticipant));
        OnPropertyChanged(nameof(GridColumns));
        OnPropertyChanged(nameof(GridRows));
        OnPropertyChanged(nameof(ShowWaitingState));
        OnPropertyChanged(nameof(ParticipantAvatarSize));

        var size = ParticipantAvatarSize;
        foreach (var p in Participants)
            p.AvatarSize = size;
    }

    [RelayCommand]
    private async Task ToggleMuteAsync()
    {
        await _callService.ToggleMuteAsync();
        IsMuted = _callService.IsMuted;
    }

    [RelayCommand]
    private async Task LeaveCallAsync()
    {
        await _callService.LeaveCallAsync();
        Cleanup();
        _store.Clear();
    }

    [RelayCommand]
    private void CloseUi() => _store.CloseCallUi();

    private void SubscribeHubEvents()
    {
        _hub.CallParticipantJoined += OnParticipantJoined;
        _hub.CallParticipantLeft += OnParticipantLeft;
        _hub.ParticipantMuteChanged += OnParticipantMuteChanged;
        _hub.CallEnded += OnCallEnded;
        _hub.ActiveCallUpdated += OnActiveCallUpdated;
    }

    private void UnsubscribeHubEvents()
    {
        _hub.CallParticipantJoined -= OnParticipantJoined;
        _hub.CallParticipantLeft -= OnParticipantLeft;
        _hub.ParticipantMuteChanged -= OnParticipantMuteChanged;
        _hub.CallEnded -= OnCallEnded;
        _hub.ActiveCallUpdated -= OnActiveCallUpdated;
    }

    private void OnParticipantJoined(string callId, CallParticipantDto dto)
    {
        if (callId != CallId) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (Participants.Any(p => p.UserId == dto.UserId)) return;
            Participants.Add(new CallParticipantViewModel(dto));
            OnParticipantsChanged();
        });
    }

    private void OnParticipantLeft(string callId, int userId)
    {
        if (callId != CallId) return;

        Dispatcher.UIThread.Post(() =>
        {
            var vm = Participants.FirstOrDefault(p => p.UserId == userId);
            if (vm != null)
            {
                Participants.Remove(vm);
                OnParticipantsChanged();
            }
        });
    }

    private void OnParticipantMuteChanged(string callId, int userId, bool isMuted)
    {
        if (callId != CallId) return;

        Dispatcher.UIThread.Post(() =>
        {
            var vm = Participants.FirstOrDefault(p => p.UserId == userId);
            vm?.IsMuted = isMuted;
        });
    }

    private void OnCallEnded(string callId, CallEndReason reason)
    {
        if (callId != CallId) return;

        Dispatcher.UIThread.Post(() =>
        {
            Cleanup();
            _store.Clear();
        });
    }

    private void OnActiveCallUpdated(CallStateDto state)
    {
        if (state.CallId != CallId) return;

        Dispatcher.UIThread.Post(() =>
        {
            var incoming = state.Participants.Select(p => p.UserId).ToHashSet();
            var current = Participants.Select(p => p.UserId).ToHashSet();

            foreach (var vm in Participants.Where(p => !incoming.Contains(p.UserId)).ToList())
                Participants.Remove(vm);

            foreach (var p in state.Participants.Where(p => !current.Contains(p.UserId)))
                Participants.Add(new CallParticipantViewModel(p));

            OnParticipantsChanged();
        });
    }

    private void OnDurationTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.UtcNow - _callStartedAt;
        DurationText = elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}" : $"{elapsed.Minutes}:{elapsed.Seconds:D2}";
    }

    private void Cleanup()
    {
        _durationTimer.Stop();
        UnsubscribeHubEvents();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Cleanup();
        base.Dispose(disposing);
    }
}