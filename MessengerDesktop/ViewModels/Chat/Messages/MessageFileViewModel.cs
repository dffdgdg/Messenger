using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.Services.UI;
using MessengerShared.DTO.Message;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class MessageFileViewModel(MessageFileDTO file,IFileDownloadService? downloadService = null, INotificationService? notificationService = null)
    : ObservableObject
{
    private CancellationTokenSource? _downloadCts;

    public MessageFileDTO File { get; } = file ?? throw new ArgumentNullException(nameof(file));

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

    public string FileIcon => PreviewType switch
    {
        "image" => "🖼️",
        "video" => "🎬",
        "audio" => "🎵",
        "file" when ContentType.Contains("pdf") => "📕",
        "file" when ContentType.Contains("word") || ContentType.Contains("document") => "📘",
        "file" when ContentType.Contains("excel") || ContentType.Contains("spreadsheet") => "📗",
        "file" when ContentType.Contains("zip") || ContentType.Contains("rar") || ContentType.Contains("7z") => "📦",
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

        _downloadCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p => Dispatcher.UIThread.Post(() => DownloadProgress = p));

            var filePath = await downloadService.DownloadFileAsync(Url, FileName, progress, _downloadCts.Token);

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
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

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

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

public enum DownloadState
{
    NotStarted,
    Downloading,
    Completed,
    Failed,
    Cancelled
}