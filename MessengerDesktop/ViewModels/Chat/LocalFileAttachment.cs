using Avalonia.Media.Imaging;
using System.IO;

namespace MessengerDesktop.ViewModels.Chat;

public class LocalFileAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public MemoryStream Data { get; set; } = new();
    public Bitmap? Thumbnail { get; set; }

    public long FileSize 
        => Data?.Length ?? 0;

    public string FileSizeFormatted
    {
        get
        {
            var size = FileSize;
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{size / 1024.0:F1} KB";
            return $"{size / (1024.0 * 1024.0):F1} MB";
        }
    }

    public void Dispose()
    {
        Data?.Dispose();
        Thumbnail?.Dispose();
    }
}
