namespace MessengerAPI.Services.Abstractions;

public interface IPollService
{
    Task<Result<PollDto>> GetPollAsync(int pollId, int userId);
    Task<Result<MessageDto>> CreatePollAsync(CreatePollDto dto, int createdByUserId);
    Task<Result<PollDto>> VoteAsync(PollVoteDto voteDto);
}