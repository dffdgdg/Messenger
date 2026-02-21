using MessengerAPI.Common;
using MessengerAPI.Services.Chat;
using MessengerAPI.Services.Messaging;
using MessengerShared.DTO.Message;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class FilesController(IFileService fileService, IChatService chatService, ILogger<FilesController> logger)
    : BaseController<FilesController>(logger)
{
    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<MessageFileDTO>>> Upload(
        [FromQuery] int chatId, IFormFile file)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(userId, chatId);

            if (file is null || file.Length == 0)
                return Result<MessageFileDTO>.Failure("Файл не предоставлен");

            var result = await fileService.SaveMessageFileAsync(file, chatId);
            return Result<MessageFileDTO>.Success(result);
        }, "Файл загружен успешно");
    }
}