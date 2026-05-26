using Shared.DTO.Message;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

public sealed class MessagesController(IMessageService message, ILogger<MessagesController> logger) : BaseController<MessagesController>(logger)
{
    [HttpPost]
    [EnableRateLimiting("messaging")]
    public async Task<IActionResult> CreateMessage([FromBody] CreateMessageRequest request)
        => Map(await message.CreateMessageAsync(GetCurrentUserId(), request));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateMessage(int id, [FromBody] UpdateMessageDto updateDto)
    {
        if (id != updateDto.Id)
            return Map(Result.Failure("Несоответствие ID сообщения"));
        return Map(await message.UpdateMessageAsync(id, GetCurrentUserId(), updateDto));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMessage(int id)
        => Map(await message.DeleteMessageAsync(id, GetCurrentUserId()));

    [HttpPost("{id}/pin")]
    public async Task<IActionResult> PinMessage(int id)
        => Map(await message.PinMessageAsync(id, GetCurrentUserId()));

    [HttpDelete("{id}/pin")]
    public async Task<IActionResult> UnpinMessage(int id)
        => Map(await message.UnpinMessageAsync(id, GetCurrentUserId()));

    [HttpGet("chat/{chatId}/pinned")]
    public async Task<IActionResult> GetPinnedMessages(int chatId)
        => Map(await message.GetPinnedMessagesAsync(chatId, GetCurrentUserId()));

    [HttpGet("chat/{chatId}/latest")]
    public async Task<IActionResult> GetLatestMessages(int chatId, [FromQuery] int take = 50)
        => Map(await message.GetLatestMessagesAsync(chatId, GetCurrentUserId(), take));

    [HttpGet("chat/{chatId}/around/{messageId}")]
    public async Task<IActionResult> GetMessagesAround(int chatId, int messageId, [FromQuery] int count = 50)
        => Map(await message.GetMessagesAroundAsync(chatId, messageId, GetCurrentUserId(), count));

    [HttpGet("chat/{chatId}/before/{messageId}")]
    public async Task<IActionResult> GetMessagesBefore(int chatId, int messageId, [FromQuery] int count = 30)
        => Map(await message.GetMessagesBeforeAsync(chatId, messageId, GetCurrentUserId(), count));

    [HttpGet("chat/{chatId}/after/{messageId}")]
    public async Task<IActionResult> GetMessagesAfter(int chatId, int messageId, [FromQuery] int count = 30)
        => Map(await message.GetMessagesAfterAsync(chatId, messageId, GetCurrentUserId(), count));

    [HttpPost("chat/{chatId}/search")]
    public async Task<IActionResult> SearchMessages(int chatId, [FromBody] SearchMessagesQueryDto query)
        => Map(await message.SearchMessagesAsync(chatId, GetCurrentUserId(), query));

    [HttpGet("chat/{chatId}/counts")]
    public async Task<IActionResult> GetChatCounts(int chatId)
        => Map(await message.GetChatCountsAsync(chatId, GetCurrentUserId()));

    [HttpPost("user/{userId}/search")]
    public async Task<IActionResult> GlobalSearch(int userId, [FromBody] GlobalSearchQueryDto query)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<GlobalSearchResponseDto>();
        return Map(await message.GlobalSearchAsync(userId, query));
    }
}