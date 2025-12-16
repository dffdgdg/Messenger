using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using MessengerDesktop.Services.Platform;
using System;
using System.Threading.Tasks;

using AvaloniaNotification = Avalonia.Controls.Notifications.Notification;
using AvaloniaNotificationType = Avalonia.Controls.Notifications.NotificationType;

namespace MessengerDesktop.Services;

public interface INotificationService : IDisposable
{
    void Initialize(Window window);
    void ShowWindow(string title, string message, AvaloniaNotificationType type = AvaloniaNotificationType.Information, int durationMs = 3000);
    Task ShowBothAsync(string title, string message, AvaloniaNotificationType type = AvaloniaNotificationType.Information, bool copyToClipboard = true);
    Task ShowErrorAsync(string message, bool copyToClipboard = true);
    Task ShowSuccessAsync(string message, bool copyToClipboard = true);
    Task ShowWarningAsync(string message, bool copyToClipboard = true);
    Task ShowInfoAsync(string message, bool copyToClipboard = true);
}

public class NotificationService(IPlatformService platformService) : INotificationService
{
    private readonly IPlatformService _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
    private WindowNotificationManager? _notificationManager;
    private bool _disposed;

    public void Initialize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _notificationManager = new WindowNotificationManager(window)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3,
            Margin = new Thickness(0, 40, 20, 0)
        };
    }

    public void ShowWindow(string title,string message, AvaloniaNotificationType type = AvaloniaNotificationType.Information, int durationMs = 3000)
    {
        if (_disposed || _notificationManager == null)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationService] Cannot show notification: {title} - {message}");
            return;
        }

        var notification = new AvaloniaNotification(title, message, type, TimeSpan.FromMilliseconds(durationMs));
        _notificationManager.Show(notification);
    }

    public async Task ShowBothAsync(
    string title,
    string message,
    AvaloniaNotificationType type = AvaloniaNotificationType.Information,
    bool copyToClipboard = true)
    {
        ShowWindow(title, message, type);

        if (copyToClipboard)
        {
            await _platformService.CopyToClipboardAsync(message);
        }
    }

    public Task ShowErrorAsync(string message, bool copyToClipboard = true)
        => ShowBothAsync("Ошибка", message, AvaloniaNotificationType.Error, copyToClipboard);

    public Task ShowSuccessAsync(string message, bool copyToClipboard = true)
        => ShowBothAsync("Успех", message, AvaloniaNotificationType.Success, copyToClipboard);

    public Task ShowWarningAsync(string message, bool copyToClipboard = true)
        => ShowBothAsync("Предупреждение", message, AvaloniaNotificationType.Warning, copyToClipboard);

    public Task ShowInfoAsync(string message, bool copyToClipboard = true)
        => ShowBothAsync("Messenger", message, AvaloniaNotificationType.Information, copyToClipboard);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notificationManager = null;
        GC.SuppressFinalize(this);
    }

}