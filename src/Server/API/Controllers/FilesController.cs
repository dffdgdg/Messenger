using API.Application.Configuration;
using API.Application.Features.File.Commands;
using API.Application.Features.File.Queries;
using API.Application.Services.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

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

    [HttpGet("{fileId:int}/download")]
    public async Task<IActionResult> Download(int fileId, [FromQuery] int? contextMessageId, CancellationToken ct)
        => await ServeFile(handlers.DownloadFile.HandleAsync(new DownloadFileQuery(fileId, contextMessageId, GetCurrentUserId()), ct));

    [HttpGet("voice/{messageId:int}/download")]
    public async Task<IActionResult> DownloadVoice(int messageId, CancellationToken ct)
        => await ServeFile(handlers.DownloadVoice.HandleAsync(new DownloadVoiceQuery(messageId, GetCurrentUserId()), ct));

    [HttpGet("avatar/user/{userId:int}/download")]
    public async Task<IActionResult> DownloadUserAvatar(int userId, CancellationToken ct)
        => await ServeFile(handlers.DownloadUserAvatar.HandleAsync(new DownloadUserAvatarQuery(userId, GetCurrentUserId()), ct));

    [HttpGet("avatar/chat/{chatId:int}/download")]
    public async Task<IActionResult> DownloadChatAvatar(int chatId, CancellationToken ct)
        => await ServeFile(handlers.DownloadChatAvatar.HandleAsync(new DownloadChatAvatarQuery(chatId, GetCurrentUserId()), ct));

    private async Task<IActionResult> ServeFile(Task<API.Domain.Common.Result<FileDownloadInfo>> resultTask)
    {
        var result = await resultTask;

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Бизнес-ошибка [{ErrorType}]: {Error}", result.ErrorType, result.Error);
            return MapFailure<object>(result);
        }

        var info = result.Value!;
        return PhysicalFile(info.AbsolutePath, info.ContentType, info.FileName, enableRangeProcessing: true);
    }
}