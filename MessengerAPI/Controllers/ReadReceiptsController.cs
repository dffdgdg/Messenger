using MessengerAPI.Services.ReadReceipt;

namespace MessengerAPI.Controllers;

public sealed class ReadReceiptsController(IReadReceiptService readReceiptService, ILogger<ReadReceiptsController> logger)
    : BaseController<ReadReceiptsController>(logger)
{
    [HttpPost("mark-read")]
    public async Task<ActionResult<ApiResponse<ReadReceiptResponseDto>>> MarkAsRead([FromBody] MarkAsReadDto request)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => readReceiptService.MarkAsReadAsync(userId, request));
    }

    [HttpGet("chat/{chatId}/unread-count")]
    public async Task<ActionResult<ApiResponse<int>>> GetUnreadCount(int chatId)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => readReceiptService.GetUnreadCountAsync(userId, chatId));
    }

    [HttpGet("unread-counts")]
    public async Task<ActionResult<ApiResponse<AllUnreadCountsDto>>> GetAllUnreadCounts()
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => readReceiptService.GetAllUnreadCountsAsync(userId));
    }
}