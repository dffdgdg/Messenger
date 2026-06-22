using API.Domain.Common;
using Microsoft.AspNetCore.Http;
using Shared.Dto.Message;

namespace API.Application.Services.Abstractions;

public interface IFileService
{
    Task<Result<string>> SaveImageAsync(IFormFile file, string subFolder, string? oldFilePath = null);
    Task<Result<MessageFileDto>> SaveMessageFileAsync(IFormFile file, int chatId, int userId);
    void DeleteFile(string? filePath);
    bool IsValidImage(IFormFile file);
}