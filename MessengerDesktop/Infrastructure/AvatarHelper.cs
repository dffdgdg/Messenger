using System;
using System.IO;

namespace MessengerDesktop.Infrastructure;

public static class AvatarHelper
{
    /// <summary>
    /// URI аватара по умолчанию. Валидируется при загрузке класса.
    /// </summary>
    private static readonly Uri DefaultAvatarUri = new("avares://MessengerDesktop/Assets/Images/default-avatar.webp");

    /// <summary>
    /// Возвращает валидный абсолютный URI для аватара.
    /// При пустом или некорректном входе возвращает аватар по умолчанию.
    /// </summary>
    public static Uri GetSafeUri(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return DefaultAvatarUri;

        try
        {
            if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var absoluteUri)
                && absoluteUri.Scheme is "http" or "https" or "avares")
            {
                return absoluteUri;
            }

            var baseUri = new Uri(App.ApiUrl, UriKind.Absolute);
            return new Uri(baseUri, avatarUrl.TrimStart('/'));
        }
        catch (UriFormatException)
        {
            return DefaultAvatarUri;
        }
    }

    /// <summary>
    /// Возвращает абсолютный URI с параметром сброса кеша (?v=...),
    /// или null, если входная строка пуста.
    /// </summary>
    public static Uri? GetUriWithCacheBuster(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return null;

        Uri resolved;
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var absoluteUri)
            && absoluteUri.Scheme is "http" or "https")
        {
            resolved = absoluteUri;
        }
        else
        {
            var baseUri = new Uri(App.ApiUrl, UriKind.Absolute);
            resolved = new Uri(baseUri, avatarUrl.TrimStart('/'));
        }

        var builder = new UriBuilder(resolved);
        var query = System.Web.HttpUtility.ParseQueryString(builder.Query);
        query["v"] = DateTime.UtcNow.Ticks.ToString();
        builder.Query = query.ToString();

        return builder.Uri;
    }

    public static string GetUrlWithCacheBuster(string? avatarUrl) =>
        GetUriWithCacheBuster(avatarUrl)?.AbsoluteUri ?? string.Empty;
}

public static class MimeTypeHelper
{
    /// <summary>
    /// Определяет MIME-тип по расширению файла.
    /// Для неизвестных расширений возвращает application/octet-stream.
    /// </summary>
    public static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
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
}