using CommunityToolkit.Mvvm.ComponentModel;
using Shared.Contracts.Chat;
using System.Diagnostics;

namespace Core.ViewModels.Chats;

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

        ContactUserId = dto.ContactUserId;
        ContactIsOnline = dto.ContactIsOnline;
        ContactStatusType = dto.ContactStatusType;
        ContactStatusExpiresAt = dto.ContactStatusExpiresAt;
        ShowHistoryForNewMembers = dto.ShowHistoryForNewMembers;
    }
    partial void OnAvatarChanged(string? value) =>
        Debug.WriteLine($"[ChatListItem id={Id}] Avatar = '{value}'");

    partial void OnLastMessagePreviewChanged(string? value) =>
    OnPropertyChanged(nameof(FormattedPreview));

    partial void OnLastMessageSenderNameChanged(string? value) =>
        OnPropertyChanged(nameof(FormattedPreview));

    partial void OnHideSenderPrefixChanged(bool value) =>
        OnPropertyChanged(nameof(FormattedPreview));
    public int Id { get; }
    public ChatType Type { get; }
    public int CreatedById { get; }
    public int? ContactUserId { get; set; }

    public string FormattedPreview
    {
        get
        {
            if (string.IsNullOrEmpty(LastMessagePreview))
                return string.Empty;

            if (HideSenderPrefix || string.IsNullOrEmpty(LastMessageSenderName))
                return LastMessagePreview;

            return $"{LastMessageSenderName}: {LastMessagePreview}";
        }
    }

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
    [ObservableProperty] public partial bool ShowHistoryForNewMembers { get; set; }

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
        ShowHistoryForNewMembers = dto.ShowHistoryForNewMembers;
    }

    /// <summary>
    /// Применяет обновление из ChatUpdated, НЕ трогая Avatar.
    /// LastMessage* не затираются, если в DTO они пустые.
    /// </summary>
    public void ApplyExceptAvatar(ChatDto dto)
    {
        Name = dto.Name;

        if (dto.LastMessageDate.HasValue)
            LastMessageDate = dto.LastMessageDate;
        if (dto.LastMessagePreview is not null)
        {

            LastMessagePreview = dto.LastMessagePreview;
            LastMessageSenderName = dto.LastMessageSenderName;
            HideSenderPrefix = dto.HideSenderPrefix;
        }

        if (dto.UnreadCount > 0)
            UnreadCount = dto.UnreadCount;

        ContactUserId = dto.ContactUserId;
        ContactIsOnline = dto.ContactIsOnline;
        ContactStatusType = dto.ContactStatusType;
        ContactStatusExpiresAt = dto.ContactStatusExpiresAt;
    }
}