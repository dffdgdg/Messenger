using MessengerAPI.Services.Messaging;
using MessengerShared.Dto.Message;
using MessengerShared.Dto.Poll;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class PollController(IPollService pollService, ILogger<PollController> logger)
    : BaseController<PollController>(logger)
{
    [HttpGet("{pollId}")]
    public async Task<ActionResult<ApiResponse<PollDto>>> GetPoll(int pollId)
        => await ExecuteAsync(() => pollService.GetPollAsync(pollId, GetCurrentUserId()));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<MessageDto>>> CreatePoll([FromBody] CreatePollDto dto)
        => await ExecuteAsync(() => pollService.CreatePollAsync(dto, GetCurrentUserId()), "Опрос успешно создан");

    [HttpPost("vote")]
    public async Task<ActionResult<ApiResponse<PollDto>>> Vote([FromBody] PollVoteDto voteDto)
    {
        voteDto.UserId = GetCurrentUserId();
        return await ExecuteAsync(() => pollService.VoteAsync(voteDto),"Голос учтён");
    }
}