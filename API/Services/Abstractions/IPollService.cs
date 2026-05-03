namespace API.Services.Abstractions;

public interface IPollService
{
    Task<Result<MessageDto>> CreatePollAsync(CreatePollDto dto, int createdByUserId);
    Task<Result<PollDto>> ClosePollAsync(int pollId, int userId);
    Task<Result<PollDto>> GetPollAsync(int pollId, int userId);
    Task<Result<PollDto>> VoteAsync(PollVoteDto voteDto);
}