using API.Application.Services.Abstractions;
using API.Domain.Entities;

namespace API.Tests.Integration.Infrastructure;

public class NoOpCacheService : ICacheService
{
    public Task<List<int>> GetUserChatIdsAsync(int userId, Func<Task<List<int>>> factory) => factory();
    public Task<ChatMember?> GetMembershipAsync(int userId, int chatId, Func<Task<ChatMember?>> factory) => factory();
    public void InvalidateUserChats(int userId) { }
    public void InvalidateMembership(int userId, int chatId) { }
    public void InvalidateChat(int chatId) { }
}