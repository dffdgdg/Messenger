using Desktop.Services.UI;
using System;
using System.Threading.Tasks;

namespace Desktop.Services.Abstractions;

public interface INotificationService : IDisposable
{
    ReadOnlyObservableCollection<DesktopNotificationViewModel> ActiveNotifications { get; }

    void Initialize();

    void Show(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information, int durationMs = 3000, Func<Task>? onClick = null);

    Task ShowAsync(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information, bool copyToClipboard = false, Func<Task>? onClick = null);

    Task ShowErrorAsync(string message, bool copyToClipboard = false);
    Task ShowSuccessAsync(string message, bool copyToClipboard = false);
    Task ShowWarningAsync(string message, bool copyToClipboard = false);
    Task ShowInfoAsync(string message, bool copyToClipboard = false);
}