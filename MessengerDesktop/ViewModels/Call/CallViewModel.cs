using MessengerDesktop.Services.Call;
using MessengerShared.DTO.Call;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Call;

public enum CallSidePanelMode { Chat, Participants }

public partial class CallViewModel : BaseViewModel
{
    private readonly ICallService _callService;
    private readonly ICallHubConnection _hub;
    private readonly ActiveCallStore _store;
    private int _myUserId;

    [ObservableProperty] public partial string CallId { get; set; } = string.Empty;
    [ObservableProperty] public partial int ChatId { get; set; }
    [ObservableProperty] public partial string ChatName { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsMuted { get; set; }
    [ObservableProperty] public partial bool IsGroupCall { get; set; }
    [ObservableProperty] public partial string DurationText { get; set; } = "0:00";
    [ObservableProperty] public partial bool IsChatPanelOpen { get; set; }
    [ObservableProperty] public partial string MessageInput { get; set; } = string.Empty;
    [ObservableProperty] public partial int UnreadChatCount { get; set; }
    [ObservableProperty] public partial CallSidePanelMode SidePanelMode { get; set; } = CallSidePanelMode.Chat;
    public IReadOnlyList<string> PopularEmojis { get; } =
    [
        "😀", "😂", "😊", "😍", "🤔", "👍", "👏", "🙏",
        "🔥", "🎉", "❤️", "😢", "😎", "🤝", "🙌", "✅"
    ];

    public bool IsChatMode => SidePanelMode == CallSidePanelMode.Chat;
    public bool IsParticipantsMode => SidePanelMode == CallSidePanelMode.Participants;
    public bool HasUnread => UnreadChatCount > 0;

    public ObservableCollection<CallParticipantViewModel> Participants { get; } = [];
    public ObservableCollection<CallChatMessageViewModel> ChatMessages { get; } = [];

    public bool IsSingleParticipant => Participants.Count == 1;
    public bool ShowWaitingState => Participants.Count <= 1;

    public int GridColumns => Participants.Count switch
    { 1 => 1, 2 => 2, <= 4 => 2, <= 6 => 3, <= 9 => 3, _ => 4 };

    public int GridRows => Participants.Count switch
    { 1 => 1, 2 => 1, <= 4 => 2, <= 6 => 2, <= 9 => 3, _ => 3 };

    private readonly DispatcherTimer _durationTimer;
    private DateTime _callStartedAt;
    private readonly CallAudioService _audioService;

    public CallViewModel(ICallService callService, ICallHubConnection hub, ActiveCallStore store, CallAudioService audioService)
    {
        _callService = callService;
        _hub = hub;
        _store = store;
        _audioService = audioService;

        _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _durationTimer.Tick += OnDurationTick;

        SubscribeHubEvents();
        _callService.ParticipantSpeakingChanged += OnParticipantSpeakingChanged;
        _callService.MuteChanged += OnMuteChangedExternally;
        _audioService.SpeakingStateChanged += OnLocalSpeakingStateChanged;
    }

    public bool NoiseSuppressionEnabled
    {
        get => _audioService.NoiseSuppressionEnabled;
        set
        {
            if (_audioService.NoiseSuppressionEnabled == value) return;
            _audioService.NoiseSuppressionEnabled = value;
            OnPropertyChanged();
        }
    }

    public void Initialize(CallStateDto state, string chatName, bool isGroupCall, int myUserId)
    {
        _myUserId = myUserId;
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
        { 1 => 100, 2 => 80, <= 4 => 56, <= 9 => 40, _ => 32 };

    partial void OnSidePanelModeChanged(CallSidePanelMode value)
    {
        OnPropertyChanged(nameof(IsChatMode));
        OnPropertyChanged(nameof(IsParticipantsMode));

        if (value == CallSidePanelMode.Chat)
        {
            UnreadChatCount = 0;
            OnPropertyChanged(nameof(HasUnread));
        }
    }

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
    private async Task ToggleMuteAsync() => await _callService.ToggleMuteAsync();

    [RelayCommand]
    private async Task LeaveCallAsync()
    {
        await _callService.LeaveCallAsync();
        Cleanup();
        _store.Clear();
    }

    [RelayCommand]
    private void CloseUi() => _store.CloseCallUi();

    [RelayCommand]
    private void ToggleChatPanel()
    {
        if (!IsChatPanelOpen)
        {
            IsChatPanelOpen = true;
            SidePanelMode = CallSidePanelMode.Chat;
            UnreadChatCount = 0;
            OnPropertyChanged(nameof(HasUnread));
        }
        else if (SidePanelMode == CallSidePanelMode.Chat)
        {
            IsChatPanelOpen = false;
        }
        else
        {
            SidePanelMode = CallSidePanelMode.Chat;
        }
    }

    [RelayCommand]
    private void OpenParticipantsPanel()
    {
        IsChatPanelOpen = true;
        SidePanelMode = CallSidePanelMode.Participants;
    }

    [RelayCommand]
    private async Task SendChatMessageAsync()
    {
        var text = MessageInput.Trim();
        if (string.IsNullOrEmpty(text)) return;
        MessageInput = string.Empty;
        await _hub.SendCallMessageAsync(CallId, text);
    }

    [RelayCommand]
    private void InsertEmoji(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;
        MessageInput += emoji;
    }


    private void SubscribeHubEvents()
    {
        _hub.CallParticipantJoined += OnParticipantJoined;
        _hub.CallParticipantLeft += OnParticipantLeft;
        _hub.ParticipantMuteChanged += OnParticipantMuteChanged;
        _hub.CallEnded += OnCallEnded;
        _hub.ActiveCallUpdated += OnActiveCallUpdated;
        _hub.CallMessageReceived += OnCallMessageReceived;
    }

    private void UnsubscribeHubEvents()
    {
        _hub.CallParticipantJoined -= OnParticipantJoined;
        _hub.CallParticipantLeft -= OnParticipantLeft;
        _hub.ParticipantMuteChanged -= OnParticipantMuteChanged;
        _hub.CallEnded -= OnCallEnded;
        _hub.ActiveCallUpdated -= OnActiveCallUpdated;
        _hub.CallMessageReceived -= OnCallMessageReceived;
        _callService.ParticipantSpeakingChanged -= OnParticipantSpeakingChanged;
        _callService.MuteChanged -= OnMuteChangedExternally;
        _audioService.SpeakingStateChanged -= OnLocalSpeakingStateChanged;
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

    private void OnMuteChangedExternally(bool isMuted) => Dispatcher.UIThread.Post(() =>
    {
        IsMuted = isMuted;
        var me = Participants.FirstOrDefault(p => p.UserId == _myUserId);
        me?.IsMuted = isMuted;
    });

    private void OnParticipantSpeakingChanged(int userId, bool isSpeaking) => Dispatcher.UIThread.Post(() =>
    {
        var vm = Participants.FirstOrDefault(p => p.UserId == userId);
        vm?.IsSpeaking = isSpeaking;
    });

    private void OnLocalSpeakingStateChanged(bool isSpeaking) => Dispatcher.UIThread.Post(() =>
        Participants.FirstOrDefault(p => p.UserId == _myUserId)?.IsSpeaking = isSpeaking);

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

    private void OnCallMessageReceived(CallChatMessageDto dto)
    {
        if (dto.CallId != CallId) return;
        Dispatcher.UIThread.Post(() =>
        {
            ChatMessages.Add(new CallChatMessageViewModel(dto, _myUserId));

            if (!IsChatPanelOpen || SidePanelMode != CallSidePanelMode.Chat)
            {
                UnreadChatCount++;
                OnPropertyChanged(nameof(HasUnread));
            }
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