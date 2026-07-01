using Core.Services.Media.Abstractions;
using Core.Services.Media.Files;
using Core.Services.Platform.Abstractions;
using Shared.Contracts.Message;

namespace Core.Features.Chat.ViewModels.Messages;

public sealed partial class MessageFileViewModel(
    MessageFileDto file,
    IDownloadManager? downloadManager = null,
    IFileDownloadService? fileDownloadService = null,
    INotificationService? notificationService = null,
    IFileDownloadStateService? stateService = null)
    : ObservableObject, IDisposable
{
    private const int MaxDisplayFileNameLength = 18;

    private static readonly HashSet<string> ArchiveExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" };
    private static readonly HashSet<string> PdfExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf" };
    private static readonly HashSet<string> WordExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".doc", ".docx", ".rtf", ".odt" };
    private static readonly HashSet<string> ExcelExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".xls", ".xlsx", ".csv", ".ods" };

    private CancellationTokenSource? _downloadCts;
    private readonly Lock _ctsLock = new();
    private bool _disposed;
    private bool _stateLoaded;

    public MessageFileDto File { get; } = file ?? throw new ArgumentNullException(nameof(file));

    [ObservableProperty] public partial double ImageWidth { get; set; } = double.NaN;
    [ObservableProperty] public partial double ImageHeight { get; set; } = double.NaN;
    [ObservableProperty] public partial bool IsImageLoaded { get; set; }
    [ObservableProperty] public partial bool IsDownloading { get; set; }
    [ObservableProperty] public partial double DownloadProgress { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial FileDownloadStatus DownloadStatus { get; set; } = FileDownloadStatus.NotDownloaded;
    [ObservableProperty] public partial string? DownloadedFilePath { get; set; }

    public bool IsDownloaded => DownloadStatus == FileDownloadStatus.Downloaded;
    public bool IsChanged => DownloadStatus == FileDownloadStatus.Changed;

    public string ActionLabel => DownloadStatus switch
    {
        FileDownloadStatus.Downloaded => "Открыть",
        FileDownloadStatus.Changed => "Обновить",
        FileDownloadStatus.Missing => "Скачать снова",
        _ => "Скачать"
    };

    public int Id => File.Id;
    public string FileName => File.FileName;
    public string DisplayFileName => FormatDisplayFileName(FileName, MaxDisplayFileNameLength);
    public string ContentType => File.ContentType;
    public string? Url => File.Url;
    public string PreViewType => File.PreViewType;
    public bool IsImage => PreViewType == "image";
    public bool IsVideo => PreViewType == "video";
    public bool IsAudio => PreViewType == "audio";
    public bool IsGenericFile => PreViewType == "file";
    public string? ImageUrl => IsImage ? Url : null;

    private string FileExtension => Path.GetExtension(FileName) ?? string.Empty;
    private bool IsPdfFile => ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) || PdfExtensions.Contains(FileExtension);
    private bool IsWordFile => ContentType.Contains("word", StringComparison.OrdinalIgnoreCase)
                               || ContentType.Contains("document", StringComparison.OrdinalIgnoreCase) || WordExtensions.Contains(FileExtension);
    private bool IsExcelFile => ContentType.Contains("excel", StringComparison.OrdinalIgnoreCase)
                               || ContentType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) || ExcelExtensions.Contains(FileExtension);
    private bool IsArchiveFile => ContentType.Contains("zip", StringComparison.OrdinalIgnoreCase)
                               || ContentType.Contains("rar", StringComparison.OrdinalIgnoreCase)
                               || ContentType.Contains("7z", StringComparison.OrdinalIgnoreCase)
                               || ContentType.Contains("archive", StringComparison.OrdinalIgnoreCase)
                               || ContentType.Contains("compressed", StringComparison.OrdinalIgnoreCase)
                               || ArchiveExtensions.Contains(FileExtension);

    public string FileIconResourceKey => PreViewType switch
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

    public DownloadState State => (IsDownloading, DownloadStatus, HasError) switch
    {
        (true, _, _) => DownloadState.Downloading,
        (_, FileDownloadStatus.Downloaded, _) => DownloadState.Completed,
        (_, _, true) => DownloadState.Failed,
        _ => DownloadState.NotStarted
    };

    public async Task InitializeAsync()
    {
        if (_stateLoaded) return;
        _stateLoaded = true;

        if (stateService is not null)
        {
            try
            {
                var state = await stateService.GetStateAsync(File);
                if (_disposed) return;
                await RunOnUiAsync(() => ApplyState(state));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileVM] InitializeAsync error: {ex.Message}");
            }
        }

        // Файл уже качается — подключаемся к прогрессу существующей закачки, не запуская новую.
        if (!_disposed && downloadManager?.IsDownloading(File.Id) == true)
            _ = ObserveDownloadAsync(startIfMissing: false);
    }

    private static async Task RunOnUiAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            await Dispatcher.UIThread.InvokeAsync(action);
    }

    private void ApplyState(FileDownloadState state)
    {
        DownloadStatus = state.Status;
        DownloadedFilePath = state.LocalPath;

        OnPropertyChanged(nameof(IsDownloaded));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(State));
    }

    [RelayCommand]
    private async Task ExecuteActionAsync()
    {
        if (_disposed) return;

        switch (DownloadStatus)
        {
            case FileDownloadStatus.Downloaded when !string.IsNullOrEmpty(DownloadedFilePath):
                await OpenExistingFileAsync(DownloadedFilePath!);
                return;
            default:
                await ObserveDownloadAsync(startIfMissing: true);
                break;
        }
    }

    [RelayCommand]
    private async Task DownloadAsync() => await ObserveDownloadAsync(startIfMissing: true);

    [RelayCommand]
    private void CancelDownload() => downloadManager?.CancelDownload(File.Id);

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (!string.IsNullOrEmpty(DownloadedFilePath) && DownloadStatus == FileDownloadStatus.Downloaded)
        {
            await OpenExistingFileAsync(DownloadedFilePath!);
            return;
        }

        if (!IsDownloading)
            await ObserveDownloadAsync(startIfMissing: true);
    }

    [RelayCommand]
    private async Task OpenInFolderAsync()
    {
        if (fileDownloadService is null || string.IsNullOrEmpty(DownloadedFilePath)) return;
        try { await fileDownloadService.OpenFolderAsync(DownloadedFilePath); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    /// <summary>Явно сохранить уже скачанный (закэшированный) файл в системную папку "Загрузки".</summary>
    [RelayCommand]
    private async Task SaveToDownloadsAsync()
    {
        if (fileDownloadService is null || string.IsNullOrEmpty(DownloadedFilePath) || DownloadStatus != FileDownloadStatus.Downloaded)
            return;

        try
        {
            var savedPath = await fileDownloadService.ExportToDownloadsAsync(DownloadedFilePath, FileName);
            notificationService?.ShowSuccessAsync($"Файл сохранён: {savedPath}", copyToClipboard: false);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    [RelayCommand]
    private void RetryDownload()
    {
        HasError = false;
        ErrorMessage = null;
        _ = ObserveDownloadAsync(startIfMissing: true);
    }

    /// <param name="startIfMissing">
    /// true — обычный клик пользователя (можно инициировать закачку, если её ещё нет);
    /// false — фоновое подключение к уже идущей закачке (не должно её стартовать).
    /// </param>
    private async Task ObserveDownloadAsync(bool startIfMissing)
    {
        if (_disposed || downloadManager is null || string.IsNullOrEmpty(Url)) return;
        if (IsDownloading) return;
        if (!startIfMissing && !downloadManager.IsDownloading(File.Id)) return;

        ErrorMessage = null;
        HasError = false;
        IsDownloading = true;
        DownloadProgress = 0;
        OnPropertyChanged(nameof(State));

        var cts = new CancellationTokenSource();
        lock (_ctsLock)
        {
            _downloadCts?.Dispose();
            _downloadCts = cts;
        }

        try
        {
            var progress = new Progress<DownloadProgressInfo>(p =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed && p.Percentage.HasValue) DownloadProgress = p.Percentage.Value;
                }));

            var outcome = await downloadManager.DownloadAsync(File, progress, cts.Token);
            if (!_disposed)
                ApplyOutcome(outcome);
        }
        catch (OperationCanceledException)
        {
            // Отмена именно этой подписки — сама закачка в фоне могла продолжиться для других подписчиков.
            DownloadProgress = 0;
            OnPropertyChanged(nameof(State));
        }
        finally
        {
            IsDownloading = false;
            OnPropertyChanged(nameof(State));

            lock (_ctsLock)
            {
                if (_downloadCts == cts)
                    _downloadCts = null;
            }
            cts.Dispose();
        }
    }

    private void ApplyOutcome(DownloadOutcome outcome)
    {
        switch (outcome.Status)
        {
            case DownloadOutcomeStatus.Completed:
                DownloadedFilePath = outcome.FilePath;
                DownloadStatus = FileDownloadStatus.Downloaded;

                OnPropertyChanged(nameof(IsDownloaded));
                OnPropertyChanged(nameof(IsChanged));
                OnPropertyChanged(nameof(ActionLabel));
                OnPropertyChanged(nameof(State));

                notificationService?.ShowSuccessAsync($"Файл сохранён: {FileName}", copyToClipboard: false);
                break;

            case DownloadOutcomeStatus.Cancelled:
                DownloadProgress = 0;
                OnPropertyChanged(nameof(State));
                break;

            case DownloadOutcomeStatus.Failed:
                SetError(outcome.Error ?? "Не удалось скачать файл");
                notificationService?.ShowErrorAsync($"Ошибка загрузки: {outcome.Error}", copyToClipboard: false);
                break;
        }
    }

    private async Task OpenExistingFileAsync(string path)
    {
        try
        {
            await fileDownloadService!.OpenFileAsync(path);
        }
        catch (FileNotFoundException)
        {
            if (stateService is not null)
                await stateService.ResetAsync(File.Id);

            DownloadStatus = FileDownloadStatus.Missing;
            DownloadedFilePath = null;

            OnPropertyChanged(nameof(IsDownloaded));
            OnPropertyChanged(nameof(IsChanged));
            OnPropertyChanged(nameof(ActionLabel));
            OnPropertyChanged(nameof(State));
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
        OnPropertyChanged(nameof(State));
    }

    public void SetImageDimensions(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        const double maxWidth = 320.0;
        const double maxHeight = 320.0;
        var ratio = Math.Min(maxWidth / width, maxHeight / height);
        ImageWidth = width * ratio;
        ImageHeight = height * ratio;
        IsImageLoaded = true;
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 0 => "",
        0 => "0 B",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
    };

    private static string FormatDisplayFileName(string fileName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length <= maxLength)
            return fileName;

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
            return fileName[..Math.Max(1, maxLength - 1)] + "…";

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var availableNameLength = maxLength - extension.Length - 1;

        if (availableNameLength <= 0)
            return "…" + extension;

        var trimmedName = nameWithoutExtension.Length > availableNameLength
            ? nameWithoutExtension[..availableNameLength]
            : nameWithoutExtension;

        return $"{trimmedName}…{extension}";
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
            catch { /* ignore */ }
            _downloadCts = null;
        }
    }
}

public enum DownloadState { NotStarted, Downloading, Completed, Failed, Cancelled }