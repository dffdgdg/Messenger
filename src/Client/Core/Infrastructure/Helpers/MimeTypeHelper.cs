namespace Core.Infrastructure.Helpers;

public static class MimeTypeHelper
{
    /// <summary>
    /// Определяет MIME-тип по расширению файла.
    /// Для неизвестных расширений возвращает application/octet-stream.
    /// </summary>
    public static string GetMimeType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".pdf" => "application/pdf",
        ".doc" or ".docx" => "application/msword",
        ".xls" or ".xlsx" => "application/vnd.ms-excel",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };
}