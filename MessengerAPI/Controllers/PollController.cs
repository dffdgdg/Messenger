using MessengerAPI.Services.Messaging;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.Poll;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class PollController(
    IPollService pollService,
    ILogger<PollController> logger)
    : BaseController<PollController>(logger)
{
    [HttpGet("{pollId}")]
    public async Task<ActionResult<ApiResponse<PollDTO>>> GetPoll(int pollId)
        => await ExecuteAsync(() => pollService.GetPollAsync(pollId, GetCurrentUserId()));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<MessageDTO>>> CreatePoll([FromBody] CreatePollDTO dto)
        => await ExecuteAsync(() => pollService.CreatePollAsync(dto, GetCurrentUserId()), "Опрос успешно создан");

    [HttpPost("vote")]
    public async Task<ActionResult<ApiResponse<PollDTO>>> Vote([FromBody] PollVoteDTO voteDto)
    {
        voteDto.UserId = GetCurrentUserId();
        return await ExecuteAsync(() => pollService.VoteAsync(voteDto),"Голос учтён");
    }
}