using MessengerAPI.Services.Messaging;

namespace MessengerAPI.Controllers;

public sealed class FilesController(IFileService fileService, ILogger<FilesController> logger) : BaseController<FilesController>(logger)
{
    private const long MaxFileSizeBytes = 100 * 1024 * 1024;

    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<ApiResponse<MessageFileDto>>> Upload([FromQuery] int chatId, IFormFile file)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => fileService.SaveMessageFileAsync(file, chatId, userId), "Файл загружен успешно");
    }
}