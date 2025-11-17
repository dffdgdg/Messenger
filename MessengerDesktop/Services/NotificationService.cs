using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using System;
using System.Threading.Tasks;

using AvaloniaNotification = Avalonia.Controls.Notifications.Notification;
using AvaloniaNotificationType = Avalonia.Controls.Notifications.NotificationType;

namespace MessengerDesktop.Services
{
    public static class NotificationService
    {
        private static WindowNotificationManager? _windowNotificationManager;

        public static event Action<string?>? AuthTokenChanged;

        public static void Initialize(Window window)
        {
            _windowNotificationManager = new WindowNotificationManager(window)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 3,
                Margin = new Thickness(0, 40, 20, 0)
            };
        }

        public static void ShowWindow(string title,string message,AvaloniaNotificationType type = AvaloniaNotificationType.Information,int durationMs = 3000)
        {
            if (_windowNotificationManager is null)
                return;
            
            var notification = new AvaloniaNotification(title, message, type, TimeSpan.FromMilliseconds(durationMs));
            _windowNotificationManager.Show(notification);
        }

        public static async Task ShowBoth(string title,string message,
            AvaloniaNotificationType type = AvaloniaNotificationType.Information, bool copyToClipboard = true)
        {
            ShowWindow(title, message, type);
            
            if (copyToClipboard)
                await ClipboardService.CopyToClipboard(message, silent: true);
        }

        public static Task ShowError(string message, bool copyToClipboard = true)
            => ShowBoth("Ошибка", message, AvaloniaNotificationType.Error, copyToClipboard);

        public static Task ShowSuccess(string message, bool copyToClipboard = true)
            => ShowBoth("Успех", message, AvaloniaNotificationType.Success, copyToClipboard);

        public static Task ShowWarning(string message, bool copyToClipboard = true)
            => ShowBoth("Предупреждение", message, AvaloniaNotificationType.Warning, copyToClipboard);

        public static Task ShowInfo(string message, bool copyToClipboard = true)
            => ShowBoth("Messenger", message, AvaloniaNotificationType.Information, copyToClipboard);

        public static void Cleanup()
        {
            AuthTokenChanged?.Invoke(null);
        }
    }
}
