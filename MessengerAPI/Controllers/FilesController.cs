using MessengerAPI.Common;
using MessengerAPI.Services.Chat;
using MessengerAPI.Services.Messaging;
using MessengerShared.Dto.Message;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class FilesController(IFileService fileService, IChatService chatService, ILogger<FilesController> logger)
    : BaseController<FilesController>(logger)
{
    private const long MaxFileSizeBytes = 100 * 1024 * 1024;
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<ApiResponse<MessageFileDto>>> Upload([FromQuery] int chatId, IFormFile file)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(userId, chatId);

            if (file is null || file.Length == 0)
                return Result<MessageFileDto>.Failure("Файл не предоставлен");

            var result = await fileService.SaveMessageFileAsync(file, chatId);
            return Result<MessageFileDto>.Success(result);
        }, "Файл загружен успешно");
    }
}