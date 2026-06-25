using API.Application.Services.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace API.Infrastructure.Status;

public sealed partial class OnlineUserService : IOnlineUserService
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> _connections = new();
    private readonly Timer _cleanupTimer;
    private readonly ILogger<OnlineUserService> _logger;
    private volatile bool _disposed;

    public OnlineUserService(ILogger<OnlineUserService> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(_ => CleanupEmptyEntries(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public void UserConnected(int userId, string connectionId)
    {
        _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>()).TryAdd(connectionId, 0);

        LogUserConnected(userId, connectionId, GetConnectionIds(userId).Count);
    }

    public void UserDisconnected(int userId, string connectionId)
    {
        if (!_connections.TryGetValue(userId, out var userConnections))
            return;

        userConnections.TryRemove(connectionId, out _);

        LogUserDisconnected(userId, connectionId, userConnections.Count);
    }

    public bool IsOnline(int userId)
        => _connections.TryGetValue(userId, out var c) && !c.IsEmpty;

    public HashSet<int> GetOnlineUserIds()
        => [.. _connections.Where(kv => !kv.Value.IsEmpty).Select(kv => kv.Key)];

    public HashSet<int> FilterOnline(IEnumerable<int> userIds)
        => [.. userIds.Where(IsOnline)];

    public IReadOnlyCollection<string> GetConnectionIds(int userId)
        => _connections.TryGetValue(userId, out var c) ? [.. c.Keys] : [];

    public int OnlineCount
        => _connections.Count(kv => !kv.Value.IsEmpty);

    private void CleanupEmptyEntries()
    {
        var removed = 0;

        foreach (var kvp in _connections)
        {
            if (kvp.Value.IsEmpty && _connections.TryRemove(kvp))
                removed++;
        }

        if (removed > 0)
            LogCleanup(removed);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer.Dispose();
        _connections.Clear();
        LogDisposed();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Пользователь {UserId} подключился ({ConnectionId}). Активных соединений: {Count}")]
    private partial void LogUserConnected(int userId, string connectionId, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Пользователь {UserId} отключился ({ConnectionId}). Осталось соединений: {Count}")]
    private partial void LogUserDisconnected(int userId, string connectionId, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Очистка пустых записей: удалено {Count}")]
    private partial void LogCleanup(int count);

    [LoggerMessage(Level = LogLevel.Debug,Message = "OnlineUserService освобождён")]
    private partial void LogDisposed();
}