using Shared.DTO.Message;
using API.Domain.Common;
using Shared.Dto.Message;
using Shared.Dto.Search;

namespace API.Application.Services.Abstractions;

public interface IMessageService
{
    Task<Result<MessageDto>> CreateMessageAsync(int senderId, CreateMessageRequest request);
    Task<Result<MessageDto>> UpdateMessageAsync(int messageId, int userId, UpdateMessageDto dto);
    Task<Result> DeleteMessageAsync(int messageId, int userId);
    Task<Result<MessageDto>> PinMessageAsync(int messageId, int userId);
    Task<Result<MessageDto>> UnpinMessageAsync(int messageId, int userId);
    Task<Result<List<MessageDto>>> GetPinnedMessagesAsync(int chatId, int userId);
    Task<Result<PagedMessagesDto>> GetLatestMessagesAsync(int chatId, int userId, int take);
    Task<Result<PagedMessagesDto>> GetMessagesAroundAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDto>> GetMessagesBeforeAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDto>> GetMessagesAfterAsync(int chatId, int messageId, int userId, int count);
    Task<Result<SearchMessagesResponseDto>> SearchMessagesAsync(int chatId, int userId, SearchMessagesQueryDto query);
    Task<Result<GlobalSearchResponseDto>> GlobalSearchAsync(int userId, GlobalSearchQueryDto query);
    Task<Result<ChatCountsDto>> GetChatCountsAsync(int chatId, int userId);
}