using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services.Audio;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Chat.Messages;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class MessageViewModel : ObservableObject, IDisposable
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int SenderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOwn { get; set; }
    public bool IsSystemMessage { get; set; }
    public SystemEventType? SystemEventType { get; set; }
    public int? TargetUserId { get; set; }
    public string? TargetUserName { get; set; }

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
    public PollDto? PollDto { get; set; }
    [ObservableProperty] public partial PollViewModel? Poll { get; set; }
    public List<MessageFileDto> Files { get; set; } = [];
    [ObservableProperty] public partial string? SenderAvatar { get; set; }
    [ObservableProperty] public partial string? SenderName { get; set; }
    [ObservableProperty] public partial string? Content { get; set; }
    [ObservableProperty] public partial bool IsHighlighted { get; set; }
    [ObservableProperty] public partial bool IsUnread { get; set; }
    [ObservableProperty] public partial bool IsRead { get; set; }
    [ObservableProperty] public partial bool IsEdited { get; set; }
    [ObservableProperty] public partial bool IsDeleted { get; set; }
    [ObservableProperty] public partial ObservableCollection<MessageFileViewModel> FileViewModels { get; set; } = [];
    [ObservableProperty] public partial bool ShowDateSeparator { get; set; }
    [ObservableProperty] public partial string? DateSeparatorText { get; set; }
    [ObservableProperty] public partial bool IsVoiceMessage { get; set; }
    [ObservableProperty] public partial double? VoiceDurationSeconds { get; set; }
    [ObservableProperty] public partial string? VoiceFileUrl { get; set; }
    [ObservableProperty] public partial bool IsVoicePlaying { get; set; }
    [ObservableProperty] public partial bool IsVoicePaused { get; set; }
    [ObservableProperty] public partial bool IsVoiceLoading { get; set; }
    [ObservableProperty] public partial double VoicePositionPercent { get; set; }
    [ObservableProperty] public partial string VoicePositionText { get; set; } = "0:00";
    [ObservableProperty] public partial string? VoiceError { get; set; }
    private readonly IFileDownloadService? _downloadService;
    private readonly INotificationService? _notificationService;
    private readonly IApiClientService? _apiClient;
    private readonly IAudioPlayerService? _audioPlayer;
    private MemoryStream? _cachedAudioStream;
    private bool _disposed;
    private bool _subscribedToPlayer;

    #region Computed Properties

    public bool HasFiles => Files.Count > 0;
    public bool HasPoll => Poll != null;
    public bool HasImages => Files.Any(f => f.PreviewType == "image");
    public bool HasReply => ReplyToMessageId.HasValue;

    public bool HasTextContent
        => !IsDeleted && !IsSystemMessage && !string.IsNullOrWhiteSpace(Content)
           && !HasPoll && !IsVoiceMessage;

    public bool ShowSenderName
        => !IsOwn && !IsContinuation && !IsSystemMessage;

    public bool ShowDeliveryStatus
        => IsOwn && !IsDeleted && !IsSystemMessage;

    public bool CanDelete
        => IsOwn && !IsDeleted && !IsSystemMessage;

    public bool ShowNonVoiceFiles
        => HasFiles && !IsDeleted && !IsSystemMessage;

    public bool CanEdit
        => IsOwn && !IsDeleted && !IsSystemMessage
           && Poll == null && !IsVoiceMessage && !HasForward;

    public bool ShowFilesOnlyMeta
        => !HasTextContent && !IsDeleted && HasFiles && !IsVoiceMessage;

    public string DisplayContent
        => IsDeleted ? "Сообщение удалено" : (Content ?? string.Empty);

    public bool HasStructuredSystemMessage
        => IsSystemMessage && SystemEventType.HasValue;

    public bool CanOpenSenderProfile
        => SenderId > 0;

    public bool HasSystemTargetUser
        => TargetUserId > 0;

    public int SystemTargetUserId
        => TargetUserId ?? 0;

    public string SystemActorDisplayName
        => string.IsNullOrWhiteSpace(SenderName) ? "Пользователь" : SenderName!;

    public string SystemTargetDisplayName
        => string.IsNullOrWhiteSpace(TargetUserName) ? "пользователя" : TargetUserName!;

    public string SystemActionPrefixText
        => SystemEventType switch
        {
            MessengerShared.Enum.SystemEventType.ChatCreated => " создал(а) группу",
            MessengerShared.Enum.SystemEventType.MemberAdded => " добавил(а) ",
            MessengerShared.Enum.SystemEventType.MemberRemoved => " удалил(а) ",
            MessengerShared.Enum.SystemEventType.MemberLeft => " покинул(а) группу",
            MessengerShared.Enum.SystemEventType.RoleChanged => " изменил(а) роль участника ",
            _ => string.Empty
        };

    public string SystemActionSuffixText
        => SystemEventType switch
        {
            MessengerShared.Enum.SystemEventType.MemberAdded => " в группу",
            MessengerShared.Enum.SystemEventType.MemberRemoved => " из группы",
            _ => string.Empty
        };

    public string EditedLabel => IsEdited ? "изм." : string.Empty;

    public string? EditedLabelFull
        => IsEdited && EditedAt.HasValue ? $"изменено {EditedAt.Value:HH:mm}" : null;

    public bool HasForward => ForwardedFromMessageId.HasValue;

    public string ForwardedFromHeader
        => HasForward ? $"Переслано от {ForwardedFromSenderName ?? "неизвестного пользователя"}" : string.Empty;

    public bool ShowVoiceMessage
        => IsVoiceMessage && !IsDeleted;

    public bool ShowPlayButton
        => ShowVoiceMessage && !IsVoicePlaying && !IsVoicePaused && !IsVoiceLoading;
    public bool ShowPauseButton
        => IsVoicePlaying && !IsVoicePaused;
    public bool ShowResumeButton
        => IsVoicePaused;

    public string VoiceDurationFormatted
        => VoiceDurationSeconds.HasValue ? FormatTime(TimeSpan.FromSeconds(VoiceDurationSeconds.Value)) : "0:00";

    #endregion

    public MessageDto Message { get; }

    public string? SenderAvatarUrl
    {
        get => SenderAvatar;
        set => SenderAvatar = value;
    }

    public MessageViewModel(MessageDto message, IFileDownloadService? downloadService = null, INotificationService? notificationService = null)
    {
        _downloadService = downloadService;
        _notificationService = notificationService;

        try
        {
            _apiClient = App.Current.Services.GetService<IApiClientService>();
            _audioPlayer = App.Current.Services.GetService<IAudioPlayerService>();
        }
        catch { /* Если сервисы не зарегистрированы, просто продолжим без них. */ }

        Message = message;
        Id = message.Id;
        ChatId = message.ChatId;
        SenderId = message.SenderId;
        Content = message.Content;
        CreatedAt = message.CreatedAt;
        IsOwn = message.IsOwn;
        IsEdited = message.IsEdited;
        IsDeleted = message.IsDeleted;
        EditedAt = message.EditedAt;
        PollDto = message.Poll;
        Files = message.Files ?? [];

        SenderName = message.SenderName;
        SenderAvatar = message.SenderAvatarUrl;

        IsVoiceMessage = message.IsVoiceMessage;
        VoiceDurationSeconds = message.VoiceDurationSeconds;
        VoiceFileUrl = message.VoiceFileUrl;

        IsSystemMessage = message.IsSystemMessage;
        SystemEventType = message.SystemEventType;
        TargetUserId = message.TargetUserId;
        TargetUserName = message.TargetUserName;

        ReplyToMessageId = message.ReplyToMessageId;
        if (message.ReplyToMessage != null)
        {
            ReplyToSenderName = message.ReplyToMessage.SenderName;
            ReplyToContent = message.ReplyToMessage.IsDeleted ? "[Сообщение удалено]" : message.ReplyToMessage.Content;
            ReplyToIsDeleted = message.ReplyToMessage.IsDeleted;
        }

        ForwardedFromMessageId = message.ForwardedFromMessageId;
        if (message.ForwardedFrom != null)
        {
            ForwardedFromSenderName = message.ForwardedFrom.OriginalSenderName;
        }

        if (message.Poll != null)
            Poll = CreatePollViewModel(message.Poll);

        if (message.Files?.Count > 0)
        {
            FileViewModels = new ObservableCollection<MessageFileViewModel>(message.Files.Select(f => new MessageFileViewModel(f, downloadService, notificationService)));
        }

        SubscribeToAudioPlayer();
    }

    #region Audio Player

    private void SubscribeToAudioPlayer()
    {
        if (_audioPlayer == null || !IsVoiceMessage || _subscribedToPlayer) return;

        _audioPlayer.PlaybackStarted += OnPlaybackStarted;
        _audioPlayer.PlaybackPaused += OnPlaybackPaused;
        _audioPlayer.PlaybackResumed += OnPlaybackResumed;
        _audioPlayer.PlaybackStopped += OnPlaybackStopped;
        _audioPlayer.PositionChanged += OnPositionChanged;
        _subscribedToPlayer = true;
    }

    private void UnsubscribeFromAudioPlayer()
    {
        if (_audioPlayer == null || !_subscribedToPlayer) return;

        _audioPlayer.PlaybackStarted -= OnPlaybackStarted;
        _audioPlayer.PlaybackPaused -= OnPlaybackPaused;
        _audioPlayer.PlaybackResumed -= OnPlaybackResumed;
        _audioPlayer.PlaybackStopped -= OnPlaybackStopped;
        _audioPlayer.PositionChanged -= OnPositionChanged;
        _subscribedToPlayer = false;
    }

    private void OnPlaybackStarted(int messageId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            if (messageId == Id)
            {
                IsVoicePlaying = true;
                IsVoicePaused = false;
                VoiceError = null;
            }
            else
            {
                ResetPlayerState();
            }
        });
    }

    private void OnPlaybackPaused(int messageId)
    {
        if (messageId != Id) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            IsVoicePaused = true;
        });
    }

    private void OnPlaybackResumed(int messageId)
    {
        if (messageId != Id) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            IsVoicePaused = false;
        });
    }

    private void OnPlaybackStopped(int messageId)
    {
        if (messageId != Id) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            ResetPlayerState();
        });
    }

    private void OnPositionChanged(int messageId, TimeSpan position)
    {
        if (messageId != Id) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            var duration = _audioPlayer?.Duration ?? TimeSpan.Zero;
            VoicePositionPercent = duration.TotalMilliseconds > 0
                ? position.TotalMilliseconds / duration.TotalMilliseconds * 100 : 0;

            VoicePositionText = FormatTime(position);
        });
    }

    private void ResetPlayerState()
    {
        IsVoicePlaying = false;
        IsVoicePaused = false;
        IsVoiceLoading = false;
        VoicePositionPercent = 0;
        VoicePositionText = "0:00";
    }

    [RelayCommand]
    private async Task PlayVoice()
    {
        if (_audioPlayer == null || _apiClient == null || _disposed) return;

        if (string.IsNullOrEmpty(VoiceFileUrl))
        {
            VoiceError = "URL аудио недоступен";
            return;
        }

        VoiceError = null;

        if (_audioPlayer.CurrentMessageId == Id && _audioPlayer.IsPaused)
        {
            _audioPlayer.Resume();
            return;
        }

        if (_cachedAudioStream == null)
        {
            IsVoiceLoading = true;

            try
            {
                var stream = await _apiClient.GetStreamAsync(VoiceFileUrl);
                if (stream == null)
                {
                    VoiceError = "Не удалось загрузить аудио";
                    IsVoiceLoading = false;
                    return;
                }

                var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                await stream.DisposeAsync();

                if (_disposed)
                {
                    await memoryStream.DisposeAsync();
                    return;
                }

                _cachedAudioStream = memoryStream;
            }
            catch (Exception ex)
            {
                VoiceError = $"Ошибка: {ex.Message}";
                IsVoiceLoading = false;
                return;
            }

            IsVoiceLoading = false;
        }

        var playStream = new MemoryStream(_cachedAudioStream.ToArray());
        _audioPlayer.Play(Id, playStream);
    }

    [RelayCommand]
    private void PauseVoice()
    {
        if (_audioPlayer?.CurrentMessageId == Id)
            _audioPlayer.Pause();
    }

    [RelayCommand]
    private void StopVoice()
    {
        if (_audioPlayer?.CurrentMessageId == Id)
            _audioPlayer.Stop();
    }

    [RelayCommand]
    private void SeekVoice(double percent)
    {
        if (_audioPlayer?.CurrentMessageId == Id)
            _audioPlayer.Seek(percent / 100.0);
    }

    [RelayCommand]
    private async Task DownloadVoice()
    {
        if (string.IsNullOrEmpty(VoiceFileUrl) || _downloadService == null)
            return;

        try
        {
            var fileName = $"voice_{Id}_{CreatedAt:yyyyMMdd_HHmmss}.wav";
            var path = await _downloadService.DownloadFileAsync(VoiceFileUrl, fileName);

            if (path != null)
                _notificationService?.ShowSuccessAsync($"Голосовое сохранено: {fileName}", copyToClipboard: false);
        }
        catch (Exception ex)
        {
            _notificationService?.ShowErrorAsync($"Ошибка загрузки: {ex.Message}", copyToClipboard: false);
        }
    }

    private static string FormatTime(TimeSpan time)
        => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    #endregion

    public void UpdatePoll(PollDto pollDto)
    {
        PollDto = pollDto;
        Message.Poll = pollDto;

        if (Poll != null)
            Poll.ApplyDto(pollDto);
        else
            Poll = CreatePollViewModel(pollDto);

        _ = PersistPollStateToCacheAsync();

        OnPropertyChanged(nameof(HasPoll));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
    }

    private async Task PersistPollStateToCacheAsync()
    {
        try
        {
            await App.Current.Services.GetRequiredService<ILocalCacheService>().UpsertMessageAsync(Message);
        }
        catch { /* best-effort only */ }
    }

    public void ApplyUpdate(MessageDto updated)
    {
        Content = updated.Content;
        IsEdited = updated.IsEdited;
        EditedAt = updated.EditedAt;
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
        OnPropertyChanged(nameof(EditedLabel));
        OnPropertyChanged(nameof(EditedLabelFull));
        OnPropertyChanged(nameof(CanEdit));
    }

    public void MarkAsDeleted()
    {
        if (_audioPlayer?.CurrentMessageId == Id)
            _audioPlayer.Stop();

        IsDeleted = true;
        Content = null;
        IsVoiceMessage = false;
        VoiceFileUrl = null;
        VoiceDurationSeconds = null;
        ResetPlayerState();

        _cachedAudioStream?.Dispose();
        _cachedAudioStream = null;

        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
        OnPropertyChanged(nameof(ShowNonVoiceFiles));
        OnPropertyChanged(nameof(ShowDeliveryStatus));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(ShowVoiceMessage));
        OnPropertyChanged(nameof(VoiceDurationFormatted));
    }

    public void MarkAsRead() => IsRead = true;

    private static PollViewModel? CreatePollViewModel(PollDto pollDto)
    {
        try
        {
            var apiClient = App.Current.Services.GetRequiredService<IApiClientService>();
            var authManager = App.Current.Services.GetRequiredService<IAuthManager>();
            var userId = authManager.Session.UserId ?? 0;
            if (userId == 0) return null;
            return new PollViewModel(pollDto, userId, apiClient);
        }
        catch
        {
            return null;
        }
    }

    #region Property Changed Handlers

    partial void OnContentChanged(string? value)
    {
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
    }

    partial void OnPollChanged(PollViewModel? value)
    {
        OnPropertyChanged(nameof(HasPoll));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
        OnPropertyChanged(nameof(CanEdit));
    }

    partial void OnIsEditedChanged(bool value)
    {
        OnPropertyChanged(nameof(EditedLabel));
        OnPropertyChanged(nameof(EditedLabelFull));
    }

    partial void OnEditedAtChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(EditedLabel));
        OnPropertyChanged(nameof(EditedLabelFull));
    }

    partial void OnIsDeletedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
        OnPropertyChanged(nameof(ShowDeliveryStatus));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
    }

    partial void OnIsReadChanged(bool value)
        => OnPropertyChanged(nameof(ShowDeliveryStatus));

    partial void OnIsContinuationChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSenderName));
        UpdateGroupPosition();
    }

    partial void OnHasNextFromSameChanged(bool value)
        => UpdateGroupPosition();

    partial void OnIsVoiceMessageChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowVoiceMessage));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
        OnPropertyChanged(nameof(ShowNonVoiceFiles));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ShowPlayButton));
    }

    partial void OnVoiceDurationSecondsChanged(double? value)
        => OnPropertyChanged(nameof(VoiceDurationFormatted));

    partial void OnIsVoicePlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlayButton));
        OnPropertyChanged(nameof(ShowPauseButton));
    }

    partial void OnIsVoicePausedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlayButton));
        OnPropertyChanged(nameof(ShowPauseButton));
        OnPropertyChanged(nameof(ShowResumeButton));
    }

    partial void OnIsVoiceLoadingChanged(bool value)
        => OnPropertyChanged(nameof(ShowPlayButton));

    partial void OnForwardedFromMessageIdChanged(int? value)
    {
        OnPropertyChanged(nameof(HasForward));
        OnPropertyChanged(nameof(ForwardedFromHeader));
        OnPropertyChanged(nameof(CanEdit));
    }

    partial void OnForwardedFromSenderNameChanged(string? value)
        => OnPropertyChanged(nameof(ForwardedFromHeader));

    private void UpdateGroupPosition()
    {
        GroupPosition = (IsContinuation, HasNextFromSame) switch
        {
            (false, false) => MessageGroupPosition.Alone,
            (false, true) => MessageGroupPosition.First,
            (true, true) => MessageGroupPosition.Middle,
            (true, false) => MessageGroupPosition.Last,
        };
    }

    #endregion

    #region Grouping Logic

    private static readonly TimeSpan GroupingThreshold = TimeSpan.FromMinutes(2);

    public static bool CanGroup(MessageViewModel a, MessageViewModel b)
    {
        if (a.IsSystemMessage || b.IsSystemMessage) return false;
        if (a.SenderId != b.SenderId) return false;
        if (a.IsDeleted || b.IsDeleted) return false;
        if (a.CreatedAt.Date != b.CreatedAt.Date) return false;
        if ((b.CreatedAt - a.CreatedAt).Duration() > GroupingThreshold)
            return false;
        return true;
    }

    public static void RecalculateGrouping(IList<MessageViewModel> messages)
    {
        for (int i = 0; i < messages.Count; i++)
        {
            var current = messages[i];
            var prev = i > 0 ? messages[i - 1] : null;
            var next = i < messages.Count - 1 ? messages[i + 1] : null;
            current.IsContinuation = prev != null && CanGroup(prev, current);
            current.HasNextFromSame = next != null && CanGroup(current, next);
        }
    }

    public static void UpdateGroupingAround(IList<MessageViewModel> messages, int index)
    {
        int start = Math.Max(0, index - 1);
        int end = Math.Min(messages.Count - 1, index + 1);
        for (int i = start; i <= end; i++)
        {
            var current = messages[i];
            var prev = i > 0 ? messages[i - 1] : null;
            var next = i < messages.Count - 1 ? messages[i + 1] : null;
            current.IsContinuation = prev != null && CanGroup(prev, current);
            current.HasNextFromSame = next != null && CanGroup(current, next);
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_audioPlayer?.CurrentMessageId == Id)
            _audioPlayer.Stop();

        UnsubscribeFromAudioPlayer();

        _cachedAudioStream?.Dispose();
        _cachedAudioStream = null;
    }

    #endregion
}