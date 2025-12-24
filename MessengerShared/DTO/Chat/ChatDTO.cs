using CommunityToolkit.Mvvm.ComponentModel;
using MessengerShared.Enum;

namespace MessengerShared.DTO;

public partial class ChatDTO : ObservableObject
{
    public int Id { get; set; }
    public string? Name { get; set; } = string.Empty;
    public ChatType Type { get; set; } = ChatType.Chat;
    public int CreatedById { get; set; }
    public DateTime? LastMessageDate { get; set; }
    public string? Avatar { get; set; } = string.Empty;
    public string? LastMessagePreview { get; set; }
    public string? LastMessageSenderName { get; set; }

    [ObservableProperty]
    private int _unreadCount;
}