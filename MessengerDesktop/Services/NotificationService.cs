using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using DesktopNotifications;
using DesktopNotifications.FreeDesktop;
using DesktopNotifications.Windows;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AvaloniaNotification = Avalonia.Controls.Notifications.Notification;
using AvaloniaNotificationType = Avalonia.Controls.Notifications.NotificationType;

namespace MessengerDesktop.Services
{
    public static class NotificationService
    {
        private static WindowNotificationManager? _windowNotificationManager;
        private static DesktopNotifications.INotificationManager? _systemNotificationManager;
        private static string? _authToken;

        public static event Action<string?>? AuthTokenChanged;

        public static void Initialize(Window window)
        {
            // 🔹 Встроенные уведомления Avalonia
            _windowNotificationManager = new WindowNotificationManager(window)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 3,
                Margin = new Thickness(0, 40, 20, 0)
            };

            // 🔹 Системные уведомления
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                   _systemNotificationManager = new WindowsNotificationManager();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    _systemNotificationManager = new FreeDesktopNotificationManager();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System notifications unavailable: {ex.Message}");
                _systemNotificationManager = null;
            }
        }

        public static void SetAuthToken(string token)
        {
            _authToken = token;
            AuthTokenChanged?.Invoke(_authToken);
        }

        public static string? GetAuthToken() => _authToken;

        // ─────────────────────────────────────────────
        // Встроенные уведомления Avalonia
        // ─────────────────────────────────────────────
        public static void ShowWindow(string title, string message, AvaloniaNotificationType type = AvaloniaNotificationType.Information, int durationMs = 3000)
        {
            if (_windowNotificationManager is null) return;
            var notification = new AvaloniaNotification(title, message, type, TimeSpan.FromMilliseconds(durationMs));
            _windowNotificationManager.Show(notification);
        }

        // ─────────────────────────────────────────────
        // Системные уведомления
        // ─────────────────────────────────────────────
        public static async Task ShowSystem(string title, string message)
        {
            if (_systemNotificationManager is null) return;

            var nf = new DesktopNotifications.Notification
            {
                Title = title,
                Body = message
            };

            try
            {
                await _systemNotificationManager.ShowNotification(nf);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System notification failed: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // Оба варианта
        // ─────────────────────────────────────────────
        public static async Task ShowBoth(string title, string message, AvaloniaNotificationType type = AvaloniaNotificationType.Information, bool copyToClipboard = true)
        {
            ShowWindow(title, message, type);
            await ShowSystem(title, message);

            if (copyToClipboard)
                await ClipboardService.CopyToClipboard(message, silent: true);
        }

        public static async Task ShowError(string message, bool copyToClipboard = true)
            => await ShowBoth("Ошибка", message, AvaloniaNotificationType.Error, copyToClipboard);

        public static async Task ShowSuccess(string message, bool copyToClipboard = true)
            => await ShowBoth("Успех", message, AvaloniaNotificationType.Success, copyToClipboard);

        public static async Task ShowWarning(string message, bool copyToClipboard = true)
            => await ShowBoth("Предупреждение", message, AvaloniaNotificationType.Warning, copyToClipboard);

        public static async Task ShowInfo(string message, bool copyToClipboard = true)
            => await ShowBoth("Messenger", message, AvaloniaNotificationType.Information, copyToClipboard);

        public static void Cleanup()
        {
            _authToken = null;
            AuthTokenChanged?.Invoke(null);
        }
    }
}
