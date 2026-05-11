using Desktop.Data.Repositories.Abstractions;
using Desktop.Infrastructure.Diagnostics;
using Desktop.Infrastructure.Helpers;
using Desktop.Services.Features.Media.Files;
using Desktop.ViewModels.Chat.Commands;
using Desktop.ViewModels.Chat.Messages;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MsgShared = Shared;

namespace Desktop.ViewModels.Chat;

public sealed partial class MessageViewModel : ObservableObject, IDisposable
{
    #region Properties

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

    #endregion

    #region Observable Properties

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

    #endregion

    #region Commands

    public ICommand? MentionClickCommand { get; set; }

    private ICommand? _replyActionCommand;
    private ICommand? _forwardActionCommand;

    public ICommand ReplyActionCommand => _replyActionCommand ??= new RelayCommand(() => Commands?.Reply?.Execute(this));

    public ICommand ForwardActionCommand => _forwardActionCommand ??= new RelayCommand(() => Commands?.Forward?.Execute(this));

    #endregion

    #region Cached Computed Properties
    public bool OriginalIsVoiceMessage { get; private set; }
    public bool OriginalHasPoll { get; private set; }
    public bool ShowPollResultsButton { get; private set; }
    public string DisplayContent { get; private set; } = string.Empty;
    public bool HasTextContent { get; private set; }
    public bool ShowFilesOnlyMeta { get; private set; }
    public bool ShowNonVoiceFiles { get; private set; }
    public bool HasFiles { get; private set; }
    public bool HasImages { get; private set; }
    public bool HasPoll { get; private set; }
    public bool HasReply { get; private set; }
    public bool HasForward { get; private set; }
    public string ForwardedFromHeader { get; private set; } = string.Empty;
    public bool ShowSenderName { get; private set; }
    public bool CanOpenSenderProfile { get; private set; }
    public string? SenderAvatarUrl { get => SenderAvatar; set => SenderAvatar = value; }
    public bool ShowDeliveryStatus { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanPin { get; private set; }
    public bool ShowPinAction { get; private set; }
    public bool ShowUnpinAction { get; private set; }
    public string EditedLabel { get; private set; } = string.Empty;
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
    public string ContentPreview { get; private set; } = string.Empty;
    #endregion

    #region Private Fields

    private readonly IFileDownloadService? _downloadService;
    private readonly INotificationService? _notificationService;
    private readonly IFileDownloadStateService? _stateService;
    private readonly IApiClientService? _apiClient;
    private readonly IAudioPlayerService? _audioPlayer;

    private static readonly TimeSpan VoiceLoadTimeout = TimeSpan.FromSeconds(20);

    private byte[]? _cachedAudioBytes;
    private bool _disposed;
    private bool _subscribedToPlayer;
    private bool _subscribedToPlaybackEvents;
    private PollViewModel? _boundPollVm;
    private readonly int _currentUserId;

    #endregion

    #region Notification Batches

    private static readonly string[] ContentProps = [nameof(DisplayContent), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(ContentPreview)];

    private static readonly string[] EditedProps = [nameof(EditedLabel), nameof(EditedLabelFull)];

    private static readonly string[] VoiceButtonProps = [nameof(ShowPlayButton), nameof(ShowPauseButton), nameof(ShowResumeButton)];

    private static readonly string[] PollDerivedProps = [nameof(HasPoll), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(CanEdit), nameof(ShowPollResultsButton)];

    private static readonly string[] ForwardDerivedProps = [nameof(HasForward), nameof(ForwardedFromHeader), nameof(CanEdit)];

    private static readonly string[] PinProps = [nameof(ShowPinAction), nameof(ShowUnpinAction)];

    private static readonly string[] DeletedProps =
    [
        nameof(DisplayContent), nameof(HasTextContent), nameof(ShowFilesOnlyMeta),
        nameof(ShowNonVoiceFiles), nameof(ShowDeliveryStatus), nameof(CanEdit),
        nameof(CanDelete), nameof(ShowVoiceMessage), nameof(VoiceDurationFormatted)
    ];

    #endregion

    #region Constructor

    public MessageViewModel(MessageDto message, IFileDownloadService? downloadService = null, INotificationService? notificationService = null,
        IAudioPlayerService? audioPlayer = null, IApiClientService? apiClient = null, int currentUserId = 0, IFileDownloadStateService? stateService = null)
    {
        _downloadService = downloadService;
        _notificationService = notificationService;
        _stateService = stateService;
        _audioPlayer = audioPlayer;
        _currentUserId = currentUserId;
        _apiClient = apiClient;
        Message = message;
        MemoryDiagnostics.OnMessageVmCreated();

        MapFromDto(message);
        InitChildViewModels(message);

        SystemActorDisplayName = string.IsNullOrWhiteSpace(message.SenderName) ? "Пользователь" : message.SenderName;

        SystemTargetDisplayName = string.IsNullOrWhiteSpace(message.TargetUserName) ? "пользователя" : message.TargetUserName;

        SystemActionPrefixText = BuildSystemActionPrefix(message.SystemEventType);
        SystemActionSuffixText = BuildSystemActionSuffix(message.SystemEventType);

        RecacheAllProperties();
        SubscribeToAudioPlayer();
    }

    private void MapFromDto(MessageDto message)
    {
        (Id, ChatId, SenderId, Content, CreatedAt, IsOwn) = (message.Id, message.ChatId, message.SenderId, message.Content, message.CreatedAt, message.IsOwn);

        (IsEdited, IsDeleted, EditedAt, PollDto, Files) = (message.IsEdited, message.IsDeleted, message.EditedAt, message.Poll, message.Files ?? []);

        IsPinned = message.IsPinned;
        (SenderName, SenderAvatar) = (message.SenderName, message.SenderAvatarUrl);

        (IsVoiceMessage, VoiceDurationSeconds, VoiceWaveform, VoiceFileUrl) = (message.IsVoiceMessage, message.VoiceDurationSeconds, message.VoiceWaveform, message.VoiceFileUrl);

        (IsSystemMessage, SystemEventType, TargetUserId, TargetUserName) = (message.IsSystemMessage, message.SystemEventType, message.TargetUserId, message.TargetUserName);

        (ReplyToMessageId, ForwardedFromMessageId) = (message.ReplyToMessageId, message.ForwardedFromMessageId);

        OriginalIsVoiceMessage = message.IsVoiceMessage;
        OriginalHasPoll = message.Poll is not null;

        if (message.ReplyToMessage is { } reply)
        {
            ReplyToSenderName = reply.SenderName;
            ReplyToIsDeleted = reply.IsDeleted;
            ReplyToContent = ChatPreviewFormatter.BuildReplyPreview(reply);
        }

        if (message.ForwardedFrom is not null)
            ForwardedFromSenderName = message.ForwardedFrom.OriginalSenderName;
    }

    private PollViewModel? CreatePollViewModel(PollDto pollDto, int? ownerId = null)
    {
        if (_currentUserId == 0 || _apiClient == null)
        {
            return null;
        }

        return new PollViewModel(pollDto, _currentUserId, _apiClient, ownerId);
    }

    private static string BuildSystemActionPrefix(SystemEventType? eventType) => eventType switch
    {
        MsgShared.Enum.SystemEventType.ChatCreated => " создал(а) группу",
        MsgShared.Enum.SystemEventType.MemberAdded => " добавил(а) ",
        MsgShared.Enum.SystemEventType.MemberRemoved => " удалил(а) ",
        MsgShared.Enum.SystemEventType.MemberLeft => " покинул(а) группу",
        MsgShared.Enum.SystemEventType.RoleChanged => " изменил(а) роль участника ",
        MsgShared.Enum.SystemEventType.CallStarted => " начал(а) звонок",
        MsgShared.Enum.SystemEventType.MessagePinned => " закрепил(а) сообщение",
        MsgShared.Enum.SystemEventType.MessageUnpinned => " открепил(а) сообщение",
        _ => string.Empty
    };

    private void InitChildViewModels(MessageDto message)
    {
        if (message.Poll is not null)
        {
            Poll = CreatePollViewModel(message.Poll, message.SenderId);
            BindPollViewModel(Poll);
        }

        if (Files.Count > 0)
        {
            FileViewModels = new(Files.Select(f => new MessageFileViewModel(
                f,
                _downloadService,
                _notificationService,
                _stateService)));

            _ = InitFileStatesAsync();
        }
    }

    private async Task InitFileStatesAsync()
    {
        foreach (var fileVm in FileViewModels)
        {
            await fileVm.InitializeAsync();
        }
    }
    private static string BuildSystemActionSuffix(SystemEventType? eventType) => eventType switch
    {
        MsgShared.Enum.SystemEventType.MemberAdded => " в группу",
        MsgShared.Enum.SystemEventType.MemberRemoved => " из группы",
        _ => string.Empty
    };

    #endregion

    #region RecacheAllProperties & Groups

    /// <summary>
    /// Единственное место полного пересчёта всех кэшированных свойств.
    /// Вызывается при инициализации и при MarkAsDeleted.
    /// </summary>
    private void RecacheAllProperties()
    {
        RecacheFileGroup();
        RecachePollGroup();
        RecacheReplyForwardGroup();
        RecacheContentGroup();
        RecacheEditGroup();
        RecacheDeliveryGroup();
        RecachePinGroup();
        RecacheSenderGroup();
        RecacheSystemGroup();
        RecacheVoiceGroup();
        RecacheVoiceButtons();
        UpdateBubbleClasses();
    }

    private void RecacheFileGroup()
    {
        HasFiles = Files.Count > 0;
        HasImages = Files.Any(f => f.PreviewType == "image");
    }

    private void RecachePollGroup()
    {
        HasPoll = Poll is not null;
        ShowPollResultsButton = Poll is { ShowResultsButton: true };
    }

    private void RecacheReplyForwardGroup()
    {
        HasReply = ReplyToMessageId.HasValue;
        HasForward = ForwardedFromMessageId.HasValue;
        ForwardedFromHeader = HasForward ? $"Переслано от {ForwardedFromSenderName ?? "неизвестного пользователя"}" : string.Empty;
    }

    private void RecacheContentGroup()
    {
        DisplayContent = IsDeleted ? "Сообщение удалено" : (Content ?? string.Empty);
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
            return $"{ChatPreviewFormatter.BuildContentPreview(Content)}";

        return ChatPreviewFormatter.BuildContentPreview(Content, "Сообщение");
    }
    private void RecacheEditGroup()
    {
        CanEdit = IsOwn && !IsDeleted && !IsSystemMessage && Poll is null && !IsVoiceMessage && !HasForward;
        EditedLabel = IsEdited ? "изм." : string.Empty;
        EditedLabelFull = IsEdited && EditedAt.HasValue ? $"изменено {EditedAt.Value:HH:mm}" : null;
    }

    private void RecacheDeliveryGroup()
    {
        ShowDeliveryStatus = IsOwn && !IsDeleted && !IsSystemMessage;
        CanDelete = IsOwn && !IsDeleted && !IsSystemMessage;
    }

    private void RecachePinGroup()
    {
        CanPin = !IsDeleted && !IsSystemMessage;
        ShowPinAction = CanPin && !IsPinned;
        ShowUnpinAction = CanPin && IsPinned;
    }

    private void RecacheSenderGroup()
    {
        ShowSenderName = !IsOwn && !IsContinuation && !IsSystemMessage;
        CanOpenSenderProfile = SenderId > 0;
    }

    private void RecacheSystemGroup()
    {
        HasStructuredSystemMessage = IsSystemMessage && SystemEventType.HasValue && SystemEventType != MsgShared.Enum.SystemEventType.CallEnded
            && SystemEventType != MsgShared.Enum.SystemEventType.CallStarted;
        HasSystemTargetUser = TargetUserId > 0;
        SystemTargetUserId = TargetUserId ?? 0;
    }

    private void RecacheVoiceGroup()
    {
        ShowVoiceMessage = IsVoiceMessage && !IsDeleted;
        VoiceDurationFormatted = VoiceDurationSeconds.HasValue ? FormatTime(TimeSpan.FromSeconds(VoiceDurationSeconds.Value)) : "0:00";
    }

    private void RecacheVoiceButtons()
    {
        ShowPlayButton = ShowVoiceMessage && !IsVoicePlaying && !IsVoicePaused && !IsVoiceLoading;
        ShowPauseButton = IsVoicePlaying && !IsVoicePaused;
        ShowResumeButton = IsVoicePaused;
        Notify(VoiceButtonProps);
    }

    private void UpdateBubbleClasses()
    {
        var owner = IsOwn ? "Own" : "Other";
        var pos = GroupPosition switch
        {
            MessageGroupPosition.First => "First",
            MessageGroupPosition.Middle => "Middle",
            MessageGroupPosition.Last => "Last",
            _ => "Alone"
        };
        BubbleClasses = $"MessageBubble {owner} {pos}";
    }

    #endregion

    #region Public Update API

    public void ApplyUpdate(MessageDto updated)
    {
        Content = updated.Content;
        IsEdited = updated.IsEdited;
        EditedAt = updated.EditedAt;
        IsPinned = updated.IsPinned;

        RecacheContentGroup();
        RecacheEditGroup();
        RecachePinGroup();

        Notify(ContentProps);
        Notify(EditedProps);
        Notify(PinProps);
        OnPropertyChanged(nameof(CanEdit));
    }

    public void MarkAsDeleted()
    {
        if (_audioPlayer?.CurrentMessageId == Id)
            _audioPlayer.Stop();

        IsDeleted = true;
        Content = null;
        IsPinned = false;
        VoiceFileUrl = null;
        VoiceDurationSeconds = null;

        ResetPlayerState();
        _cachedAudioBytes = null;

        RecacheAllProperties();

        Notify(DeletedProps);
        Notify(PinProps);
        OnPropertyChanged(nameof(BubbleClasses));
    }

    public void MarkAsRead() => IsRead = true;

    #endregion

    #region Poll

    public void UpdatePoll(PollDto pollDto)
    {
        PollDto = pollDto;
        Message.Poll = pollDto;

        if (Poll is not null)
        {
            Poll.ApplyDto(pollDto);
        }
        else
        {
            Poll = CreatePollViewModel(pollDto);
            BindPollViewModel(Poll);
        }

        _ = PersistPollStateToCacheAsync();

        RecachePollGroup();
        RecacheContentGroup();
        RecacheEditGroup();

        Notify(PollDerivedProps);
    }

    private void OnPollServerStateApplied(PollDto pollDto)
    {
        PollDto = pollDto;
        Message.Poll = pollDto;
        _ = PersistPollStateToCacheAsync();
    }

    private void BindPollViewModel(PollViewModel? pollViewModel)
    {
        if (ReferenceEquals(_boundPollVm, pollViewModel)) return;

        if (_boundPollVm != null)
        {
            _boundPollVm.ServerStateApplied -= OnPollServerStateApplied;
            _boundPollVm.PropertyChanged -= OnBoundPollPropertyChanged;
            _boundPollVm.ShowResultsRequested -= OnShowResultsRequested;
        }

        _boundPollVm = pollViewModel;

        if (_boundPollVm != null)
        {
            _boundPollVm.ServerStateApplied += OnPollServerStateApplied;
            _boundPollVm.PropertyChanged += OnBoundPollPropertyChanged;
            _boundPollVm.ShowResultsRequested += OnShowResultsRequested;
        }
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
        try
        {
            await App.Current.Services.GetRequiredService<ILocalCacheService>().UpsertMessageAsync(Message);
        }
        catch { /* best-effort */ }
        finally
        {
            Message.Files = originalFiles;
        }
    }
    #endregion

    #region Subscriptions

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

    #endregion

    #region Event Handlers

    private void PostIfNotDisposed(Action action) => Dispatcher.UIThread.Post(() => { if (!_disposed) action(); });

    private void OnPlaybackStarted(int msgId) => PostIfNotDisposed(() =>
    {
        if (msgId == Id)
        {
            SubscribeToPlaybackEvents();
            IsVoicePlaying = true;
            IsVoicePaused = false;
            VoiceError = null;
            RecacheVoiceButtons();
        }
        else
        {
            ResetPlayerState();
        }
    });

    private void OnPlaybackPaused(int msgId)
    {
        if (msgId == Id)
            PostIfNotDisposed(() => { IsVoicePaused = true; RecacheVoiceButtons(); });
    }

    private void OnPlaybackResumed(int msgId)
    {
        if (msgId == Id)
            PostIfNotDisposed(() => { IsVoicePaused = false; RecacheVoiceButtons(); });
    }

    private void OnPlaybackStopped(int msgId)
    {
        if (msgId != Id) return;
        UnsubscribeFromPlaybackEvents();
        PostIfNotDisposed(() =>
        {
            ResetPlayerState();
            _cachedAudioBytes = null;
        });
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
        IsVoicePlaying = IsVoicePaused = IsVoiceLoading = false;
        VoicePositionPercent = 0;
        VoicePositionText = "0:00";
        RecacheVoiceButtons();
    }

    #endregion

    #region Commands

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

        if (string.IsNullOrEmpty(VoiceFileUrl))
        {
            VoiceError = "URL аудио недоступен";
            return false;
        }

        mediaUrl = BuildMediaUrl(VoiceFileUrl);
        return true;
    }

    private bool TryResumeIfPaused()
    {
        if (_audioPlayer!.CurrentMessageId != Id || !_audioPlayer.IsPaused) return false;
        _audioPlayer.Resume();
        return true;
    }
    public bool IsDisposed => _disposed;
    private const long MaxCachedAudioBytes = 5 * 1024 * 1024; // 5 MB
    private async Task<byte[]?> LoadAudioBytesAsync(string mediaUrl)
    {
        IsVoiceLoading = true;
        RecacheVoiceButtons();

        try
        {
            using var cts = new CancellationTokenSource(VoiceLoadTimeout);
            await using var stream = await _apiClient!.GetStreamAsync(mediaUrl, cts.Token);

            if (stream is null)
            {
                VoiceError = "Не удалось загрузить аудио";
                return null;
            }

            await using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cts.Token);

            if (_disposed) return null;

            var data = ms.ToArray();

            if (data.Length <= MaxCachedAudioBytes)
                _cachedAudioBytes = data;

            return data;
        }
        catch (OperationCanceledException)
        {
            VoiceError = "Не удалось загрузить голосовое: превышено время ожидания.";
            return null;
        }
        catch (Exception ex)
        {
            VoiceError = $"Ошибка: {ex.Message}";
            return null;
        }
        finally
        {
            IsVoiceLoading = false;
            RecacheVoiceButtons();
        }
    }

    [RelayCommand]
    private void PauseVoice()
    {
        if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Pause();
    }

    [RelayCommand]
    private void StopVoice()
    {
        if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Stop();
    }

    [RelayCommand]
    private void SeekVoice(double pct)
    {
        if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Seek(pct / 100.0);
    }

    [RelayCommand]
    private async Task DownloadVoice()
    {
        if (string.IsNullOrEmpty(VoiceFileUrl) || _downloadService is null) return;
        try
        {
            var mediaUrl = BuildMediaUrl(VoiceFileUrl);
            var name = $"voice_{Id}_{CreatedAt:yyyyMMdd_HHmmss}.wav";
            var path = await _downloadService.DownloadFileAsync(mediaUrl, name);
            if (path is not null)
                _notificationService?.ShowSuccessAsync($"Голосовое сохранено: {name}", copyToClipboard: false);
        }
        catch (Exception ex)
        {
            _notificationService?.ShowErrorAsync($"Ошибка загрузки: {ex.Message}", copyToClipboard: false);
        }
    }

    #endregion

    #region Grouping

    private static readonly long GroupingThresholdTicks = TimeSpan.FromMinutes(2).Ticks;

    private void UpdateGroupPosition() => GroupPosition = (IsContinuation, HasNextFromSame) switch
    {
        (false, false) => MessageGroupPosition.Alone,
        (false, true) => MessageGroupPosition.First,
        (true, true) => MessageGroupPosition.Middle,
        (true, false) => MessageGroupPosition.Last,
    };

    public static bool CanGroup(MessageViewModel a, MessageViewModel b) => !a.IsSystemMessage && !b.IsSystemMessage && a.SenderId == b.SenderId && !a.IsDeleted && !b.IsDeleted
        && a.CreatedAt.Date == b.CreatedAt.Date && Math.Abs(b.CreatedAt.Ticks - a.CreatedAt.Ticks) <= GroupingThresholdTicks;

    public static void RecalculateGrouping(IList<MessageViewModel> msgs) =>
        ApplyGrouping(msgs, 0, msgs.Count - 1);

    public static void UpdateGroupingAround(IList<MessageViewModel> msgs, int idx) =>
        ApplyGrouping(msgs, Math.Max(0, idx - 1), Math.Min(msgs.Count - 1, idx + 1));

    private static void ApplyGrouping(IList<MessageViewModel> msgs, int start, int end)
    {
        for (var i = start; i <= end; i++)
        {
            var cur = msgs[i];
            cur.IsContinuation = i > 0 && CanGroup(msgs[i - 1], cur);
            cur.HasNextFromSame = i < msgs.Count - 1 && CanGroup(cur, msgs[i + 1]);
        }
    }

    #endregion

    #region PropertyChanged Partial Methods

    partial void OnContentChanged(string? value)
    {
        RecacheContentGroup();
        Notify(ContentProps);
    }

    partial void OnPollChanged(PollViewModel? oldValue, PollViewModel? newValue)
    {
        if (oldValue != null && !ReferenceEquals(oldValue, newValue))
            oldValue.Dispose();

        BindPollViewModel(newValue);
        RecachePollGroup();
        RecacheContentGroup();
        RecacheEditGroup();
        Notify(PollDerivedProps);
    }

    partial void OnIsEditedChanged(bool value)
    {
        RecacheEditGroup();
        Notify(EditedProps);
    }

    partial void OnEditedAtChanged(DateTime? value)
    {
        EditedLabelFull = IsEdited && value.HasValue ? $"изменено {value.Value:HH:mm}" : null;
        Notify(EditedProps);
    }

    partial void OnIsDeletedChanged(bool value)
    {
        RecacheContentGroup();
        RecacheEditGroup();
        RecacheDeliveryGroup();
        RecachePinGroup();

        Notify(ContentProps);
        Notify(PinProps);
        OnPropertyChanged(nameof(ShowDeliveryStatus));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanPin));
    }

    partial void OnIsReadChanged(bool value) => OnPropertyChanged(nameof(ShowDeliveryStatus));

    partial void OnIsContinuationChanged(bool value)
    {
        RecacheSenderGroup();
        OnPropertyChanged(nameof(ShowSenderName));
        UpdateGroupPosition();
    }

    partial void OnHasNextFromSameChanged(bool value) => UpdateGroupPosition();

    partial void OnIsVoiceMessageChanged(bool value)
    {
        RecacheVoiceGroup();
        RecacheContentGroup();
        RecacheEditGroup();
        RecacheVoiceButtons();

        Notify([nameof(ShowVoiceMessage), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(ShowNonVoiceFiles), nameof(CanEdit), nameof(ShowPlayButton)]);
    }

    partial void OnVoiceDurationSecondsChanged(double? value)
    {
        VoiceDurationFormatted = value.HasValue ? FormatTime(TimeSpan.FromSeconds(value.Value)) : "0:00";
        OnPropertyChanged(nameof(VoiceDurationFormatted));
    }

    partial void OnIsVoicePlayingChanged(bool value) => RecacheVoiceButtons();
    partial void OnIsVoicePausedChanged(bool value) => RecacheVoiceButtons();
    partial void OnIsVoiceLoadingChanged(bool value) => RecacheVoiceButtons();

    partial void OnForwardedFromMessageIdChanged(int? value)
    {
        RecacheReplyForwardGroup();
        RecacheEditGroup();
        Notify(ForwardDerivedProps);
    }

    partial void OnForwardedFromSenderNameChanged(string? value)
    {
        ForwardedFromHeader = HasForward ? $"Переслано от {value ?? "неизвестного пользователя"}" : string.Empty;
        OnPropertyChanged(nameof(ForwardedFromHeader));
    }

    partial void OnIsPinnedChanged(bool value)
    {
        RecachePinGroup();
        Notify(PinProps);
    }

    partial void OnGroupPositionChanged(MessageGroupPosition value)
    {
        UpdateBubbleClasses();
        OnPropertyChanged(nameof(BubbleClasses));
    }

    #endregion

    #region Helpers

    private void Notify(string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
    }

    private static string FormatTime(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static string BuildMediaUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
            return $"{App.ApiUrl.TrimEnd('/')}/{url.TrimStart('/')}";

        if (!Uri.TryCreate(App.ApiUrl, UriKind.Absolute, out var apiUri))
            return absoluteUri.ToString();

        var sameHost = string.Equals(absoluteUri.Host, apiUri.Host, StringComparison.OrdinalIgnoreCase) && absoluteUri.Port == apiUri.Port;

        if (sameHost) return absoluteUri.ToString();

        if (!absoluteUri.AbsolutePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return absoluteUri.ToString();

        return new Uri(apiUri, absoluteUri.PathAndQuery).ToString();
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Poll?.Dispose();
        BindPollViewModel(null);
        Commands = null;

        if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Stop();
        UnsubscribeFromAudioPlayer();
        _cachedAudioBytes = null;

        MemoryDiagnostics.OnMessageVmFinalized();
        GC.SuppressFinalize(this);
    }
}