using MessengerAPI.Common;
using MessengerAPI.Services.Chat;
using MessengerAPI.Services.ReadReceipt;
using MessengerShared.DTO.ReadReceipt;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class ReadReceiptsController(
    IReadReceiptService readReceiptService,
    IChatService chatService,
    ILogger<ReadReceiptsController> logger)
    : BaseController<ReadReceiptsController>(logger)
{
    [HttpPost("mark-read")]
    public async Task<ActionResult<ApiResponse<ReadReceiptResponseDTO>>> MarkAsRead([FromBody] MarkAsReadDTO request)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(userId, request.ChatId);
            var result = await readReceiptService.MarkAsReadAsync(userId, request);
            return Result<ReadReceiptResponseDTO>.Success(result);
        }, "Сообщения отмечены как прочитанные");
    }

    [HttpGet("chat/{chatId}/unread-count")]
    public async Task<ActionResult<ApiResponse<UnreadCountDTO>>> GetUnreadCount(int chatId)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(userId, chatId);
            var count = await readReceiptService.GetUnreadCountAsync(userId, chatId);
            return Result<UnreadCountDTO>.Success(new UnreadCountDTO(chatId, count));
        });
    }

    [HttpGet("unread-counts")]
    public async Task<ActionResult<ApiResponse<AllUnreadCountsDTO>>> GetAllUnreadCounts()
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            var result = await readReceiptService.GetAllUnreadCountsAsync(userId);
            return Result<AllUnreadCountsDTO>.Success(result);
        }, "Количество непрочитанных получено");
    }
}