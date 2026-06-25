using API.Application.Features.ReadReceipt;
using API.Application.Features.ReadReceipt.Commands;
using API.Application.Features.ReadReceipt.Queries;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.ReadReceipt;

namespace API.Web.Controllers;

public sealed class ReadReceiptsController(IReadReceiptHandlers handlers, ILogger<ReadReceiptsController> logger)
    : BaseController<ReadReceiptsController>(logger)
{
    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkAsRead([FromBody] MarkAsReadDto request)
        => Map(await handlers.MarkAsRead.HandleAsync(new MarkAsReadCommand(GetCurrentUserId(), request)));

    [HttpGet("chat/{chatId}/unread-count")]
    public async Task<IActionResult> GetUnreadCount(int chatId)
        => Map(await handlers.GetUnreadCounts.HandleAsync(new GetUnreadCountQuery(GetCurrentUserId(), chatId)));

    [HttpGet("unread-counts")]
    public async Task<IActionResult> GetAllUnreadCounts()
        => Map(await handlers.GetAllUnreadCounts.HandleAsync(new GetAllUnreadCountsQuery(GetCurrentUserId())));
}