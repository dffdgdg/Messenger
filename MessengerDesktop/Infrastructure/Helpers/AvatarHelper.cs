using System;
using System.IO;

namespace MessengerDesktop.Infrastructure.Helpers;

public static class AvatarHelper
{
    private const string DefaultAvatar = "avares://MessengerDesktop/Assets/Images/default-avatar.webp";

    public static string GetSafeUrl(string? avatarUrl)
    {
        if (string.IsNullOrEmpty(avatarUrl))
            return DefaultAvatar;

        try
        {
            if (avatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return avatarUrl;

            var baseUri = new Uri(App.ApiUrl);
            var avatarUri = new Uri(baseUri, avatarUrl.TrimStart('/'));
            return avatarUri.ToString();
        }
        catch
        {
            return DefaultAvatar;
        }
    }

    public static string GetUrlWithCacheBuster(string? avatarUrl)
    {
        if (string.IsNullOrEmpty(avatarUrl))
            return string.Empty;

        var version = DateTime.UtcNow.Ticks.ToString();
        var avatarUri = avatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? avatarUrl : $"{App.ApiUrl.TrimEnd('/')}/{avatarUrl.TrimStart('/')}";

        var uriBuilder = new UriBuilder(avatarUri);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
        query["v"] = version;
        uriBuilder.Query = query.ToString();

        return uriBuilder.ToString();
    }
}
public static class MimeTypeHelper
{
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