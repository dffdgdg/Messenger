using MessengerShared.DTO.Message;

namespace MessengerAPI.Services.Abstractions;

public interface IMessageService
{
    Task<Result<MessageDto>> CreateMessageAsync(int senderId, CreateMessageRequest request);
    Task<Result<MessageDto>> UpdateMessageAsync(int messageId, int userId, UpdateMessageDto dto);
    Task<Result> DeleteMessageAsync(int messageId, int userId);
    Task<Result<MessageDto>> PinMessageAsync(int messageId, int userId);
    Task<Result<MessageDto>> UnpinMessageAsync(int messageId, int userId);
    Task<Result<List<MessageDto>>> GetPinnedMessagesAsync(int chatId, int userId);
    Task<Result<PagedMessagesDto>> GetChatMessagesAsync(int chatId, int userId, int page, int pageSize);
    Task<Result<PagedMessagesDto>> GetMessagesAroundAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDto>> GetMessagesBeforeAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDto>> GetMessagesAfterAsync(int chatId, int messageId, int userId, int count);
    Task<Result<SearchMessagesResponseDto>> SearchMessagesAsync(int chatId, int userId, SearchMessagesQueryDto query);
    Task<Result<GlobalSearchResponseDto>> GlobalSearchAsync(int userId, GlobalSearchQueryDto query);
}