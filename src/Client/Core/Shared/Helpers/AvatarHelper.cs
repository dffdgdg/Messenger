using Core.Shared.Configuration;

namespace Core.Shared.Helpers;

public static class AvatarHelper
{
    private static Uri DefaultAvatarUri => AppConfig.DefaultAvatarUri;

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

            var baseUri = new Uri(AppConfig.ApiUrl, UriKind.Absolute);
            return new Uri(baseUri, avatarUrl.TrimStart('/'));
        }
        catch (UriFormatException)
        {
            return DefaultAvatarUri;
        }
    }

    public static Uri? GetUriWithCacheBuster(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return null;

        Uri resolved;
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var absoluteUri) && absoluteUri.Scheme is "http" or "https")
        {
            resolved = absoluteUri;
        }
        else
        {
            var baseUri = new Uri(AppConfig.ApiUrl, UriKind.Absolute);
            resolved = new Uri(baseUri, avatarUrl.TrimStart('/'));
        }

        var stableVersion = Math.Abs(resolved.AbsolutePath.GetHashCode()) % 10000;
        var builder = new UriBuilder(resolved)
        {
            Query = resolved.Query.TrimStart('?') + (string.IsNullOrEmpty(resolved.Query) ? "" : "&") + $"v={stableVersion}"
        };
        return builder.Uri;
    }

    public static string WithFreshCacheBuster(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return string.Empty;

        Uri resolved;
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https")
        {
            resolved = abs;
        }
        else
        {
            var baseUri = new Uri(AppConfig.ApiUrl, UriKind.Absolute);
            resolved = new Uri(baseUri, avatarUrl.TrimStart('/'));
        }

        var path = resolved.GetLeftPart(UriPartial.Path);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{path}?v={ts}";
    }

    public static string GetUrlWithCacheBuster(string? avatarUrl) => GetUriWithCacheBuster(avatarUrl)?.AbsoluteUri ?? string.Empty;
}