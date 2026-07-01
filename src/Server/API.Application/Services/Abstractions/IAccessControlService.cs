using API.Domain.Common;
using API.Domain.Entities;
using Shared.Enum;

namespace API.Application.Services.Abstractions;

public interface IAccessControlService
{
    Task<bool> IsMemberAsync(int userId, int chatId);
    Task<bool> IsOwnerAsync(int userId, int chatId);
    Task<bool> IsAdminAsync(int userId, int chatId);
    Task<ChatRole?> GetRoleAsync(int userId, int chatId);
    Task<List<int>> GetUserChatIdsAsync(int userId);
    Task<List<int>> GetChatMemberIdsAsync(int chatId);
    Task<ChatType> GetChatTypeAsync(int chatId);
    Task<ChatMember?> GetChatMemberAsync(int userId, int chatId);
    Task<bool> CanViewUserAvatarAsync(int viewerId, int targetUserId);

    void InvalidateSystemAdminCache();

    async Task<Result> EnsureMemberOfAsync(int userId, int chatId)
    {
        var isMember = await IsMemberAsync(userId, chatId);
        return isMember ? Result.Success() : Result.Forbidden("Нет доступа к чату");
    }
}