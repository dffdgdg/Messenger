using MessengerAPI.Services.Messaging;

namespace MessengerAPI.Controllers;

public sealed class PollsController(IPollService poll, ILogger<PollsController> logger) : BaseController<PollsController>(logger)
{
    [HttpGet("{pollId}")]
    public async Task<IActionResult> GetPoll(int pollId)
        => Map(await poll.GetPollAsync(pollId, GetCurrentUserId()));

    [HttpPost]
    public async Task<IActionResult> CreatePoll([FromBody] CreatePollDto dto)
        => Map(await poll.CreatePollAsync(dto, GetCurrentUserId()));

    [HttpPost("vote")]
    public async Task<IActionResult> Vote([FromBody] PollVoteDto voteDto)
    {
        voteDto.UserId = GetCurrentUserId();
        return Map(await poll.VoteAsync(voteDto));
    }
}