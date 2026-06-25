using API.Application.Common;
using API.Application.Features.Poll.Commands;
using API.Application.Features.Poll.Queries;
using API.Domain.Common;
using Shared.Contracts.Message;
using Shared.Contracts.Poll;

namespace API.Application.Features.Poll;

public class PollHandlers(
    ClosePollCommandHandler closePoll,
    CreatePollCommandHandler createPoll,
    VotePollCommandHandler votePoll,
    GetPollQueryHandler getPoll)
    : IPollHandlers
{
    public ICommandHandler<ClosePollCommand, PollDto> ClosePoll => closePoll;
    public ICommandHandler<CreatePollCommand, MessageDto> CreatePoll => createPoll;
    public ICommandHandler<VotePollCommand, PollDto> VotePoll => votePoll;
    public IQueryHandler<GetPollQuery, Result<PollDto>> GetPoll => getPoll;
}