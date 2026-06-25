using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Chat;
using Shared.Enum;

namespace API.Application.Features.Chat.Queries;

public class GetUserDialogsQueryHandler(GetUserChatsQueryHandler chatsHandler)
    : IQueryHandler<GetUserDialogsQuery, Result<List<ChatDto>>>
{
    public virtual async Task<Result<List<ChatDto>>> HandleAsync(GetUserDialogsQuery query, CancellationToken ct = default)
    {
        // Переиспользуем хендлер, фильтруем результат
        // Когда появится GetDialogChatIdsAsync в репо — можно оптимизировать
        var all = await chatsHandler.HandleAsync(new GetUserChatsQuery(query.UserId), ct);
        if (all.IsFailure) return all;

        return Result<List<ChatDto>>.Success(all.Value!.Where(c => c.Type == ChatType.Contact).ToList());
    }
}

