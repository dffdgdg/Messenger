using API.Application.Features.Message;
using API.Application.Features.Message.Commands;
using API.Application.Features.Message.Queries;
using API.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shared.Contracts.Message;
using Shared.Contracts.Search;

namespace API.Web.Controllers;

public sealed class MessagesController(IMessageHandlers handlers, ILogger<MessagesController> logger)
    : BaseController<MessagesController>(logger)
{
    [HttpPost]
    [EnableRateLimiting("messaging")]
    public async Task<IActionResult> CreateMessage([FromBody] CreateMessageRequest request)
        => Map(await handlers.Create.HandleAsync(new CreateMessageCommand(GetCurrentUserId(), request)));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateMessage(int id, [FromBody] UpdateMessageDto dto)
    {
        if (id != dto.Id)
            return Map(Result.Failure("Несоответствие ID сообщения"));

        return Map(await handlers.Update.HandleAsync(new UpdateMessageCommand(id, GetCurrentUserId(), dto)));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMessage(int id)
        => Map(await handlers.Delete.HandleAsync(new DeleteMessageCommand(id, GetCurrentUserId())));

    [HttpPost("{id}/pin")]
    public async Task<IActionResult> PinMessage(int id)
        => Map(await handlers.Pin.HandleAsync(new PinMessageCommand(id, GetCurrentUserId())));

    [HttpDelete("{id}/pin")]
    public async Task<IActionResult> UnpinMessage(int id)
        => Map(await handlers.Unpin.HandleAsync(new UnpinMessageCommand(id, GetCurrentUserId())));

    [HttpGet("chat/{chatId}/pinned")]
    public async Task<IActionResult> GetPinnedMessages(int chatId)
        => Map(await handlers.GetPinned.HandleAsync(new GetPinnedMessagesQuery(chatId, GetCurrentUserId())));

    [HttpGet("chat/{chatId}/latest")]
    public async Task<IActionResult> GetLatestMessages(int chatId, [FromQuery] int take = 50)
        => Map(await handlers.GetLatest.HandleAsync(new GetLatestMessagesQuery(chatId, GetCurrentUserId(), take)));

    [HttpGet("chat/{chatId}/around/{messageId}")]
    public async Task<IActionResult> GetMessagesAround(int chatId, int messageId, [FromQuery] int count = 50)
        => Map(await handlers.GetAround.HandleAsync(new GetMessagesAroundQuery(chatId, messageId, GetCurrentUserId(), count)));

    [HttpGet("chat/{chatId}/before/{messageId}")]
    public async Task<IActionResult> GetMessagesBefore(int chatId, int messageId, [FromQuery] int count = 30)
        => Map(await handlers.GetBefore.HandleAsync(new GetMessagesBeforeQuery(chatId, messageId, GetCurrentUserId(), count)));

    [HttpGet("chat/{chatId}/after/{messageId}")]
    public async Task<IActionResult> GetMessagesAfter(int chatId, int messageId, [FromQuery] int count = 30)
        => Map(await handlers.GetAfter.HandleAsync(new GetMessagesAfterQuery(chatId, messageId, GetCurrentUserId(), count)));

    [HttpPost("chat/{chatId}/search")]
    public async Task<IActionResult> SearchMessages(int chatId, [FromBody] SearchMessagesQueryDto query)
        => Map(await handlers.Search.HandleAsync(new SearchMessagesQuery(chatId, GetCurrentUserId(), query)));

    [HttpGet("chat/{chatId}/counts")]
    public async Task<IActionResult> GetChatCounts(int chatId)
        => Map(await handlers.GetChatCounts.HandleAsync(new GetChatCountsQuery(chatId, GetCurrentUserId())));

    [HttpPost("user/{userId}/search")]
    [EnableRateLimiting("search")]
    public async Task<IActionResult> GlobalSearch(int userId, [FromBody] GlobalSearchQueryDto query)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<GlobalSearchResponseDto>();

        return Map(await handlers.GlobalSearch.HandleAsync(new GlobalSearchQuery(userId, query)));
    }
}