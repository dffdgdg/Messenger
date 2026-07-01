using API.Application.Configuration;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Application.Services.Base;
using API.Domain.Common;
using API.Domain.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Contracts.Message;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace API.Infrastructure.Services.Features.Messaging;

public partial class FileService(
    IUnitOfWork unitOfWork,
    IAccessControlService accessControl,
    IMessageRepository messageRepository,
    IUserRepository userRepository,
    IChatRepository chatRepository,
    IPendingUploadStore pendingUploadStore,
    IWebHostEnvironment env,
    IUrlBuilder urlBuilder,
    IOptions<MessengerSettings> settings,
    ILogger<FileService> logger) : BaseService<FileService>(unitOfWork, logger), IFileService
{
    private readonly MessengerSettings _settings = settings.Value;

    private static readonly HashSet<string> AllowedImageTypes = ["image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp"];
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

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
        var contentType = file.ContentType ?? "application/octet-stream";

        var token = pendingUploadStore.Register(userId, chatId, absolutePath, resultRelativePath, file.FileName, contentType, file.Length);

        LogFileSaved(fileName, chatId);

        return Result<MessageFileDto>.Success(new MessageFileDto
        {
            Id = 0,
            MessageId = 0,
            FileName = file.FileName,
            ContentType = contentType,
            Url = null,
            UploadToken = token,
            PreViewType = FileMappings.DeterminePreViewType(contentType),
            FileSize = file.Length
        });
    }

    public async Task<Result<FileDownloadInfo>> ResolveDownloadAsync(int fileId, int? contextMessageId, int userId, CancellationToken ct = default)
    {
        var file = await messageRepository.FindFileForDownloadAsync(fileId, ct);
        if (file is null || string.IsNullOrEmpty(file.Path))
            return Result<FileDownloadInfo>.NotFound("Файл не найден");

        if (file.Message.IsDeleted == true)
            return Result<FileDownloadInfo>.NotFound("Файл не найден");

        if (contextMessageId.HasValue)
        {
            var contextMessage = await messageRepository.FindUserMessageByIdAsync(contextMessageId.Value, ct);
            if (contextMessage is null || contextMessage.IsDeleted == true)
                return Result<FileDownloadInfo>.NotFound("Сообщение не найдено");

            var ownerMessageId = contextMessage.ForwardedFromMessageId ?? contextMessage.Id;

            if (file.MessageId != ownerMessageId)
                return Result<FileDownloadInfo>.Forbidden("Нет доступа к файлу");

            var access = await accessControl.EnsureMemberOfAsync(userId, contextMessage.ChatId);
            if (access.IsFailure) return access.As<FileDownloadInfo>();
        }
        else
        {
            var legacyAccess = await accessControl.EnsureMemberOfAsync(userId, file.Message.ChatId);
            if (legacyAccess.IsFailure) return legacyAccess.As<FileDownloadInfo>();
        }

        var absolutePath = GetAbsolutePath(file.Path.TrimStart('/'));

        if (!IsWithinUploadsRoot(absolutePath) || !File.Exists(absolutePath))
            return Result<FileDownloadInfo>.NotFound("Файл отсутствует на сервере");

        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        return Result<FileDownloadInfo>.Success(new FileDownloadInfo(absolutePath, contentType, file.FileName));
    }

    public async Task<Result<FileDownloadInfo>> ResolveVoiceDownloadAsync(int messageId, int userId, CancellationToken ct = default)
    {
        var contextMessage = await messageRepository.FindUserMessageByIdAsync(messageId, ct);
        if (contextMessage is null || contextMessage.IsDeleted == true)
            return Result<FileDownloadInfo>.NotFound("Голосовое сообщение не найдено");

        var access = await accessControl.EnsureMemberOfAsync(userId, contextMessage.ChatId);
        if (access.IsFailure) return access.As<FileDownloadInfo>();

        var ownerMessageId = contextMessage.ForwardedFromMessageId ?? contextMessage.Id;

        var voice = await messageRepository.FindVoiceForDownloadAsync(ownerMessageId, ct);
        if (voice is null || string.IsNullOrEmpty(voice.FilePath))
            return Result<FileDownloadInfo>.NotFound("Голосовое сообщение не найдено");

        if (voice.Message.IsDeleted == true)
            return Result<FileDownloadInfo>.NotFound("Голосовое сообщение не найдено");

        return ResolveStoredFile(voice.FilePath, $"voice_{messageId}");
    }

    public async Task<Result<FileDownloadInfo>> ResolveUserAvatarDownloadAsync(int targetUserId, int viewerId, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureCanViewUserAvatarAsync(viewerId, targetUserId);
        if (access.IsFailure) return access.As<FileDownloadInfo>();

        var user = await userRepository.FindByIdAsync(targetUserId, ct);
        if (user is null || string.IsNullOrEmpty(user.Avatar))
            return Result<FileDownloadInfo>.NotFound("Аватар не найден");

        return ResolveStoredFile(user.Avatar, $"avatar_user_{targetUserId}");
    }

    public async Task<Result<FileDownloadInfo>> ResolveChatAvatarDownloadAsync(int chatId, int viewerId, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureMemberOfAsync(viewerId, chatId);
        if (access.IsFailure) return access.As<FileDownloadInfo>();

        var chat = await chatRepository.FindByIdAsync(chatId, ct);
        if (chat is null || string.IsNullOrEmpty(chat.Avatar))
            return Result<FileDownloadInfo>.NotFound("Аватар не найден");

        return ResolveStoredFile(chat.Avatar, $"avatar_chat_{chatId}");
    }

    private Result<FileDownloadInfo> ResolveStoredFile(string relativePath, string downloadFileNameWithoutExt)
    {
        var absolutePath = GetAbsolutePath(relativePath.TrimStart('/'));

        if (!IsWithinUploadsRoot(absolutePath) || !File.Exists(absolutePath))
            return Result<FileDownloadInfo>.NotFound("Файл отсутствует на сервере");

        if (!ContentTypeProvider.TryGetContentType(absolutePath, out var contentType))
            contentType = "application/octet-stream";

        var fileName = $"{downloadFileNameWithoutExt}{Path.GetExtension(absolutePath)}";

        return Result<FileDownloadInfo>.Success(new FileDownloadInfo(absolutePath, contentType, fileName));
    }

    private bool IsWithinUploadsRoot(string absolutePath)
    {
        var uploadsRoot = Path.GetFullPath(GetAbsolutePath("uploads"));
        var fullPath = Path.GetFullPath(absolutePath);
        return fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase);
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