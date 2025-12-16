using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class MessagesController(IMessageService messageService, IChatService chatService, ILogger<MessagesController> logger) 
        : BaseController<MessagesController>(logger)
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

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<MessageDTO>>> UpdateMessage(int id, [FromBody] UpdateMessageDTO updateDto)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                ValidateModel();

                if (id != updateDto.Id)
                    throw new ArgumentException("Несоответствие ID сообщения");

                var result = await messageService.UpdateMessageAsync(id, currentUserId, updateDto, Request);
                return result;
            }, "Сообщение успешно отредактировано");
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMessage(int id)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await messageService.DeleteMessageAsync(id, currentUserId);
            }, "Сообщение успешно удалено");
        }

        [HttpGet("chat/{chatId}")]
        public async Task<ActionResult<ApiResponse<PagedMessagesDTO>>> GetChatMessages(
            int chatId,
            [FromQuery] int? userId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 15)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);

                var result = await messageService.GetChatMessagesAsync(
                    chatId,
                    userId ?? currentUserId,
                    page,
                    pageSize,
                    Request);
                return result;
            }, "Сообщения получены успешно");
        }

        [HttpGet("chat/{chatId}/search")]
        public async Task<ActionResult<ApiResponse<SearchMessagesResponseDTO>>> SearchMessages(
            int chatId,
            [FromQuery] string query = "",
            [FromQuery] int? userId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);

                var result = await messageService.SearchMessagesAsync(
                    chatId,
                    userId ?? currentUserId,
                    query,
                    page,
                    pageSize,
                    Request);
                return result;
            }, "Поиск завершён");
        }
    }
}