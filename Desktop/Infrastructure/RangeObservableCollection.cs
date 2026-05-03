using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Desktop.Infrastructure;

public class RangeObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnPropertyChanged(e);
    }

    public void AddRange(IEnumerable<T> items)
    {
        if (items == null) return;

        _suppressNotification = true;
        foreach (var item in items)
            Items.Add(item);
        _suppressNotification = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
    }

    public void InsertRange(int index, IEnumerable<T> items)
    {
        if (items == null) return;

        _suppressNotification = true;
        var list = items as List<T> ?? [.. items];
        ((List<T>)Items).InsertRange(index, list);
        _suppressNotification = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
    }

    public void RemoveRange(IEnumerable<T> items)
    {
        if (items == null) return;

        _suppressNotification = true;
        var toRemove = new HashSet<T>(items);
        ((List<T>)Items).RemoveAll(toRemove.Contains);
        _suppressNotification = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
    }
}