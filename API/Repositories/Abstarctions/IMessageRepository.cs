using API.Repositories.Base;

namespace API.Repositories.Abstarctions;

public interface IMessageRepository : IRepository<Message>
{
    Task<UserMessage?> FindUserMessageByIdAsync(int messageId, CancellationToken ct = default);
    Task<UserMessage?> FindUserMessageWithIncludesAsync(int messageId, CancellationToken ct = default);
    Task<List<UserMessage>> GetPagedAsync(int chatId, int skip, int take, CancellationToken ct = default);
    Task<List<UserMessage>> GetBeforeAsync(int chatId, int beforeId, int take, CancellationToken ct = default);
    Task<List<UserMessage>> GetAfterAsync(int chatId, int afterId, int take, CancellationToken ct = default);
    Task<List<UserMessage>> GetAroundAsync(int chatId, int messageId, int half, CancellationToken ct = default);
    Task<List<UserMessage>> GetPinnedAsync(int chatId, CancellationToken ct = default);
    Task<int> CountAsync(int chatId, CancellationToken ct = default);
}