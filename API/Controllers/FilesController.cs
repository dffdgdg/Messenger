using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

public sealed class FilesController(IFileService fileService, IOptions<MessengerSettings> settings, ILogger<FilesController> logger) : BaseController<FilesController>(logger)
{
    [HttpPost("upload")]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Upload([FromQuery] int chatId, IFormFile file)
    {
        if (file.Length > settings.Value.MaxFileSizeBytes)
            return BadRequest(ApiResponse<MessageFileDto>.Fail("Файл превышает максимально допустимый размер"));
        return Map(await fileService.SaveMessageFileAsync(file, chatId, GetCurrentUserId()));
    }
}