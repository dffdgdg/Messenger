using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MessengerDesktop.Helpers;
using MessengerDesktop.Services.Api;
using MessengerShared.DTO;

namespace MessengerDesktop.ViewModels.Chat;

public class ChatAttachmentManager(int chatId,IApiClientService apiClient,IStorageProvider? storageProvider = null) : IDisposable
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private const long MaxFileSizeBytes = 20 * 1024 * 1024;
    private bool _disposed;

    public ObservableCollection<LocalFileAttachment> Attachments { get; } = [];

    public async Task<bool> PickAndAddFilesAsync()
    {
        if (storageProvider is null)
        {
            Debug.WriteLine("StorageProvider is not available");
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
            Debug.WriteLine($"File picker error: {ex.Message}");
            return false;
        }
    }

    public async Task AddFileAsync(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                Debug.WriteLine($"File too large: {fileInfo.Name}");
                return;
            }

            var fileName = Path.GetFileName(filePath);
            var contentType = MimeTypeHelper.GetMimeType(filePath);

            await using var fileStream = File.OpenRead(filePath);
            var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var attachment = new LocalFileAttachment
            {
                FileName = fileName,
                ContentType = contentType,
                FilePath = filePath,
                Data = memoryStream
            };

            if (contentType.StartsWith("image/"))
            {
                try
                {
                    memoryStream.Position = 0;
                    attachment.Thumbnail = new Bitmap(memoryStream);
                    memoryStream.Position = 0;
                }
                catch
                {
                    // Игнорируем ошибку создания превью
                }
            }

            Attachments.Add(attachment);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing file {filePath}: {ex.Message}");
        }
    }

    public async Task<List<MessageFileDTO>> UploadAllAsync(CancellationToken ct = default)
    {
        var uploadedFiles = new List<MessageFileDTO>();

        foreach (var local in Attachments.ToList())
        {
            try
            {
                local.Data.Position = 0;
                var uploadResult = await _apiClient.UploadFileAsync<MessageFileDTO>(
                    $"api/files/upload?chatId={chatId}",
                    local.Data,
                    local.FileName,
                    local.ContentType,
                    ct);

                if (uploadResult is { Success: true, Data: not null })
                {
                    uploadedFiles.Add(uploadResult.Data);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Upload error: {ex.Message}");
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
        {
            attachment.Dispose();
        }
        Attachments.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}