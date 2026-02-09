using CommunityToolkit.Mvvm.ComponentModel;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.UI;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.Poll;
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

    [ObservableProperty]
    private DateTime? _editedAt;

    /// <summary>
    /// Сырые данные опроса (для обновлений через SignalR)
    /// </summary>
    public PollDTO? PollDto { get; set; }

    /// <summary>
    /// ViewModel опроса для привязки в UI
    /// </summary>
    [ObservableProperty]
    private PollViewModel? _poll;

    public List<MessageFileDTO> Files { get; set; } = [];

    [ObservableProperty]
    private string? _senderAvatar, _senderName, _content;

    [ObservableProperty]
    private bool _showSenderName, _isHighlighted, _isUnread, _isRead, _isEdited, _isDeleted;

    [ObservableProperty]
    private ObservableCollection<MessageFileViewModel> _fileViewModels = [];

    [ObservableProperty]
    private bool _showDateSeparator;

    [ObservableProperty]
    private string? _dateSeparatorText;

    #region Computed Properties

    public bool HasFiles => Files.Count > 0;
    public bool HasPoll => Poll != null;
    public bool HasImages => Files.Any(f => f.PreviewType == "image");

    /// <summary>
    /// Есть текстовый контент для отображения (не удалено, не пусто, не опрос)
    /// </summary>
    public bool HasTextContent => !IsDeleted && !string.IsNullOrWhiteSpace(Content) && !HasPoll;

    /// <summary>
    /// Показывать мета-информацию только для файлов (когда нет текста и не удалено)
    /// </summary>
    public bool ShowFilesOnlyMeta => !HasTextContent && !IsDeleted && HasFiles;

    /// <summary>
    /// Показывать статус доставки (галочки) — только для своих не-удалённых сообщений
    /// </summary>
    public bool ShowDeliveryStatus => IsOwn && !IsDeleted;

    public bool CanEdit => IsOwn && !IsDeleted && Poll == null;
    public bool CanDelete => IsOwn && !IsDeleted;

    public string DisplayContent => IsDeleted ? "Сообщение удалено" : (Content ?? string.Empty);

    public string EditedLabel => IsEdited ? "изм." : string.Empty;

    public string? EditedLabelFull => IsEdited && EditedAt.HasValue
        ? $"изменено {EditedAt.Value:HH:mm}"
        : null;

    #endregion

    public MessageDTO Message { get; }

    public string? SenderAvatarUrl
    {
        get => SenderAvatar;
        set => SenderAvatar = value;
    }

    public MessageViewModel(
        MessageDTO message,
        IFileDownloadService? downloadService = null,
        INotificationService? notificationService = null)
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
        ShowSenderName = message.ShowSenderName;

        if (message.Poll != null)
        {
            Poll = CreatePollViewModel(message.Poll);
        }

        if (message.Files?.Count > 0)
        {
            FileViewModels = new ObservableCollection<MessageFileViewModel>(
                message.Files.Select(f => new MessageFileViewModel(f, downloadService, notificationService)));
        }
    }

    /// <summary>
    /// Обновить опрос из DTO (например, после голосования через SignalR)
    /// </summary>
    public void UpdatePoll(PollDTO pollDto)
    {
        PollDto = pollDto;

        if (Poll != null)
        {
            Poll.ApplyDto(pollDto);
        }
        else
        {
            Poll = CreatePollViewModel(pollDto);
        }

        OnPropertyChanged(nameof(HasPoll));
        OnPropertyChanged(nameof(HasTextContent));
        OnPropertyChanged(nameof(ShowFilesOnlyMeta));
    }

    public void ApplyUpdate(MessageDTO updated)
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
        OnPropertyChanged(nameof(ShowDeliveryStatus));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
    }

    /// <summary>
    /// Обновить статус прочтения
    /// </summary>
    public void MarkAsRead() => IsRead = true;

    private static PollViewModel? CreatePollViewModel(PollDTO pollDto)
    {
        try
        {
            var apiClient = App.Current.Services.GetRequiredService<IApiClientService>();
            var authManager = App.Current.Services.GetRequiredService<IAuthManager>();
            var userId = authManager.Session.UserId ?? 0;

            if (userId == 0)
                return null;

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

    partial void OnIsReadChanged(bool value) => OnPropertyChanged(nameof(ShowDeliveryStatus));

    #endregion
}