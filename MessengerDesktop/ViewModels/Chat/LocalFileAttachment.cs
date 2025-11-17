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
}
