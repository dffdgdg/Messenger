using System;

namespace Desktop.ViewModels.Chats;

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

        // Для контактных чатов — ID собеседника
        ContactUserId = dto.ContactUserId;
        ContactIsOnline = dto.ContactIsOnline;
        ContactStatusType = dto.ContactStatusType;
        ContactStatusExpiresAt = dto.ContactStatusExpiresAt;
    }

    public int Id { get; }
    public ChatType Type { get; }
    public int CreatedById { get; }
    public int? ContactUserId { get; set; }

    [ObservableProperty] public partial string? Name { get; set; }
    [ObservableProperty] public partial DateTime? LastMessageDate { get; set; }
    [ObservableProperty] public partial string? Avatar { get; set; }
    [ObservableProperty] public partial string? LastMessagePreview { get; set; }
    [ObservableProperty] public partial string? LastMessageSenderName { get; set; }
    [ObservableProperty] public partial int UnreadCount { get; set; }
    [ObservableProperty] public partial bool HideSenderPrefix { get; set; }
    [ObservableProperty] public partial bool ContactIsOnline { get; set; }
    [ObservableProperty] public partial UserStatusType ContactStatusType { get; set; } = UserStatusType.Online;
    [ObservableProperty] public partial DateTime? ContactStatusExpiresAt { get; set; }

    public bool ShowStatusIndicator => Type == ChatType.Contact;

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
        UnreadCount = UnreadCount,
        HideSenderPrefix = HideSenderPrefix
    };

    public void Apply(ChatDto dto)
    {
        Name = dto.Name;
        LastMessageDate = dto.LastMessageDate;
        Avatar = dto.Avatar;
        LastMessagePreview = dto.LastMessagePreview;
        LastMessageSenderName = dto.LastMessageSenderName;
        UnreadCount = dto.UnreadCount;
        HideSenderPrefix = dto.HideSenderPrefix;
        ContactUserId = dto.ContactUserId;
        ContactIsOnline = dto.ContactIsOnline;
        ContactStatusType = dto.ContactStatusType;
        ContactStatusExpiresAt = dto.ContactStatusExpiresAt;
    }
}