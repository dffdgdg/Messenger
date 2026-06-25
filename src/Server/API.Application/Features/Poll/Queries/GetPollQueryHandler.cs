using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Poll;

namespace API.Application.Features.Poll.Queries;

public class GetPollQueryHandler(IPollRepository pollRepository, IAccessControlService accessControl)
    : IQueryHandler<GetPollQuery, Result<PollDto>>
{
    public virtual async Task<Result<PollDto>> HandleAsync(GetPollQuery query, CancellationToken ct = default)
    {
        var poll = await pollRepository.FindByIdWithDetailsAsync(query.PollId, ct);
        if (poll is null)
            return Result<PollDto>.NotFound($"Опрос с ID {query.PollId} не найден");

        var access = await accessControl.EnsureMemberOfAsync(query.UserId, poll.Message!.ChatId);
        if (access.IsFailure) return access.As<PollDto>();

        return Result<PollDto>.Success(poll.ToDto(query.UserId));
    }
}

