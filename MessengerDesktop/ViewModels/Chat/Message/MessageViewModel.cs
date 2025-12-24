using CommunityToolkit.Mvvm.ComponentModel;
using MessengerDesktop.Services;
using MessengerDesktop.Services.UI;
using MessengerShared.DTO;
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
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOwn { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? EditedAt { get; set; }
    public PollDTO? Poll { get; set; }
    public List<MessageFileDTO> Files { get; set; } = [];

    [ObservableProperty]
    private string? _senderAvatar;

    [ObservableProperty]
    private string? _senderName;

    [ObservableProperty]
    private bool _showSenderName;

    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private bool _isUnread;

    [ObservableProperty]
    private bool _isRead;

    [ObservableProperty]
    private ObservableCollection<MessageFileViewModel> _fileViewModels = [];

    public bool HasFiles => Files.Count > 0;
    public bool HasPoll => Poll != null;
    public bool HasImages => Files.Any(f => f.PreviewType == "image");

    public MessageDTO Message { get; }

    public string? SenderAvatarUrl
    {
        get => SenderAvatar;
        set => SenderAvatar = value;
    }

    public MessageViewModel(MessageDTO message,IFileDownloadService? downloadService = null,INotificationService? notificationService = null)
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
}