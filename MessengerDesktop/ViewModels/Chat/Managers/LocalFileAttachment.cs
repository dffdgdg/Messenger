using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public sealed class LocalFileAttachment : IDisposable
{
    private bool _disposed;

    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public MemoryStream Data { get; init; } = new();
    public Bitmap? Thumbnail { get; set; }

    public long FileSize => Data?.Length ?? 0;

    public string FileSizeFormatted => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        _ => $"{FileSize / (1024.0 * 1024.0):F1} MB"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Data?.Dispose();
        Thumbnail?.Dispose();
    }
}