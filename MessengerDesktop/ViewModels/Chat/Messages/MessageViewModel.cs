using CommunityToolkit.Mvvm.ComponentModel;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.UI;
using MessengerShared.Dto.Message;
using MessengerShared.Dto.Poll;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MessengerDesktop.ViewModels.Chat;

public partial class MessageViewModel : ObservableObject
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int SenderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOwn { get; set; }

    [ObservableProperty] private int? _replyToMessageId;
    [ObservableProperty] private string? _replyToSenderName;
    [ObservableProperty] private string? _replyToContent;
    [ObservableProperty] private bool _replyToIsDeleted;
    [ObservableProperty] private DateTime? _editedAt;
    [ObservableProperty] private bool _isContinuation;
    [ObservableProperty] private bool _hasNextFromSame;
    [ObservableProperty] private MessageGroupPosition _groupPosition = MessageGroupPosition.Alone;

    // ── Forward ──
    [ObservableProperty] private int? _forwardedFromMessageId;
    [ObservableProperty] private string? _forwardedFromSenderName;

    public PollDto? PollDto { get; set; }
    [ObservableProperty] private PollViewModel? _poll;
    public List<MessageFileDto> Files { get; set; } = [];

    [ObservableProperty] private string? _senderAvatar, _senderName, _content;
    [ObservableProperty] private bool _isHighlighted, _isUnread, _isRead, _isEdited, _isDeleted;
    [ObservableProperty] private ObservableCollection<MessageFileViewModel> _fileViewModels = [];
    [ObservableProperty] private bool _showDateSeparator;
    [ObservableProperty] private string? _dateSeparatorText;

    // Voice message
    [ObservableProperty] private bool _isVoiceMessage;
    [ObservableProperty] private string? _transcriptionStatus;
    [ObservableProperty] private string? _transcriptionText;

    #region Computed Properties

    public bool HasFiles => Files.Count > 0;
    public bool HasPoll => Poll != null;
    public bool HasImages => Files.Any(f => f.PreviewType == "image");
    public bool HasReply => ReplyToMessageId.HasValue;
    public bool HasTextContent => !IsDeleted && !string.IsNullOrWhiteSpace(Content) && !HasPoll && !IsVoiceMessage;
    public bool ShowNonVoiceFiles => HasFiles && !IsVoiceMessage && !IsDeleted;
    public bool ShowFilesOnlyMeta => !HasTextContent && !IsDeleted && HasFiles && !IsVoiceMessage;
    public bool ShowDeliveryStatus => IsOwn && !IsDeleted;
    public bool CanDelete => IsOwn && !IsDeleted;
    public string DisplayContent => IsDeleted ? "Сообщение удалено" : (Content ?? string.Empty);
    public string EditedLabel => IsEdited ? "изм." : string.Empty;
    public string? EditedLabelFull => IsEdited && EditedAt.HasValue
        ? $"изменено {EditedAt.Value:HH:mm}" : null;
    public bool ShowSenderName => !IsOwn && !IsContinuation;

    // ── Forward ──
    public bool HasForward => ForwardedFromMessageId.HasValue;
    public string ForwardedFromHeader => HasForward
        ? $"Переслано от {ForwardedFromSenderName ?? "неизвестного пользователя"}"
        : string.Empty;
    public bool CanEdit => IsOwn && !IsDeleted && Poll == null && !IsVoiceMessage && !HasForward;

    // Voice computed
    public bool ShowVoiceMessage => IsVoiceMessage && !IsDeleted;
    public bool IsTranscriptionPending => TranscriptionStatus is "pending" or "processing";
    public bool IsTranscriptionDone => TranscriptionStatus == "done";
    public bool IsTranscriptionFailed => TranscriptionStatus == "failed";
    public bool HasTranscriptionText => !string.IsNullOrWhiteSpace(TranscriptionText);

    public string TranscriptionStatusDisplay => TranscriptionStatus switch
    {
        "pending" => "Ожидает расшифровки...",
        "processing" => "Расшифровка...",
        "done" => "Расшифровано",
        "failed" => "Ошибка расшифровки",
        _ => ""
    };

    #endregion

    public MessageDto Message { get; }

    public string? SenderAvatarUrl
    {
        get => SenderAvatar;
        set => SenderAvatar = value;
    }

    public MessageViewModel(MessageDto message, IFileDownloadService? downloadService = null, INotificationService? notificationService = null)
    {
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

        // Voice message
        IsVoiceMessage = message.IsVoiceMessage;
        TranscriptionStatus = message.TranscriptionStatus;
        TranscriptionText = message.IsVoiceMessage ? message.Content : null;

        // Reply
        ReplyToMessageId = message.ReplyToMessageId;
        if (message.ReplyToMessage != null)
        {
            ReplyToSenderName = message.ReplyToMessage.SenderName;
            ReplyToContent = message.ReplyToMessage.IsDeleted
                ? "[Сообщение удалено]"
                : message.ReplyToMessage.Content;
            ReplyToIsDeleted = message.ReplyToMessage.IsDeleted;
        }

        // Forward
        ForwardedFromMessageId = message.ForwardedFromMessageId;
        if (message.ForwardedFrom != null)
        {
            ForwardedFromSenderName = message.ForwardedFrom.OriginalSenderName;
        }

        if (message.Poll != null)
            Poll = CreatePollViewModel(message.Poll);

        if (message.Files?.Count > 0)
        {
            FileViewModels = new ObservableCollection<MessageFileViewModel>(
                message.Files.Select(f => new MessageFileViewModel(f, downloadService, notificationService)));
        }
    }

    public void UpdateTranscription(string? status, string? transcription)
    {
        TranscriptionStatus = status;
        TranscriptionText = transcription;

        if (!string.IsNullOrWhiteSpace(transcription))
        {
            Content = transcription;
        }
    }

    public void UpdatePoll(PollDto pollDto)
    {
        PollDto = pollDto;
        if (Poll != null) Poll.ApplyDto(pollDto);
        else Poll = CreatePollViewModel(pollDto);
        OnPropertyChanged(nameof(HasPoll));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
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
        IsDeleted = true;
        Content = null;
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
        OnPropertyChanged(nameof(ShowNonVoiceFiles));
        OnPropertyChanged(nameof(ShowDeliveryStatus));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(ShowVoiceMessage));
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
        catch { return null; }
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

    partial void OnIsReadChanged(bool value) => OnPropertyChanged(nameof(ShowDeliveryStatus));

    partial void OnIsContinuationChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSenderName));
        UpdateGroupPosition();
    }

    partial void OnHasNextFromSameChanged(bool value) => UpdateGroupPosition();

    partial void OnIsVoiceMessageChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowVoiceMessage));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
        OnPropertyChanged(nameof(ShowNonVoiceFiles));
        OnPropertyChanged(nameof(CanEdit));
    }

    partial void OnTranscriptionStatusChanged(string? value)
    {
        OnPropertyChanged(nameof(IsTranscriptionPending));
        OnPropertyChanged(nameof(IsTranscriptionDone));
        OnPropertyChanged(nameof(IsTranscriptionFailed));
        OnPropertyChanged(nameof(TranscriptionStatusDisplay));
    }

    partial void OnTranscriptionTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasTranscriptionText));
    }

    partial void OnForwardedFromMessageIdChanged(int? value)
    {
        OnPropertyChanged(nameof(HasForward));
        OnPropertyChanged(nameof(ForwardedFromHeader));
        OnPropertyChanged(nameof(CanEdit));
    }

    partial void OnForwardedFromSenderNameChanged(string? value)
    {
        OnPropertyChanged(nameof(ForwardedFromHeader));
    }

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
        if (a.SenderId != b.SenderId) return false;
        if (a.IsDeleted || b.IsDeleted) return false;
        if (a.CreatedAt.Date != b.CreatedAt.Date) return false;
        if ((b.CreatedAt - a.CreatedAt).Duration() > GroupingThreshold) return false;
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
}