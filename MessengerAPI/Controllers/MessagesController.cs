using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessagesController(IMessageService messageService, ILogger<MessagesController> logger) : BaseController<MessagesController>(logger)
    {
        [HttpPost]
        public async Task<ActionResult<ApiResponse<MessageDTO>>> CreateMessage([FromBody] MessageDTO messageDto)
        {
            return await ExecuteAsync(async () =>
            {
                ValidateModel();
                var result = await messageService.CreateMessageAsync(messageDto, Request);
                return result;
            }, "Сообщение успешно отправлено");
        }

        [HttpGet("chat/{chatId}")]
        public async Task<ActionResult<ApiResponse<PagedMessagesDTO>>> GetChatMessages(
            int chatId,
            [FromQuery] int? userId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            return await ExecuteAsync(async () =>
            {
                var result = await messageService.GetChatMessagesAsync(chatId, userId, page, pageSize, Request);
                return result;
            }, "Сообщения получены успешно");
        }
    }
}