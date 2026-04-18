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
    [ObservableProperty] public partial string? VoiceFileUrl { get; set; }
    [ObservableProperty] public partial bool IsVoicePlaying { get; set; }
    [ObservableProperty] public partial bool IsVoicePaused { get; set; }
    [ObservableProperty] public partial bool IsVoiceLoading { get; set; }
    [ObservableProperty] public partial double VoicePositionPercent { get; set; }
    [ObservableProperty] public partial string VoicePositionText { get; set; } = "0:00";
    [ObservableProperty] public partial string? VoiceError { get; set; }
    public ICommand? MentionClickCommand { get; set; }
    private readonly ChatContext? _chatContext;

    public string? SenderAvatarUrl { get => SenderAvatar; set => SenderAvatar = value; }
    public bool HasFiles => Files.Count > 0;
    public bool HasPoll => Poll != null;
    public bool HasImages => Files.Any(f => f.PreviewType == "image");
    public bool HasReply => ReplyToMessageId.HasValue;
    public bool HasForward => ForwardedFromMessageId.HasValue;
    public bool HasTextContent => !IsDeleted && !IsSystemMessage && !string.IsNullOrWhiteSpace(Content) && !HasPoll && !IsVoiceMessage;
    public bool ShowSenderName => !IsOwn && !IsContinuation && !IsSystemMessage;
    public bool ShowDeliveryStatus => IsOwn && !IsDeleted && !IsSystemMessage;
    public bool CanPin => !IsDeleted && !IsSystemMessage;
    public bool ShowPinAction => CanPin && !IsPinned;
    public bool ShowUnpinAction => CanPin && IsPinned;
    public bool CanDelete => IsOwn && !IsDeleted && !IsSystemMessage;
    public bool ShowNonVoiceFiles => HasFiles && !IsDeleted && !IsSystemMessage;
    public bool CanEdit => IsOwn && !IsDeleted && !IsSystemMessage && Poll == null && !IsVoiceMessage && !HasForward;
    public bool ShowFilesOnlyMeta => !HasTextContent && !IsDeleted && HasFiles && !IsVoiceMessage;
    public string DisplayContent => IsDeleted ? "Сообщение удалено" : (Content ?? string.Empty);
    public bool HasStructuredSystemMessage => IsSystemMessage && SystemEventType.HasValue;
    public bool CanOpenSenderProfile => SenderId > 0;
    public bool HasSystemTargetUser => TargetUserId > 0;
    public int SystemTargetUserId => TargetUserId ?? 0;
    public string SystemActorDisplayName => string.IsNullOrWhiteSpace(SenderName) ? "Пользователь" : SenderName!;
    public string SystemTargetDisplayName => string.IsNullOrWhiteSpace(TargetUserName) ? "пользователя" : TargetUserName!;
    public string EditedLabel => IsEdited ? "изм." : string.Empty;
    public string? EditedLabelFull => IsEdited && EditedAt.HasValue ? $"изменено {EditedAt.Value:HH:mm}" : null;
    public string ForwardedFromHeader => HasForward ? $"Переслано от {ForwardedFromSenderName ?? "неизвестного пользователя"}" : string.Empty;
    public bool ShowVoiceMessage => IsVoiceMessage && !IsDeleted;
    public bool ShowPlayButton => ShowVoiceMessage && !IsVoicePlaying && !IsVoicePaused && !IsVoiceLoading;
    public bool ShowPauseButton => IsVoicePlaying && !IsVoicePaused;
    public bool ShowResumeButton => IsVoicePaused;
    public string VoiceDurationFormatted => VoiceDurationSeconds.HasValue ? FormatTime(TimeSpan.FromSeconds(VoiceDurationSeconds.Value)) : "0:00";

    public string SystemActionPrefixText => SystemEventType switch
    {
        MessengerShared.Enum.SystemEventType.ChatCreated => " создал(а) группу",
        MessengerShared.Enum.SystemEventType.MemberAdded => " добавил(а) ",
        MessengerShared.Enum.SystemEventType.MemberRemoved => " удалил(а) ",
        MessengerShared.Enum.SystemEventType.MemberLeft => " покинул(а) группу",
        MessengerShared.Enum.SystemEventType.RoleChanged => " изменил(а) роль участника ",
        _ => string.Empty
    };

    public string SystemActionSuffixText => SystemEventType switch
    {
        MessengerShared.Enum.SystemEventType.MemberAdded => " в группу",
        MessengerShared.Enum.SystemEventType.MemberRemoved => " из группы",
        _ => string.Empty
    };

    private readonly IFileDownloadService? _downloadService;
    private readonly INotificationService? _notificationService;
    private readonly IApiClientService? _apiClient;
    private readonly IAudioPlayerService? _audioPlayer;
    private byte[]? _cachedAudioBytes;
    private bool _disposed, _subscribedToPlayer;
    private PollViewModel? _boundPollVm;

    private static readonly string[] ContentProps = [nameof(DisplayContent), nameof(HasTextContent), nameof(ShowFilesOnlyMeta)];
    private static readonly string[] EditedProps = [nameof(EditedLabel), nameof(EditedLabelFull)];
    private static readonly string[] VoiceButtonProps = [nameof(ShowPlayButton), nameof(ShowPauseButton), nameof(ShowResumeButton)];

    private static readonly string[] DeletedProps =
    [
        nameof(DisplayContent), nameof(HasTextContent), nameof(ShowFilesOnlyMeta),
        nameof(ShowNonVoiceFiles), nameof(ShowDeliveryStatus), nameof(CanEdit),
        nameof(CanDelete), nameof(ShowVoiceMessage), nameof(VoiceDurationFormatted)
    ];

    public MessageViewModel(MessageDto message, IFileDownloadService? downloadService = null, INotificationService? notificationService = null,
        IAudioPlayerService? audioPlayer = null, IApiClientService? apiClient = null, ChatContext? chatContext = null)
    {
        _chatContext = chatContext;
        _downloadService = downloadService;
        _notificationService = notificationService;
        _audioPlayer = audioPlayer;
        _apiClient = apiClient;
        Message = message;

        (Id, ChatId, SenderId, Content, CreatedAt, IsOwn) = (message.Id, message.ChatId, message.SenderId, message.Content, message.CreatedAt, message.IsOwn);
        (IsEdited, IsDeleted, EditedAt, PollDto, Files) = (message.IsEdited, message.IsDeleted, message.EditedAt, message.Poll, message.Files ?? []);
        IsPinned = message.IsPinned;
        (SenderName, SenderAvatar) = (message.SenderName, message.SenderAvatarUrl);
        (IsVoiceMessage, VoiceDurationSeconds, VoiceFileUrl) = (message.IsVoiceMessage, message.VoiceDurationSeconds, message.VoiceFileUrl);
        (IsSystemMessage, SystemEventType, TargetUserId, TargetUserName) = (message.IsSystemMessage, message.SystemEventType, message.TargetUserId, message.TargetUserName);
        (ReplyToMessageId, ForwardedFromMessageId) = (message.ReplyToMessageId, message.ForwardedFromMessageId);

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
            Poll = CreatePollViewModel(message.Poll, chatContext);
            BindPollViewModel(Poll);
        }
        if (Files.Count > 0)
            FileViewModels = new(Files.Select(f => new MessageFileViewModel(f, downloadService, notificationService)));

        SubscribeToAudioPlayer();
    }

    private void Notify(params string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
    }

    private static string FormatTime(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

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

    private void PostIfNotDisposed(Action action) =>
        Dispatcher.UIThread.Post(() => { if (!_disposed) action(); });

    private void OnPlaybackStarted(int msgId) => PostIfNotDisposed(() =>
    {
        if (msgId == Id) { IsVoicePlaying = true; IsVoicePaused = false; VoiceError = null; }
        else { ResetPlayerState(); }
    });

    private void OnPlaybackPaused(int msgId) { if (msgId == Id) PostIfNotDisposed(() => IsVoicePaused = true); }
    private void OnPlaybackResumed(int msgId) { if (msgId == Id) PostIfNotDisposed(() => IsVoicePaused = false); }
    private void OnPlaybackStopped(int msgId) { if (msgId == Id) PostIfNotDisposed(ResetPlayerState); }

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
    }

    [RelayCommand]
    private async Task PlayVoice()
    {
        if (_audioPlayer == null || _apiClient == null || _disposed) return;
        if (string.IsNullOrEmpty(VoiceFileUrl)) { VoiceError = "URL аудио недоступен"; return; }

        VoiceError = null;

        if (_audioPlayer.CurrentMessageId == Id && _audioPlayer.IsPaused) { _audioPlayer.Resume(); return; }

        if (_cachedAudioBytes == null)
        {
            IsVoiceLoading = true;
            try
            {
                await using var stream = await _apiClient.GetStreamAsync(VoiceFileUrl);
                if (stream == null) { VoiceError = "Не удалось загрузить аудио"; IsVoiceLoading = false; return; }

                await using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                if (_disposed) return;
                _cachedAudioBytes = ms.ToArray();
            }
            catch (Exception ex) { VoiceError = $"Ошибка: {ex.Message}"; }
            finally { IsVoiceLoading = false; }

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
            var name = $"voice_{Id}_{CreatedAt:yyyyMMdd_HHmmss}.wav";
            var path = await _downloadService.DownloadFileAsync(VoiceFileUrl, name);
            if (path != null) _notificationService?.ShowSuccessAsync($"Голосовое сохранено: {name}", copyToClipboard: false);
        }
        catch (Exception ex) { _notificationService?.ShowErrorAsync($"Ошибка загрузки: {ex.Message}", copyToClipboard: false); }
    }

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
            Poll = CreatePollViewModel(pollDto, _chatContext);
            BindPollViewModel(Poll);
        }
        _ = PersistPollStateToCacheAsync();
        Notify(nameof(HasPoll), nameof(HasTextContent), nameof(ShowFilesOnlyMeta));
    }
    private void OnPollServerStateApplied(PollDto pollDto)
    {
        PollDto = pollDto;
        Message.Poll = pollDto;
        _ = PersistPollStateToCacheAsync();
    }

    private void BindPollViewModel(PollViewModel? pollViewModel)
    {
        if (ReferenceEquals(_boundPollVm, pollViewModel))
            return;

        _boundPollVm?.ServerStateApplied -= OnPollServerStateApplied;

        _boundPollVm = pollViewModel;

        _boundPollVm?.ServerStateApplied += OnPollServerStateApplied;
    }

    public void ApplyUpdate(MessageDto updated)
    {
        Content = updated.Content;
        IsEdited = updated.IsEdited;
        EditedAt = updated.EditedAt;
        IsPinned = updated.IsPinned;
        Notify([.. ContentProps, .. EditedProps, nameof(CanEdit), nameof(CanPin), nameof(ShowPinAction), nameof(ShowUnpinAction)]);
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
        Notify(DeletedProps);
    }

    public void MarkAsRead() => IsRead = true;

    private static PollViewModel? CreatePollViewModel(PollDto pollDto, ChatContext? context = null)
    {
        try
        {
            var sp = App.Current.Services;
            var userId = sp.GetRequiredService<IAuthManager>().Session.UserId ?? 0;
            return userId == 0 ? null : new PollViewModel(pollDto, userId, sp.GetRequiredService<IApiClientService>(), context);
        }
        catch { return null; }
    }

    private async Task PersistPollStateToCacheAsync()
    {
        try { await App.Current.Services.GetRequiredService<ILocalCacheService>().UpsertMessageAsync(Message); }
        catch { /* best-effort */ }
    }

    partial void OnContentChanged(string? value) => Notify(ContentProps);
    partial void OnPollChanged(PollViewModel? value)
    {
        BindPollViewModel(value);
        Notify(nameof(HasPoll), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(CanEdit));
    }
    partial void OnIsEditedChanged(bool value) => Notify(EditedProps);
    partial void OnEditedAtChanged(DateTime? value) => Notify(EditedProps);
    partial void OnIsDeletedChanged(bool value) => Notify(nameof(DisplayContent), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(ShowDeliveryStatus), nameof(CanEdit), nameof(CanDelete), nameof(CanPin), nameof(ShowPinAction), nameof(ShowUnpinAction)); partial void OnIsReadChanged(bool value) => OnPropertyChanged(nameof(ShowDeliveryStatus));
    partial void OnIsContinuationChanged(bool value) { OnPropertyChanged(nameof(ShowSenderName)); UpdateGroupPosition(); }
    partial void OnHasNextFromSameChanged(bool value) => UpdateGroupPosition();
    partial void OnIsVoiceMessageChanged(bool value) => Notify(nameof(ShowVoiceMessage), nameof(HasTextContent), nameof(ShowFilesOnlyMeta), nameof(ShowNonVoiceFiles), nameof(CanEdit), nameof(ShowPlayButton));
    partial void OnVoiceDurationSecondsChanged(double? value) => OnPropertyChanged(nameof(VoiceDurationFormatted));
    partial void OnIsVoicePlayingChanged(bool value) => Notify(nameof(ShowPlayButton), nameof(ShowPauseButton));
    partial void OnIsVoicePausedChanged(bool value) => Notify(VoiceButtonProps);
    partial void OnIsVoiceLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowPlayButton));
    partial void OnForwardedFromMessageIdChanged(int? value) => Notify(nameof(HasForward), nameof(ForwardedFromHeader), nameof(CanEdit));
    partial void OnForwardedFromSenderNameChanged(string? value) => OnPropertyChanged(nameof(ForwardedFromHeader));
    partial void OnIsPinnedChanged(bool value) => Notify(nameof(ShowPinAction), nameof(ShowUnpinAction));

    private void UpdateGroupPosition() => GroupPosition = (IsContinuation, HasNextFromSame) switch
    {
        (false, false) => MessageGroupPosition.Alone,
        (false, true) => MessageGroupPosition.First,
        (true, true) => MessageGroupPosition.Middle,
        (true, false) => MessageGroupPosition.Last,
    };

    private static readonly TimeSpan GroupingThreshold = TimeSpan.FromMinutes(2);

    public static bool CanGroup(MessageViewModel a, MessageViewModel b) =>
        !a.IsSystemMessage && !b.IsSystemMessage && a.SenderId == b.SenderId
        && !a.IsDeleted && !b.IsDeleted && a.CreatedAt.Date == b.CreatedAt.Date
        && (b.CreatedAt - a.CreatedAt).Duration() <= GroupingThreshold;

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