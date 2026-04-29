using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Abstractions;

public interface IFileDownloadService
{
    Task<string?> DownloadFileAsync(string url, string fileName, IProgress<double>? progress = null, CancellationToken ct = default);
    string GetDownloadsFolder();
    Task OpenFileAsync(string filePath);
    Task OpenFolderAsync(string folderPath);
}