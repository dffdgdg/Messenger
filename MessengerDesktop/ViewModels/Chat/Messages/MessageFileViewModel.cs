using MessengerDesktop.Services.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class MessageFileViewModel(MessageFileDto file, IFileDownloadService? downloadService = null,
    INotificationService? notificationService = null) : ObservableObject, IDisposable
{
    private const int MaxDisplayFileNameLength = 18;
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz"
    };
    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };
    private static readonly HashSet<string> WordExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".rtf", ".odt"
    };
    private static readonly HashSet<string> ExcelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xls", ".xlsx", ".csv", ".ods"
    };

    private CancellationTokenSource? _downloadCts;
    private readonly Lock _ctsLock = new();
    private bool _disposed;

    public MessageFileDto File { get; } = file ?? throw new ArgumentNullException(nameof(file));

    [ObservableProperty] public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    [ObservableProperty]
    public partial bool IsDownloaded { get; set; }

    [ObservableProperty]
    public partial string? DownloadedFilePath { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial DownloadState State { get; set; } = DownloadState.NotStarted;

    public int Id => File.Id;
    public string FileName => File.FileName;
    public string DisplayFileName => FormatDisplayFileName(FileName, MaxDisplayFileNameLength);
    public string ContentType => File.ContentType;
    public string? Url => File.Url;
    public string PreviewType => File.PreviewType;

    public bool IsImage => PreviewType == "image";
    public bool IsVideo => PreviewType == "video";
    public bool IsAudio => PreviewType == "audio";
    public bool IsGenericFile => PreviewType == "file";

    public string? ImageUrl => IsImage ? Url : null;

    private string FileExtension => Path.GetExtension(FileName) ?? string.Empty;

    private bool IsPdfFile => ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) || PdfExtensions.Contains(FileExtension);

    private bool IsWordFile => ContentType.Contains("word", StringComparison.OrdinalIgnoreCase)
        || ContentType.Contains("document", StringComparison.OrdinalIgnoreCase) || WordExtensions.Contains(FileExtension);

    private bool IsExcelFile => ContentType.Contains("excel", StringComparison.OrdinalIgnoreCase)
        || ContentType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) || ExcelExtensions.Contains(FileExtension);

    private bool IsArchiveFile =>
        ContentType.Contains("zip", StringComparison.OrdinalIgnoreCase)
        || ContentType.Contains("rar", StringComparison.OrdinalIgnoreCase)
        || ContentType.Contains("7z", StringComparison.OrdinalIgnoreCase)
        || ContentType.Contains("archive", StringComparison.OrdinalIgnoreCase)
        || ContentType.Contains("compressed", StringComparison.OrdinalIgnoreCase)
        || ArchiveExtensions.Contains(FileExtension);

    public string FileIconResourceKey => PreviewType switch
    {
        "image" => "FileTypeImageIcon",
        "video" => "FileTypeVideoIcon",
        "audio" => "FileTypeAudioIcon",
        "file" when IsPdfFile => "FileTypePdfIcon",
        "file" when IsWordFile => "FileTypeWordIcon",
        "file" when IsExcelFile => "FileTypeExcelIcon",
        "file" when IsArchiveFile => "FileTypeArchiveIcon",
        _ => "FileTypeDefaultIcon"
    };

    public string FileSizeFormatted => FormatFileSize(File.FileSize);

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (_disposed || IsDownloading || string.IsNullOrEmpty(Url) || downloadService == null)
            return;

        ErrorMessage = null;
        HasError = false;
        IsDownloading = true;
        DownloadProgress = 0;
        State = DownloadState.Downloading;

        var cts = new CancellationTokenSource();

        lock (_ctsLock)
        {
            _downloadCts?.Dispose();
            _downloadCts = cts;
        }

        try
        {
            var progress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed) DownloadProgress = p;
                }));

            var filePath = await downloadService.DownloadFileAsync(Url, FileName, progress, cts.Token);

            if (filePath != null)
            {
                DownloadedFilePath = filePath;
                IsDownloaded = true;
                State = DownloadState.Completed;
                notificationService?.ShowSuccessAsync($"Файл сохранён: {FileName}", copyToClipboard: false);
            }
        }
        catch (OperationCanceledException)
        {
            State = DownloadState.Cancelled;
            DownloadProgress = 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
            State = DownloadState.Failed;
            notificationService?.ShowErrorAsync($"Ошибка загрузки: {ex.Message}", copyToClipboard: false);
        }
        finally
        {
            IsDownloading = false;

            lock (_ctsLock)
            {
                if (_downloadCts == cts)
                    _downloadCts = null;
            }

            cts.Dispose();
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        lock (_ctsLock)
        {
            try { _downloadCts?.Cancel(); }
            catch (ObjectDisposedException) { /* CTS уже был удалён */ }
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (downloadService == null) return;

        try
        {
            if (!string.IsNullOrEmpty(DownloadedFilePath))
            {
                await downloadService.OpenFileAsync(DownloadedFilePath);
            }
            else if (!IsDownloaded && !IsDownloading)
            {
                await DownloadAsync();
                if (IsDownloaded && !string.IsNullOrEmpty(DownloadedFilePath))
                {
                    await downloadService.OpenFileAsync(DownloadedFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task OpenInFolderAsync()
    {
        if (downloadService == null || string.IsNullOrEmpty(DownloadedFilePath))
            return;

        await downloadService.OpenFolderAsync(DownloadedFilePath);
    }

    [RelayCommand]
    private void RetryDownload()
    {
        HasError = false;
        ErrorMessage = null;
        State = DownloadState.NotStarted;
        _ = DownloadAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_ctsLock)
        {
            try
            {
                _downloadCts?.Cancel();
                _downloadCts?.Dispose();
            }
            catch { /* Объект уже может быть в процессе удаления */ }
            _downloadCts = null;
        }
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 0 => "",
        0 => "0 B",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024
            => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
    };

    private static string FormatDisplayFileName(string fileName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length <= maxLength)
            return fileName;

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
            return fileName[..Math.Max(1, maxLength - 1)] + "…";

        var nameWithoutExtension =
            Path.GetFileNameWithoutExtension(fileName);
        var availableNameLength =
            maxLength - extension.Length - 1;

        if (availableNameLength <= 0)
            return "…" + extension;

        var trimmedName = nameWithoutExtension.Length > availableNameLength
                ? nameWithoutExtension[..availableNameLength] : nameWithoutExtension;

        return $"{trimmedName}…{extension}";
    }
}

public enum DownloadState { NotStarted, Downloading, Completed, Failed, Cancelled }