using System;

namespace MessengerDesktop.ViewModels.Chats;

public partial class ChatListItemViewModel : ObservableObject
{
    public ChatListItemViewModel(ChatDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

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
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial DateTime? LastMessageDate { get; set; }

    [ObservableProperty]
    public partial string? Avatar { get; set; }

    [ObservableProperty]
    public partial string? LastMessagePreview { get; set; }

    [ObservableProperty]
    public partial string? LastMessageSenderName { get; set; }

    [ObservableProperty]
    public partial int UnreadCount { get; set; }

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
