using API.Application.Configuration;
using API.Application.Features.File;
using API.Application.Features.File.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Shared.Contracts.Message;
using Shared.Infrastructure;

namespace API.Web.Controllers;

public sealed class FilesController(IFileHandlers handlers, IOptions<MessengerSettings> settings, ILogger<FilesController> logger)
    : BaseController<FilesController>(logger)
{
    [HttpPost("upload")]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Upload([FromQuery] int chatId, IFormFile file)
    {
        if (file.Length > settings.Value.MaxFileSizeBytes)
            return BadRequest(ApiResponse<MessageFileDto>.Fail("Файл превышает максимально допустимый размер"));

        return Map(await handlers.UploadFile.HandleAsync(new UploadFileCommand(chatId, GetCurrentUserId(), file)));
    }
}