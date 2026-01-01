using CommunityToolkit.Mvvm.ComponentModel;
using MessengerDesktop.Services;
using MessengerDesktop.Services.UI;
using MessengerShared.DTO.Chat.Poll;
using MessengerShared.DTO.Message;
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

    public PollDTO? Poll { get; set; }
    public List<MessageFileDTO> Files { get; set; } = [];

    [ObservableProperty]
    private string? _senderAvatar, _senderName, _content;

    [ObservableProperty]
    private bool _showSenderName, _isHighlighted, _isUnread, _isRead, _isEdited, _isDeleted;

    [ObservableProperty]
    private ObservableCollection<MessageFileViewModel> _fileViewModels = [];

    public bool HasFiles => Files.Count > 0;
    public bool HasPoll => Poll != null;
    public bool HasImages => Files.Any(f => f.PreviewType == "image");

    /// <summary>
    /// Можно ли редактировать сообщение (только своё, не удалённое, без опроса)
    /// </summary>
    public bool CanEdit => IsOwn && !IsDeleted && Poll == null;

    /// <summary>
    /// Можно ли удалить сообщение (только своё, не удалённое)
    /// </summary>
    public bool CanDelete => IsOwn && !IsDeleted;

    /// <summary>
    /// Отображаемый текст с учётом удаления
    /// </summary>
    public string DisplayContent => IsDeleted ? "Сообщение удалено" : (Content ?? string.Empty);

    /// <summary>
    /// Текст статуса редактирования
    /// </summary>
    public string? EditedLabel => IsEdited && EditedAt.HasValue ? $"изменено {EditedAt.Value:HH:mm}" : null;

    public MessageDTO Message { get; }

    public string? SenderAvatarUrl
    {
        get => SenderAvatar;
        set => SenderAvatar = value;
    }

    public MessageViewModel(MessageDTO message, IFileDownloadService? downloadService = null, INotificationService? notificationService = null)
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
        Poll = message.Poll;
        Files = message.Files ?? [];

        SenderName = message.SenderName;
        SenderAvatar = message.SenderAvatarUrl;
        ShowSenderName = message.ShowSenderName;

        if (message.Files?.Count > 0)
        {
            FileViewModels = new ObservableCollection<MessageFileViewModel>
                (message.Files.Select(f => new MessageFileViewModel(f, downloadService, notificationService)));
        }
    }

    /// <summary>
    /// Применить обновление после редактирования
    /// </summary>
    public void ApplyUpdate(MessageDTO updated)
    {
        Content = updated.Content;
        IsEdited = updated.IsEdited;
        EditedAt = updated.EditedAt;

        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(EditedLabel));
        OnPropertyChanged(nameof(CanEdit));
    }

    /// <summary>
    /// Пометить как удалённое
    /// </summary>
    public void MarkAsDeleted()
    {
        IsDeleted = true;
        Content = null;

        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
    }

    partial void OnIsEditedChanged(bool value) => OnPropertyChanged(nameof(EditedLabel));
    partial void OnEditedAtChanged(DateTime? value) => OnPropertyChanged(nameof(EditedLabel));
    partial void OnIsDeletedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
    }
}