using Core.Data.Models.Cache;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Data.Repositories.Abstractions;

public interface IChatCacheRepository
{
    Task UpsertAsync(CachedChat chat);
    Task UpsertBatchAsync(IReadOnlyList<CachedChat> chats);
    Task<List<CachedChat>> GetByTypeAsync(int[] chatTypes);
    Task<CachedChat?> GetByIdAsync(int chatId);
    Task UpdateLastMessageAsync(int chatId, string? preView, string? senderName, long dateTicks);
    Task DeleteAsync(int chatId);
    Task<int> GetCountAsync();
}