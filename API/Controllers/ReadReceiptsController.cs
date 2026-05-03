namespace API.Controllers;

public sealed class ReadReceiptsController(IReadReceiptService receipt, ILogger<ReadReceiptsController> logger) : BaseController<ReadReceiptsController>(logger)
{
    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkAsRead([FromBody] MarkAsReadDto request)
        => Map(await receipt.MarkAsReadAsync(GetCurrentUserId(), request));

    [HttpGet("chat/{chatId}/unread-count")]
    public async Task<IActionResult> GetUnreadCount(int chatId)
        => Map(await receipt.GetUnreadCountAsync(GetCurrentUserId(), chatId));

    [HttpGet("unread-counts")]
    public async Task<IActionResult> GetAllUnreadCounts()
        => Map(await receipt.GetAllUnreadCountsAsync(GetCurrentUserId()));
}