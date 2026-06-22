namespace Core.ViewModels.Chat.Context;

public sealed class ChatInfoPanelMediaItem(MessageViewModel message, MessageFileDto file)
{
    public MessageViewModel Message { get; } = message;
    public MessageFileDto File { get; } = file;
    public DateTime CreatedAt => Message.CreatedAt;
    public string? PreviewUrl => File.Url;
    public string AltText => string.IsNullOrWhiteSpace(File.FileName) ? "Фото" : File.FileName;
}

public sealed class ChatInfoPanelFileItem(MessageViewModel message, MessageFileDto file)
{
    public MessageViewModel Message { get; } = message;
    public MessageFileDto File { get; } = file;
    public DateTime CreatedAt => Message.CreatedAt;
    public string FileName => string.IsNullOrWhiteSpace(File.FileName) ? "Файл" : File.FileName;
    public string FileSizeFormatted => FormatFileSize(File.FileSize);

    public string FileExtension =>
        string.IsNullOrWhiteSpace(Path.GetExtension(FileName)) ? "FILE" : Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 0 => string.Empty,
        0 => "0 B",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
    };
}
