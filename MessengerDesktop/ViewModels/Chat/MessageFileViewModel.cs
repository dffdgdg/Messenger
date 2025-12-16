// ViewModels/Chat/MessageFileViewModel.cs
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat
{
    public partial class MessageFileViewModel : ObservableObject
    {
        private readonly IFileDownloadService? _downloadService;
        private readonly INotificationService? _notificationService;
        private CancellationTokenSource? _downloadCts;

        public MessageFileDTO File { get; }

        #region Observable Properties

        [ObservableProperty]
        private bool isDownloading;

        [ObservableProperty]
        private double downloadProgress;

        [ObservableProperty]
        private bool isDownloaded;

        [ObservableProperty]
        private string? downloadedFilePath;

        [ObservableProperty]
        private string? errorMessage;

        [ObservableProperty]
        private bool hasError;

        [ObservableProperty]
        private DownloadState state = DownloadState.NotStarted;

        #endregion

        #region Computed Properties

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

        #endregion

        public MessageFileViewModel(MessageFileDTO file)
        {
            File = file;
            _downloadService = App.Current?.Services?.GetService<IFileDownloadService>();
            _notificationService = App.Current?.Services?.GetService<INotificationService>();
        }

        public MessageFileViewModel(MessageFileDTO file, IFileDownloadService downloadService)
        {
            File = file;
            _downloadService = downloadService;
        }

        #region Commands

        [RelayCommand]
        private async Task DownloadAsync()
        {
            if (IsDownloading || string.IsNullOrEmpty(Url) || _downloadService == null)
                return;

            // Сброс состояния
            ErrorMessage = null;
            HasError = false;
            IsDownloading = true;
            DownloadProgress = 0;
            State = DownloadState.Downloading;

            _downloadCts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<double>(p =>
                {
                    Dispatcher.UIThread.Post(() => DownloadProgress = p);
                });

                var filePath = await _downloadService.DownloadFileAsync(
                    Url,
                    FileName,
                    progress,
                    _downloadCts.Token);

                if (filePath != null)
                {
                    DownloadedFilePath = filePath;
                    IsDownloaded = true;
                    State = DownloadState.Completed;

                    _notificationService?.ShowSuccessAsync(
                        $"Файл сохранён: {FileName}",
                        copyToClipboard: false);
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

                _notificationService?.ShowErrorAsync(
                    $"Ошибка загрузки: {ex.Message}",
                    copyToClipboard: false);
            }
            finally
            {
                IsDownloading = false;
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }

        [RelayCommand]
        private void CancelDownload()
        {
            _downloadCts?.Cancel();
        }

        [RelayCommand]
        private async Task OpenFileAsync()
        {
            if (_downloadService == null)
                return;

            try
            {
                if (!string.IsNullOrEmpty(DownloadedFilePath))
                {
                    await _downloadService.OpenFileAsync(DownloadedFilePath);
                }
                else if (!IsDownloaded && !IsDownloading)
                {
                    // Сначала скачиваем, потом открываем
                    await DownloadAsync();

                    if (IsDownloaded && !string.IsNullOrEmpty(DownloadedFilePath))
                    {
                        await _downloadService.OpenFileAsync(DownloadedFilePath);
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
            if (_downloadService == null || string.IsNullOrEmpty(DownloadedFilePath))
                return;

            await _downloadService.OpenFolderAsync(DownloadedFilePath);
        }

        [RelayCommand]
        private void RetryDownload()
        {
            HasError = false;
            ErrorMessage = null;
            State = DownloadState.NotStarted;
            _ = DownloadAsync();
        }

        #endregion

        #region Helpers

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return "";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        #endregion
    }

    public enum DownloadState
    {
        NotStarted,
        Downloading,
        Completed,
        Failed,
        Cancelled
    }
}