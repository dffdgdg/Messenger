using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MessengerDesktop.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public sealed class ChatAttachmentManager(int chatId, IApiClientService apiClient, IStorageProvider? storageProvider = null) : IDisposable
{
    private bool _disposed;

    public ObservableCollection<LocalFileAttachment> Attachments { get; } = [];

    public async Task<bool> PickAndAddFilesAsync()
    {
        if (storageProvider is null)
        {
            Debug.WriteLine("[ChatAttachmentManager] StorageProvider не доступен");
            return false;
        }

        try
        {
            var options = new FilePickerOpenOptions
            {
                Title = "Выберите файлы для прикрепления",
                AllowMultiple = true
            };

            var files = await storageProvider.OpenFilePickerAsync(options);

            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (path is not null)
                {
                    await AddFileAsync(path);
                }
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
            {
                await fileStream.CopyToAsync(memoryStream);
            }
            memoryStream.Position = 0;

            if (contentType.StartsWith("image/"))
            {
                thumbnail = TryCreateThumbnail(memoryStream);
            }

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

    private static Bitmap? TryCreateThumbnail(MemoryStream stream)
    {
        try
        {
            stream.Position = 0;
            var bitmap = new Bitmap(stream);
            stream.Position = 0;
            return bitmap;
        }
        catch
        {
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
                var uploadResult = await apiClient.UploadFileAsync<MessageFileDto>(
                    ApiEndpoints.File.Upload(chatId), local.Data, local.FileName, local.ContentType, ct);

                if (uploadResult is { Success: true, Data: not null })
                {
                    uploadedFiles.Add(uploadResult.Data);
                }
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
        if (Attachments.Remove(attachment))
        {
            attachment.Dispose();
            return true;
        }
        return false;
    }

    public void Clear()
    {
        foreach (var attachment in Attachments)
            attachment.Dispose();

        Attachments.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        _disposed = true;
    }
}