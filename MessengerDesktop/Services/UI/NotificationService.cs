using MessengerDesktop.Services.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.UI;

public enum DesktopNotificationType { Information, Success, Warning, Error }

public interface INotificationService : IDisposable
{
    ReadOnlyObservableCollection<DesktopNotificationViewModel> ActiveNotifications { get; }

    void Initialize();

    void Show(string title, string message,
        DesktopNotificationType type = DesktopNotificationType.Information,
        int durationMs = 3000, Func<Task>? onClick = null);

    Task ShowAsync(string title, string message,
        DesktopNotificationType type = DesktopNotificationType.Information,
        bool copyToClipboard = false, Func<Task>? onClick = null);

    Task ShowErrorAsync(string message, bool copyToClipboard = false);
    Task ShowSuccessAsync(string message, bool copyToClipboard = false);
    Task ShowWarningAsync(string message, bool copyToClipboard = false);
    Task ShowInfoAsync(string message, bool copyToClipboard = false);
}

public class NotificationService : INotificationService
{
    private const int MaxVisibleNotifications = 3;
    private const int AnimationDurationMs = 180;
    private const int DefaultDurationMs = 3000;

    private readonly IPlatformService _platformService;
    private readonly ObservableCollection<DesktopNotificationViewModel> _activeNotifications = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _lifetimes = [];
    private readonly SemaphoreSlim _sync = new(1, 1);

    private volatile bool _disposed;
    private volatile bool _initialized;

    public NotificationService(IPlatformService platformService)
    {
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        ActiveNotifications = new ReadOnlyObservableCollection<DesktopNotificationViewModel>(_activeNotifications);
    }

    public ReadOnlyObservableCollection<DesktopNotificationViewModel> ActiveNotifications { get; }

    public void Initialize()
    {
        if (_initialized)
        {
            Debug.WriteLine("NotificationService already initialized");
            return;
        }

        _initialized = true;
    }

    public void Show(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information,
        int durationMs = DefaultDurationMs, Func<Task>? onClick = null)
    {
        ThrowIfDisposed();

        if (!_initialized)
        {
            Debug.WriteLine($"[NotificationService] Cannot show notification (not initialized): {title} - {message}");
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await ShowInternalAsync(title, message, type, durationMs, onClick);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Debug.WriteLine($"[NotificationService] Failed to show notification: {ex.Message}");
            }
        });
    }

    public async Task ShowAsync(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information,
        bool copyToClipboard = false, Func<Task>? onClick = null)
    {
        ThrowIfDisposed();

        Show(title, message, type, DefaultDurationMs, onClick);

        if (copyToClipboard)
            await _platformService.CopyToClipboardAsync(message);
    }

    public Task ShowErrorAsync(string message, bool copyToClipboard = false) =>
        ShowAsync("Ошибка", message, DesktopNotificationType.Error, copyToClipboard);

    public Task ShowSuccessAsync(string message, bool copyToClipboard = false) =>
        ShowAsync("Успех", message, DesktopNotificationType.Success, copyToClipboard);

    public Task ShowWarningAsync(string message, bool copyToClipboard = false) =>
        ShowAsync("Предупреждение", message, DesktopNotificationType.Warning, copyToClipboard);

    public Task ShowInfoAsync(string message, bool copyToClipboard = false) =>
        ShowAsync("Messenger", message, DesktopNotificationType.Information, copyToClipboard);

    private async Task ShowInternalAsync(string title, string message,
        DesktopNotificationType type, int durationMs, Func<Task>? onClick)
    {
        var notification = new DesktopNotificationViewModel(
            title, message, type, durationMs, CloseNotificationAsync, onClick);
        var cts = new CancellationTokenSource();

        List<DesktopNotificationViewModel>? stale = null;

        await _sync.WaitAsync();
        try
        {
            _lifetimes[notification.Id] = cts;
            _activeNotifications.Insert(0, notification);
            notification.IsVisible = true;

            for (var i = _activeNotifications.Count - 1; i >= MaxVisibleNotifications; i--)
            {
                stale ??= [];
                stale.Add(_activeNotifications[i]);
            }
        }
        finally
        {
            _sync.Release();
        }

        if (stale is not null)
        {
            foreach (var s in stale)
                await CloseNotificationAsync(s);
        }

        _ = RunLifetimeAsync(notification, cts.Token);
    }

    private async Task RunLifetimeAsync(DesktopNotificationViewModel notification, CancellationToken ct)
    {
        try
        {
            await Task.Delay(notification.DurationMs, ct);
            await Dispatcher.UIThread.InvokeAsync(() => CloseNotificationAsync(notification));
        }
        catch (OperationCanceledException) { }
    }

    private async Task CloseNotificationAsync(DesktopNotificationViewModel notification)
    {
        if (_disposed) return;

        CancellationTokenSource? cts;

        await _sync.WaitAsync();
        try
        {
            if (!_lifetimes.Remove(notification.Id, out cts))
                return;

            notification.IsVisible = false;
        }
        finally
        {
            _sync.Release();
        }

        await cts.CancelAsync();
        cts.Dispose();

        await Task.Delay(AnimationDurationMs);

        if (_disposed) return;

        try
        {
            await _sync.WaitAsync();
            try
            {
                _activeNotifications.Remove(notification);
            }
            finally
            {
                _sync.Release();
            }
        }
        catch (ObjectDisposedException) { /* _sync был уничтожен в Dispose() пока мы ждали анимацию */ }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, nameof(NotificationService));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var cts in _lifetimes.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _lifetimes.Clear();
        _activeNotifications.Clear();
        _initialized = false;
        _sync.Dispose();

        GC.SuppressFinalize(this);
    }
}

public sealed partial class DesktopNotificationViewModel(string title, string message, DesktopNotificationType type,
    int durationMs, Func<DesktopNotificationViewModel, Task> closeAsync, Func<Task>? onClick = null) : ObservableObject
{
    private readonly Func<DesktopNotificationViewModel, Task> _closeAsync =
        closeAsync ?? throw new ArgumentNullException(nameof(closeAsync));

    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; } = title;
    public string Message { get; } = message;
    public DesktopNotificationType Type { get; } = type;
    public int DurationMs { get; } = Math.Max(durationMs, 1500);
    public bool IsClickable => onClick is not null;

    public string AccentHex => Type switch
    {
        DesktopNotificationType.Success => "#31C48D",
        DesktopNotificationType.Warning => "#F6AD55",
        DesktopNotificationType.Error => "#F56565",
        _ => "#4F8CFF"
    };

    public string IconGlyph => Type switch
    {
        DesktopNotificationType.Success => "✓",
        DesktopNotificationType.Warning => "!",
        DesktopNotificationType.Error => "✕",
        _ => "i"
    };

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (onClick is not null)
            await onClick();

        await _closeAsync(this);
    }

    [RelayCommand]
    private Task CloseAsync() => _closeAsync(this);
}