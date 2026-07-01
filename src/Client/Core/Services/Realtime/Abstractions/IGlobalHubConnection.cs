using Shared.Contracts.Chat;
using Shared.Contracts.Message;
using Shared.Contracts.Notification;
using Shared.Contracts.Online;
using Shared.Contracts.Poll;
using Shared.Contracts.ReadReceipt;
using Shared.Contracts.User;

namespace Core.Services.Realtime.Abstractions;

public interface IGlobalHubConnection : IAsyncDisposable, IDisposable
{
    event Action<NotificationDto>? NotificationReceived;
    event Action<UserStatusDto>? UserStatusChanged;
    event Action<int, int>? UnreadCountChanged;
    event Action<int>? TotalUnreadChanged;
    event Action<MessageDto>? MessageReceivedGlobally;
    event Action<MessageDto>? MessageUpdatedGlobally;
    event Action<PollDto>? PollUpdatedGlobally;
    event Action<int, int>? MessageDeletedGlobally;
    event Action<UserDto>? UserProfileUpdated;
    event Action<UserRole>? UserRoleUpdated;
    event Action<int, int>? UserTyping;
    event Action<int, int, int?, DateTime?>? MessageRead;
    event Action<int, UserDto>? MemberJoined;
    event Action<int, int>? MemberLeft;
    event Action? Reconnected;
    event Action<ChatUpdateEventDto>? ChatUpdated;
    event Action<int>? ChatRemoved;
    event Action<UserPermissionsChangedDto>? UserPermissionsChanged;
    event Action<UserBannedDto>? UserBanned;

    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    void SetCurrentChat(int? chatId);
    Task<AllUnreadCountsDto?> GetUnreadCountsAsync();
    Task MarkChatAsReadAsync(int chatId);
    int GetUnreadCount(int chatId);
    int GetTotalUnread();
    Task<ChatReadInfoDto?> GetReadInfoAsync(int chatId);
    Task MarkMessageAsReadAsync(int chatId, int messageId);
    Task SendTypingAsync(int chatId);
    Task SetStatusAsync(UserStatusType status, string? duration = null);
}