namespace API.Repositories.Abstarctions;

public interface IReadReceiptRepository
{
    Task<ChatMember?> FindMemberAsync(int chatId, int userId, CancellationToken ct = default);
    Task<ChatMember?> FindMemberReadonlyAsync(int chatId, int userId, CancellationToken ct = default);
    Task<int> CountUnreadAsync(int chatId, int userId, int lastReadMessageId, CancellationToken ct = default);
    Task<Dictionary<int, int>> GetUnreadCountsAsync(int userId, IEnumerable<int> chatIds, CancellationToken ct = default);
    Task<List<UnreadCountProjection>> GetAllUnreadCountsAsync(int userId, CancellationToken ct = default);
    Task UpdateReadPointerAsync(ChatMember member, int messageId, DateTime readAt, CancellationToken ct = default);
    Task<bool> MessageExistsAsync(int messageId, int chatId, CancellationToken ct = default);
    Task<int> GetLastMessageIdAsync(int chatId, CancellationToken ct = default);
    Task<UnreadInfoProjection?> GetUnreadInfoAsync(int chatId, int userId, int lastReadMessageId, CancellationToken ct = default);
}

public sealed record UnreadCountProjection(int ChatId, int UnreadCount);

public sealed record UnreadInfoProjection(int Count, int FirstUnreadId);