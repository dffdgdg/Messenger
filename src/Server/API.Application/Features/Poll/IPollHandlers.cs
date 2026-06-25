using API.Application.Common;
using API.Application.Features.Poll.Commands;
using API.Application.Features.Poll.Queries;
using API.Domain.Common;
using Shared.Contracts.Message;
using Shared.Contracts.Poll;

namespace API.Application.Features.Poll;

public interface IPollHandlers
{
    ICommandHandler<ClosePollCommand, PollDto> ClosePoll { get; }
    ICommandHandler<CreatePollCommand, MessageDto> CreatePoll { get; }
    ICommandHandler<VotePollCommand, PollDto> VotePoll { get; }
    IQueryHandler<GetPollQuery, Result<PollDto>> GetPoll { get; }
}