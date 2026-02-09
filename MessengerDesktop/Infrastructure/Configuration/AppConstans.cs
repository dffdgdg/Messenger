namespace MessengerDesktop.Infrastructure.Configuration;

public static class AppConstants
{
    public const long MaxFileSizeBytes = 20 * 1024 * 1024;
    public const int DefaultDebounceMs = 300;
    public const int DefaultPageSize = 50;
    public const int LoadMorePageSize = 30;
    public const int SearchPageSize = 20;
    public const int HighlightDurationMs = 3000;
    public const int MarkAsReadDebounceMs = 300;
    public const int MarkAsReadCooldownSeconds = 1;
}