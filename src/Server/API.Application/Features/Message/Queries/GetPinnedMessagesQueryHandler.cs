using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Message;

namespace API.Application.Features.Message.Queries;

public class GetPinnedMessagesQueryHandler(IMessageRepository messageRepository, IChatRepository chatRepository,
    IAccessControlService accessControl, IUrlBuilder urlBuilder)
    : IQueryHandler<GetPinnedMessagesQuery, Result<List<MessageDto>>>
{
    public virtual async Task<Result<List<MessageDto>>> HandleAsync(GetPinnedMessagesQuery query, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureMemberOfAsync(query.UserId, query.ChatId);
        if (access.IsFailure) return access.As<List<MessageDto>>();

        var cutoff = await MessageHistoryHelper.GetHistoryCutoffAsync(query.ChatId, query.UserId, chatRepository, accessControl);

        var pinned = await messageRepository.GetPinnedAsync(query.ChatId, cutoff, ct);

        return Result<List<MessageDto>>.Success([.. pinned.Select(m => m.ToDto(query.UserId, urlBuilder))]);
    }
}

