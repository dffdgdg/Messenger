namespace MessengerAPI.Services.Abstractions;

public interface IChatService
{
    Task<Result<List<ChatDto>>> GetUserChatsAsync(int userId);
    Task<Result<ChatDto>> GetChatForUserAsync(int chatId, int userId);
    Task<Result<List<ChatDto>>> GetUserDialogsAsync(int userId);
    Task<Result<List<ChatDto>>> GetUserGroupsAsync(int userId);
    Task<Result<ChatDto>> GetContactChatAsync(int userId, int contactUserId);
    Task<Result<List<UserDto>>> GetChatMembersAsync(int chatId, int userId);
    Task<Result<ChatDto>> CreateChatAsync(ChatDto dto, CancellationToken ct = default);
    Task<Result<ChatDto>> UpdateChatAsync(int chatId, int userId, UpdateChatDto dto);
    Task<Result> DeleteChatAsync(int chatId, int userId);
    Task<Result<string>> UploadChatAvatarAsync(int chatId, int userId, IFormFile file);
    Task<Result> RemoveChatAvatarAsync(int chatId, int userId);
}