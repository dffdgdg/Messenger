namespace Core.Shared.Configuration;

public static class AppConfig
{
    public static string ApiUrl { get; set; } = string.Empty;

    public static bool WasDiscovered { get; set; }

    public static Uri DefaultAvatarUri { get; set; } =
        new("avares://Desktop/Assets/Images/default-avatar.webp");
    public static Func<Task>? LogoutCallback { get; set; }
    public static IServiceProvider Services { get; set; } = null!;

    public static Action<string, bool>? SetApiUrlCallback { get; set; }

    public static Action? RestartApplicationCallback { get; set; }

    public static void SetApiUrl(string url, bool manual = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be empty", nameof(url));

        if (!url.EndsWith('/'))
            url += "/";

        ApiUrl = url;
        SetApiUrlCallback?.Invoke(url, manual);
    }

    public static void RestartApplication() => RestartApplicationCallback?.Invoke();
}