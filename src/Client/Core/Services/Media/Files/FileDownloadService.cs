using Core.Services.Auth.Abstractions;
using Core.Services.Media.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace Core.Services.Media.Files;

public class FileDownloadService(HttpClient httpClient, ISessionStore sessionStore, IAuthManager authManager) : IFileDownloadService
{
    private const string SafeUnixPath = "/usr/bin:/bin";
    private const string PartSuffix = ".part";

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ISessionStore _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    private readonly IAuthManager _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));

    public string GetDownloadsFolder()
    {
        // TODO(Android): SpecialFolder.UserProfile недоступен на Android — нужен
        // отдельный путь через IPlatformService (SAF/MediaStore).
        string downloadsPath;

        string? xdgDownload = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
        downloadsPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : (!string.IsNullOrEmpty(xdgDownload) && Directory.Exists(xdgDownload)
                ? xdgDownload
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

        if (!Directory.Exists(downloadsPath))
            Directory.CreateDirectory(downloadsPath);

        return downloadsPath;
    }

    public async Task<FileFetchResult> FetchAsync(string url, string destinationPath, IProgress<FileFetchProgress>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));
        if (string.IsNullOrEmpty(destinationPath))
            throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationPath));

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var partPath = destinationPath + PartSuffix;
        var existingBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0L;

        try
        {
            using var response = await SendWithRefreshAsync(url, existingBytes, ct);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // Файл на сервере изменился/укоротился — старый .part больше не валиден.
                TryDeleteFile(partPath);
                return await FetchAsync(url, destinationPath, progress, ct);
            }

            response.EnsureSuccessStatusCode();

            var isResumed = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (existingBytes > 0 && !isResumed)
            {
                // Сервер не поддержал Range — качаем с нуля.
                existingBytes = 0;
                TryDeleteFile(partPath);
            }

            var totalBytes = ResolveTotalBytes(response, existingBytes);
            var downloadedBytes = existingBytes;
            var lastReportedPercentage = -1.0;
            var lastReportedBytes = existingBytes;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(partPath, isResumed ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloadedBytes += bytesRead;

                    ReportProgress(progress, downloadedBytes, totalBytes, ref lastReportedPercentage, ref lastReportedBytes);
                }
            }

            File.Move(partPath, destinationPath, overwrite: true);
            progress?.Report(new FileFetchProgress(downloadedBytes, totalBytes ?? downloadedBytes));

            return new FileFetchResult(FileFetchStatus.Completed, destinationPath, downloadedBytes);
        }
        catch (OperationCanceledException)
        {
            // .part остаётся на диске — следующий вызов докачает с этого места.
            return new FileFetchResult(FileFetchStatus.Cancelled, partPath, existingBytes);
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"HTTP error downloading file: {ex.Message}");
            throw new InvalidOperationException($"Ошибка сети: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error downloading file: {ex.Message}");
            throw new InvalidOperationException($"Ошибка записи файла: {ex.Message}", ex);
        }
    }

    private static long? ResolveTotalBytes(HttpResponseMessage response, long existingBytes)
    {
        if (response.Content.Headers.ContentRange?.Length is long fullLength)
            return fullLength;

        var contentLength = response.Content.Headers.ContentLength;
        return contentLength.HasValue ? contentLength.Value + existingBytes : null;
    }

    private static void ReportProgress(IProgress<FileFetchProgress>? progress, long downloaded, long? total,
        ref double lastReportedPercentage, ref long lastReportedBytes)
    {
        if (progress is null) return;

        if (total is > 0)
        {
            var percentage = (double)downloaded / total.Value * 100;
            if (percentage - lastReportedPercentage < 1 && percentage < 100) return;
            lastReportedPercentage = percentage;
        }
        else
        {
            const long step = 256 * 1024;
            if (downloaded - lastReportedBytes < step) return;
            lastReportedBytes = downloaded;
        }

        progress.Report(new FileFetchProgress(downloaded, total));
    }

    private async Task<HttpResponseMessage> SendWithRefreshAsync(string url, long rangeStart, CancellationToken ct)
    {
        var response = await SendAsync(url, rangeStart, ct);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();

        var refreshed = await _authManager.TryRefreshTokenAsync();
        if (!refreshed)
            throw new InvalidOperationException("Сессия истекла. Войдите заново.");

        return await SendAsync(url, rangeStart, ct);
    }

    private Task<HttpResponseMessage> SendAsync(string url, long rangeStart, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        var token = _sessionStore.Token;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (rangeStart > 0)
            request.Headers.Range = new RangeHeaderValue(rangeStart, null);

        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    public async Task<string> ExportToDownloadsAsync(string sourceFilePath, string suggestedFileName, CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Файл не найден", sourceFilePath);

        var downloadsFolder = GetDownloadsFolder();
        var sanitizedName = SanitizeFileName(suggestedFileName);

        var (destinationPath, destinationStream) = CreateUniqueFileAtomic(downloadsFolder, sanitizedName);

        await using (destinationStream)
        await using (var source = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            await source.CopyToAsync(destinationStream, ct);
        }

        return destinationPath;
    }

    /// <summary>
    /// Атомарно резервирует уникальное имя файла через FileMode.CreateNew,
    /// без предварительного File.Exists — старая реализация имела
    /// TOCTOU-гонку (два параллельных скачивания одного имени могли
    /// затереть друг друга).
    /// </summary>
    private static (string Path, FileStream Stream) CreateUniqueFileAtomic(string folder, string fileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var counter = 0; counter <= 10000; counter++)
        {
            var candidateName = counter == 0 ? fileName : $"{nameWithoutExt} ({counter}){extension}";
            var path = Path.Combine(folder, candidateName);

            try
            {
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                return (path, stream);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Занято другим потоком/процессом между попытками — пробуем следующее имя.
            }
        }

        throw new InvalidOperationException("Too many files with the same name");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? "downloaded_file" : sanitized;
    }

    public Task OpenFileAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("FilePath cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
        {
            Debug.WriteLine($"Файл не найден: {filePath}");
            throw new FileNotFoundException("Файл не найден", filePath);
        }

        try { Process.Start(CreateOpenFileStartInfo(filePath)); }
        catch (Exception ex)
        {
            Debug.WriteLine($"Не удалось открыть файл: {ex.Message}");
            throw new InvalidOperationException($"Не удалось открыть файл: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    public Task OpenFolderAsync(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            throw new ArgumentException("FolderPath cannot be null or empty", nameof(folderPath));

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(CreateSafeProcessStartInfo(explorerPath, $"/select,{folderPath}"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(CreateSafeProcessStartInfo("/usr/bin/open", "-R", folderPath));
            }
            else
            {
                var directory = Path.GetDirectoryName(folderPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    const string xdgOpenPrimaryPath = "/usr/bin/xdg-open";
                    const string xdgOpenSecondaryPath = "/bin/xdg-open";
                    var xdgOpenPath = File.Exists(xdgOpenPrimaryPath) ? xdgOpenPrimaryPath : xdgOpenSecondaryPath;
                    Process.Start(CreateSafeProcessStartInfo(xdgOpenPath, directory));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening folder: {ex.Message}");
            throw new InvalidOperationException($"Не удалось открыть папку: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    private static ProcessStartInfo CreateOpenFileStartInfo(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new ProcessStartInfo { FileName = filePath, UseShellExecute = true, Verb = "open" };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return CreateSafeProcessStartInfo("/usr/bin/open", filePath);

        const string xdgOpenPrimaryPath = "/usr/bin/xdg-open";
        const string xdgOpenSecondaryPath = "/bin/xdg-open";
        var xdgOpenPath = File.Exists(xdgOpenPrimaryPath) ? xdgOpenPrimaryPath : xdgOpenSecondaryPath;

        return CreateSafeProcessStartInfo(xdgOpenPath, filePath);
    }

    private static ProcessStartInfo CreateSafeProcessStartInfo(string executablePath, params string[] arguments)
    {
        if (!Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("Executable path must be absolute.", nameof(executablePath));

        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Executable was not found.", executablePath);

        var processStartInfo = new ProcessStartInfo { FileName = executablePath, UseShellExecute = false };

        foreach (var argument in arguments)
            processStartInfo.ArgumentList.Add(argument);

        processStartInfo.Environment["PATH"] = SafeUnixPath;
        return processStartInfo;
    }
}