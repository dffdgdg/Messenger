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
    protected bool Disposed { get; private set; }

    protected bool IsAlive => !Disposed && !Ctx.IsDisposed;

    public virtual void Dispose()
    {
        if (Disposed)
            return;

        Disposed = true;
        GC.SuppressFinalize(this);
    }
}
