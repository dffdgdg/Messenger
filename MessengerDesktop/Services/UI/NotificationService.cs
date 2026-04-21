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

    void Show(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information, int durationMs = 3000, Func<Task>? onClick = null);

    Task ShowAsync(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information, bool copyToClipboard = false, Func<Task>? onClick = null);

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

    private int _disposed;
    private int _initializedFlag;

    public NotificationService(IPlatformService platformService)
    {
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        ActiveNotifications = new ReadOnlyObservableCollection<DesktopNotificationViewModel>(_activeNotifications);
    }

    ~NotificationService() => Dispose(disposing: false);

    public ReadOnlyObservableCollection<DesktopNotificationViewModel> ActiveNotifications { get; }

    private bool IsInitialized => Volatile.Read(ref _initializedFlag) == 1;
    private bool IsDisposed => Interlocked.CompareExchange(ref _disposed, 0, 0) == 1;

    public void Initialize()
    {
        if (Interlocked.CompareExchange(ref _initializedFlag, 1, 0) != 0)
        {
            Debug.WriteLine("[NotificationService] Сервис уже инициализирован");
            return;
        }
    }

    public void Show(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information, int durationMs = DefaultDurationMs, Func<Task>? onClick = null)
    {
        ThrowIfDisposed();

        if (!IsInitialized)
        {
            Debug.WriteLine($"[NotificationService] Невозможно показать уведомление (сервис не инициализирован): {title} - {message}");
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
                Debug.WriteLine($"[NotificationService] Ошибка при отображении уведомления: {ex.Message}");
            }
        });
    }

    public async Task ShowAsync(string title, string message, DesktopNotificationType type = DesktopNotificationType.Information, bool copyToClipboard = false, Func<Task>? onClick = null)
    {
        ThrowIfDisposed();

        Show(title, message, type, DefaultDurationMs, onClick);

        if (copyToClipboard)
            await _platformService.CopyToClipboardAsync(message);
    }

    public Task ShowErrorAsync(string message, bool copyToClipboard = false) => ShowAsync("Ошибка", message, DesktopNotificationType.Error, copyToClipboard);

    public Task ShowSuccessAsync(string message, bool copyToClipboard = false) => ShowAsync("Успех", message, DesktopNotificationType.Success, copyToClipboard);

    public Task ShowWarningAsync(string message, bool copyToClipboard = false) => ShowAsync("Предупреждение", message, DesktopNotificationType.Warning, copyToClipboard);

    public Task ShowInfoAsync(string message, bool copyToClipboard = false) => ShowAsync("Messenger", message, DesktopNotificationType.Information, copyToClipboard);

    private async Task ShowInternalAsync(string title, string message, DesktopNotificationType type, int durationMs, Func<Task>? onClick)
    {
        var notification = new DesktopNotificationViewModel(title, message, type, durationMs, CloseNotificationAsync, onClick);
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
            {
                await CloseNotificationAsync(s);
            }
        }

        _ = RunLifetimeAsync(notification, cts.Token).ContinueWith(t => Debug.WriteLine($"[NotificationService] Ошибка в RunLifetimeAsync: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task RunLifetimeAsync(DesktopNotificationViewModel notification, CancellationToken ct)
    {
        try
        {
            await Task.Delay(notification.DurationMs, ct);
            await Dispatcher.UIThread.InvokeAsync(() => CloseNotificationAsync(notification));
        }
        catch (OperationCanceledException) { /* ожидаемо */ }
    }

    private async Task CloseNotificationAsync(DesktopNotificationViewModel notification)
    {
        if (IsDisposed) return;

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

        if (IsDisposed) return;

        await _sync.WaitAsync();
        try
        {
            _activeNotifications.Remove(notification);
        }
        catch (ObjectDisposedException) { /* _sync disposed во время анимации */ }
        finally
        {
            if (!IsDisposed)
                _sync.Release();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(IsDisposed, nameof(NotificationService));

    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (disposing)
        {
            foreach (var cts in _lifetimes.Values)
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch (ObjectDisposedException) { /* Токен уже освобожден */ }
            }
        }

        _lifetimes.Clear();
        _activeNotifications.Clear();
        Interlocked.Exchange(ref _initializedFlag, 0);
        _sync.Dispose();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public sealed partial class DesktopNotificationViewModel(string title, string message, DesktopNotificationType type, int durationMs,
    Func<DesktopNotificationViewModel, Task> closeAsync, Func<Task>? onClick = null) : ObservableObject
{
    private static class NotificationColors
    {
        public const string Success = "#31C48D";
        public const string Warning = "#F6AD55";
        public const string Error = "#F56565";
        public const string Information = "#4F8CFF";
    }

    private static class NotificationIcons
    {
        public const string Success = "✓";
        public const string Warning = "!";
        public const string Error = "✕";
        public const string Information = "i";
    }

    private readonly Func<DesktopNotificationViewModel, Task> _closeAsync = closeAsync ?? throw new ArgumentNullException(nameof(closeAsync));

    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; } = title;
    public string Message { get; } = message;
    public DesktopNotificationType Type { get; } = type;
    public int DurationMs { get; } = Math.Max(durationMs, 1500);
    public bool IsClickable => onClick is not null;

    public string AccentHex => Type switch
    {
        DesktopNotificationType.Success => NotificationColors.Success,
        DesktopNotificationType.Warning => NotificationColors.Warning,
        DesktopNotificationType.Error => NotificationColors.Error,
        _ => NotificationColors.Information
    };

    public string IconGlyph => Type switch
    {
        DesktopNotificationType.Success => NotificationIcons.Success,
        DesktopNotificationType.Warning => NotificationIcons.Warning,
        DesktopNotificationType.Error => NotificationIcons.Error,
        _ => NotificationIcons.Information
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