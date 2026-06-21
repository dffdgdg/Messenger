using API.Application.Configuration;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Application.Services.Base;
using API.Domain.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Dto.Message;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace API.Application.Services.Features.Messaging;

public partial class FileService(
    IUnitOfWork unitOfWork,
    IAccessControlService accessControl,
    IWebHostEnvironment env,
    IUrlBuilder urlBuilder,
    IOptions<MessengerSettings> settings,
    ILogger<FileService> logger) : BaseService<FileService>(unitOfWork, logger), IFileService
{
    private readonly MessengerSettings _settings = settings.Value;

    private static readonly HashSet<string> AllowedImageTypes = ["image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp"];

    public async Task<Result<string>> SaveImageAsync(IFormFile file, string subFolder, string? oldFilePath = null)
    {
        if (!IsValidImage(file))
            return Result<string>.Failure("Некорректный файл изображения");

        if (!string.IsNullOrEmpty(oldFilePath))
            DeleteFile(oldFilePath);

        var fileName = $"{Guid.NewGuid()}.webp";
        var relativePath = Path.Combine("uploads", subFolder, fileName);
        var absolutePath = GetAbsolutePath(relativePath);

        EnsureDirectoryExists(absolutePath);

        using var image = await Image.LoadAsync(file.OpenReadStream());

        if (image.Width > _settings.MaxImageDimension || image.Height > _settings.MaxImageDimension)
        {
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(_settings.MaxImageDimension, _settings.MaxImageDimension), Mode = ResizeMode.Max }));
        }

        await image.SaveAsWebpAsync(absolutePath, new WebpEncoder { Quality = _settings.ImageQuality });

        var resultPath = NormalizeToWebPath(relativePath);

        LogImageSaved(resultPath);

        return Result<string>.Success(resultPath);
    }

    public async Task<Result<MessageFileDto>> SaveMessageFileAsync(IFormFile file, int chatId, int userId)
    {
        var access = await accessControl.EnsureMemberOfAsync(userId, chatId);
        if (access.IsFailure) return access.As<MessageFileDto>();

        if (file is null || file.Length == 0)
            return Result<MessageFileDto>.Failure("Файл не предоставлен");

        if (file.Length > _settings.MaxFileSizeBytes)
            return Result<MessageFileDto>.Failure($"Файл слишком большой. Максимум: {_settings.MaxFileSizeBytes / 1024 / 1024} MB");

        var ext = Path.GetExtension(file.FileName) ?? string.Empty;
        var fileName = $"{Guid.NewGuid()}{ext}";
        var relativePath = Path.Combine("uploads", "chats", chatId.ToString(), fileName);
        var absolutePath = GetAbsolutePath(relativePath);

        EnsureDirectoryExists(absolutePath);

        await using (var fs = new FileStream(absolutePath, FileMode.Create))
            await file.CopyToAsync(fs);

        var resultRelativePath = NormalizeToWebPath(relativePath);

        LogFileSaved(fileName, chatId);

        return Result<MessageFileDto>.Success(new MessageFileDto
        {
            Id = 0,
            MessageId = 0,
            FileName = file.FileName,
            ContentType = file.ContentType ?? "application/octet-stream",
            Url = urlBuilder.BuildUrl(resultRelativePath)!,
            PreviewType = FileMappings.DeterminePreviewType(file.ContentType),
            FileSize = file.Length
        });
    }

    public void DeleteFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        var fullPath = GetAbsolutePath(filePath.TrimStart('/'));

        if (!File.Exists(fullPath))
        {
            LogFileNotFound(fullPath);
            return;
        }

        try
        {
            File.Delete(fullPath);
            LogFileDeleted(fullPath);
        }
        catch (Exception ex)
        {
            LogFileDeletionFailed(fullPath, ex);
        }
    }

    public bool IsValidImage(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return false;

        if (file.Length > _settings.MaxFileSizeBytes)
            return false;

        if (!AllowedImageTypes.Contains(file.ContentType?.ToLowerInvariant() ?? ""))
            return false;

        return true;
    }

    private string GetAbsolutePath(string relativePath)
        => Path.Combine(env.WebRootPath ?? "wwwroot", relativePath);

    private static string NormalizeToWebPath(string relativePath)
        => string.Concat(Path.AltDirectorySeparatorChar, relativePath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "Изображение сохранено: {FilePath}")]
    private partial void LogImageSaved(string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Файл сохранён: {FileName} для чата {ChatId}")]
    private partial void LogFileSaved(string fileName, int chatId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Файл не найден для удаления: {FilePath}")]
    private partial void LogFileNotFound(string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Файл удалён: {FilePath}")]
    private partial void LogFileDeleted(string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось удалить файл: {FilePath}")]
    private partial void LogFileDeletionFailed(string filePath, Exception ex);

    #endregion
}