using MessengerAPI.Services.Messaging;
using MessengerShared.DTO.Message;
using Microsoft.AspNetCore.RateLimiting;

namespace MessengerAPI.Controllers;

public sealed class MessagesController(IMessageService messageService, ITranscriptionService transcriptionService,
    ILogger<MessagesController> logger) : BaseController<MessagesController>(logger)
{
    [HttpPost]
    [EnableRateLimiting("messaging")]
    public async Task<ActionResult<ApiResponse<MessageDto>>> CreateMessage([FromBody] CreateMessageRequest request)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => messageService.CreateMessageAsync(userId, request),"Сообщение успешно отправлено");
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<MessageDto>>> UpdateMessage(int id, [FromBody] UpdateMessageDto updateDto)
        => await ExecuteAsync(async () =>
        {
            if (id != updateDto.Id)
                return Result<MessageDto>.Failure("Несоответствие ID сообщения");
            return await messageService.UpdateMessageAsync(id, GetCurrentUserId(), updateDto);
        }, "Сообщение успешно отредактировано");

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMessage(int id)
        => await ExecuteAsync(() => messageService.DeleteMessageAsync(id, GetCurrentUserId()),"Сообщение успешно удалено");

    [HttpGet("chat/{chatId}")]
    public async Task<ActionResult<ApiResponse<PagedMessagesDto>>> GetChatMessages(int chatId,[FromQuery] int page = 1,[FromQuery] int pageSize = 15)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => messageService.GetChatMessagesAsync(chatId, userId, page, pageSize), "Сообщения получены успешно");
    }

    [HttpGet("chat/{chatId}/around/{messageId}")]
    public async Task<ActionResult<ApiResponse<PagedMessagesDto>>> GetMessagesAround(int chatId, int messageId, [FromQuery] int count = 50)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => messageService.GetMessagesAroundAsync(chatId, messageId, userId, count));
    }

    [HttpGet("chat/{chatId}/before/{messageId}")]
    public async Task<ActionResult<ApiResponse<PagedMessagesDto>>> GetMessagesBefore(int chatId, int messageId, [FromQuery] int count = 30)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => messageService.GetMessagesBeforeAsync(chatId, messageId, userId, count));
    }

    [HttpGet("chat/{chatId}/after/{messageId}")]
    public async Task<ActionResult<ApiResponse<PagedMessagesDto>>> GetMessagesAfter(int chatId, int messageId, [FromQuery] int count = 30)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => messageService.GetMessagesAfterAsync(chatId, messageId, userId, count));
    }

    [HttpGet("chat/{chatId}/search")]
    [EnableRateLimiting("search")]
    public async Task<ActionResult<ApiResponse<SearchMessagesResponseDto>>> SearchMessages(
        int chatId, [FromQuery] string query = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => messageService.SearchMessagesAsync(chatId, userId, query, page, pageSize));
    }

    [HttpGet("user/{userId}/search")]
    [EnableRateLimiting("search")]
    public async Task<ActionResult<ApiResponse<GlobalSearchResponseDto>>> GlobalSearch(
        int userId,
        [FromQuery] string query = "",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<GlobalSearchResponseDto>();

        return await ExecuteAsync(() => messageService.GlobalSearchAsync(userId, query, page, pageSize));
    }

    [HttpGet("{id}/transcription")]
    public async Task<ActionResult<ApiResponse<VoiceTranscriptionDto>>> GetTranscription(int id, CancellationToken ct)
        => await ExecuteAsync(() => transcriptionService.GetTranscriptionAsync(id, ct));

    [HttpPost("{id}/transcription/retry")]
    public async Task<IActionResult> RetryTranscription(int id, CancellationToken ct)
        => await ExecuteAsync(() => transcriptionService.RetryTranscriptionAsync(id, ct), "Расшифровка запущена повторно");
}