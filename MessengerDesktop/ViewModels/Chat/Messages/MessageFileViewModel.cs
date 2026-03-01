using MessengerDesktop.Services.UI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class MessageFileViewModel(
    MessageFileDto file,
    IFileDownloadService? downloadService = null,
    INotificationService? notificationService = null)
    : ObservableObject
{
    private CancellationTokenSource? _downloadCts;
    private readonly object _ctsLock = new();

    public MessageFileDto File { get; } = file ?? throw new ArgumentNullException(nameof(file));

    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private string? _downloadedFilePath;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private DownloadState _state = DownloadState.NotStarted;

    public int Id => File.Id;
    public string FileName => File.FileName;
    public string ContentType => File.ContentType;
    public string? Url => File.Url;
    public string PreviewType => File.PreviewType;

    public bool IsImage => PreviewType == "image";
    public bool IsVideo => PreviewType == "video";
    public bool IsAudio => PreviewType == "audio";
    public bool IsGenericFile => PreviewType == "file";

    public string? ImageUrl => IsImage ? Url : null;

    public string FileIcon => PreviewType switch
    {
        "image" => "🖼️",
        "video" => "🎬",
        "audio" => "🎵",
        "file" when ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) => "📕",
        "file" when ContentType.Contains("word", StringComparison.OrdinalIgnoreCase)
                     || ContentType.Contains("document", StringComparison.OrdinalIgnoreCase) => "📘",
        "file" when ContentType.Contains("excel", StringComparison.OrdinalIgnoreCase)
                     || ContentType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) => "📗",
        "file" when ContentType.Contains("zip", StringComparison.OrdinalIgnoreCase)
                     || ContentType.Contains("rar", StringComparison.OrdinalIgnoreCase)
                     || ContentType.Contains("7z", StringComparison.OrdinalIgnoreCase) => "📦",
        _ => "📄"
    };

    public string FileSizeFormatted => FormatFileSize(File.FileSize);

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsDownloading || string.IsNullOrEmpty(Url) || downloadService == null)
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
                Dispatcher.UIThread.Post(() => DownloadProgress = p));

            var filePath = await downloadService.DownloadFileAsync(
                Url, FileName, progress, cts.Token);

            if (filePath != null)
            {
                DownloadedFilePath = filePath;
                IsDownloaded = true;
                State = DownloadState.Completed;
                notificationService?.ShowSuccessAsync(
                    $"Файл сохранён: {FileName}", copyToClipboard: false);
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
            notificationService?.ShowErrorAsync(
                $"Ошибка загрузки: {ex.Message}", copyToClipboard: false);
        }
        finally
        {
            IsDownloading = false;

            lock (_ctsLock)
            {
                if (_downloadCts == cts)
                {
                    _downloadCts = null;
                }
            }

            cts.Dispose();
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        lock (_ctsLock)
        {
            try
            {
                _downloadCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
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

    private static string FormatFileSize(long bytes) => bytes switch
    {
        <= 0 => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
    };
}

public enum DownloadState
{
    NotStarted,
    Downloading,
    Completed,
    Failed,
    Cancelled
}