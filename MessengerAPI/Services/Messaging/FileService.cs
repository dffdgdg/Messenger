using MessengerAPI.Configuration;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.DTO.Message;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace MessengerAPI.Services.Messaging
{
    public interface IFileService
    {
        Task<string> SaveImageAsync(IFormFile file, string subFolder, string? oldFilePath = null);
        Task<MessageFileDTO> SaveMessageFileAsync(IFormFile file, int chatId);
        void DeleteFile(string? filePath);
        bool IsValidImage(IFormFile file);
    }

    public class FileService(MessengerDbContext context,IWebHostEnvironment env,IUrlBuilder urlBuilder,IOptions<MessengerSettings> settings,
        ILogger<FileService> logger) : BaseService<FileService>(context, logger), IFileService
    {
        private readonly MessengerSettings _settings = settings.Value;

        private static readonly HashSet<string> AllowedImageTypes = ["image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp"];

        public async Task<string> SaveImageAsync(IFormFile file, string subFolder, string? oldFilePath = null)
        {
            if (!IsValidImage(file))
                throw new ArgumentException("Некорректный файл изображения");

            if (!string.IsNullOrEmpty(oldFilePath))
                DeleteFile(oldFilePath);

            var fileName = $"{Guid.NewGuid()}.webp";
            var relativePath = Path.Combine("uploads", subFolder, fileName);
            var absolutePath = GetAbsolutePath(relativePath);

            EnsureDirectoryExists(absolutePath);

            using var image = await Image.LoadAsync(file.OpenReadStream());

            if (image.Width > _settings.MaxImageDimension || image.Height > _settings.MaxImageDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(_settings.MaxImageDimension,_settings.MaxImageDimension),
                    Mode = ResizeMode.Max
                }));
            }

            await image.SaveAsWebpAsync(absolutePath, new WebpEncoder
            {
                Quality = _settings.ImageQuality
            });

            var resultPath = "/" + relativePath.Replace('\\', '/');

            _logger.LogInformation("Изображение сохранено: {FilePath}", resultPath);

            return resultPath;
        }

        public async Task<MessageFileDTO> SaveMessageFileAsync(IFormFile file, int chatId)
        {
            if (file is null || file.Length == 0)
                throw new ArgumentException("Файл не предоставлен");

            if (file.Length > _settings.MaxFileSizeBytes)
                throw new ArgumentException($"Файл слишком большой. Максимум: {_settings.MaxFileSizeBytes / 1024 / 1024} MB");

            var ext = Path.GetExtension(file.FileName) ?? string.Empty;
            var fileName = $"{Guid.NewGuid()}{ext}";
            var relativePath = Path.Combine("uploads", "chats", chatId.ToString(), fileName);
            var absolutePath = GetAbsolutePath(relativePath);

            EnsureDirectoryExists(absolutePath);

            await using (var fs = new FileStream(absolutePath, FileMode.Create))
            {
                await file.CopyToAsync(fs);
            }

            var resultRelativePath = "/" + relativePath.Replace('\\', '/');

            _logger.LogDebug("Файл сохранён: {FileName} для чата {ChatId}",fileName, chatId);

            return new MessageFileDTO
            {
                Id = 0,
                MessageId = 0,
                FileName = file.FileName,
                ContentType = file.ContentType ?? "application/octet-stream",
                Url = urlBuilder.BuildUrl(resultRelativePath)!,
                PreviewType = DeterminePreviewType(file.ContentType),
                FileSize = file.Length
            };
        }

        public void DeleteFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            var fullPath = GetAbsolutePath(filePath.TrimStart('/'));

            if (!File.Exists(fullPath))
            {
                _logger.LogDebug("Файл не найден для удаления: {FilePath}", filePath);
                return;
            }

            try
            {
                File.Delete(fullPath);
                _logger.LogInformation("Файл удалён: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось удалить файл: {FilePath}", filePath);
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

        private static void EnsureDirectoryExists(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private static string DeterminePreviewType(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return "file";

            var type = contentType.ToLowerInvariant();

            return type switch
            {
                _ when type.StartsWith("image/") => "image",
                _ when type.StartsWith("video/") => "video",
                _ when type.StartsWith("audio/") => "audio",
                _ => "file"
            };
        }
    }
}