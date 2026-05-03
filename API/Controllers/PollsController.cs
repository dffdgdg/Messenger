namespace API.Controllers;

public sealed class PollsController(IPollService poll, ILogger<PollsController> logger) : BaseController<PollsController>(logger)
{
    [HttpPost]
    public async Task<IActionResult> CreatePoll([FromBody] CreatePollDto dto)
        => Map(await poll.CreatePollAsync(dto, GetCurrentUserId()));

    [HttpPost("vote")]
    public async Task<IActionResult> Vote([FromBody] PollVoteDto voteDto)
    {
        voteDto.UserId = GetCurrentUserId();
        return Map(await poll.VoteAsync(voteDto));
    }

    [HttpPost("{pollId}/close")]
    public async Task<IActionResult> ClosePoll(int pollId)
        => Map(await poll.ClosePollAsync(pollId, GetCurrentUserId()));

    [HttpGet("{pollId}")]
    public async Task<IActionResult> GetPoll(int pollId)
        => Map(await poll.GetPollAsync(pollId, GetCurrentUserId()));
}