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
        var list = items as IList<T> ?? [.. items];
        if (list.Count == 0) return;

        _suppressNotification = true;
        var startIndex = Items.Count;
        foreach (var item in list)
            Items.Add(item);
        _suppressNotification = false;

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            (System.Collections.IList)list,
            startIndex));
    }

    public void InsertRange(int index, IEnumerable<T> items)
    {
        if (items == null) return;
        var list = items as List<T> ?? [.. items];
        if (list.Count == 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        _suppressNotification = true;
        ((List<T>)Items).InsertRange(index, list);
        _suppressNotification = false;

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            (System.Collections.IList)list,
            index));

        sw.Stop();
        System.Diagnostics.Debug.WriteLine($"[InsertRange] count={list.Count} took={sw.ElapsedMilliseconds}ms");
    }

    public void RemoveRange(IEnumerable<T> items)
    {
        if (items == null) return;
        var toRemove = items as IList<T> ?? [.. items];
        if (toRemove.Count == 0) return;

        _suppressNotification = true;
        var indices = new List<int>();
        foreach (var item in toRemove)
        {
            var idx = Items.IndexOf(item);
            if (idx >= 0) indices.Add(idx);
        }
        ((List<T>)Items).RemoveAll(toRemove.Contains);
        _suppressNotification = false;

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}