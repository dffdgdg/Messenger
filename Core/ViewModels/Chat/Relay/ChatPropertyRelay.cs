using System.Collections.Specialized;
using System.ComponentModel;

namespace Core.ViewModels.Chat.Relay;

/// <summary>
/// Централизует подписки на PropertyChanged и CollectionChanged,
/// транслируя изменения обработчиков в свойства ChatViewModel.
/// </summary>
public sealed class ChatPropertyRelay(Action<string> onPropertyChanged) : IDisposable
{
    private readonly List<(INotifyPropertyChanged Source, PropertyChangedEventHandler Handler)> _props = [];
    private readonly List<(INotifyCollectionChanged Source, NotifyCollectionChangedEventHandler Handler)> _cols = [];

    /// <summary>
    /// Пробрасывает изменения свойств source в VM.
    /// mappings: (sourcePropName, vmPropName)
    /// </summary>
    public void Forward(INotifyPropertyChanged source, params (string From, string To)[] mappings)
    {
        var lookup = mappings.GroupBy(m => m.From).ToDictionary(g => g.Key, g => g.Select(x => x.To).ToArray());

        void handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && lookup.TryGetValue(e.PropertyName, out var targets))
                foreach (var t in targets)
                    onPropertyChanged(t);
        }

        source.PropertyChanged += handler;
        _props.Add((source, handler));
    }

    /// <summary>
    /// При любом изменении коллекции уведомляет о перечисленных VM-свойствах.
    /// </summary>
    public void ForwardCollection(INotifyCollectionChanged source, params string[] vmProperties)
    {
        void handler(object? _, NotifyCollectionChangedEventArgs e)
        {
            foreach (var p in vmProperties)
                onPropertyChanged(p);
        }
        source.CollectionChanged += handler;
        _cols.Add((source, handler));
    }

    /// <summary>
    /// При любом изменении коллекции выполняет произвольное действие.
    /// </summary>
    public void ForwardCollection(INotifyCollectionChanged source, Action onChanged)
    {
        void handler(object? _, NotifyCollectionChangedEventArgs e) => onChanged();
        source.CollectionChanged += handler;
        _cols.Add((source, handler));
    }

    public void Dispose()
    {
        foreach (var (src, h) in _props)
            src.PropertyChanged -= h;
        _props.Clear();

        foreach (var (src, h) in _cols)
            src.CollectionChanged -= h;
        _cols.Clear();
    }
}