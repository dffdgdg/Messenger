using Shared.Contracts.Poll;

namespace API.Application.Features.Poll.Commands;

public sealed record CreatePollCommand(CreatePollDto Dto, int CreatedByUserId);
public sealed record VotePollCommand(PollVoteDto Dto);
public sealed record ClosePollCommand(int PollId, int UserId);
