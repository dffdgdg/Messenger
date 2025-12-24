using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Platform;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

using AvaloniaNotification = Avalonia.Controls.Notifications.Notification;
using AvaloniaNotificationType = Avalonia.Controls.Notifications.NotificationType;

namespace MessengerDesktop.Services.UI;

public interface INotificationService : IDisposable
{
    void Initialize(Window window);
    void ShowWindow(string title, string message, AvaloniaNotificationType type = AvaloniaNotificationType.Information, int durationMs = 3000);
    Task ShowBothAsync(string title, string message, AvaloniaNotificationType type = AvaloniaNotificationType.Information, bool copyToClipboard = false);
    Task ShowErrorAsync(string message, bool copyToClipboard = false);
    Task ShowSuccessAsync(string message, bool copyToClipboard = false);
    Task ShowWarningAsync(string message, bool copyToClipboard = false);
    Task ShowInfoAsync(string message, bool copyToClipboard = false);
    Task ShowCopyableErrorAsync(string message);
}

public class NotificationService(IPlatformService platformService) : INotificationService
{
    private readonly IPlatformService _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
    private WindowNotificationManager? _notificationManager;
    private bool _disposed;
    private bool _initialized;

    public void Initialize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_initialized)
        {
            Debug.WriteLine("NotificationService already initialized");
            return;
        }

        _notificationManager = new WindowNotificationManager(window)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3,
            Margin = new Thickness(0, 40, 20, 0)
        };

        _initialized = true;
    }

    public void ShowWindow(
        string title,
        string message,
        AvaloniaNotificationType type = AvaloniaNotificationType.Information,
        int durationMs = 3000)
    {
        ThrowIfDisposed();

        if (_notificationManager == null)
        {
            Debug.WriteLine($"[NotificationService] Cannot show notification (not initialized): {title} - {message}");
            return;
        }

        if (durationMs < 0)
            durationMs = 3000;

        var notification = new AvaloniaNotification(title, message, type, TimeSpan.FromMilliseconds(durationMs));
        _notificationManager.Show(notification);
    }

    public async Task ShowBothAsync(
        string title,
        string message,
        AvaloniaNotificationType type = AvaloniaNotificationType.Information,
        bool copyToClipboard = false)
    {
        ThrowIfDisposed();

        ShowWindow(title, message, type);

        if (copyToClipboard)
        {
            await _platformService.CopyToClipboardAsync(message);
        }
    }

    public Task ShowErrorAsync(string message, bool copyToClipboard = false)
        => ShowBothAsync("Ошибка", message, AvaloniaNotificationType.Error, copyToClipboard);

    public Task ShowSuccessAsync(string message, bool copyToClipboard = false)
        => ShowBothAsync("Успех", message, AvaloniaNotificationType.Success, copyToClipboard);

    public Task ShowWarningAsync(string message, bool copyToClipboard = false)
        => ShowBothAsync("Предупреждение", message, AvaloniaNotificationType.Warning, copyToClipboard);

    public Task ShowInfoAsync(string message, bool copyToClipboard = false)
        => ShowBothAsync("Messenger", message, AvaloniaNotificationType.Information, copyToClipboard);

    public async Task ShowCopyableErrorAsync(string message)
    {
        ThrowIfDisposed();

        ShowWindow("Ошибка", message, AvaloniaNotificationType.Error);

        var clipboardText = $"Ошибка: {message}\nВремя: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        await _platformService.CopyToClipboardAsync(clipboardText);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(NotificationService));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notificationManager = null;
        _initialized = false;

        GC.SuppressFinalize(this);
    }
}