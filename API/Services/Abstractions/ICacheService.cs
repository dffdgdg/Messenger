namespace API.Services.Abstractions;

public interface ICacheService
{
    Task<List<int>> GetUserChatIdsAsync(int userId, Func<Task<List<int>>> factory);
    Task<ChatMember?> GetMembershipAsync(int userId, int chatId, Func<Task<ChatMember?>> factory);
    void InvalidateUserChats(int userId);
    void InvalidateMembership(int userId, int chatId);
    void InvalidateChat(int chatId);
    void InvalidateChatMembers(int chatId);
}