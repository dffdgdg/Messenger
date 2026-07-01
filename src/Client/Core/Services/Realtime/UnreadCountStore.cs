using System.Diagnostics;

namespace Core.Services.Realtime;

/// <summary>
/// Хранит и обновляет счётчики непрочитанных сообщений.
/// </summary>
/// 
public sealed class UnreadCountStore
{
    private readonly Dictionary<int, int> _counts = [];
    private readonly Lock _lock = new();
    private int _total;

    public event Action<int, int>? UnreadCountChanged;
    public event Action<int>? TotalUnreadChanged;

    public int GetCount(int chatId)
    {
        lock (_lock) return _counts.GetValueOrDefault(chatId);
    }

    public int Total
    {
        get { lock (_lock) return _total; }
    }

    public void Update(int chatId, int newCount)
    {
        int total;
        lock (_lock)
        {
            var old = _counts.GetValueOrDefault(chatId);
            _counts[chatId] = newCount;
            _total = Math.Max(0, _total - old + newCount);
            total = _total;
        }
        UnreadCountChanged?.Invoke(chatId, newCount);
        TotalUnreadChanged?.Invoke(total);
    }

    public void Increment(int chatId)
    {
        int val, total;
        lock (_lock)
        {
            val = _counts.GetValueOrDefault(chatId) + 1;
            _counts[chatId] = val;
            total = ++_total;
        }
        UnreadCountChanged?.Invoke(chatId, val);
        TotalUnreadChanged?.Invoke(total);
    }

    public void LoadFrom(AllUnreadCountsDto counts)
    {
        Dictionary<int, int> snapshot;
        int total;
        lock (_lock)
        {
            _counts.Clear();
            _total = counts.TotalUnread;
            foreach (var c in counts.Chats)
                _counts[c.ChatId] = c.UnreadCount;
            snapshot = new(_counts);
            total = _total;
        }

        foreach (var kv in snapshot)
            UnreadCountChanged?.Invoke(kv.Key, kv.Value);
        TotalUnreadChanged?.Invoke(total);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _counts.Clear();
            _total = 0;
        }
    }
}