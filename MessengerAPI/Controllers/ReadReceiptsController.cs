using MessengerAPI.Services.ReadReceipt;

namespace MessengerAPI.Controllers;

public sealed class ReadReceiptsController(IReadReceiptService readReceiptService, ILogger<ReadReceiptsController> logger)
    : BaseController<ReadReceiptsController>(logger)
{
    [HttpPost("mark-read")]
    public async Task<ActionResult<ApiResponse<ReadReceiptResponseDto>>> MarkAsRead([FromBody] MarkAsReadDto request) => await ExecuteAsync(()
        => readReceiptService.MarkAsReadAsync(GetCurrentUserId(), request));

    [HttpGet("chat/{chatId}/unread-count")]
    public async Task<ActionResult<ApiResponse<int>>> GetUnreadCount(int chatId) => await ExecuteAsync(()
        => readReceiptService.GetUnreadCountAsync(GetCurrentUserId(), chatId));

    [HttpGet("unread-counts")]
    public async Task<ActionResult<ApiResponse<AllUnreadCountsDto>>> GetAllUnreadCounts() => await ExecuteAsync(()
        => readReceiptService.GetAllUnreadCountsAsync(GetCurrentUserId()));
}