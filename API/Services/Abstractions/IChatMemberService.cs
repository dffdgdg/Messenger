namespace API.Services.Abstractions;

public interface IChatMemberService
{
    Task<Result<ChatMemberDto>> AddMemberAsync(int chatId, int userId, int addedByUserId, ChatRole role = ChatRole.Member);
    Task<Result> RemoveMemberAsync(int chatId, int userId, int removedByUserId);
    Task<Result<ChatMemberDto>> UpdateRoleAsync(int chatId, int userId, ChatRole newRole, int updatedByUserId);
    Task<Result<List<ChatMemberDto>>> GetMembersAsync(int chatId, int userId);
    Task<Result> LeaveAsync(int chatId, int userId);
}