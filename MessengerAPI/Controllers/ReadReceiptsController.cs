using MessengerAPI.Services;
using MessengerShared.DTO.ReadReceipt;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class ReadReceiptsController(IReadReceiptService readReceiptService,IChatService chatService,ILogger<ReadReceiptsController> logger)
        : BaseController<ReadReceiptsController>(logger)
    {
        /// <summary>
        /// Отметить сообщения как прочитанные
        /// </summary>
        [HttpPost("mark-read")]
        public async Task<ActionResult<ApiResponse<ReadReceiptResponseDTO>>> MarkAsRead([FromBody] MarkAsReadDTO request)
        {
            var userId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(userId, request.ChatId);
                return await readReceiptService.MarkAsReadAsync(userId, request);
            }, "Сообщения отмечены как прочитанные");
        }

        /// <summary>
        /// Получить количество непрочитанных в чате
        /// </summary>
        [HttpGet("chat/{chatId}/unread-count")]
        public async Task<ActionResult<ApiResponse<UnreadCountDTO>>> GetUnreadCount(int chatId)
        {
            var userId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(userId, chatId);
                var count = await readReceiptService.GetUnreadCountAsync(userId, chatId);
                return new UnreadCountDTO(default, default) { ChatId = chatId, UnreadCount = count };
            });
        }

        /// <summary>
        /// Получить все непрочитанные
        /// </summary>
        [HttpGet("unread-counts")]
        public async Task<ActionResult<ApiResponse<AllUnreadCountsDTO>>> GetAllUnreadCounts()
        {
            var userId = GetCurrentUserId();

            return await ExecuteAsync(async () => await readReceiptService.GetAllUnreadCountsAsync(userId), "Количество непрочитанных получено");
        }
    }
}