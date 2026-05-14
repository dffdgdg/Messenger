using API.Repositories.Base;

namespace API.Repositories.Abstarctions;

public interface IMessageRepository : IRepository<Message>
{
    Task<UserMessage?> FindUserMessageByIdAsync(int messageId, CancellationToken ct = default);
    Task<UserMessage?> FindUserMessageWithIncludesAsync(int messageId, CancellationToken ct = default);
    Task<UserMessage?> FindUserMessageForDeleteAsync(int messageId, CancellationToken ct = default);
    Task<UserMessage?> FindUserMessageWithIncludesNoTrackingAsync(int messageId, CancellationToken ct = default);
    Task<List<UserMessage>> GetBeforeAsync(int chatId, int beforeId, int take, DateTime? cutoff = null, CancellationToken ct = default);
    Task<List<UserMessage>> GetAfterAsync(int chatId, int afterId, int take, DateTime? cutoff = null, CancellationToken ct = default);
    Task<List<UserMessage>> GetUserMessagesForMixedAsync(int chatId, int? beforeId, int? afterId, DateTime? cutoff, CancellationToken ct = default);
    Task<List<SystemMessage>> GetSystemMessagesAsync(int chatId, int? beforeId, int? afterId, DateTime? cutoff, CancellationToken ct = default);
    Task<ChatCountsDto> GetChatCountsAsync(int chatId, DateTime? cutoff);
    Task<List<UserMessage>> GetPinnedAsync(int chatId, DateTime? cutoff = null, CancellationToken ct = default);
    Task<bool> HasOlderAsync(int chatId, int beforeId, DateTime? cutoff, CancellationToken ct = default);
    Task<bool> HasNewerAsync(int chatId, int afterId, DateTime? cutoff, CancellationToken ct = default);
    Task<bool> ExistsInChatAsync(int messageId, int chatId, CancellationToken ct = default);
    new Task<bool> ExistsAsync(int messageId, CancellationToken ct = default);

    Task<(List<Message> Messages, bool HasOlder)> GetLatestAsync(int chatId, int take, DateTime? cutoff = null, CancellationToken ct = default);

    Task<(List<UserMessage> Items, int Total)> SearchInChatAsync(
        int chatId, string escapedQuery,
        int? senderId, DateTime? dateFrom, DateTime? dateTo,
        bool hasFiles, bool hasVoice, bool hasPoll, bool onlyText,
        bool oldestFirst, int page, int pageSize, DateTime? cutoff,
        CancellationToken ct = default);

    Task<(List<UserMessage> Items, int Total)> SearchGlobalAsync(
        IEnumerable<int> chatIds, string escapedQuery,
        int? senderId, DateTime? dateFrom, DateTime? dateTo,
        bool hasFiles, bool hasVoice, bool hasPoll, bool onlyText,
        bool oldestFirst, int page, int pageSize,
        Dictionary<int, DateTime> historyFilter,
        CancellationToken ct = default);

    Task<List<int>> GetForwardedToChatIdsAsync(int originalMessageId, CancellationToken ct = default);
    Task<int> SoftDeleteAsync(int messageId, DateTime editedAt, CancellationToken ct = default);
    Task<int> PinAsync(int messageId, int pinnedByUserId, DateTime pinnedAt, CancellationToken ct = default);
    Task<int> UnpinAsync(int messageId, CancellationToken ct = default);
    void Add(UserMessage message);
    void RemoveVoiceMessage(VoiceMessage voiceMessage);
}