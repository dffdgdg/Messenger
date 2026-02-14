using MessengerAPI.Common;
using MessengerAPI.Model;
using MessengerAPI.Services.Chat;
using MessengerAPI.Services.Messaging;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.Search;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Controllers;

public class MessagesController(IMessageService messageService, IChatService chatService, ITranscriptionService transcriptionService,
    TranscriptionQueue transcriptionQueue, MessengerDbContext context, ILogger<MessagesController> logger) : BaseController<MessagesController>(logger)
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<MessageDTO>>> CreateMessage([FromBody] MessageDTO messageDto) => await ExecuteAsync(async () =>
    {
        ValidateModel();
        return await messageService.CreateMessageAsync(messageDto);
    }, "Сообщение успешно отправлено");

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<MessageDTO>>> UpdateMessage(int id, [FromBody] UpdateMessageDTO updateDto)
    {
        var currentUserId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            ValidateModel();
            if (id != updateDto.Id) throw new ArgumentException("Несоответствие ID сообщения");
            return await messageService.UpdateMessageAsync(id, currentUserId, updateDto);
        }, "Сообщение успешно отредактировано");
    }

    [HttpGet("user/{userId}/search")]
    public async Task<ActionResult<ApiResponse<GlobalSearchResponseDTO>>> GlobalSearch(int userId, [FromQuery] string query = "",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId != userId) return Forbidden<GlobalSearchResponseDTO>();

        return await ExecuteAsync(() => messageService.GlobalSearchAsync(userId, query, page, pageSize), "Поиск завершён");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMessage(int id)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(() => messageService.DeleteMessageAsync(id, currentUserId), "Сообщение успешно удалено");
    }

    [HttpGet("chat/{chatId}")]
    public async Task<ActionResult<ApiResponse<PagedMessagesDTO>>> GetChatMessages(int chatId, [FromQuery] int? userId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        var currentUserId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
            return await messageService.GetChatMessagesAsync(chatId, userId ?? currentUserId, page, pageSize);
        }, "Сообщения получены успешно");
    }

    [HttpGet("chat/{chatId}/around/{messageId}")]
    public async Task<ActionResult<ApiResponse<PagedMessagesDTO>>> GetMessagesAround(int chatId, int messageId,
        [FromQuery] int? userId = null, [FromQuery] int count = 50)
    {
        var currentUserId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
            return await messageService.GetMessagesAroundAsync(chatId, messageId, userId ?? currentUserId, count);
        }, "Сообщения получены успешно");
    }

    [HttpGet("chat/{chatId}/before/{messageId}")]
    public async Task<ActionResult<ApiResponse<PagedMessagesDTO>>> GetMessagesBefore(int chatId, int messageId,
        [FromQuery] int? userId = null, [FromQuery] int count = 30)
    {
        var currentUserId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
            return await messageService.GetMessagesBeforeAsync(chatId, messageId, userId ?? currentUserId, count);
        }, "Сообщения получены успешно");
    }

    [HttpGet("chat/{chatId}/after/{messageId}")]
    public async Task<ActionResult<ApiResponse<PagedMessagesDTO>>> GetMessagesAfter(int chatId, int messageId,
        [FromQuery] int? userId = null, [FromQuery] int count = 30)
    {
        var currentUserId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
            return await messageService.GetMessagesAfterAsync(chatId, messageId, userId ?? currentUserId, count);
        }, "Сообщения получены успешно");
    }

    [HttpGet("{id}/transcription")]
    public async Task<ActionResult<ApiResponse<VoiceTranscriptionDTO>>> GetTranscription(int id, CancellationToken ct)
        => await ExecuteResultAsync(() => transcriptionService.GetTranscriptionAsync(id, ct));

    [HttpPost("{id}/transcription/retry")]
    public async Task<IActionResult> RetryTranscription(int id, CancellationToken ct)
    {
        return await ExecuteResultAsync(async () =>
        {
            var message = await context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

            if (message == null) return Result.Failure("Сообщение не найдено");

            if (!message.IsVoiceMessage) return Result.Failure("Не является голосовым");

            if (message.TranscriptionStatus == "processing") return Result.Failure("Расшифровка уже выполняется");

            await transcriptionQueue.EnqueueAsync(id, ct);
            return Result.Success();
        }, "Расшифровка запущена повторно");
    }
}