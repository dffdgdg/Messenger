using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Message;

namespace API.Application.Features.Message.Queries;

public class GetChatCountsQueryHandler(IMessageRepository messageRepository, IChatRepository chatRepository,
    IAccessControlService accessControl) : IQueryHandler<GetChatCountsQuery, Result<ChatCountsDto>>
{
    public virtual async Task<Result<ChatCountsDto>> HandleAsync(GetChatCountsQuery query, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureMemberOfAsync(query.UserId, query.ChatId);
        if (access.IsFailure) return access.As<ChatCountsDto>();

        var cutoff = await MessageHistoryHelper.GetHistoryCutoffAsync(query.ChatId, query.UserId, chatRepository, accessControl);

        var counts = await messageRepository.GetChatCountsAsync(query.ChatId, cutoff);

        return Result<ChatCountsDto>.Success(counts);
    }
}

