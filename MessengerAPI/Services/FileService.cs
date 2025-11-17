using MessengerAPI.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using MessengerShared.DTO;

namespace MessengerAPI.Services
{
    public interface IFileService
    {
        Task<string> SaveImageAsync(IFormFile file, string subFolder, string? oldFilePath = null);
        Task<MessageFileDTO> SaveMessageFileAsync(IFormFile file, int chatId, HttpRequest request);
        void DeleteFile(string? filePath);
        bool IsValidImage(IFormFile file);
    }

    public class FileService(MessengerDbContext context,IWebHostEnvironment env,ILogger<FileService> logger) 
        : BaseService<FileService>(context, logger), IFileService
    {
        private readonly string[] _allowedImageTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];
        private const long _maxFileSize = 20 * 1024 * 1024; // 20 MB for attachments

        public async Task<string> SaveImageAsync(IFormFile file, string subFolder, string? oldFilePath = null)
        {
            try
            {
                if (!IsValidImage(file))
                    throw new ArgumentException("Invalid image file");

                if (!string.IsNullOrEmpty(oldFilePath))
                    DeleteFile(oldFilePath);

                var fileName = $"{Guid.NewGuid()}.webp";
                var relativePath = Path.Combine("uploads", subFolder, fileName);
                var absolutePath = Path.Combine(env.WebRootPath ?? "wwwroot", relativePath);

                var directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                using var image = await Image.LoadAsync(file.OpenReadStream());

                if (image.Width > 1600 || image.Height > 1600)
                    image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(1600, 1600), Mode = ResizeMode.Max }));

                await image.SaveAsWebpAsync(absolutePath, new WebpEncoder { Quality = 85 });

                var resultPath = "/" + relativePath.Replace('\\', '/');
                _logger.LogInformation("Image saved successfully: {FilePath}", resultPath);

                return resultPath;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid image file provided");
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "saving image file");
                throw;
            }
        }

        public async Task<MessageFileDTO> SaveMessageFileAsync(IFormFile file, int chatId, HttpRequest request)
        {
            try
            {
                if (file == null || file.Length == 0)
                    throw new ArgumentException("No file provided");

                if (file.Length > _maxFileSize)
                    throw new ArgumentException("File too large");

                var ext = Path.GetExtension(file.FileName) ?? string.Empty;
                var fileName = $"{Guid.NewGuid()}{ext}";
                var relativeFolder = Path.Combine("uploads", "chats", chatId.ToString());
                var relativePath = Path.Combine(relativeFolder, fileName);
                var absolutePath = Path.Combine(env.WebRootPath ?? "wwwroot", relativePath);

                var directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                await using (var fs = new FileStream(absolutePath, FileMode.Create))
                {
                    await file.CopyToAsync(fs);
                }

                var mf = new MessageFile
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType ?? "application/octet-stream",
                    Path = "/" + relativePath.Replace('\\', '/'),
                };

                _context.MessageFiles.Add(mf);
                await SaveChangesAsync();

                var dto = new MessageFileDTO
                {
                    Id = mf.Id,
                    MessageId = mf.MessageId,
                    FileName = mf.FileName,
                    ContentType = mf.ContentType,
                    Url = mf.Path != null ? $"{request.Scheme}://{request.Host}{mf.Path}" : null,
                    PreviewType = GetPreviewType(mf.ContentType)
                };

                return dto;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "saving message file");
                throw;
            }
        }

        public void DeleteFile(string? filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) 
                    return;

                var fullPath = Path.Combine(env.WebRootPath ?? "wwwroot", filePath.TrimStart('/'));
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    _logger.LogInformation("File deleted successfully: {FilePath}", filePath);
                }
                else
                    _logger.LogWarning("File not found for deletion: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "deleting file");
                throw;
            }
        }

        public bool IsValidImage(IFormFile file)
        {
            if (file == null)
                return false;

            if (file.Length == 0)
            {
                _logger.LogWarning("Empty file provided");
                return false;
            }

            if (file.Length > _maxFileSize)
            {
                _logger.LogWarning("File too large: {FileSize} bytes, max allowed: {MaxSize}", file.Length, _maxFileSize);
                return false;
            }

            _ = (file.ContentType ?? string.Empty).ToLower();
            // allow any content types here for attachments; image checks separate
            return true;
        }

        private static string GetPreviewType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return "file";
            var t = contentType.ToLowerInvariant();
            if (t.StartsWith("image/")) return "image";
            if (t.StartsWith("video/")) return "video";
            if (t.StartsWith("audio/")) return "audio";
            return "file";
        }
    }
}