using API.Domain.Common;
using Microsoft.AspNetCore.Http;
using Shared.Contracts.Message;

namespace API.Application.Services.Abstractions;

public sealed record FileDownloadInfo(string AbsolutePath, string ContentType, string FileName);

public interface IFileService
{
    Task<Result<string>> SaveImageAsync(IFormFile file, string subFolder, string? oldFilePath = null);
    Task<Result<MessageFileDto>> SaveMessageFileAsync(IFormFile file, int chatId, int userId);
    Task<Result<FileDownloadInfo>> ResolveDownloadAsync(int fileId, int? contextMessageId, int userId, CancellationToken ct = default);
    Task<Result<FileDownloadInfo>> ResolveVoiceDownloadAsync(int messageId, int userId, CancellationToken ct = default);
    Task<Result<FileDownloadInfo>> ResolveUserAvatarDownloadAsync(int targetUserId, int viewerId, CancellationToken ct = default);
    Task<Result<FileDownloadInfo>> ResolveChatAvatarDownloadAsync(int chatId, int viewerId, CancellationToken ct = default);
    void DeleteFile(string? filePath);
    bool IsValidImage(IFormFile file);
}