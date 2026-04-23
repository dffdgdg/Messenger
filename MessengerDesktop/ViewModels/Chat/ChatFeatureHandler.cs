using System;

namespace MessengerDesktop.ViewModels.Chat;

/// <summary>
/// Базовый класс для feature handlers.
/// Предоставляет доступ к ChatContext и стандартный Dispose.
/// Наследует ObservableObject для собственных [ObservableProperty].
/// </summary>
public abstract class ChatFeatureHandler(ChatContext context) : ObservableObject, IDisposable
{
    protected ChatContext Ctx { get; } = context ?? throw new ArgumentNullException(nameof(context));

    private bool _disposed;
    protected bool Disposed => _disposed;
    protected bool IsAlive => !_disposed && !Ctx.IsDisposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeManaged();
        GC.SuppressFinalize(this);
    }

    protected virtual void DisposeManaged() { /* Expected to be overridden by derived classes */ }
}