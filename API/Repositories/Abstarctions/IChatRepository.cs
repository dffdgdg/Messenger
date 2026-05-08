using API.Repositories.Base;

namespace API.Repositories.Abstarctions;

public interface IChatRepository : IRepository<Chat>
{
    Task<Chat?> FindByIdWithMembersAsync(int chatId, CancellationToken ct = default);
    Task<List<Chat>> GetByIdsAsync(IEnumerable<int> chatIds, CancellationToken ct = default);
    Task<Chat?> FindContactChatAsync(int userId, int contactUserId, CancellationToken ct = default);
    Task<List<int>> GetMemberIdsAsync(int chatId, CancellationToken ct = default);
    Task<ChatMember?> GetMemberAsync(int chatId, int userId, CancellationToken ct = default);
    Task<bool> IsMemberAsync(int chatId, int userId, CancellationToken ct = default);
    void AddMember(ChatMember member);
    void RemoveMember(ChatMember member);
}