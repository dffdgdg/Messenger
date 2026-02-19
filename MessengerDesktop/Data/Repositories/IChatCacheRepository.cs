using MessengerDesktop.Data.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MessengerDesktop.Data.Repositories;

public interface IChatCacheRepository
{
    Task UpsertAsync(CachedChat chat);
    Task UpsertBatchAsync(IReadOnlyList<CachedChat> chats);
    Task<List<CachedChat>> GetByTypeAsync(int[] chatTypes);
    Task<CachedChat?> GetByIdAsync(int chatId);
    Task UpdateLastMessageAsync(int chatId, string? preview, string? senderName, long dateTicks);
    Task DeleteAsync(int chatId);
    Task<int> GetCountAsync();
}