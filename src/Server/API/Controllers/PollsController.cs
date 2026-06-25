using API.Application.Features.Poll;
using API.Application.Features.Poll.Commands;
using API.Application.Features.Poll.Queries;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Poll;

namespace API.Web.Controllers;

public sealed class PollsController(IPollHandlers handlers, ILogger<PollsController> logger)
    : BaseController<PollsController>(logger)
{
    [HttpPost]
    public async Task<IActionResult> CreatePoll([FromBody] CreatePollDto dto)
        => Map(await handlers.CreatePoll.HandleAsync(new CreatePollCommand(dto, GetCurrentUserId())));

    [HttpPost("vote")]
    public async Task<IActionResult> Vote([FromBody] PollVoteDto voteDto)
    {
        voteDto.UserId = GetCurrentUserId();
        return Map(await handlers.VotePoll.HandleAsync(new VotePollCommand(voteDto)));
    }

    [HttpPost("{pollId}/close")]
    public async Task<IActionResult> ClosePoll(int pollId)
        => Map(await handlers.ClosePoll.HandleAsync(new ClosePollCommand(pollId, GetCurrentUserId())));

    [HttpGet("{pollId}")]
    public async Task<IActionResult> GetPoll(int pollId)
        => Map(await handlers.GetPoll.HandleAsync(new GetPollQuery(pollId, GetCurrentUserId())));
}