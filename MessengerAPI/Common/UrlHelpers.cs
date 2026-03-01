namespace MessengerAPI.Common;

public static class UrlHelpers
{
    public static string? BuildFullUrl(this string? path, IUrlBuilder? urlBuilder)
    {
        if (string.IsNullOrEmpty(path) || urlBuilder == null)
            return path;

        return urlBuilder.BuildUrl(path);
    }
}