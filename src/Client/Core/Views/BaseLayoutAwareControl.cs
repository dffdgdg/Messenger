using Avalonia.Reactive;
using Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Views;

/// <summary>
/// Базовый класс для View, которым нужен доступ к LayoutMode.
/// Использует слабые ссылки для предотвращения утечек памяти.
/// </summary>
public abstract class BaseLayoutAwareControl : UserControl
{
    private static ILayoutModeProvider? _cachedProvider;
    private static readonly object _providerLock = new();

    private static ILayoutModeProvider? CachedProvider
    {
        get
        {
            if (_cachedProvider is null)
            {
                lock (_providerLock)
                {
                    _cachedProvider ??= AppConfig.Services.GetService<ILayoutModeProvider>();
                }
            }
            return _cachedProvider;
        }
    }

    public static readonly DirectProperty<BaseLayoutAwareControl, LayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<BaseLayoutAwareControl, LayoutMode>(nameof(LayoutMode), o => o.LayoutMode, unsetValue: LayoutMode.Normal);

    private LayoutMode _layoutMode = LayoutMode.Normal;
    private IDisposable? _layoutModeSubscription;
    private bool _isSubscribed;

    public LayoutMode LayoutMode
    {
        get => _layoutMode;
        protected set
        {
            var previous = _layoutMode;
            if (previous == value) return;

            SetAndRaise(LayoutModeProperty, ref _layoutMode, value);
            OnLayoutModeChanged(previous, value);
        }
    }

    /// <summary>
    /// Переопределите для реакции на смену LayoutMode.
    /// Вызывается ДО обновления биндингов.
    /// </summary>
    protected virtual void OnLayoutModeChanged(LayoutMode previous, LayoutMode current) { }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToLayoutMode();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnsubscribeFromLayoutMode();
    }

    /// <summary>
    /// Переопределите если нужна дополнительная логика подписки.
    /// Всегда вызывайте base.SubscribeToLayoutMode()!
    /// </summary>
    protected virtual void SubscribeToLayoutMode()
    {
        if (_isSubscribed) return;

        var provider = CachedProvider;
        if (provider is null) return;

        _isSubscribed = true;
        LayoutMode = provider.LayoutMode;
        _layoutModeSubscription = provider.LayoutModeChanged
            .Subscribe(new AnonymousObserver<LayoutMode>(m => LayoutMode = m));
    }

    /// <summary>
    /// Переопределите если нужна дополнительная логика отписки.
    /// Всегда вызывайте base.UnsubscribeFromLayoutMode()!
    /// </summary>
    protected virtual void UnsubscribeFromLayoutMode()
    {
        _isSubscribed = false;
        _layoutModeSubscription?.Dispose();
        _layoutModeSubscription = null;
    }
}