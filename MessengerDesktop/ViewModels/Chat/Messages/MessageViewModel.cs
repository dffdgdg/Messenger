using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services.Audio;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Chat.Messages;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

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
    public List<MessageFileDto> Files { get; set; } = [];
    public PollDto? PollDto { get; set; }
    public MessageDto Message { get; }
    public ChatCommands? Commands { get; set; }
    private ICommand? _replyActionCommand;
    private ICommand? _forwardActionCommand;
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

    public ICommand? MentionClickCommand { get; set; }
    private readonly ChatContext? _chatContext;
    public ICommand ReplyActionCommand => _replyActionCommand ??= new RelayCommand(() => Commands?.Reply?.Execute(this));
    public ICommand ForwardActionCommand => _forwardActionCommand ??= new RelayCommand(() => Commands?.Forward?.Execute(this));
    public string? SenderAvatarUrl { get => SenderAvatar; set => SenderAvatar = value; }

    // ── Кэшированные вычисляемые свойства (были геттеры с вычислениями) ──
    public bool HasFiles { get; private set; }
    public bool HasPoll { get; private set; }
    public bool HasImages { get; private set; }
    public bool HasReply { get; private set; }
    public bool HasForward { get; private set; }
    public bool HasTextContent { get; private set; }
    public bool ShowSenderName { get; private set; }
    public bool ShowDeliveryStatus { get; private set; }
    public bool CanPin { get; private set; }
    public bool ShowPinAction { get; private set; }
    public bool ShowUnpinAction { get; private set; }
    public bool CanDelete { get; private set; }
    public bool ShowNonVoiceFiles { get; private set; }
    public bool CanEdit { get; private set; }
    public bool ShowFilesOnlyMeta { get; private set; }
    public string DisplayContent { get; private set; } = string.Empty;
    public bool HasStructuredSystemMessage { get; private set; }
    public bool CanOpenSenderProfile { get; private set; }
    public bool HasSystemTargetUser { get; private set; }
    public int SystemTargetUserId { get; private set; }
    public string EditedLabel { get; private set; } = string.Empty;
    public string? EditedLabelFull { get; private set; }
    public string ForwardedFromHeader { get; private set; } = string.Empty;
    public bool ShowVoiceMessage { get; private set; }
    public bool ShowPlayButton { get; private set; }
    public bool ShowPauseButton { get; private set; }
    public bool ShowResumeButton { get; private set; }
    public string VoiceDurationFormatted { get; private set; } = "0:00";

    // ── Кэшированный класс пузыря (1 биндинг вместо 6) ──
    public string BubbleClasses { get; private set; } = "MessageBubble Other Alone";

    private readonly IFileDownloadService? _downloadService;
    private readonly INotificationService? _notificationService;
    private readonly IApiClientService? _apiClient;
    private readonly IAudioPlayerService? _audioPlayer;
    private static readonly TimeSpan VoiceLoadTimeout = TimeSpan.FromSeconds(20);
    private byte[]? _cachedAudioBytes;
    private bool _disposed, _subscribedToPlayer;
    private PollViewModel? _boundPollVm;

    public string SystemActorDisplayName { get; }
    public string SystemTargetDisplayName { get; }
    public string SystemActionPrefixText { get; }
    public string SystemActionSuffixText { get; }

    private static readonly string[] ContentProps = [nameof(DisplayContent), nameof(HasTextContent), nameof(ShowFilesOnlyMeta)];
    private static readonly string[] EditedProps = [nameof(EditedLabel), nameof(EditedLabelFull)];
    private static readonly string[] VoiceButtonProps = [nameof(ShowPlayButton), nameof(ShowPauseButton), nameof(ShowResumeButton)];

    private static readonly string[] DeletedProps =
    [
        nameof(DisplayContent), nameof(HasTextContent), nameof(ShowFilesOnlyMeta),
        nameof(ShowNonVoiceFiles), nameof(ShowDeliveryStatus), nameof(CanEdit),
        nameof(CanDelete), nameof(ShowVoiceMessage), nameof(VoiceDurationFormatted)
    ];

    public MessageViewModel(MessageDto message, IFileDownloadService? downloadService = null,
        INotificationService? notificationService = null,
        IAudioPlayerService? audioPlayer = null, IApiClientService? apiClient = null,
        ChatContext? chatContext = null)
    {
        _chatContext = chatContext;
        _downloadService = downloadService;
        _notificationService = notificationService;
        _audioPlayer = audioPlayer;
        _apiClient = apiClient;
        Message = message;

        (Id, ChatId, SenderId, Content, CreatedAt, IsOwn) =
            (message.Id, message.ChatId, message.SenderId, message.Content, message.CreatedAt, message.IsOwn);
        (IsEdited, IsDeleted, EditedAt, PollDto, Files) =
            (message.IsEdited, message.IsDeleted, message.EditedAt, message.Poll, message.Files ?? []);
        IsPinned = message.IsPinned;
        (SenderName, SenderAvatar) = (message.SenderName, message.SenderAvatarUrl);
        (IsVoiceMessage, VoiceDurationSeconds, VoiceWaveform, VoiceFileUrl) =
            (message.IsVoiceMessage, message.VoiceDurationSeconds, message.VoiceWaveform, message.VoiceFileUrl);
        (IsSystemMessage, SystemEventType, TargetUserId, TargetUserName) =
            (message.IsSystemMessage, message.SystemEventType, message.TargetUserId, message.TargetUserName);
        (ReplyToMessageId, ForwardedFromMessageId) =
            (message.ReplyToMessageId, message.ForwardedFromMessageId);

        if (message.ReplyToMessage is { } reply)
        {
            ReplyToSenderName = reply.SenderName;
            ReplyToContent = reply.IsDeleted ? "[Сообщение удалено]" : reply.Content;
            ReplyToIsDeleted = reply.IsDeleted;
        }

        if (message.ForwardedFrom != null)
            ForwardedFromSenderName = message.ForwardedFrom.OriginalSenderName;

        if (message.Poll != null)
        {
            Poll = CreatePollViewModel(message.Poll);
            BindPollViewModel(Poll);
        }

        if (Files.Count > 0)
            FileViewModels = new(Files.Select(f => new MessageFileViewModel(f, downloadService, notificationService)));

        // ── Кэшируем все вычисляемые свойства один раз ──
        RecacheAllProperties();

        SystemActorDisplayName = string.IsNullOrWhiteSpace(message.SenderName) ? "Пользователь" : message.SenderName;
        SystemTargetDisplayName = string.IsNullOrWhiteSpace(message.TargetUserName) ? "пользователя" : message.TargetUserName;

        SystemActionPrefixText = message.SystemEventType switch
        {
            MessengerShared.Enum.SystemEventType.ChatCreated => " создал(а) группу",
            MessengerShared.Enum.SystemEventType.MemberAdded => " добавил(а) ",
            MessengerShared.Enum.SystemEventType.MemberRemoved => " удалил(а) ",
            MessengerShared.Enum.SystemEventType.MemberLeft => " покинул(а) группу",
            MessengerShared.Enum.SystemEventType.RoleChanged => " изменил(а) роль участника ",
            MessengerShared.Enum.SystemEventType.CallStarted => " начал(а) звонок",
            _ => string.Empty
        };

        SystemActionSuffixText = message.SystemEventType switch
        {
            MessengerShared.Enum.SystemEventType.MemberAdded => " в группу",
            MessengerShared.Enum.SystemEventType.MemberRemoved => " из группы",
            _ => string.Empty
        };

        // ── Подписка только на старт/стоп (позиция — динамически) ──
        SubscribeToAudioPlayer();

        // ── Освобождаем тяжёлые поля DTO (копии уже в VM) ──
        Message.Files = null;
    }

    // ── Центральный метод пересчёта кэшированных свойств ──
    private void RecacheAllProperties()
    {
        HasFiles = Files.Count > 0;
        HasPoll = Poll != null;
        HasImages = Files.Any(f => f.PreviewType == "image");
        HasReply = ReplyToMessageId.HasValue;
        HasForward = ForwardedFromMessageId.HasValue;
        DisplayContent = IsDeleted ? "Сообщение удалено" : (Content ?? string.Empty);
        HasTextContent = !IsDeleted && !IsSystemMessage && !string.IsNullOrWhiteSpace(Content) && !HasPoll && !IsVoiceMessage;
        ShowSenderName = !IsOwn && !IsContinuation && !IsSystemMessage;
        ShowDeliveryStatus = IsOwn && !IsDeleted && !IsSystemMessage;
        CanPin = !IsDeleted && !IsSystemMessage;
        ShowPinAction = CanPin && !IsPinned;
        ShowUnpinAction = CanPin && IsPinned;
        CanDelete = IsOwn && !IsDeleted && !IsSystemMessage;
        ShowNonVoiceFiles = HasFiles && !IsDeleted && !IsSystemMessage;
        CanEdit = IsOwn && !IsDeleted && !IsSystemMessage && Poll == null && !IsVoiceMessage && !HasForward;
        ShowFilesOnlyMeta = !HasTextContent && !IsDeleted && HasFiles && !IsVoiceMessage;
        HasStructuredSystemMessage = IsSystemMessage && SystemEventType.HasValue
            && SystemEventType != MessengerShared.Enum.SystemEventType.CallEnded
            && SystemEventType != MessengerShared.Enum.SystemEventType.CallStarted;
        CanOpenSenderProfile = SenderId > 0;
        HasSystemTargetUser = TargetUserId > 0;
        SystemTargetUserId = TargetUserId ?? 0;
        EditedLabel = IsEdited ? "изм." : string.Empty;
        EditedLabelFull = IsEdited && EditedAt.HasValue ? $"изменено {EditedAt.Value:HH:mm}" : null;
        ForwardedFromHeader = HasForward
            ? $"Переслано от {ForwardedFromSenderName ?? "неизвестного пользователя"}"
            : string.Empty;
        ShowVoiceMessage = IsVoiceMessage && !IsDeleted;
        ShowPlayButton = ShowVoiceMessage && !IsVoicePlaying && !IsVoicePaused && !IsVoiceLoading;
        ShowPauseButton = IsVoicePlaying && !IsVoicePaused;
        ShowResumeButton = IsVoicePaused;
        VoiceDurationFormatted = VoiceDurationSeconds.HasValue
            ? FormatTime(TimeSpan.FromSeconds(VoiceDurationSeconds.Value))
            : "0:00";

        UpdateBubbleClasses();
    }

    private void UpdateBubbleClasses()
    {
        var owner = IsOwn ? "Own" : "Other";
        var pos = GroupPosition switch
        {
            MessageGroupPosition.Alone => "Alone",
            MessageGroupPosition.First => "First",
            MessageGroupPosition.Middle => "Middle",
            MessageGroupPosition.Last => "Last",
            _ => "Alone"
        };
        BubbleClasses = $"MessageBubble {owner} {pos}";
    }

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

        var sameHost = string.Equals(absoluteUri.Host, apiUri.Host, StringComparison.OrdinalIgnoreCase)
            && absoluteUri.Port == apiUri.Port;

        if (sameHost)
            return absoluteUri.ToString();

        if (!absoluteUri.AbsolutePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return absoluteUri.ToString();

        return new Uri(apiUri, absoluteUri.PathAndQuery).ToString();
    }

    #region Audio Player — динамическая подписка

    /// <summary>
    /// Подписываемся только на Started/Stopped.
    /// Position/Pause/Resume — только для текущего проигрываемого сообщения.
    /// </summary>
    private void SubscribeToAudioPlayer()
    {
        if (_audioPlayer == null || !IsVoiceMessage || _subscribedToPlayer) return;
        _audioPlayer.PlaybackStarted += OnPlaybackStarted;
        _audioPlayer.PlaybackStopped += OnPlaybackStopped;
        _subscribedToPlayer = true;
    }

    private void SubscribeToPlaybackEvents()
    {
        if (_audioPlayer == null) return;
        _audioPlayer.PlaybackPaused += OnPlaybackPaused;
        _audioPlayer.PlaybackResumed += OnPlaybackResumed;
        _audioPlayer.PositionChanged += OnPositionChanged;
    }

    private void UnsubscribeFromPlaybackEvents()
    {
        if (_audioPlayer == null) return;
        _audioPlayer.PlaybackPaused -= OnPlaybackPaused;
        _audioPlayer.PlaybackResumed -= OnPlaybackResumed;
        _audioPlayer.PositionChanged -= OnPositionChanged;
    }

    private void UnsubscribeFromAudioPlayer()
    {
        if (_audioPlayer == null || !_subscribedToPlayer) return;
        _audioPlayer.PlaybackStarted -= OnPlaybackStarted;
        _audioPlayer.PlaybackStopped -= OnPlaybackStopped;
        UnsubscribeFromPlaybackEvents();
        _subscribedToPlayer = false;
    }

    private void PostIfNotDisposed(Action action) =>
        Dispatcher.UIThread.Post(() => { if (!_disposed) action(); });

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
        if (msgId == Id) PostIfNotDisposed(() => { IsVoicePaused = true; RecacheVoiceButtons(); });
    }

    private void OnPlaybackResumed(int msgId)
    {
        if (msgId == Id) PostIfNotDisposed(() => { IsVoicePaused = false; RecacheVoiceButtons(); });
    }

    private void OnPlaybackStopped(int msgId)
    {
        if (msgId != Id) return;
        UnsubscribeFromPlaybackEvents();
        PostIfNotDisposed(() =>
        {
            ResetPlayerState();
            _cachedAudioBytes = null; // Освобождаем память
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

    private void RecacheVoiceButtons()
    {
        ShowPlayButton = ShowVoiceMessage && !IsVoicePlaying && !IsVoicePaused && !IsVoiceLoading;
        ShowPauseButton = IsVoicePlaying && !IsVoicePaused;
        ShowResumeButton = IsVoicePaused;
        Notify(VoiceButtonProps);
    }

    #endregion

    #region Voice Commands

    [RelayCommand]
    private async Task PlayVoice()
    {
        if (_audioPlayer == null || _apiClient == null || _disposed) return;
        if (string.IsNullOrEmpty(VoiceFileUrl)) { VoiceError = "URL аудио недоступен"; return; }

        VoiceError = null;
        var mediaUrl = BuildMediaUrl(VoiceFileUrl);

        if (_audioPlayer.CurrentMessageId == Id && _audioPlayer.IsPaused) { _audioPlayer.Resume(); return; }

        if (_cachedAudioBytes == null)
        {
            IsVoiceLoading = true;
            RecacheVoiceButtons();
            try
            {
                using var timeoutCts = new CancellationTokenSource(VoiceLoadTimeout);
                await using var stream = await _apiClient.GetStreamAsync(mediaUrl, timeoutCts.Token);

                if (stream == null) { VoiceError = "Не удалось загрузить аудио"; IsVoiceLoading = false; RecacheVoiceButtons(); return; }

                await using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, timeoutCts.Token);
                if (_disposed) return;
                _cachedAudioBytes = ms.ToArray();
            }
            catch (OperationCanceledException)
            {
                VoiceError = "Не удалось загрузить голосовое: превышено время ожидания.";
            }
            catch (Exception ex) { VoiceError = $"Ошибка: {ex.Message}"; }
            finally { IsVoiceLoading = false; RecacheVoiceButtons(); }

            if (_cachedAudioBytes == null) return;
        }

        _audioPlayer.Play(Id, new MemoryStream(_cachedAudioBytes, writable: false));
    }

    [RelayCommand] private void PauseVoice() { if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Pause(); }
    [RelayCommand] private void StopVoice() { if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Stop(); }
    [RelayCommand] private void SeekVoice(double pct) { if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Seek(pct / 100.0); }

    [RelayCommand]
    private async Task DownloadVoice()
    {
        if (string.IsNullOrEmpty(VoiceFileUrl) || _downloadService == null) return;
        try
        {
            var mediaUrl = BuildMediaUrl(VoiceFileUrl);
            var name = $"voice_{Id}_{CreatedAt:yyyyMMdd_HHmmss}.wav";
            var path = await _downloadService.DownloadFileAsync(mediaUrl, name);
            if (path != null) _notificationService?.ShowSuccessAsync($"Голосовое сохранено: {name}", copyToClipboard: false);
        }
        catch (Exception ex) { _notificationService?.ShowErrorAsync($"Ошибка загрузки: {ex.Message}", copyToClipboard: false); }
    }

    #endregion

    #region Poll

    public void UpdatePoll(PollDto pollDto)
    {
        PollDto = pollDto;
        Message.Poll = pollDto;
        if (Poll != null)
        {
            Poll.ApplyDto(pollDto);
        }
        else
        {
            Poll = CreatePollViewModel(pollDto);
            BindPollViewModel(Poll);
        }
        _ = PersistPollStateToCacheAsync();
        HasPoll = Poll != null;
        HasTextContent = !IsDeleted && !IsSystemMessage && !string.IsNullOrWhiteSpace(Content) && !HasPoll && !IsVoiceMessage;
        ShowFilesOnlyMeta = !HasTextContent && !IsDeleted && HasFiles && !IsVoiceMessage;
        CanEdit = IsOwn && !IsDeleted && !IsSystemMessage && Poll == null && !IsVoiceMessage && !HasForward;
        Notify([nameof(HasPoll), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(CanEdit)]);
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
        _boundPollVm?.ServerStateApplied -= OnPollServerStateApplied;
        _boundPollVm = pollViewModel;
        _boundPollVm?.ServerStateApplied += OnPollServerStateApplied;
    }

    private static PollViewModel? CreatePollViewModel(PollDto pollDto)
    {
        try
        {
            var sp = App.Current.Services;
            var userId = sp.GetRequiredService<IAuthManager>().Session.UserId ?? 0;
            return userId == 0 ? null : new PollViewModel(pollDto, userId, sp.GetRequiredService<IApiClientService>());
        }
        catch { return null; }
    }

    private async Task PersistPollStateToCacheAsync()
    {
        try
        {
            // Восстанавливаем Files для маппинга (Poll уже установлен в Message.Poll выше)
            Message.Files = Files;
            await App.Current.Services.GetRequiredService<ILocalCacheService>().UpsertMessageAsync(Message);
            Message.Files = null; // Снова освобождаем
        }
        catch { /* best-effort */ }
    }

    #endregion

    #region Updates from Manager

    public void ApplyUpdate(MessageDto updated)
    {
        Content = updated.Content;
        IsEdited = updated.IsEdited;
        EditedAt = updated.EditedAt;
        IsPinned = updated.IsPinned;

        // Пересчитать кэшированные свойства
        DisplayContent = Content ?? string.Empty;
        HasTextContent = !IsDeleted && !IsSystemMessage && !string.IsNullOrWhiteSpace(Content) && !HasPoll && !IsVoiceMessage;
        EditedLabel = IsEdited ? "изм." : string.Empty;
        EditedLabelFull = IsEdited && EditedAt.HasValue ? $"изменено {EditedAt.Value:HH:mm}" : null;
        CanEdit = IsOwn && !IsDeleted && !IsSystemMessage && Poll == null && !IsVoiceMessage && !HasForward;
        CanPin = !IsDeleted && !IsSystemMessage;
        ShowPinAction = CanPin && !IsPinned;
        ShowUnpinAction = CanPin && IsPinned;
        ShowFilesOnlyMeta = !HasTextContent && !IsDeleted && HasFiles && !IsVoiceMessage;

        Notify(ContentProps);
        Notify(EditedProps);
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanPin));
        OnPropertyChanged(nameof(ShowPinAction));
        OnPropertyChanged(nameof(ShowUnpinAction));
    }

    public void MarkAsDeleted()
    {
        if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Stop();
        IsDeleted = true;
        Content = null;
        IsVoiceMessage = false;
        IsPinned = false;
        VoiceFileUrl = null;
        VoiceDurationSeconds = null;
        ResetPlayerState();
        _cachedAudioBytes = null;

        RecacheAllProperties();
        Notify(DeletedProps);
        OnPropertyChanged(nameof(ShowPinAction));
        OnPropertyChanged(nameof(ShowUnpinAction));
        OnPropertyChanged(nameof(BubbleClasses));
    }

    public void MarkAsRead() => IsRead = true;

    #endregion

    #region PropertyChanged Partial Methods

    partial void OnContentChanged(string? value)
    {
        DisplayContent = IsDeleted ? "Сообщение удалено" : (value ?? string.Empty);
        HasTextContent = !IsDeleted && !IsSystemMessage && !string.IsNullOrWhiteSpace(value) && !HasPoll && !IsVoiceMessage;
        ShowFilesOnlyMeta = !HasTextContent && !IsDeleted && HasFiles && !IsVoiceMessage;
        Notify(ContentProps);
    }

    partial void OnPollChanged(PollViewModel? value)
    {
        BindPollViewModel(value);
        HasPoll = value != null;
        HasTextContent = !IsDeleted && !IsSystemMessage && !string.IsNullOrWhiteSpace(Content) && !HasPoll && !IsVoiceMessage;
        ShowFilesOnlyMeta = !HasTextContent && !IsDeleted && HasFiles && !IsVoiceMessage;
        CanEdit = IsOwn && !IsDeleted && !IsSystemMessage && Poll == null && !IsVoiceMessage && !HasForward;
        Notify([nameof(HasPoll), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(CanEdit)]);
    }

    partial void OnIsEditedChanged(bool value)
    {
        EditedLabel = value ? "изм." : string.Empty;
        Notify(EditedProps);
    }

    partial void OnEditedAtChanged(DateTime? value)
    {
        EditedLabelFull = IsEdited && value.HasValue ? $"изменено {value.Value:HH:mm}" : null;
        Notify(EditedProps);
    }

    partial void OnIsDeletedChanged(bool value)
    {
        DisplayContent = value ? "Сообщение удалено" : (Content ?? string.Empty);
        HasTextContent = !value && !IsSystemMessage && !string.IsNullOrWhiteSpace(Content) && !HasPoll && !IsVoiceMessage;
        ShowFilesOnlyMeta = !HasTextContent && !value && HasFiles && !IsVoiceMessage;
        ShowDeliveryStatus = IsOwn && !value && !IsSystemMessage;
        CanEdit = IsOwn && !value && !IsSystemMessage && Poll == null && !IsVoiceMessage && !HasForward;
        CanDelete = IsOwn && !value && !IsSystemMessage;
        CanPin = !value && !IsSystemMessage;
        ShowPinAction = CanPin && !IsPinned;
        ShowUnpinAction = CanPin && IsPinned;
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
        OnPropertyChanged(nameof(ShowDeliveryStatus));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanPin));
        OnPropertyChanged(nameof(ShowPinAction));
        OnPropertyChanged(nameof(ShowUnpinAction));
    }

    partial void OnIsReadChanged(bool value) => OnPropertyChanged(nameof(ShowDeliveryStatus));

    partial void OnIsContinuationChanged(bool value)
    {
        ShowSenderName = !IsOwn && !value && !IsSystemMessage;
        OnPropertyChanged(nameof(ShowSenderName));
        UpdateGroupPosition();
    }

    partial void OnHasNextFromSameChanged(bool value) => UpdateGroupPosition();

    partial void OnIsVoiceMessageChanged(bool value)
    {
        ShowVoiceMessage = value && !IsDeleted;
        HasTextContent = !IsDeleted && !IsSystemMessage && !string.IsNullOrWhiteSpace(Content) && !HasPoll && !value;
        ShowFilesOnlyMeta = !HasTextContent && !IsDeleted && HasFiles && !value;
        ShowNonVoiceFiles = HasFiles && !IsDeleted && !IsSystemMessage;
        CanEdit = IsOwn && !IsDeleted && !IsSystemMessage && Poll == null && !value && !HasForward;
        RecacheVoiceButtons();
        Notify([nameof(ShowVoiceMessage), nameof(HasTextContent), nameof(ShowFilesOnlyMeta),
            nameof(ShowNonVoiceFiles), nameof(CanEdit), nameof(ShowPlayButton)]);
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
        HasForward = value.HasValue;
        ForwardedFromHeader = value.HasValue
            ? $"Переслано от {ForwardedFromSenderName ?? "неизвестного пользователя"}"
            : string.Empty;
        CanEdit = IsOwn && !IsDeleted && !IsSystemMessage && Poll == null && !IsVoiceMessage && !value.HasValue;
        Notify([nameof(HasForward), nameof(ForwardedFromHeader), nameof(CanEdit)]);
    }

    partial void OnForwardedFromSenderNameChanged(string? value)
    {
        ForwardedFromHeader = HasForward
            ? $"Переслано от {value ?? "неизвестного пользователя"}"
            : string.Empty;
        OnPropertyChanged(nameof(ForwardedFromHeader));
    }

    partial void OnIsPinnedChanged(bool value)
    {
        ShowPinAction = CanPin && !value;
        ShowUnpinAction = CanPin && value;
        Notify([nameof(ShowPinAction), nameof(ShowUnpinAction)]);
    }

    #endregion

    #region Grouping

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

    private static readonly long GroupingThresholdTicks = TimeSpan.FromMinutes(2).Ticks;

    public static bool CanGroup(MessageViewModel a, MessageViewModel b) =>
        !a.IsSystemMessage && !b.IsSystemMessage
        && a.SenderId == b.SenderId
        && !a.IsDeleted && !b.IsDeleted
        && a.CreatedAt.Date == b.CreatedAt.Date
        && Math.Abs(b.CreatedAt.Ticks - a.CreatedAt.Ticks) <= GroupingThresholdTicks;

    public static void RecalculateGrouping(IList<MessageViewModel> msgs) =>
        ApplyGrouping(msgs, 0, msgs.Count - 1);

    public static void UpdateGroupingAround(IList<MessageViewModel> msgs, int idx) =>
        ApplyGrouping(msgs, Math.Max(0, idx - 1), Math.Min(msgs.Count - 1, idx + 1));

    private static void ApplyGrouping(IList<MessageViewModel> msgs, int start, int end)
    {
        for (int i = start; i <= end; i++)
        {
            var cur = msgs[i];
            cur.IsContinuation = i > 0 && CanGroup(msgs[i - 1], cur);
            cur.HasNextFromSame = i < msgs.Count - 1 && CanGroup(cur, msgs[i + 1]);
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_audioPlayer?.CurrentMessageId == Id) _audioPlayer.Stop();
        UnsubscribeFromAudioPlayer();
        BindPollViewModel(null);
        _cachedAudioBytes = null;
    }
}