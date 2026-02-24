using MessengerAPI.Common;
using MessengerAPI.Services.Chat;
using MessengerAPI.Services.ReadReceipt;
using MessengerShared.Dto.ReadReceipt;
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
    public async Task<ActionResult<ApiResponse<ReadReceiptResponseDto>>> MarkAsRead([FromBody] MarkAsReadDto request)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(userId, request.ChatId);
            var result = await readReceiptService.MarkAsReadAsync(userId, request);
            return Result<ReadReceiptResponseDto>.Success(result);
        }, "Сообщения отмечены как прочитанные");
    }

    [HttpGet("chat/{chatId}/unread-count")]
    public async Task<ActionResult<ApiResponse<UnreadCountDto>>> GetUnreadCount(int chatId)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(userId, chatId);
            var count = await readReceiptService.GetUnreadCountAsync(userId, chatId);
            return Result<UnreadCountDto>.Success(new UnreadCountDto(chatId, count));
        });
    }

    [HttpGet("unread-counts")]
    public async Task<ActionResult<ApiResponse<AllUnreadCountsDto>>> GetAllUnreadCounts()
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            var result = await readReceiptService.GetAllUnreadCountsAsync(userId);
            return Result<AllUnreadCountsDto>.Success(result);
        }, "Количество непрочитанных получено");
    }
}