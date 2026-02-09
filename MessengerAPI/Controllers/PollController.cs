using MessengerAPI.Services;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.Poll;
using MessengerShared.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class PollController(IPollService pollService, ILogger<PollController> logger)
        : BaseController<PollController>(logger)
    {
        [HttpGet("{pollId}")]
        public async Task<ActionResult<ApiResponse<PollDTO>>> GetPoll(
            int pollId, [FromQuery] int userId) => await ExecuteAsync(async () =>
            {
                var poll = await pollService.GetPollAsync(pollId, userId);
                return poll ?? throw new KeyNotFoundException($"Опрос с ID {pollId} не найден");
            }, "Poll получен успешно");

        [HttpPost]
        public async Task<ActionResult<ApiResponse<MessageDTO>>> CreatePoll(
            [FromBody] CreatePollDTO dto) => await ExecuteAsync(async () =>
            {
                ValidateModel();
                var userId = GetCurrentUserId();
                return await pollService.CreatePollAsync(dto, userId);
            }, "Опрос успешно создан");

        [HttpPost("vote")]
        public async Task<ActionResult<ApiResponse<PollDTO>>> Vote(
            [FromBody] PollVoteDTO voteDto) => await ExecuteAsync(async () =>
            {
                ValidateModel();
                return await pollService.VoteAsync(voteDto);
            }, "Голос учтён");
    }
}