using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
    public interface IFileDownloadService
    {
        Task<string?> DownloadFileAsync(string url, string fileName, IProgress<double>? progress = null, CancellationToken ct = default);
        string GetDownloadsFolder();
        Task OpenFileAsync(string filePath);
        Task OpenFolderAsync(string folderPath);
    }

    public class FileDownloadService(HttpClient httpClient) : IFileDownloadService
    {
        private const string SafeUnixPath = "/usr/bin:/bin";
        private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        public string GetDownloadsFolder()
        {
            string downloadsPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
            else
            {
                var xdgDownload = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
                if (!string.IsNullOrEmpty(xdgDownload) && Directory.Exists(xdgDownload))
                {
                    downloadsPath = xdgDownload;
                }
                else
                {
                    downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                }
            }

            if (!Directory.Exists(downloadsPath))
                Directory.CreateDirectory(downloadsPath);

            return downloadsPath;
        }

        public async Task<string?> DownloadFileAsync(string url,string fileName,IProgress<double>? progress = null,CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("URL cannot be null or empty", nameof(url));

            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("FileName cannot be null or empty", nameof(fileName));

            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                var downloadsFolder = GetDownloadsFolder();
                var filePath = GetUniqueFilePath(downloadsFolder, SanitizeFileName(fileName));

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(filePath,FileMode.Create, FileAccess.Write,
                    FileShare.None, bufferSize: 81920, useAsync: true);

                var buffer = new byte[81920];
                int bytesRead;
                var lastReportedProgress = 0.0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0 && progress != null)
                    {
                        var currentProgress = (double)downloadedBytes / totalBytes * 100;

                        if (currentProgress - lastReportedProgress >= 1 || currentProgress >= 100)
                        {
                            progress.Report(currentProgress);
                            lastReportedProgress = currentProgress;
                        }
                    }
                }

                progress?.Report(100);
                return filePath;
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

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(sanitized) ? "downloaded_file" : sanitized;
        }

        private static string GetUniqueFilePath(string folder, string fileName)
        {
            var filePath = Path.Combine(folder, fileName);

            if (!File.Exists(filePath))
                return filePath;

            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var counter = 1;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(folder, $"{nameWithoutExt} ({counter}){extension}");
                counter++;

                if (counter > 10000)
                    throw new InvalidOperationException("Too many files with the same name");
            }

            return filePath;
        }

        public Task OpenFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("FilePath cannot be null or empty", nameof(filePath));

            if (!File.Exists(filePath))
            {
                Debug.WriteLine($"File not found: {filePath}");
                throw new FileNotFoundException("Файл не найден", filePath);
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening file: {ex.Message}");
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
                    var explorerPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        "explorer.exe");

                    Process.Start(CreateSafeProcessStartInfo(explorerPath, $"/select,\"{folderPath}\""));
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    const string openPath = "/usr/bin/open";
                    Process.Start(CreateSafeProcessStartInfo(openPath, $"-R \"{folderPath}\""));
                }
                else
                {
                    var directory = Path.GetDirectoryName(folderPath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        const string xdgOpenPrimaryPath = "/usr/bin/xdg-open";
                        const string xdgOpenSecondaryPath = "/bin/xdg-open";

                        var xdgOpenPath = File.Exists(xdgOpenPrimaryPath)
                            ? xdgOpenPrimaryPath
                            : xdgOpenSecondaryPath;

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
        private static ProcessStartInfo CreateSafeProcessStartInfo(string executablePath, string arguments)
        {
            if (!Path.IsPathFullyQualified(executablePath))
                throw new ArgumentException("Executable path must be absolute.", nameof(executablePath));

            if (!File.Exists(executablePath))
                throw new FileNotFoundException("Executable was not found.", executablePath);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false
            };

            processStartInfo.Environment["PATH"] = SafeUnixPath;
            return processStartInfo;
        }
    }
}