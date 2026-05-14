using API.Repositories.Base;
using API.Repositories.Projections;

namespace API.Repositories.Abstarctions;

public interface IChatRepository : IRepository<Chat>
{
    Task<Chat?> FindByIdWithMembersAsync(int chatId, CancellationToken ct = default);
    Task<Chat?> FindContactChatAsync(int userId, int contactUserId, CancellationToken ct = default);
    Task<List<int>> GetMemberIdsAsync(int chatId, CancellationToken ct = default);
    Task<bool> IsMemberAsync(int chatId, int userId, CancellationToken ct = default);
    void AddMember(ChatMember member);
    void RemoveMember(ChatMember member);
    Task<List<Chat>> GetByIdsAsync(IEnumerable<int> chatIds, CancellationToken ct = default);
    Task<List<LastMessageProjection>> GetLastMessagesAsync(IEnumerable<int> chatIds, CancellationToken ct = default);
    Task<List<DialogPartnerProjection>> GetDialogPartnersAsync(IEnumerable<int> chatIds, int currentUserId, CancellationToken ct = default);
    Task UpdateLastMessageTimeAsync(int chatId, DateTime time, CancellationToken ct = default);
    Task<List<ChatMemberProjection>> GetMembersWithUsersAsync(int chatId, CancellationToken ct = default);
    Task<List<string>> GetVoiceFilePathsAsync(int chatId, CancellationToken ct = default);
    Task<bool?> GetShowHistoryForNewMembersAsync(int chatId, CancellationToken ct = default);
    Task<List<Chat>> GetContactChatsWithMembersAsync(IEnumerable<int> chatIds, CancellationToken ct = default);
    Task<List<Chat>> SearchGroupChatsAsync(IEnumerable<int> chatIds, string query, int take, CancellationToken ct = default);

    Task<List<MemberNotificationProjection>> GetMembersForNotificationAsync(int chatId, int? excludeUserId, CancellationToken ct = default);

    Task<Dictionary<int, DateTime>> GetHistoryRestrictionsAsync(IEnumerable<int> chatIds, int userId, CancellationToken ct = default);
}