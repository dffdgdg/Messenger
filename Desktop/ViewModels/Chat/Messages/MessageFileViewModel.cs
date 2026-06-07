using Desktop.Services.Features.Media.Files;

namespace Desktop.ViewModels.Chat;

public sealed partial class MessageFileViewModel(
    MessageFileDto file,
    IFileDownloadService? downloadService = null,
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

    // ── Изображения ────────────────────────────────────────────────
    [ObservableProperty] public partial double ImageWidth { get; set; } = double.NaN;
    [ObservableProperty] public partial double ImageHeight { get; set; } = double.NaN;
    [ObservableProperty] public partial bool IsImageLoaded { get; set; }

    // ── Скачивание ──────────────────────────────────────────────────
    [ObservableProperty] public partial bool IsDownloading { get; set; }
    [ObservableProperty] public partial double DownloadProgress { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }

    // ── Состояние скачанного файла ──────────────────────────────────
    [ObservableProperty] public partial FileDownloadStatus DownloadStatus { get; set; } = FileDownloadStatus.NotDownloaded;
    [ObservableProperty] public partial string? DownloadedFilePath { get; set; }

    /// <summary>Совместимость со старым кодом (XAML-биндинги).</summary>
    public bool IsDownloaded => DownloadStatus == FileDownloadStatus.Downloaded;

    /// <summary>Файл изменился на сервере после скачивания.</summary>
    public bool IsChanged => DownloadStatus == FileDownloadStatus.Changed;

    /// <summary>Текст кнопки действия.</summary>
    public string ActionLabel => DownloadStatus switch
    {
        FileDownloadStatus.Downloaded => "Открыть",
        FileDownloadStatus.Changed => "Обновить",
        FileDownloadStatus.Missing => "Скачать снова",
        _ => "Скачать"
    };

    // ── Мета ────────────────────────────────────────────────────────
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
    private bool IsArchiveFile => ContentType.Contains("zip", StringComparison.OrdinalIgnoreCase)
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

    // ── Состояние (старый enum для совместимости) ────────────────────
    public DownloadState State => (IsDownloading, DownloadStatus, HasError) switch
    {
        (true, _, _) => DownloadState.Downloading,
        (_, FileDownloadStatus.Downloaded, _) => DownloadState.Completed,
        (_, _, true) => DownloadState.Failed,
        _ => DownloadState.NotStarted
    };

    // ────────────────────────────────────────────────────────────────
    // Инициализация состояния из БД
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Вызвать один раз после создания VM.
    /// Восстанавливает статус скачивания из локальной БД.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_stateLoaded || stateService is null) return;
        _stateLoaded = true;

        try
        {
            var state = await stateService.GetStateAsync(File);
            if (_disposed) return;

            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyState(state);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_disposed)
                        ApplyState(state);
                });
            }

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileVM] InitializeAsync error: {ex.Message}");
        }
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

    // ────────────────────────────────────────────────────────────────
    // Команды
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Универсальная кнопка: Скачать / Открыть / Обновить / Скачать снова.
    /// </summary>
    [RelayCommand]
    private async Task ExecuteActionAsync()
    {
        if (_disposed || downloadService is null) return;

        switch (DownloadStatus)
        {
            case FileDownloadStatus.Downloaded when !string.IsNullOrEmpty(DownloadedFilePath):
                await OpenExistingFileAsync(DownloadedFilePath!);
                return;

            default:
                await DownloadInternalAsync();
                break;
        }
    }

    /// <summary>Кнопка «Скачать» (старый биндинг в XAML).</summary>
    [RelayCommand]
    private async Task DownloadAsync() => await DownloadInternalAsync();

    [RelayCommand]
    private void CancelDownload()
    {
        lock (_ctsLock)
        {
            try { _downloadCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (downloadService is null) return;

        if (!string.IsNullOrEmpty(DownloadedFilePath) &&
            DownloadStatus == FileDownloadStatus.Downloaded)
        {
            await OpenExistingFileAsync(DownloadedFilePath!);
            return;
        }

        if (!IsDownloading)
            await DownloadInternalAsync();
    }

    [RelayCommand]
    private async Task OpenInFolderAsync()
    {
        if (downloadService is null || string.IsNullOrEmpty(DownloadedFilePath)) return;
        try
        {
            await downloadService.OpenFolderAsync(DownloadedFilePath);
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
        _ = DownloadInternalAsync();
    }

    // ────────────────────────────────────────────────────────────────
    // Внутренняя логика скачивания
    // ────────────────────────────────────────────────────────────────

    private async Task DownloadInternalAsync()
    {
        if (_disposed || IsDownloading || string.IsNullOrEmpty(Url) || downloadService is null)
            return;

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
            var progress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed) DownloadProgress = p;
                }));

            var filePath = await downloadService.DownloadFileAsync(
                Url, FileName, progress, cts.Token);

            if (filePath is null) return;

            if (stateService is not null)
                await stateService.RegisterDownloadAsync(File, filePath);

            DownloadedFilePath = filePath;
            DownloadStatus = FileDownloadStatus.Downloaded;

            OnPropertyChanged(nameof(IsDownloaded));
            OnPropertyChanged(nameof(IsChanged));
            OnPropertyChanged(nameof(ActionLabel));
            OnPropertyChanged(nameof(State));

            notificationService?.ShowSuccessAsync($"Файл сохранён: {FileName}", copyToClipboard: false);
        }
        catch (OperationCanceledException)
        {
            DownloadProgress = 0;
            OnPropertyChanged(nameof(State));
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            notificationService?.ShowErrorAsync(
                $"Ошибка загрузки: {ex.Message}", copyToClipboard: false);
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

    private async Task OpenExistingFileAsync(string path)
    {
        try
        {
            await downloadService!.OpenFileAsync(path);
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
            catch { }
            _downloadCts = null;
        }
    }
}

public enum DownloadState { NotStarted, Downloading, Completed, Failed, Cancelled }