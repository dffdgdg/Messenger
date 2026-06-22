using API.Application.Configuration;
using API.Application.Services.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Shared.Dto.Message;
using Shared.Response;

namespace API.Web.Controllers;

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