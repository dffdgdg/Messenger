using Desktop.Data.Repositories.Abstractions;
using Desktop.Infrastructure.Diagnostics;
using Desktop.Infrastructure.Helpers;
using Desktop.Services.Features.Media.Files;
using Desktop.ViewModels.Chat.Commands;
using Desktop.ViewModels.Chat.Messages;
using Microsoft.Extensions.DependencyInjection;
using Shared.Helpers;
using System.Windows.Input;

namespace Desktop.ViewModels.Chat;

public sealed partial class MessageViewModel : ObservableObject, IDisposable
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int? SenderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOwn { get; set; }
    public bool IsSystemMessage { get; set; }
    public SystemEventType? SystemEventType { get; set; }
    public int? TargetUserId { get; set; }
    public string? TargetUserName { get; set; }
    public List<MessageFileDto> Files { get; set; } = [];
    public PollDto? PollDto { get; set; }
    public MessageDto Message { get; }
    public ChatCommands? Commands { get; set; }

    [ObservableProperty] public partial int? ReplyToMessageId { get; set; }
    [ObservableProperty] public partial string? ReplyToSenderName { get; set; }
    [ObservableProperty] public partial string? ReplyToContent { get; set; }
    [ObservableProperty] public partial bool ReplyToIsDeleted { get; set; }
    [ObservableProperty] public partial DateTime? EditedAt { get; set; }
    [ObservableProperty] public partial bool IsContinuation { get; set; }
    [ObservableProperty] public partial bool HasNextFromSame { get; set; }
    [ObservableProperty] public partial MessageGroupPosition GroupPosition { get; set; } = MessageGroupPosition.Alone;
    [ObservableProperty] public partial int? ForwardedFromMessageId { get; set; }
    [ObservableProperty] public partial string? ForwardedFromSenderName { get; set; }
    [ObservableProperty] public partial int? ForwardedFromSenderId { get; set; }
    [ObservableProperty] public partial PollViewModel? Poll { get; set; }
    [ObservableProperty] public partial string? SenderAvatar { get; set; }
    [ObservableProperty] public partial string? SenderName { get; set; }
    [ObservableProperty] public partial string? Content { get; set; }
    [ObservableProperty] public partial bool IsHighlighted { get; set; }
    [ObservableProperty] public partial bool IsUnread { get; set; }
    [ObservableProperty] public partial bool IsRead { get; set; }
    [ObservableProperty] public partial bool IsEdited { get; set; }
    [ObservableProperty] public partial bool IsDeleted { get; set; }
    [ObservableProperty] public partial bool IsPinned { get; set; }
    [ObservableProperty] public partial ObservableCollection<MessageFileViewModel> FileViewModels { get; set; } = [];
    [ObservableProperty] public partial bool ShowDateSeparator { get; set; }
    [ObservableProperty] public partial string? DateSeparatorText { get; set; }
    [ObservableProperty] public partial bool IsVoiceMessage { get; set; }
    [ObservableProperty] public partial double? VoiceDurationSeconds { get; set; }
    [ObservableProperty] public partial string? VoiceWaveform { get; set; }
    [ObservableProperty] public partial string? VoiceFileUrl { get; set; }
    [ObservableProperty] public partial bool IsVoicePlaying { get; set; }
    [ObservableProperty] public partial bool IsVoicePaused { get; set; }
    [ObservableProperty] public partial bool IsVoiceLoading { get; set; }
    [ObservableProperty] public partial double VoicePositionPercent { get; set; }
    [ObservableProperty] public partial string VoicePositionText { get; set; } = "0:00";
    [ObservableProperty] public partial string? VoiceError { get; set; }

    public ICommand? MentionClickCommand { get; set; }

    public ICommand ReplyActionCommand => _replyActionCommand ??= new RelayCommand(() => Commands?.Reply?.Execute(this));
    private ICommand? _replyActionCommand;

    public ICommand ForwardActionCommand => _forwardActionCommand ??= new RelayCommand(() => Commands?.Forward?.Execute(this));
    private ICommand? _forwardActionCommand;

    public bool IsNewIncoming { get; set; }
    public string SystemMessageTime { get; private set; } = "";
    public bool OriginalIsVoiceMessage { get; private set; }
    public bool OriginalHasPoll { get; private set; }
    public bool ShowPollResultsButton { get; private set; }
    public string DisplayContent { get; private set; } = "";
    public bool HasTextContent { get; private set; }
    public bool ShowFilesOnlyMeta { get; private set; }
    public bool ShowNonVoiceFiles { get; private set; }
    public bool HasFiles { get; private set; }
    public bool HasImages { get; private set; }
    public bool HasPoll { get; private set; }
    public bool HasReply { get; private set; }
    public bool HasForward { get; private set; }
    public string ForwardedFromHeader { get; private set; } = "";
    public bool CanOpenForwardSenderProfile { get; private set; }
    public bool ShowSenderName { get; private set; }
    public bool CanOpenSenderProfile { get; private set; }
    public string? SenderAvatarUrl { get => SenderAvatar; set => SenderAvatar = value; }
    public bool IsTextMessage => !OriginalIsVoiceMessage && !OriginalHasPoll && !IsSystemMessage;
    public bool ShowDeliveryStatus { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanPin { get; private set; }
    public bool ShowPinAction { get; private set; }
    public bool ShowUnpinAction { get; private set; }
    public string EditedLabel { get; private set; } = "";
    public string? EditedLabelFull { get; private set; }
    public bool HasStructuredSystemMessage { get; private set; }
    public bool HasSystemTargetUser { get; private set; }
    public int SystemTargetUserId { get; private set; }
    public string SystemActorDisplayName { get; }
    public string SystemTargetDisplayName { get; }
    public string SystemActionPrefixText { get; }
    public string SystemActionSuffixText { get; }
    public bool ShowVoiceMessage { get; private set; }
    public bool ShowPlayButton { get; private set; }
    public bool ShowPauseButton { get; private set; }
    public bool ShowResumeButton { get; private set; }
    public string VoiceDurationFormatted { get; private set; } = "0:00";
    public string BubbleClasses { get; private set; } = "MessageBubble Other Alone";
    public string ContentPreview { get; private set; } = "";
    public bool IsDisposed => _disposed;

    private readonly IFileDownloadService? _downloadService;
    private readonly INotificationService? _notificationService;
    private readonly IFileDownloadStateService? _stateService;
    private readonly IApiClientService? _apiClient;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly int _currentUserId;

    private static readonly TimeSpan VoiceLoadTimeout = TimeSpan.FromSeconds(20);
    private const long MaxCachedAudioBytes = 5 * 1024 * 1024;

    private byte[]? _cachedAudioBytes;
    private bool _disposed, _heavyInitDone, _suppressNotifications;
    private bool _subscribedToPlayer, _subscribedToPlaybackEvents;
    private PollViewModel? _boundPollVm;

    private static readonly string[] ContentProps = [nameof(DisplayContent), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(ContentPreview)];
    private static readonly string[] EditedProps = [nameof(EditedLabel), nameof(EditedLabelFull)];
    private static readonly string[] VoiceButtonProps = [nameof(ShowPlayButton), nameof(ShowPauseButton), nameof(ShowResumeButton)];
    private static readonly string[] PollDerivedProps = [nameof(HasPoll), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(CanEdit), nameof(ShowPollResultsButton)];
    private static readonly string[] ForwardDerivedProps = [nameof(HasForward), nameof(ForwardedFromHeader), nameof(CanEdit), nameof(CanOpenForwardSenderProfile)];
    private static readonly string[] PinProps = [nameof(ShowPinAction), nameof(ShowUnpinAction)];
    private static readonly string[] DeletedProps = [nameof(DisplayContent), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(ShowNonVoiceFiles), nameof(ShowDeliveryStatus), nameof(CanEdit), nameof(CanDelete), nameof(ShowVoiceMessage), nameof(VoiceDurationFormatted)];

    public MessageViewModel(MessageDto message, IFileDownloadService? downloadService = null, INotificationService? notificationService = null,
        IAudioPlayerService? audioPlayer = null, IApiClientService? apiClient = null, int currentUserId = 0, IFileDownloadStateService? stateService = null)
    {
        (_downloadService, _notificationService, _stateService, _audioPlayer, _apiClient, _currentUserId, Message) =
            (downloadService, notificationService, stateService, audioPlayer, apiClient, currentUserId, message);

        MemoryDiagnostics.OnMessageVmCreated();
        MapFromDto(message);

        SystemActorDisplayName = string.IsNullOrWhiteSpace(message.SenderName) ? "Пользователь" : message.SenderName;
        SystemTargetDisplayName = string.IsNullOrWhiteSpace(message.TargetUserName) ? "пользователя" : message.TargetUserName;
        SystemActionPrefixText = SystemEventMeta.GetPrefix(message.SystemEventType);
        SystemActionSuffixText = SystemEventMeta.GetSuffix(message.SystemEventType);

        InitChildViewModels(message);
        RecacheAllProperties();
        SubscribeToAudioPlayer();
    }

    private void MapFromDto(MessageDto m)
    {
        (Id, ChatId, SenderId, Content, CreatedAt, IsOwn) = (m.Id, m.ChatId, m.SenderId, m.Content, m.CreatedAt, m.IsOwn);
        (IsEdited, IsDeleted, EditedAt, PollDto, Files) = (m.IsEdited, m.IsDeleted, m.EditedAt, m.Poll, m.Files ?? []);
        IsPinned = m.IsPinned;
        (SenderName, SenderAvatar) = (m.SenderName, m.SenderAvatarUrl);
        (IsVoiceMessage, VoiceDurationSeconds, VoiceWaveform, VoiceFileUrl) = (m.IsVoiceMessage, m.VoiceDurationSeconds, m.VoiceWaveform, m.VoiceFileUrl);
        (IsSystemMessage, SystemEventType, TargetUserId, TargetUserName) = (m.IsSystemMessage, m.SystemEventType, m.TargetUserId, m.TargetUserName);
        (ReplyToMessageId, ForwardedFromMessageId) = (m.ReplyToMessageId, m.ForwardedFromMessageId);
        OriginalIsVoiceMessage = m.IsVoiceMessage;
        OriginalHasPoll = m.Poll is not null;

        if (m.ReplyToMessage is { } reply)
        {
            ReplyToSenderName = reply.SenderName;
            ReplyToIsDeleted = reply.IsDeleted;
            ReplyToContent = ChatPreviewFormatter.BuildReplyPreview(reply);
        }

        if (m.ForwardedFrom is not null)
        {
            ForwardedFromSenderName = m.ForwardedFrom.OriginalSenderName;
            ForwardedFromSenderId = m.ForwardedFrom.OriginalSenderId;
        }
    }

    public void EnsureHeavyInit()
    {
        if (_heavyInitDone || _disposed) return;
        _heavyInitDone = true;

        InitChildViewModels(Message);
        RecacheFileGroup();
        RecachePollGroup();
        RecacheContentGroup();
        RecacheEditGroup();

        if (HasFiles || HasPoll)
        {
            Notify(ContentProps);
            Notify(PollDerivedProps);
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasImages));
            OnPropertyChanged(nameof(FileViewModels));
        }
    }

    private void InitChildViewModels(MessageDto m)
    {
        if (m.Poll is not null)
        {
            Poll = CreatePollViewModel(m.Poll, m.SenderId);
            BindPollViewModel(Poll);
        }

        if (Files.Count > 0)
        {
            FileViewModels = new(Files.Select(f => new MessageFileViewModel(f, _downloadService, _notificationService, _stateService)));
            // _ = InitFileStatesAsync();
        }
    }

    private async Task InitFileStatesAsync()
    {
        foreach (var fileVm in FileViewModels)
            await fileVm.InitializeAsync();
    }

    private PollViewModel? CreatePollViewModel(PollDto pollDto, int? ownerId = null)
    {
        if (_currentUserId == 0 || _apiClient == null) return null;
        return new PollViewModel(pollDto, _currentUserId, _apiClient, ownerId);
    }

    private void RecacheAllProperties()
    {
        _suppressNotifications = true;
        try
        {
            RecacheFileGroup(); RecachePollGroup(); RecacheReplyForwardGroup();
            RecacheContentGroup(); RecacheEditGroup(); RecacheDeliveryGroup();
            RecachePinGroup(); RecacheSenderGroup(); RecacheSystemGroup();
            RecacheVoiceGroup(); RecacheVoiceButtons(); UpdateBubbleClasses();
        }
        finally { _suppressNotifications = false; }
        OnPropertyChanged(string.Empty);
    }

    private void RecacheFileGroup() { HasFiles = Files.Count > 0; HasImages = Files.Any(f => f.PreviewType == "image"); }
    private void RecachePollGroup() { HasPoll = Poll is not null; ShowPollResultsButton = Poll is { ShowResultsButton: true }; }

    private void RecacheReplyForwardGroup()
    {
        HasReply = ReplyToMessageId.HasValue;
        HasForward = ForwardedFromMessageId.HasValue;
        ForwardedFromHeader = HasForward ? $"Переслано от {ForwardedFromSenderName ?? "неизвестного пользователя"}" : "";
        CanOpenForwardSenderProfile = HasForward && ForwardedFromSenderId > 0;
    }

    private void RecacheContentGroup()
    {
        DisplayContent = IsDeleted ? "Сообщение удалено" : (Content ?? "");
        HasTextContent = !IsDeleted && !IsSystemMessage && !string.IsNullOrWhiteSpace(Content) && !HasPoll && !IsVoiceMessage;
        ShowFilesOnlyMeta = !HasTextContent && !IsDeleted && HasFiles && !IsVoiceMessage;
        ShowNonVoiceFiles = HasFiles && !IsDeleted && !IsSystemMessage;
        ContentPreview = BuildSelfPreview();
    }

    private string BuildSelfPreview()
    {
        if (IsDeleted) return "Сообщение удалено";
        if (IsVoiceMessage) return "Голосовое сообщение";
        if (HasPoll) return "📊 " + ChatPreviewFormatter.BuildContentPreview(Content, "Опрос");

        var filesCount = Files?.Count ?? 0;
        if (filesCount > 0 && string.IsNullOrWhiteSpace(Content))
            return filesCount == 1 ? "Вложение" : $"{filesCount} файл(ов)";
        if (filesCount > 0 && !string.IsNullOrWhiteSpace(Content))
            return ChatPreviewFormatter.BuildContentPreview(Content);

        return ChatPreviewFormatter.BuildContentPreview(Content, "Сообщение");
    }

    private void RecacheEditGroup()
    {
        CanEdit = IsOwn && !IsDeleted && !IsSystemMessage && Poll is null && !IsVoiceMessage && !HasForward;
        EditedLabel = IsEdited ? "изм." : "";
        EditedLabelFull = IsEdited && EditedAt.HasValue ? $"изменено {EditedAt.Value:HH:mm}" : null;
    }

    private void RecacheDeliveryGroup() { ShowDeliveryStatus = IsOwn && !IsDeleted && !IsSystemMessage; CanDelete = IsOwn && !IsDeleted && !IsSystemMessage; }

    private void RecachePinGroup() { CanPin = !IsDeleted && !IsSystemMessage; ShowPinAction = CanPin && !IsPinned; ShowUnpinAction = CanPin && IsPinned; }

    private void RecacheSenderGroup() { ShowSenderName = !IsOwn && !IsContinuation && !IsSystemMessage; CanOpenSenderProfile = SenderId > 0; }

    private void RecacheSystemGroup()
    {
        HasStructuredSystemMessage = IsSystemMessage && SystemEventType.HasValue;
        HasSystemTargetUser = TargetUserId > 0;
        SystemTargetUserId = TargetUserId ?? 0;
        SystemMessageTime = IsSystemMessage ? CreatedAt.ToString("HH:mm") : "";
    }

    private void RecacheVoiceGroup()
    {
        ShowVoiceMessage = IsVoiceMessage && !IsDeleted;
        VoiceDurationFormatted = VoiceDurationSeconds.HasValue ? FormatTime(TimeSpan.FromSeconds(VoiceDurationSeconds.Value)) : "0:00";
    }

    private void RecacheVoiceButtons()
    {
        (ShowPlayButton, ShowPauseButton, ShowResumeButton) = ShowVoiceMessage switch
        {
            true when !IsVoicePlaying && !IsVoicePaused && !IsVoiceLoading => (true, false, false),
            true when IsVoicePlaying && !IsVoicePaused => (false, true, false),
            true when IsVoicePaused => (false, false, true),
            _ => (false, false, false)
        };
        Notify(VoiceButtonProps);
    }

    private void UpdateBubbleClasses()
    {
        var pos = GroupPosition switch { MessageGroupPosition.First => "First", MessageGroupPosition.Middle => "Middle", MessageGroupPosition.Last => "Last", _ => "Alone" };
        BubbleClasses = $"MessageBubble {(IsOwn ? "Own" : "Other")} {pos}";
    }

    public void ApplyUpdate(MessageDto updated)
    {
        (Content, IsEdited, EditedAt, IsPinned) = (updated.Content, updated.IsEdited, updated.EditedAt, updated.IsPinned);
        RecacheContentGroup(); RecacheEditGroup(); RecachePinGroup();
        Notify(ContentProps); Notify(EditedProps); Notify(PinProps);
        OnPropertyChanged(nameof(CanEdit));
    }

    public void MarkAsDeleted()
    {
        if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Stop();

        (IsDeleted, Content, IsPinned, VoiceFileUrl, VoiceDurationSeconds) = (true, null, false, null, null);
        (ReplyToMessageId, ReplyToSenderName, ReplyToContent) = (null, null, null);
        ReplyToIsDeleted = false;
        (ForwardedFromMessageId, ForwardedFromSenderName, ForwardedFromSenderId) = (null, null, null);

        ResetPlayerState();
        _cachedAudioBytes = null;
        RecacheAllProperties();
        EnsureHeavyInit();
    }

    public void MarkAsRead() => IsRead = true;

    public void UpdatePoll(PollDto pollDto)
    {
        PollDto = pollDto;
        Message.Poll = pollDto;

        if (Poll is not null)
        {
            Poll.ApplyDto(pollDto);
        }
        else { Poll = CreatePollViewModel(pollDto); BindPollViewModel(Poll); }

        _ = PersistPollStateToCacheAsync();
        RecachePollGroup(); RecacheContentGroup(); RecacheEditGroup();
        Notify(PollDerivedProps);
    }

    private void OnPollServerStateApplied(PollDto pollDto) { PollDto = pollDto; Message.Poll = pollDto; _ = PersistPollStateToCacheAsync(); }

    private void BindPollViewModel(PollViewModel? vm)
    {
        if (ReferenceEquals(_boundPollVm, vm)) return;

        if (_boundPollVm != null) { _boundPollVm.ServerStateApplied -= OnPollServerStateApplied; _boundPollVm.PropertyChanged -= OnBoundPollPropertyChanged; _boundPollVm.ShowResultsRequested -= OnShowResultsRequested; }
        _boundPollVm = vm;
        if (_boundPollVm != null) { _boundPollVm.ServerStateApplied += OnPollServerStateApplied; _boundPollVm.PropertyChanged += OnBoundPollPropertyChanged; _boundPollVm.ShowResultsRequested += OnShowResultsRequested; }
    }

    private void OnShowResultsRequested(PollViewModel vm) => Commands?.ShowPollResults?.Execute(vm);

    private void OnBoundPollPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PollViewModel.ShowResultsButton))
        {
            ShowPollResultsButton = _boundPollVm is { ShowResultsButton: true };
            OnPropertyChanged(nameof(ShowPollResultsButton));
        }
    }

    private async Task PersistPollStateToCacheAsync()
    {
        var originalFiles = Message.Files;
        Message.Files = [];
        try { await App.Current.Services.GetRequiredService<ILocalCacheService>().UpsertMessageAsync(Message); }
        catch { }
        finally { Message.Files = originalFiles; }
    }

    private void SubscribeToAudioPlayer()
    {
        if (_audioPlayer is null || !IsVoiceMessage || _subscribedToPlayer) return;
        _audioPlayer.PlaybackStarted += OnPlaybackStarted;
        _audioPlayer.PlaybackStopped += OnPlaybackStopped;
        _subscribedToPlayer = true;
    }

    private void SubscribeToPlaybackEvents()
    {
        if (_audioPlayer is null || _subscribedToPlaybackEvents) return;
        _audioPlayer.PlaybackPaused += OnPlaybackPaused;
        _audioPlayer.PlaybackResumed += OnPlaybackResumed;
        _audioPlayer.PositionChanged += OnPositionChanged;
        _subscribedToPlaybackEvents = true;
    }

    private void UnsubscribeFromPlaybackEvents()
    {
        if (_audioPlayer is null || !_subscribedToPlaybackEvents) return;
        _audioPlayer.PlaybackPaused -= OnPlaybackPaused;
        _audioPlayer.PlaybackResumed -= OnPlaybackResumed;
        _audioPlayer.PositionChanged -= OnPositionChanged;
        _subscribedToPlaybackEvents = false;
    }

    private void UnsubscribeFromAudioPlayer()
    {
        if (_audioPlayer is null || !_subscribedToPlayer) return;
        _audioPlayer.PlaybackStarted -= OnPlaybackStarted;
        _audioPlayer.PlaybackStopped -= OnPlaybackStopped;
        UnsubscribeFromPlaybackEvents();
        _subscribedToPlayer = false;
    }
    private CancellationTokenSource? _postCts = new();

    private void PostIfNotDisposed(Action action)
    {
        if (_disposed) return;

        var cts = _postCts;
        if (cts?.IsCancellationRequested != false) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) action();
        }, DispatcherPriority.Normal);
    }

    private void OnPlaybackStarted(int msgId) => PostIfNotDisposed(() =>
    {
        if (msgId == Id)
        {
            SubscribeToPlaybackEvents();
            (IsVoicePlaying, IsVoicePaused, VoiceError) = (true, false, null);
            RecacheVoiceButtons();
        }
        else
        {
            ResetPlayerState();
        }
    });

    private void OnPlaybackPaused(int msgId) { if (msgId == Id) PostIfNotDisposed(() => { IsVoicePaused = true; RecacheVoiceButtons(); }); }
    private void OnPlaybackResumed(int msgId) { if (msgId == Id) PostIfNotDisposed(() => { IsVoicePaused = false; RecacheVoiceButtons(); }); }

    private void OnPlaybackStopped(int msgId)
    {
        if (msgId != Id) return;
        UnsubscribeFromPlaybackEvents();
        PostIfNotDisposed(() => { ResetPlayerState(); _cachedAudioBytes = null; });
    }

    private void OnPositionChanged(int msgId, TimeSpan pos)
    {
        if (msgId != Id) return;
        PostIfNotDisposed(() =>
        {
            var dur = _audioPlayer?.Duration ?? TimeSpan.Zero;
            VoicePositionPercent = dur.TotalMilliseconds > 0 ? pos / dur * 100 : 0;
            VoicePositionText = FormatTime(pos);
        });
    }

    private void ResetPlayerState()
    {
        (IsVoicePlaying, IsVoicePaused, IsVoiceLoading) = (false, false, false);
        (VoicePositionPercent, VoicePositionText) = (0, "0:00");
        RecacheVoiceButtons();
    }

    [RelayCommand]
    private async Task PlayVoice()
    {
        if (!CanStartPlayback(out var mediaUrl)) return;
        if (TryResumeIfPaused()) return;

        if (_cachedAudioBytes is null)
        {
            _cachedAudioBytes = await LoadAudioBytesAsync(mediaUrl!);
            if (_cachedAudioBytes is null) return;
        }
        _audioPlayer!.Play(Id, new MemoryStream(_cachedAudioBytes, writable: false));
    }

    private bool CanStartPlayback(out string? mediaUrl)
    {
        mediaUrl = null;
        if (_audioPlayer is null || _apiClient is null || _disposed) return false;

        if (string.IsNullOrEmpty(VoiceFileUrl)) { VoiceError = "URL аудио недоступен"; return false; }

        mediaUrl = BuildMediaUrl(VoiceFileUrl);
        return true;
    }

    private bool TryResumeIfPaused()
    {
        if (_audioPlayer!.CurrentMessageId != Id || !_audioPlayer.IsPaused) return false;
        _audioPlayer.Resume();
        return true;
    }

    private async Task<byte[]?> LoadAudioBytesAsync(string mediaUrl)
    {
        IsVoiceLoading = true; RecacheVoiceButtons();
        try
        {
            using var cts = new CancellationTokenSource(VoiceLoadTimeout);
            await using var stream = await _apiClient!.GetStreamAsync(mediaUrl, cts.Token);
            if (stream is null) { VoiceError = "Не удалось загрузить аудио"; return null; }

            await using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cts.Token);
            if (_disposed) return null;

            var data = ms.ToArray();
            if (data.Length <= MaxCachedAudioBytes) _cachedAudioBytes = data;
            return data;
        }
        catch (OperationCanceledException) { VoiceError = "Превышено время ожидания"; return null; }
        catch (Exception ex) { VoiceError = $"Ошибка: {ex.Message}"; return null; }
        finally { IsVoiceLoading = false; RecacheVoiceButtons(); }
    }

    [RelayCommand] private void PauseVoice() { if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Pause(); }
    [RelayCommand] private void StopVoice() { if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Stop(); }
    [RelayCommand] private void SeekVoice(double pct) { if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Seek(pct / 100.0); }

    [RelayCommand]
    private async Task DownloadVoice()
    {
        if (string.IsNullOrEmpty(VoiceFileUrl) || _downloadService is null) return;
        try
        {
            var path = await _downloadService.DownloadFileAsync(BuildMediaUrl(VoiceFileUrl), $"voice_{Id}_{CreatedAt:yyyyMMdd_HHmmss}.wav");
            if (path is not null) _notificationService?.ShowSuccessAsync($"Голосовое сохранено: {path}", copyToClipboard: false);
        }
        catch (Exception ex) { _notificationService?.ShowErrorAsync($"Ошибка: {ex.Message}", copyToClipboard: false); }
    }

    private static readonly long GroupingThresholdTicks = TimeSpan.FromMinutes(5).Ticks;

    private void UpdateGroupPosition() => GroupPosition = (IsContinuation, HasNextFromSame) switch
    {
        (false, false) => MessageGroupPosition.Alone,
        (false, true) => MessageGroupPosition.First,
        (true, true) => MessageGroupPosition.Middle,
        (true, false) => MessageGroupPosition.Last,
    };

    public static bool CanGroup(MessageViewModel a, MessageViewModel b) =>
        !a.IsSystemMessage && !b.IsSystemMessage && a.SenderId == b.SenderId && !a.IsDeleted && !b.IsDeleted &&
        a.CreatedAt.Date == b.CreatedAt.Date && Math.Abs(b.CreatedAt.Ticks - a.CreatedAt.Ticks) <= GroupingThresholdTicks;

    public static void RecalculateGrouping(IList<MessageViewModel> msgs) => ApplyGrouping(msgs, 0, msgs.Count - 1);
    public static void UpdateGroupingAround(IList<MessageViewModel> msgs, int idx) => ApplyGrouping(msgs, Math.Max(0, idx - 1), Math.Min(msgs.Count - 1, idx + 1));

    private static void ApplyGrouping(IList<MessageViewModel> msgs, int start, int end)
    {
        for (var i = start; i <= end; i++)
        {
            msgs[i].IsContinuation = i > 0 && CanGroup(msgs[i - 1], msgs[i]);
            msgs[i].HasNextFromSame = i < msgs.Count - 1 && CanGroup(msgs[i], msgs[i + 1]);
        }
    }
    partial void OnContentChanged(string? value) { if (_suppressNotifications) return; RecacheContentGroup(); Notify(ContentProps); }

    partial void OnPollChanged(PollViewModel? oldValue, PollViewModel? newValue)
    {
        if (oldValue != null && !ReferenceEquals(oldValue, newValue)) oldValue.Dispose();
        BindPollViewModel(newValue);
        if (_suppressNotifications) return;
        RecachePollGroup(); RecacheContentGroup(); RecacheEditGroup(); Notify(PollDerivedProps);
    }

    partial void OnIsEditedChanged(bool value) { if (!_suppressNotifications) { RecacheEditGroup(); Notify(EditedProps); } }
    partial void OnEditedAtChanged(DateTime? value) { if (!_suppressNotifications) { EditedLabelFull = IsEdited && value.HasValue ? $"изменено {value.Value:HH:mm}" : null; Notify(EditedProps); } }

    partial void OnIsDeletedChanged(bool value)
    {
        if (_suppressNotifications) return;
        RecacheContentGroup(); RecacheEditGroup(); RecacheDeliveryGroup(); RecachePinGroup();
        Notify(ContentProps); Notify(PinProps);
        OnPropertyChanged(nameof(ShowDeliveryStatus)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanDelete)); OnPropertyChanged(nameof(CanPin));
    }

    partial void OnIsReadChanged(bool value) { if (!_suppressNotifications) OnPropertyChanged(nameof(ShowDeliveryStatus)); }
    partial void OnIsContinuationChanged(bool value) { if (!_suppressNotifications) { RecacheSenderGroup(); OnPropertyChanged(nameof(ShowSenderName)); UpdateGroupPosition(); } }
    partial void OnHasNextFromSameChanged(bool value) { if (!_suppressNotifications) UpdateGroupPosition(); }

    partial void OnVoiceDurationSecondsChanged(double? value)
    {
        VoiceDurationFormatted = value.HasValue ? FormatTime(TimeSpan.FromSeconds(value.Value)) : "0:00";
        if (!_suppressNotifications) OnPropertyChanged(nameof(VoiceDurationFormatted));
    }

    partial void OnIsVoicePlayingChanged(bool value) => RecacheVoiceButtons();
    partial void OnIsVoicePausedChanged(bool value) => RecacheVoiceButtons();
    partial void OnIsVoiceLoadingChanged(bool value) => RecacheVoiceButtons();

    partial void OnForwardedFromMessageIdChanged(int? value) { if (_suppressNotifications) return; RecacheReplyForwardGroup(); RecacheEditGroup(); Notify(ForwardDerivedProps); }
    partial void OnForwardedFromSenderNameChanged(string? value) { ForwardedFromHeader = HasForward ? $"Переслано от {value ?? "неизвестного пользователя"}" : ""; if (!_suppressNotifications) OnPropertyChanged(nameof(ForwardedFromHeader)); }
    partial void OnForwardedFromSenderIdChanged(int? value) { CanOpenForwardSenderProfile = HasForward && value > 0; if (!_suppressNotifications) OnPropertyChanged(nameof(CanOpenForwardSenderProfile)); }
    partial void OnIsPinnedChanged(bool value) { if (_suppressNotifications) return; RecachePinGroup(); Notify(PinProps); }
    partial void OnGroupPositionChanged(MessageGroupPosition value) { UpdateBubbleClasses(); if (!_suppressNotifications) OnPropertyChanged(nameof(BubbleClasses)); }
    private void Notify(string[] names) { if (_suppressNotifications) return; foreach (var n in names) OnPropertyChanged(n); }
    private static string FormatTime(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static string BuildMediaUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute)) return $"{App.ApiUrl.TrimEnd('/')}/{url.TrimStart('/')}";
        if (!Uri.TryCreate(App.ApiUrl, UriKind.Absolute, out var api)) return absolute.ToString();

        bool sameHost = string.Equals(absolute.Host, api.Host, StringComparison.OrdinalIgnoreCase) && absolute.Port == api.Port;
        return sameHost || !absolute.AbsolutePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ? absolute.ToString() : new Uri(api, absolute.PathAndQuery).ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _postCts?.Cancel();

        if (_audioPlayer?.CurrentMessageId == Id)
        {
            _audioPlayer.Stop();
        }
        UnsubscribeFromAudioPlayer();

        BindPollViewModel(null);
        Poll?.Dispose();
        Poll = null;

        if (FileViewModels is { Count: > 0 })
        {
            foreach (var fileVm in FileViewModels)
            {
                fileVm?.Dispose();
            }
            FileViewModels.Clear();
        }

        _cachedAudioBytes = null;
        Commands = null;

        _postCts?.Dispose();
        _postCts = null;

        MemoryDiagnostics.OnMessageVmDisposed();
    }
}