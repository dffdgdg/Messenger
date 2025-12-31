using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class MessagesController(
        IMessageService messageService,
        IChatService chatService,
        ILogger<MessagesController> logger) : BaseController<MessagesController>(logger)
    {
        [HttpPost]
        public async Task<ActionResult<ApiResponse<MessageDTO>>> CreateMessage([FromBody] MessageDTO messageDto)
            => await ExecuteAsync(async () =>
            {
                ValidateModel();
                var result = await messageService.CreateMessageAsync(messageDto, Request);
                return result;
            }, "Сообщение успешно отправлено");

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

        [HttpGet("user/{userId}/search")]
        public async Task<ActionResult<ApiResponse<GlobalSearchResponseDTO>>> GlobalSearch(
            int userId,
            [FromQuery] string query = "",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var currentUserId = GetCurrentUserId();

            if (currentUserId != userId)
                return Forbidden<GlobalSearchResponseDTO>();

            return await ExecuteAsync(async () =>
            {
                var result = await messageService.GlobalSearchAsync(userId, query, page, pageSize, Request);
                return result;
            }, "Поиск завершён");
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
                var result = await messageService.GetChatMessagesAsync(chatId, userId ?? currentUserId, page, pageSize, Request);
                return result;
            }, "Сообщения получены успешно");
        }

        /// <summary>
        /// Получить сообщения вокруг указанного ID (для скролла к непрочитанным)
        /// </summary>
        [HttpGet("chat/{chatId}/around/{messageId}")]
        public async Task<ActionResult<ApiResponse<PagedMessagesDTO>>> GetMessagesAround(
            int chatId,
            int messageId,
            [FromQuery] int? userId = null,
            [FromQuery] int count = 50)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
                var result = await messageService.GetMessagesAroundAsync(
                    chatId, messageId, userId ?? currentUserId, count, Request);
                return result;
            }, "Сообщения получены успешно");
        }

        /// <summary>
        /// Получить сообщения до указанного ID (для подгрузки старых)
        /// </summary>
        [HttpGet("chat/{chatId}/before/{messageId}")]
        public async Task<ActionResult<ApiResponse<PagedMessagesDTO>>> GetMessagesBefore(
            int chatId,
            int messageId,
            [FromQuery] int? userId = null,
            [FromQuery] int count = 30)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
                var result = await messageService.GetMessagesBeforeAsync(
                    chatId, messageId, userId ?? currentUserId, count, Request);
                return result;
            }, "Сообщения получены успешно");
        }

        /// <summary>
        /// Получить сообщения после указанного ID (для подгрузки новых)
        /// </summary>
        [HttpGet("chat/{chatId}/after/{messageId}")]
        public async Task<ActionResult<ApiResponse<PagedMessagesDTO>>> GetMessagesAfter(
            int chatId,
            int messageId,
            [FromQuery] int? userId = null,
            [FromQuery] int count = 30)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
                var result = await messageService.GetMessagesAfterAsync(
                    chatId, messageId, userId ?? currentUserId, count, Request);
                return result;
            }, "Сообщения получены успешно");
        }
    }
}