using Desktop.ViewModels.Chat.Context;

namespace Desktop.ViewModels.Chat.Shared;

/// <summary>
/// Базовый класс для feature handlers.
/// Предоставляет доступ к ChatContext и стандартный Dispose.
/// Наследует ObservableObject для собственных [ObservableProperty].
/// </summary>
public abstract class ChatFeatureHandler(ChatContext context) : ObservableObject, IDisposable
{
    protected ChatContext Ctx { get; } = context
        ?? throw new ArgumentNullException(nameof(context));

    private bool _disposed;
    protected bool Disposed => _disposed;
    protected bool IsAlive => !_disposed && !Ctx.IsDisposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <param name="disposing">
    /// true  — вызов из Dispose(), освобождаем управляемые ресурсы;
    /// false — вызов из финализатора, только неуправляемые ресурсы.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        // В данном классе нет неуправляемых ресурсов,
        // поэтому финализатор не нужен — только управляемая ветка.
        if (disposing)
        {
            DisposeManagedResources();
        }
    }

    /// <summary>
    /// Переопределите для освобождения управляемых ресурсов.
    /// </summary>
    protected virtual void DisposeManagedResources() { }
}