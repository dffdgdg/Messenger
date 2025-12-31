using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class PollController(IPollService pollService, ILogger<PollController> logger) : BaseController<PollController>(logger)
    {
        [HttpGet("{pollId}")]
        public async Task<ActionResult<ApiResponse<PollDTO>>> GetPoll(int pollId, [FromQuery] int userId)
            => await ExecuteAsync(async () =>
            {
                var poll = await pollService.GetPollAsync(pollId, userId);
                return poll ?? throw new KeyNotFoundException($"Опрос с ID {pollId} не найден");
            }, "Poll получен успешно");

        [HttpPost]
        public async Task<ActionResult<ApiResponse<MessageDTO>>> CreatePoll([FromBody] PollDTO pollDto)
            => await ExecuteAsync(async () =>
            {
                ValidateModel();
                var result = await pollService.CreatePollAsync(pollDto);
                return result;
            }, "Опрос успешно создан");

        [HttpPost("vote")]
        public async Task<ActionResult<ApiResponse<PollDTO>>> Vote([FromBody] PollVoteDTO voteDto)
            => await ExecuteAsync(async () =>
            {
                ValidateModel();
                var result = await pollService.VoteAsync(voteDto);
                return result;
            }, "Голос учтён");
    }
}