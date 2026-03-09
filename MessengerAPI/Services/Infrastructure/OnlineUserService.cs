namespace MessengerAPI.Services.Infrastructure;

public interface IOnlineUserService : IDisposable
{
    void UserConnected(int userId, string connectionId);
    void UserDisconnected(int userId, string connectionId);
    bool IsOnline(int userId);
    HashSet<int> GetOnlineUserIds();
    HashSet<int> FilterOnline(IEnumerable<int> userIds);
    int OnlineCount { get; }
}

public sealed class OnlineUserService : IOnlineUserService
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> _connections = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public OnlineUserService()
    {
        _cleanupTimer = new Timer(callback: _ => CleanupEmptyEntries(), state: null,
            dueTime: TimeSpan.FromMinutes(5), period: TimeSpan.FromMinutes(5));
    }

    public void UserConnected(int userId, string connectionId)
    {
        var userConnections = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        userConnections.TryAdd(connectionId, 0);
    }

    public void UserDisconnected(int userId, string connectionId)
    {
        if (!_connections.TryGetValue(userId, out var userConnections))
            return;

        userConnections.TryRemove(connectionId, out _);
    }

    public bool IsOnline(int userId)
        => _connections.TryGetValue(userId, out var c) && !c.IsEmpty;

    public HashSet<int> GetOnlineUserIds()
        => [.. _connections.Where(kv => !kv.Value.IsEmpty).Select(kv => kv.Key)];

    public HashSet<int> FilterOnline(IEnumerable<int> userIds)
        => [.. userIds.Where(IsOnline)];

    public int OnlineCount
        => _connections.Count(kv => !kv.Value.IsEmpty);

    private void CleanupEmptyEntries()
    {
        var removedCount = 0;

        foreach (var kvp in _connections)
        {
            if (kvp.Value.IsEmpty && _connections.TryRemove(kvp))
            {
                removedCount++;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer.Dispose();
    }
}