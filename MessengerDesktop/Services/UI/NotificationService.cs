using MessengerDesktop.Services.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.UI;

public enum DesktopNotificationType
{
    Information,
    Success,
    Warning,
    Error
}

public interface INotificationService : IDisposable
{
    ReadOnlyObservableCollection<DesktopNotificationViewModel> ActiveNotifications { get; }
    void Initialize(Window window);
    void ShowWindow(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information, int durationMs = 3000, Func<Task>? onClick = null);
    Task ShowBothAsync(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information, bool copyToClipboard = false, Func<Task>? onClick = null);
    Task ShowErrorAsync(string message, bool copyToClipboard = false);
    Task ShowSuccessAsync(string message, bool copyToClipboard = false);
    Task ShowWarningAsync(string message, bool copyToClipboard = false);
    Task ShowInfoAsync(string message, bool copyToClipboard = false);
    Task ShowCopyableErrorAsync(string message);
}

public class NotificationService : INotificationService
{
    private readonly IPlatformService _platformService;
    private readonly ObservableCollection<DesktopNotificationViewModel> _activeNotifications = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _lifetimes = [];
    private readonly SemaphoreSlim _sync = new(1, 1);

    private bool _disposed;
    private bool _initialized;

    public NotificationService(IPlatformService platformService)
    {
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        ActiveNotifications = new ReadOnlyObservableCollection<DesktopNotificationViewModel>(_activeNotifications);
    }

    public ReadOnlyObservableCollection<DesktopNotificationViewModel> ActiveNotifications { get; }

    public void Initialize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_initialized)
        {
            Debug.WriteLine("NotificationService already initialized");
            return;
        }

        _initialized = true;
    }

    public void ShowWindow(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information, int durationMs = 3000, Func<Task>? onClick = null)
    {
        ThrowIfDisposed();

        if (!_initialized)
        {
            Debug.WriteLine($"[NotificationService] Cannot show notification (not initialized): {title} - {message}");
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() => ShowInternalAsync(title, message, type, durationMs, onClick));
    }

    public async Task ShowBothAsync(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information,
        bool copyToClipboard = false, Func<Task>? onClick = null)
    {
        ThrowIfDisposed();

        ShowWindow(title, message, type, 3000, onClick);

        if (copyToClipboard)
        {
            await _platformService.CopyToClipboardAsync(message);
        }
    }

    public Task ShowErrorAsync(string message, bool copyToClipboard = false) => ShowBothAsync("Ошибка", message, DesktopNotificationType.Error, copyToClipboard);
    public Task ShowSuccessAsync(string message, bool copyToClipboard = false) => ShowBothAsync("Успех", message, DesktopNotificationType.Success, copyToClipboard);
    public Task ShowWarningAsync(string message, bool copyToClipboard = false) => ShowBothAsync("Предупреждение", message, DesktopNotificationType.Warning, copyToClipboard);
    public Task ShowInfoAsync(string message, bool copyToClipboard = false) => ShowBothAsync("Messenger", message, DesktopNotificationType.Information, copyToClipboard);

    public async Task ShowCopyableErrorAsync(string message)
    {
        ThrowIfDisposed();

        ShowWindow("Ошибка", message, DesktopNotificationType.Error);

        var clipboardText = $"Ошибка: {message}\nВремя: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        await _platformService.CopyToClipboardAsync(clipboardText);
    }

    private async Task ShowInternalAsync(string title, string message, DesktopNotificationType type, int durationMs, Func<Task>? onClick)
    {
        var notification = new DesktopNotificationViewModel(title, message, type, durationMs, CloseNotificationAsync, onClick);
        var cts = new CancellationTokenSource();

        await _sync.WaitAsync();
        try
        {
            _lifetimes[notification.Id] = cts;
            _activeNotifications.Insert(0, notification);
            notification.IsVisible = true;

            while (_activeNotifications.Count > 3)
            {
                var staleNotification = _activeNotifications[^1];
                _ = CloseNotificationAsync(staleNotification);
                break;
            }
        }
        finally
        {
            _sync.Release();
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
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CloseNotificationAsync(DesktopNotificationViewModel notification)
    {
        if (_disposed)
            return;

        CancellationTokenSource? cts = null;

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

        cts.Cancel();
        cts.Dispose();

        await Task.Delay(180);

        await _sync.WaitAsync();
        try
        {
            _activeNotifications.Remove(notification);
            notification.Dispose();
        }
        finally
        {
            _sync.Release();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(NotificationService));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var lifetime in _lifetimes.Values)
        {
            lifetime.Cancel();
            lifetime.Dispose();
        }

        _lifetimes.Clear();

        foreach (var notification in _activeNotifications)
        {
            notification.Dispose();
        }

        _activeNotifications.Clear();
        _initialized = false;
        _sync.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed partial class DesktopNotificationViewModel(
    string title,
    string message,
    DesktopNotificationType type,
    int durationMs,
    Func<DesktopNotificationViewModel, Task> closeAsync,
    Func<Task>? onClick = null) : ObservableObject, IDisposable
{
    private readonly Func<DesktopNotificationViewModel, Task> _closeAsync = closeAsync ?? throw new ArgumentNullException(nameof(closeAsync));
    private int _disposed;

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
    private bool _isVisible;

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (onClick is not null)
        {
            await onClick();
        }

        await _closeAsync(this);
    }

    [RelayCommand]
    private Task CloseAsync() => _closeAsync(this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
    }
}