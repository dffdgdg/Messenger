using CommunityToolkit.Mvvm.ComponentModel;
using MessengerShared.Dto.Chat;
using MessengerShared.Enum;
using System;

namespace MessengerDesktop.ViewModels.Chats;

public partial class ChatListItemViewModel : ObservableObject
{
    public ChatListItemViewModel(ChatDto dto)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        Id = dto.Id;
        Name = dto.Name;
        Type = dto.Type;
        CreatedById = dto.CreatedById;
        LastMessageDate = dto.LastMessageDate;
        Avatar = dto.Avatar;
        LastMessagePreview = dto.LastMessagePreview;
        LastMessageSenderName = dto.LastMessageSenderName;
        UnreadCount = dto.UnreadCount;
    }

    public int Id { get; }
    public ChatType Type { get; }
    public int CreatedById { get; }

    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private DateTime? _lastMessageDate;

    [ObservableProperty]
    private string? _avatar;

    [ObservableProperty]
    private string? _lastMessagePreview;

    [ObservableProperty]
    private string? _lastMessageSenderName;

    [ObservableProperty]
    private int _unreadCount;

    public ChatDto ToDto() => new()
    {
        Id = Id,
        Name = Name,
        Type = Type,
        CreatedById = CreatedById,
        LastMessageDate = LastMessageDate,
        Avatar = Avatar,
        LastMessagePreview = LastMessagePreview,
        LastMessageSenderName = LastMessageSenderName,
        UnreadCount = UnreadCount
    };

    public void Apply(ChatDto dto)
    {
        Name = dto.Name;
        LastMessageDate = dto.LastMessageDate;
        Avatar = dto.Avatar;
        LastMessagePreview = dto.LastMessagePreview;
        LastMessageSenderName = dto.LastMessageSenderName;
        UnreadCount = dto.UnreadCount;
    }
}
