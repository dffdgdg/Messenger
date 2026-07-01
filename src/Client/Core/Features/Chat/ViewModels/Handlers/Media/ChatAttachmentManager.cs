using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Core.Features.Chat.ViewModels.Managers;
using Core.Services.Api.Abstraction;
using Core.Services.Platform.Abstractions;
using Core.Shared.Configuration;
using Core.Shared.Helpers;
using Shared.Contracts.Message;
using System.Diagnostics;

namespace Core.Features.Chat.ViewModels.Handlers.Media;

public sealed class ChatAttachmentManager(
    int chatId,
    IApiClientService apiClient,
    IPlatformService platformService) : IDisposable
{
    private readonly IApiClientService _apiClient = apiClient
        ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly IPlatformService _platformService = platformService
        ?? throw new ArgumentNullException(nameof(platformService));
    private bool _disposed;

    private const int ThumbnailMaxDimension = 200;

    public ObservableCollection<LocalFileAttachment> Attachments { get; } = [];

    public async Task<bool> PickAndAddFilesAsync()
    {
        var storageProvider = _platformService.GetStorageProvider();

        if (storageProvider is null)
        {
            Debug.WriteLine("[ChatAttachmentManager] StorageProvider недоступен");
            return false;
        }

        try
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите файлы для прикрепления",
                AllowMultiple = true
            });

            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (path is not null)
                    await AddFileAsync(path);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatAttachmentManager] File picker error: {ex.Message}");
            return false;
        }
    }

    public async Task AddFileAsync(string filePath)
    {
        MemoryStream? memoryStream = null;
        Bitmap? thumbnail = null;

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > AppConstants.MaxFileSizeBytes)
            {
                Debug.WriteLine($"[ChatAttachmentManager] файл слишком большой: {fileInfo.Name}");
                return;
            }

            var fileName = Path.GetFileName(filePath);
            var contentType = MimeTypeHelper.GetMimeType(filePath);

            memoryStream = new MemoryStream();
            await using (var fileStream = File.OpenRead(filePath))
                await fileStream.CopyToAsync(memoryStream);

            memoryStream.Position = 0;

            if (contentType.StartsWith("image/"))
                thumbnail = TryCreateThumbnail(memoryStream, ThumbnailMaxDimension);

            var attachment = new LocalFileAttachment
            {
                FileName = fileName,
                ContentType = contentType,
                FilePath = filePath,
                Data = memoryStream,
                Thumbnail = thumbnail
            };

            Attachments.Add(attachment);
            memoryStream = null;
            thumbnail = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatAttachmentManager] Error processing file {filePath}: {ex.Message}");
        }
        finally
        {
            if (memoryStream is not null)
                await memoryStream.DisposeAsync();
            thumbnail?.Dispose();
        }
    }

    private static Bitmap? TryCreateThumbnail(MemoryStream stream, int maxDimension)
    {
        Bitmap? fullBitmap = null;
        try
        {
            stream.Position = 0;
            fullBitmap = new Bitmap(stream);
            stream.Position = 0;

            var width = fullBitmap.PixelSize.Width;
            var height = fullBitmap.PixelSize.Height;

            if (width <= maxDimension && height <= maxDimension)
                return fullBitmap;

            var scale = Math.Min((double)maxDimension / width, (double)maxDimension / height);
            var newSize = new Avalonia.PixelSize(
                Math.Max(1, (int)(width * scale)),
                Math.Max(1, (int)(height * scale)));

            var resized = fullBitmap.CreateScaledBitmap(newSize);
            fullBitmap.Dispose();
            return resized;
        }
        catch
        {
            fullBitmap?.Dispose();
            return null;
        }
    }

    public async Task<List<MessageFileDto>> UploadAllAsync(CancellationToken ct = default)
    {
        var uploadedFiles = new List<MessageFileDto>();

        foreach (var local in Attachments.ToList())
        {
            try
            {
                local.Data.Position = 0;
                var result = await _apiClient.UploadFileAsync<MessageFileDto>(
                    ApiEndpoints.Files.Upload(chatId),
                    local.Data,
                    local.FileName,
                    local.ContentType,
                    ct);

                if (result is { Success: true, Data: not null })
                    uploadedFiles.Add(result.Data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatAttachmentManager] ошибка загрузки: {ex.Message}");
            }
        }

        return uploadedFiles;
    }

    public bool Remove(LocalFileAttachment attachment)
    {
        if (!Attachments.Remove(attachment)) return false;
        attachment.Dispose();
        return true;
    }

    public void Clear()
    {
        foreach (var attachment in Attachments)
            attachment.Dispose();
        Attachments.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
    }
}