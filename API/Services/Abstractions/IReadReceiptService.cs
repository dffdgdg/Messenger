namespace API.Services.Abstractions;

public interface IReadReceiptService
{
    Task<Result<ReadReceiptResponseDto>> MarkAsReadAsync(int userId, MarkAsReadDto request);
    Task<Result<ReadReceiptResponseDto>> MarkMessageAsReadAsync(int userId, int chatId, int messageId);
    Task<Result<int>> GetUnreadCountAsync(int userId, int chatId);
    Task<Result<AllUnreadCountsDto>> GetAllUnreadCountsAsync(int userId);
    Task<Dictionary<int, int>> GetUnreadCountsForChatsAsync(int userId, IEnumerable<int> chatIds);
    Task<Result> MarkAllAsReadAsync(int userId, int chatId);
    Task<Result<ChatReadInfoDto>> GetChatReadInfoAsync(int userId, int chatId);
}