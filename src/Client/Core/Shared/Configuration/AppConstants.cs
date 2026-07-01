namespace Core.Shared.Configuration;

public static class AppConstants
{
    public const int MaxFileSizeMegabytes = 300;
    public const long MaxFileSizeBytes = MaxFileSizeMegabytes * 1024L * 1024L;
    public const int DefaultDebounceMs = 300;
    public const int DefaultPageSize = 30;
    public const int LoadMorePageSize = 25;
    public const int SearchPageSize = 20;
    public const int HighlightDurationMs = 3000;
    public const int MarkAsReadDebounceMs = 300;
    public const int MarkAsReadCooldownSeconds = 1;
    public const int TypingSendDebounceMs = 1200;
    public const int TypingIndicatorDurationMs = 3500;
    public const int MaxConcurrentDownloads = 3;
}